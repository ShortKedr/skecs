import contextlib
from hashlib import sha256
import io
import json
from pathlib import Path
import tempfile
import unittest

from compare import TARGETS, check_artifacts, main


class ArtifactComparisonTests(unittest.TestCase):
    def setUp(self):
        artifacts = Path(__file__).resolve().parents[2] / "artifacts"
        artifacts.mkdir(exist_ok=True)
        temporary = tempfile.TemporaryDirectory(prefix="determinism-test-", dir=artifacts)
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        self.fixtures = root / "fixtures"
        self.fixtures.mkdir()
        self.golden = root / "golden.json"
        self.output = root / "downloaded"
        self.output.mkdir()
        self.commit = "c" * 40
        self.sdk = "10.0.204"
        self.runtime = "10.0.8"
        self.targets = ("windows-2022-Debug", "ubuntu-24.04-arm-Release")
        self.source = json.dumps({"formatVersion": 1, "capacity": 1, "operations": [{"kind": "create"}]})
        (self.fixtures / "tiny.json").write_text(self.source, encoding="utf-8")
        self.trace = [self.row(0, "initial"), self.row(1, "create")]
        self.trace[1]["entities"] = [dict(key=9, id=0, version=2, transform={"x": 10, "y": 0}, motion=None, marker=None)]
        self.trace[1]["result"] = ["9:0:2"]
        self.trace[1]["visits"] = ["0:2"]
        golden_rows = [{field: row[field] for field in ("result", "visits", "entities")} for row in self.trace]
        self.golden.write_text(json.dumps({"formatVersion": 1, "fixtures": {"tiny": golden_rows}}), encoding="utf-8")
        for target in self.targets:
            self.write_artifact(target)
        self.artifact = self.output / f"determinism-{self.targets[1]}"
        self.second = self.artifact / "second"
        self.second_trace = self.second / "tiny.jsonl"

    @staticmethod
    def row(step, operation):
        return dict(step=step, operation=operation, result=[], visits=[], entities=[], hash="a" * 64)

    @staticmethod
    def write_trace(path, trace):
        path.write_text("".join(json.dumps(row) + "\n" for row in trace), encoding="utf-8")

    def write_artifact(self, target):
        artifact = self.output / f"determinism-{target}"
        for directory in ("first", "second", "inputs", "tests"):
            (artifact / directory).mkdir(parents=True)
        (artifact / "inputs/tiny.json").write_text(self.source, encoding="utf-8")
        (artifact / "golden.json").write_bytes(self.golden.read_bytes())
        (artifact / "dotnet-info.txt").write_text(self.sdk, encoding="utf-8")
        trx = ('<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
               '<ResultSummary outcome="Completed"><Counters executed="2" passed="2" failed="0" error="0" />'
               '</ResultSummary></TestRun>')
        for name in ("skecs.trx", "determinism.trx"):
            (artifact / "tests" / name).write_text(trx, encoding="utf-8")
        os, architecture, configuration = TARGETS[target]
        manifest = dict(formatVersion=1, os=os, architecture=architecture, configuration=configuration,
                        sdk=self.sdk, runtime=self.runtime, commit=self.commit, jit={},
                        fixtures=[dict(name="tiny", inputSha256=sha256(self.source.encode()).hexdigest(), steps=1)])
        for repeat in ("first", "second"):
            run = artifact / repeat
            (run / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
            self.write_trace(run / "tiny.jsonl", self.trace)

    def compare(self):
        return check_artifacts(self.output, self.targets, self.fixtures, self.golden,
                               self.commit, self.sdk, self.runtime)

    def test_compares_different_architectures_and_configurations(self):
        self.assertEqual((4, 1), self.compare())

    def test_rejects_missing_artifact(self):
        self.artifact.rename(self.artifact.with_name("misnamed"))
        with self.assertRaisesRegex(ValueError, "missing .*ubuntu-24.04-arm-Release.*unexpected .*misnamed"):
            self.compare()

    def test_rejects_unexpected_artifact(self):
        (self.output / "extra").mkdir()
        with self.assertRaisesRegex(ValueError, "unexpected .*extra"):
            self.compare()

    def test_rejects_missing_second_process(self):
        self.second.rename(self.second.with_name("extra"))
        with self.assertRaisesRegex(ValueError, "missing .*second"):
            self.compare()

    def test_rejects_empty_trace(self):
        self.second_trace.write_bytes(b"")
        with self.assertRaisesRegex(ValueError, "tiny.jsonl: expected a nonempty file"):
            self.compare()

    def test_rejects_truncated_trace(self):
        self.write_trace(self.second_trace, self.trace[:-1])
        with self.assertRaisesRegex(ValueError, r"step 1 \(create\): missing or incomplete row"):
            self.compare()

    def test_rejects_extra_trace_row(self):
        self.write_trace(self.second_trace, self.trace + [self.row(2, "create")])
        with self.assertRaisesRegex(ValueError, "unexpected row after step 1"):
            self.compare()

    def test_rejects_invalid_json_or_duplicate_keys(self):
        for invalid in ("invalid\n", '{"step": 0, "step": 0}\n'):
            with self.subTest(invalid=invalid):
                self.second_trace.write_text(invalid, encoding="utf-8")
                with self.assertRaisesRegex(ValueError, r"step 0 \(initial\):"):
                    self.compare()

    def test_rejects_wrong_environment_or_fixture_manifest(self):
        path = self.second / "manifest.json"
        original = json.loads(path.read_text(encoding="utf-8"))
        for field, value, message in (
            ("formatVersion", True, "formatVersion expected"),
            ("architecture", "X64", "architecture expected"),
            ("runtime", "10.0.0", "runtime expected"),
            ("configuration", "Debug", "configuration expected"),
            ("commit", "d" * 40, "commit expected"),
            ("sdk", "8.0.428", "sdk expected"),
            ("jit", {"DOTNET_TieredCompilation": "0"}, "JIT flags expected"),
            ("fixtures", [], "missing fixtures"),
            ("fixtures", [dict(name="tiny", inputSha256="0" * 64, steps=1)], "input digest expected"),
            ("fixtures", [dict(original["fixtures"][0], steps=0)], "expected 1 steps"),
        ):
            with self.subTest(field=field, value=value):
                path.write_text(json.dumps(dict(original, **{field: value})), encoding="utf-8")
                with self.assertRaisesRegex(ValueError, message):
                    self.compare()

    def test_rejects_changed_retained_fixture_and_golden(self):
        path = self.artifact / "inputs/tiny.json"
        path.write_text(self.source + "\n", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "inputs differ from checkout"):
            self.compare()
        path.write_text(self.source, encoding="utf-8")
        (self.artifact / "golden.json").write_text("{}", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "golden differs from checkout"):
            self.compare()

    def test_rejects_failed_test_report(self):
        path = self.artifact / "tests/skecs.trx"
        report = path.read_text(encoding="utf-8")
        for field, expected, replacement in (("failed", 0, 1), ("passed", 2, 1), ("executed", 2, 0)):
            with self.subTest(field=field):
                path.write_text(report.replace(f'{field}="{expected}"', f'{field}="{replacement}"'), encoding="utf-8")
                with self.assertRaisesRegex(ValueError, "no successful test result"):
                    self.compare()

    def test_reports_first_changed_field_even_if_the_hash_was_retained(self):
        self.trace[1]["entities"][0]["transform"]["x"] = 11
        self.write_trace(self.second_trace, self.trace)
        with self.assertRaisesRegex(ValueError, r"step 1 \(create\): entities\[key=9\].transform.x: expected 10, got 11"):
            self.compare()

    def test_detects_divergence_in_identity_membership_visits_result_and_hash(self):
        entity = self.trace[1]["entities"][0]
        for change, message in (
            ({"entities": []}, "entities.*missing"),
            ({"entities": [dict(entity, version=3)]}, "entities.*version.*expected 2, got 3"),
            ({"entities": [dict(entity, marker={"kind": 255, "value": 42})]}, "entities.*marker.*expected None"),
            ({"result": ["different"]}, "result\\[0\\]: expected"),
            ({"visits": ["different"]}, "visits\\[0\\]: expected"),
            ({"hash": "b" * 64}, "hash: expected"),
        ):
            with self.subTest(change=change):
                self.write_trace(self.second_trace, [self.trace[0], dict(self.trace[1], **change)])
                with self.assertRaisesRegex(ValueError, message):
                    self.compare()

    def test_rejects_identical_invalid_snapshot_rows_across_all_runs(self):
        entity = self.trace[1]["entities"][0]
        for update, message in (
            ({"transform": {"x": 10}}, "invalid component fields.*transform"),
            ({"transform": {"x": 2 ** 31, "y": 0}}, "invalid component fields.*transform"),
            ({"motion": {"dx": 0, "dy": -(2 ** 31) - 1}}, "invalid component fields.*motion"),
            ({"marker": {"kind": 256, "value": 1}}, "invalid component fields.*marker"),
            ({"id": 2 ** 31}, "invalid or duplicate identity"),
            ({"version": 2 ** 31}, "invalid or duplicate identity"),
        ):
            with self.subTest(update=update):
                mutated = [self.trace[0], dict(self.trace[1], entities=[dict(entity, **update)])]
                for trace_path in self.output.glob("*/*/tiny.jsonl"):
                    self.write_trace(trace_path, mutated)
                with self.assertRaisesRegex(ValueError, message):
                    self.compare()

    def test_rejects_identical_valid_but_incorrect_golden_rows_across_all_runs(self):
        expected = self.trace[1]
        entity = expected["entities"][0]
        for update, message in (
            ({"result": ["incorrect"]}, "golden mismatch.*result"),
            ({"visits": []}, "golden mismatch.*visits"),
            ({"entities": [dict(entity, transform={"x": -11, "y": 0})]}, "golden mismatch.*transform.x"),
        ):
            with self.subTest(update=update):
                for trace_path in self.output.glob("*/*/tiny.jsonl"):
                    self.write_trace(trace_path, [self.trace[0], dict(expected, **update)])
                with self.assertRaisesRegex(ValueError, message):
                    self.compare()

    def test_cli_requires_successful_producer_and_complete_inventory(self):
        args = [str(self.output), "--fixtures", str(self.fixtures), "--commit", self.commit,
                "--sdk", self.sdk, "--runtime", self.runtime]
        with contextlib.redirect_stderr(io.StringIO()) as errors:
            self.assertEqual(1, main(args))
        self.assertIn("missing", errors.getvalue())
        for target in self.targets:
            args.extend(("--target", target))
        with contextlib.redirect_stdout(io.StringIO()) as result:
            self.assertEqual(0, main(args))
        self.assertIn("4 independent runs from 2 targets", result.getvalue())
        with contextlib.redirect_stderr(io.StringIO()) as errors:
            self.assertEqual(1, main(args + ["--producer-result", "failure"]))
        self.assertIn("producer matrix result: 'failure'", errors.getvalue())


if __name__ == "__main__":
    unittest.main()
