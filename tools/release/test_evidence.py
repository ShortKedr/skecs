"""Regression checks for binding release evidence to the exact package bytes."""

import hashlib
import json
import tempfile
import unittest
import zipfile
from pathlib import Path

from finalize_unity_zip import finalize
from prepare_release import REQUIRED_EVIDENCE, evidence_table
from unity_evidence import UNITY_KINDS, load_unity_evidence
from verify_local_report import (CHECK_MARKERS, PACKAGE_DIGEST_PREFIXES, PLAYER_BACKENDS,
                                 InvalidReport, validate_artifacts, validate_checks)
from verify_unity_zip import verify


COMMIT = "a" * 40
RUN_ID = "12345"
VERSION = "0.2.0"


class EvidenceTests(unittest.TestCase):
    def test_local_consumer_logs_must_include_the_candidate_package_digest(self):
        with tempfile.TemporaryDirectory() as temporary:
            repo = Path(temporary)
            artifacts = repo / "artifacts"
            artifacts.mkdir()
            digest = "a" * 64
            checks = []
            for kind, marker in CHECK_MARKERS.items():
                log = artifacts / f"{kind}.log"
                prefix = PACKAGE_DIGEST_PREFIXES.get(kind)
                content = f"{marker}\r\n"
                if prefix:
                    content = f"{prefix.format(version=VERSION)} {digest.upper()}\r\n" + content
                log.write_bytes(content.encode("utf-8"))
                entry = {"id": kind, "status": "passed", "exitCode": 0, "detail": "Fixture",
                         "log": log.relative_to(repo).as_posix()}
                if kind in PLAYER_BACKENDS:
                    build_log = artifacts / f"{kind}-build.log"
                    build_log.write_text(f"SKECS_UNITY_BUILD_OK {PLAYER_BACKENDS[kind]}\n", encoding="utf-8")
                    entry["buildLog"] = build_log.relative_to(repo).as_posix()
                checks.append(entry)

            validate_checks({"checks": checks}, repo, VERSION, digest)
            wrong_log = artifacts / "migration-0.1.0-to-0.2.0.log"
            wrong_log.write_bytes(wrong_log.read_bytes().replace(digest.upper().encode(), b"b" * 64))
            with self.assertRaisesRegex(InvalidReport, "SHA-256 missing or mismatched: migration"):
                validate_checks({"checks": checks}, repo, VERSION, digest)

    def test_all_consumer_evidence_must_match_package_bytes(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            package = root / "ShortKedr.SKECS.0.2.0.nupkg"
            dll = b"synthetic test assembly"
            with zipfile.ZipFile(package, "w") as archive:
                archive.writestr("lib/netstandard2.1/SKECS.dll", dll)
            license_file = root / "LICENSE"
            guide = root / "INSTALL.md"
            license_file.write_bytes(b"test license")
            guide.write_bytes(b"test guide")
            entries = {"lib/netstandard2.1/SKECS.dll": dll,
                       "LICENSE": license_file.read_bytes(), "INSTALL.md": guide.read_bytes()}
            manifest = {"package": "ShortKedr.SKECS", "version": VERSION,
                        "sourceCommit": COMMIT, "framework": "netstandard2.1",
                        "assemblyReferences": ["netstandard"],
                        "files": {name: hashlib.sha256(value).hexdigest() for name, value in entries.items()}}
            provisional = root / "provisional.zip"
            with zipfile.ZipFile(provisional, "w") as archive:
                for name, value in entries.items():
                    archive.writestr(name, value)
                archive.writestr("manifest.json", json.dumps(manifest))
            sha = hashlib.sha256(package.read_bytes()).hexdigest()
            evidence = root / "evidence"
            evidence.mkdir()
            for kind in REQUIRED_EVIDENCE:
                runtime = ("Unity 2022.3.62f1 (Editor)" if kind == "unity-2022.3-Editor"
                           else "Unity 6000.0.60f1 (Editor)" if kind == "unity-6-Editor"
                           else "Unity 2022.3.62f1 (Mono)" if kind == "unity-2022.3-Mono"
                           else "Unity 2022.3.62f1 (IL2CPP)" if kind == "unity-2022.3-IL2CPP"
                           else "Unity 6000.0.60f1 (Mono)" if kind == "unity-6-Mono"
                           else "Unity 6000.0.60f1 (IL2CPP)" if kind == "unity-6-IL2CPP"
                           else "Microsoft.NETCore.App 8.0.1" if kind == "dotnet8"
                           else "Microsoft.NETCore.App 10.0.1" if kind == "dotnet10"
                           else ".NET Framework 4.8-compatible (registry release 528040)" if kind == "framework"
                           else "Godot 4.5.stable")
                (evidence / f"{kind}.json").write_text(json.dumps({
                    "kind": kind, "packageVersion": VERSION, "sourceCommit": COMMIT,
                    "packageSha256": sha, "runId": RUN_ID, "runtime": runtime,
                    "os": "Windows 11", "result": "passed",
                }), encoding="utf-8")

            self.assertEqual(len(load_unity_evidence(evidence, VERSION, COMMIT, RUN_ID, package)),
                             len(UNITY_KINDS))
            self.assertIn("| .NET 8 |", evidence_table(evidence, VERSION, COMMIT, RUN_ID, package))
            output = root / "final.zip"
            finalize(package, provisional, output, license_file, guide, evidence,
                     VERSION, COMMIT, RUN_ID)
            verify(package, output, license_file, guide, VERSION, COMMIT,
                   True, evidence, RUN_ID)

            package.write_bytes(b"different package with same name")
            with self.assertRaisesRegex(ValueError, "packageSha256"):
                load_unity_evidence(evidence, VERSION, COMMIT, RUN_ID, package)
            with self.assertRaisesRegex(ValueError, "packageSha256"):
                evidence_table(evidence, VERSION, COMMIT, RUN_ID, package)

    def test_local_gate_requires_the_final_zip_and_unchanged_fixture_evidence(self):
        with tempfile.TemporaryDirectory() as temporary:
            repo = Path(temporary)
            output = repo / "artifacts" / "release"
            output.mkdir(parents=True)
            package = output / "ShortKedr.SKECS.0.2.0.nupkg"
            unity_zip = output / "SKECS.Unity.0.2.0.zip"
            provisional = output / "provisional.zip"
            cover = output / "branch.png"
            cover.write_bytes(b"fixture cover")
            license_file = repo / "LICENSE"
            guide = repo / "docs/skecs/INSTALL.md"
            guide.parent.mkdir(parents=True)
            license_file.write_bytes(b"license")
            guide.write_bytes(b"guide")
            entries = {"lib/netstandard2.1/SKECS.dll": b"DLL", "LICENSE": b"license", "INSTALL.md": b"guide"}
            with zipfile.ZipFile(package, "w") as archive:
                archive.writestr("lib/netstandard2.1/SKECS.dll", entries["lib/netstandard2.1/SKECS.dll"])
                archive.writestr("ShortKedr.SKECS.nuspec",
                                 f'<package><metadata><id>ShortKedr.SKECS</id><version>{VERSION}</version>'
                                 f'<repository commit="{COMMIT}"/></metadata></package>')
            manifest = {"package": "ShortKedr.SKECS", "version": VERSION, "sourceCommit": COMMIT,
                        "framework": "netstandard2.1", "assemblyReferences": ["netstandard"],
                        "files": {name: hashlib.sha256(data).hexdigest() for name, data in entries.items()}}
            with zipfile.ZipFile(provisional, "w") as archive:
                for name, data in entries.items():
                    archive.writestr(name, data)
                archive.writestr("manifest.json", json.dumps(manifest))

            checks = []
            for kind in UNITY_KINDS:
                branch, backend = kind.removeprefix("unity-").rsplit("-", 1)
                editor = "2022.3.62f3" if branch == "2022.3" else "6000.3.19f1"
                os_suffix = "64bit Professional" if branch == "2022.3" else "Professional"
                fixture = repo / "artifacts/release-consumers" / kind
                plugin = fixture / "project/Assets/Plugins/SKECS.dll"
                plugin.parent.mkdir(parents=True)
                plugin.write_bytes(entries["lib/netstandard2.1/SKECS.dll"])
                editor_log = fixture / "editor.log"
                editor_marker = "SKECS_UNITY_EDITOR_OK" if backend == "Editor" else f"SKECS_UNITY_BUILD_OK {backend}"
                engine_header = f"Initialize engine version: {editor} (123456)\n"
                editor_log.write_text(f"OS: 'Windows 11  (10.0.26200) {os_suffix}' Language: 'ru'\n"
                                      f"{engine_header}{editor_marker}\n", encoding="utf-8")
                check = {"id": kind.lower(), "status": "passed", "exitCode": 0,
                         "log": editor_log.relative_to(repo).as_posix()}
                if backend != "Editor":
                    player_log = fixture / "player.log"
                    player_log.write_text(f"{engine_header}SKECS_UNITY_OK\n", encoding="utf-8")
                    check["buildLog"] = check["log"]
                    check["log"] = player_log.relative_to(repo).as_posix()
                checks.append(check)

            report = {"headCommit": COMMIT, "checks": checks}
            report_path = output / "verification-0.2.0.json"

            def check_artifacts():
                report["artifacts"] = [
                    {"file": path.name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
                    for path in (package, unity_zip, cover)
                ]
                validate_artifacts(report, repo, report_path, VERSION, COMMIT)

            unity_zip.write_bytes(provisional.read_bytes())
            with self.assertRaisesRegex(InvalidReport, "VERIFIED_UNITY"):
                check_artifacts()
            finalize(package, provisional, unity_zip, license_file, guide, None, VERSION, COMMIT, None,
                     local_report=report, repo=repo)
            check_artifacts()
            with zipfile.ZipFile(unity_zip) as archive:
                self.assertIn(b"local Windows smoke runs", archive.read("VERIFIED_UNITY.md"))
                self.assertNotIn(b"actions/runs", archive.read("VERIFIED_UNITY.md"))

            plugin.write_bytes(b"wrong DLL")
            with self.assertRaisesRegex(InvalidReport, "fixture SKECS.dll differs"):
                check_artifacts()
            plugin.write_bytes(entries["lib/netstandard2.1/SKECS.dll"])
            with player_log.open("ab") as log:
                log.write(b"new line after finalization\n")
            with self.assertRaisesRegex(InvalidReport, "manifest verification differs"):
                check_artifacts()


if __name__ == "__main__":
    unittest.main()
