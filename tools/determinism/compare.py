#!/usr/bin/env python3
"""Validate replay artifacts and compare every step across processes and runners."""

import argparse
from contextlib import ExitStack
from hashlib import sha256
import json
from pathlib import Path
import re
import sys
from xml.etree import ElementTree


RUNNERS = {
    "ubuntu-24.04": ("Linux", "X64"),
    "windows-2022": ("Windows", "X64"),
    "ubuntu-24.04-arm": ("Linux", "Arm64"),
    "macos-15": ("macOS", "Arm64"),
}
TARGETS = {
    f"{runner}-{configuration}": (os, architecture, configuration)
    for runner, (os, architecture) in RUNNERS.items()
    for configuration in ("Debug", "Release")
}
TEST_REPORTS = ("skecs.trx", "determinism.trx")
TRACE_FIELDS = {"step", "operation", "result", "visits", "entities", "hash"}
ENTITY_FIELDS = {"key", "id", "version", "transform", "motion", "marker"}
GOLDEN_FIELDS = ("result", "visits", "entities")
COMPONENT_FIELDS = {"transform": {"x", "y"}, "motion": {"dx", "dy"}, "marker": {"kind", "value"}}
FORMAT_VERSION = 1


def require(condition, message):
    if not condition:
        raise ValueError(message)


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, f"duplicate JSON property {key!r}")
        result[key] = value
    return result


def invalid_constant(value):
    raise ValueError(f"invalid JSON number {value}")


def parse_json(source, context):
    try:
        return json.loads(source, object_pairs_hook=unique_object, parse_constant=invalid_constant)
    except ValueError as error:
        raise ValueError(f"{context}: {error}") from error


def read_json(path):
    return parse_json(path.read_text(encoding="utf-8"), path)


def check_inventory(directory, *, directories=(), files=()):
    expected = set(directories) | set(files)
    actual = {entry.name for entry in directory.iterdir()}
    require(actual == expected, f"{directory}: missing {sorted(expected - actual)}, "
                               f"unexpected {sorted(actual - expected)}")
    for name in directories:
        path = directory / name
        require(path.is_dir() and not path.is_symlink(), f"{path}: expected a directory")
    for name in files:
        path = directory / name
        require(path.is_file() and not path.is_symlink() and path.stat().st_size > 0,
                f"{path}: expected a nonempty file")


def fixture_contract(directory):
    paths = sorted(directory.iterdir())
    require(paths, f"{directory}: no fixtures")
    fixtures = {}
    for path in paths:
        require(path.is_file() and not path.is_symlink() and path.suffix == ".json"
                and re.fullmatch(r"[a-z0-9][a-z0-9-]*", path.stem), f"{path}: unexpected fixture path")
        source = path.read_bytes()
        fixture = parse_json(source.decode("utf-8"), path)
        require(isinstance(fixture, dict) and type(fixture.get("formatVersion")) is int
                and fixture["formatVersion"] == FORMAT_VERSION, f"{path}: invalid fixture format")
        operations = fixture.get("operations")
        require(isinstance(operations, list), f"{path}: missing operations")
        kinds = ["initial"]
        for step, operation in enumerate(operations, 1):
            require(isinstance(operation, dict) and isinstance(operation.get("kind"), str)
                    and operation["kind"], f"{path}: step {step} has no operation kind")
            kinds.append(operation["kind"])
        fixtures[path.stem] = (sha256(source).hexdigest(), kinds)
    return fixtures


def golden_contract(source, path, fixtures):
    golden = parse_json(source.decode("utf-8"), path)
    require(isinstance(golden, dict) and type(golden.get("formatVersion")) is int
            and golden["formatVersion"] == FORMAT_VERSION, f"{path}: invalid golden format")
    cases = golden.get("fixtures")
    require(isinstance(cases, dict) and cases, f"{path}: no golden fixtures")
    for name, rows in cases.items():
        require(name in fixtures and isinstance(rows, list) and len(rows) == len(fixtures[name][1]),
                f"{path}: golden fixture {name!r} has no matching input or wrong row count")
        for index, row in enumerate(rows):
            require(isinstance(row, dict) and row.keys() == set(GOLDEN_FIELDS),
                    f"{path}: {name} step {index}: invalid golden fields")
    return cases


def check_test_report(path):
    try:
        summary = ElementTree.parse(path).getroot().find("{*}ResultSummary")
        counters = summary.find("{*}Counters") if summary is not None else None
        executed = counters.get("executed", "") if counters is not None else ""
        require(summary is not None and summary.get("outcome") in ("Completed", "Passed")
                and re.fullmatch(r"[1-9][0-9]*", executed)
                and counters.get("passed") == executed and counters.get("failed") == "0"
                and counters.get("error") == "0", f"{path}: no successful test result")
    except ElementTree.ParseError as error:
        raise ValueError(f"{path}: invalid test report: {error}") from error


def check_manifest(directory, metadata, fixtures):
    path = directory / "manifest.json"
    manifest = read_json(path)
    require(isinstance(manifest, dict), f"{path}: expected an object")
    for field, expected in metadata.items():
        actual = manifest.get(field)
        require(type(actual) is type(expected) and actual == expected,
                f"{path}: {field} expected {expected!r}, got {actual!r}")
    jit = manifest.get("jit")
    require(isinstance(jit, dict) and all(isinstance(value, str) for value in jit.values()),
            f"{path}: invalid or missing JIT metadata")
    items = manifest.get("fixtures")
    require(isinstance(items, list), f"{path}: missing fixture inventory")
    reported = set()
    for item in items:
        require(isinstance(item, dict) and isinstance(item.get("name"), str),
                f"{path}: invalid fixture entry")
        name = item["name"]
        require(name in fixtures and name not in reported, f"{path}: unexpected or duplicate fixture {name!r}")
        digest, kinds = fixtures[name]
        actual_digest = item.get("inputSha256")
        require(isinstance(actual_digest, str) and actual_digest.lower() == digest,
                f"{path}: {name} input digest expected {digest}, got {actual_digest!r}")
        steps = item.get("steps")
        require(type(steps) is int and steps == len(kinds) - 1,
                f"{path}: {name} expected {len(kinds) - 1} steps, got {steps!r}")
        reported.add(name)
    require(reported == fixtures.keys(), f"{path}: missing fixtures {sorted(fixtures.keys() - reported)}")
    return jit


def check_artifacts(root, targets, fixtures_dir, golden_path, commit, sdk, runtime):
    fixtures = fixture_contract(fixtures_dir)
    golden = golden_path.read_bytes()
    golden_cases = golden_contract(golden, golden_path, fixtures)
    check_inventory(root, directories=[f"determinism-{target}" for target in targets])
    runs = []
    reference_jit = None
    for target in targets:
        artifact = root / f"determinism-{target}"
        check_inventory(artifact, directories=("first", "second", "inputs", "tests"),
                        files=("golden.json", "dotnet-info.txt"))
        check_inventory(artifact / "inputs", files=[f"{name}.json" for name in fixtures])
        for name, (digest, _) in fixtures.items():
            path = artifact / "inputs" / f"{name}.json"
            require(sha256(path.read_bytes()).hexdigest() == digest, f"{path}: inputs differ from checkout")
        require((artifact / "golden.json").read_bytes() == golden, f"{artifact}: golden differs from checkout")
        check_inventory(artifact / "tests", files=TEST_REPORTS)
        for report in TEST_REPORTS:
            check_test_report(artifact / "tests" / report)
        os, architecture, configuration = TARGETS[target]
        metadata = dict(formatVersion=FORMAT_VERSION, os=os, architecture=architecture,
                        configuration=configuration, commit=commit, sdk=sdk, runtime=runtime)
        for repeat in ("first", "second"):
            run = artifact / repeat
            check_inventory(run, files=["manifest.json", *(f"{name}.jsonl" for name in fixtures)])
            jit = check_manifest(run, metadata, fixtures)
            if reference_jit is None:
                reference_jit = jit
            require(jit == reference_jit, f"{run}: JIT flags expected {reference_jit!r}, got {jit!r}")
            runs.append(run)
    for name, (_, kinds) in fixtures.items():
        compare_fixture(runs, name, kinds, golden_path, golden_cases.get(name))
    return len(runs), len(fixtures)


def is_int32(value):
    return type(value) is int and -(2 ** 31) <= value < 2 ** 31


def trace_row(line, path, step, kind):
    context = f"{path}: step {step} ({kind})"
    require(line.endswith("\n"), f"{context}: missing or incomplete row")
    row = parse_json(line, context)
    require(isinstance(row, dict) and row.keys() == TRACE_FIELDS, f"{context}: invalid trace fields")
    require(type(row["step"]) is int and row["step"] == step and row["operation"] == kind,
            f"{context}: got step {row['step']!r}, operation {row['operation']!r}")
    for field in ("result", "visits"):
        require(isinstance(row[field], list) and all(isinstance(value, str) for value in row[field]),
                f"{context}: invalid {field}")
    require(isinstance(row["hash"], str) and re.fullmatch(r"[0-9a-fA-F]{64}", row["hash"]),
            f"{context}: invalid snapshot hash")
    require(isinstance(row["entities"], list), f"{context}: invalid entities")
    keys = set()
    ids = set()
    for entity in row["entities"]:
        require(isinstance(entity, dict) and entity.keys() == ENTITY_FIELDS, f"{context}: invalid entity fields")
        key, entity_id, version = (entity[field] for field in ("key", "id", "version"))
        require(all(is_int32(value) for value in (key, entity_id, version))
                and entity_id >= 0 and version > 0 and key not in keys and entity_id not in ids,
                f"{context}: invalid or duplicate identity {key!r} (ID {entity_id!r}, version {version!r})")
        keys.add(key)
        ids.add(entity_id)
        for component, field_names in COMPONENT_FIELDS.items():
            fields = entity[component]
            require(fields is None or (isinstance(fields, dict) and fields
                                      and fields.keys() == field_names and all(is_int32(value) for value in fields.values())
                                      and (component != "marker" or 0 <= fields["kind"] <= 255)),
                    f"{context}: invalid component fields for entity {key} in {component}")
    return row


def first_difference(expected, actual, path):
    if type(expected) is type(actual):
        if isinstance(expected, dict):
            for key in sorted(expected.keys() | actual.keys()):
                field = f"{path}.{key}"
                if key not in expected:
                    return f"{field}: unexpected {actual[key]!r}"
                if key not in actual:
                    return f"{field}: missing (expected {expected[key]!r})"
                difference = first_difference(expected[key], actual[key], field)
                if difference:
                    return difference
            return None
        if isinstance(expected, list):
            for index, (left, right) in enumerate(zip(expected, actual)):
                label = f"key={left['key']}" if path == "entities" and isinstance(left, dict) and "key" in left else index
                difference = first_difference(left, right, f"{path}[{label}]")
                if difference:
                    return difference
            if len(expected) != len(actual):
                index = min(len(expected), len(actual))
                if len(expected) > index:
                    return f"{path}[{index}]: missing (expected {expected[index]!r})"
                return f"{path}[{index}]: unexpected {actual[index]!r}"
            return None
        if expected == actual:
            return None
    return f"{path}: expected {expected!r}, got {actual!r}"


def compare_fixture(runs, name, kinds, golden_path, golden):
    paths = [run / f"{name}.jsonl" for run in runs]
    with ExitStack() as stack:
        streams = [stack.enter_context(path.open(encoding="utf-8")) for path in paths]
        for step, kind in enumerate(kinds):
            expected = trace_row(streams[0].readline(), paths[0], step, kind)
            if golden is not None:
                for field in GOLDEN_FIELDS:
                    difference = first_difference(golden[step][field], expected[field], field)
                    if difference:
                        raise ValueError(f"{paths[0]}: golden mismatch at step {step} ({kind}): {difference}\n"
                                         f"Reference {golden_path}: {json.dumps(golden[step])}\n"
                                         f"Actual {paths[0]}: {json.dumps(expected)}")
            for path, stream in zip(paths[1:], streams[1:]):
                actual = trace_row(stream.readline(), path, step, kind)
                for field in (*GOLDEN_FIELDS, "hash"):
                    if expected[field] != actual[field]:
                        diff = first_difference(expected[field], actual[field], field)
                        raise ValueError(f"{path}: first mismatch at step {step} ({kind}): {diff}\n"
                                         f"Reference {paths[0]}: {json.dumps(expected)}\n"
                                         f"Actual {path}: {json.dumps(actual)}")
        for path, stream in zip(paths, streams):
            require(not stream.readline(), f"{path}: unexpected row after step {len(kinds) - 1}")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("artifacts", type=Path, help="directory containing the producer artifact directories")
    parser.add_argument("--fixtures", type=Path, default=Path(__file__).resolve().parent / "fixtures")
    parser.add_argument("--golden", type=Path, help="golden reference (defaults to golden.json beside the fixtures directory)")
    parser.add_argument("--commit", required=True)
    parser.add_argument("--sdk", required=True)
    parser.add_argument("--runtime", required=True)
    parser.add_argument("--target", action="append", choices=TARGETS,
                        help="restrict local comparison to a target; defaults to the complete matrix")
    parser.add_argument("--producer-result", default="success", help="aggregate result of the CI producer matrix")
    args = parser.parse_args(argv)
    targets = args.target or TARGETS
    try:
        require(len(set(targets)) == len(targets), "duplicate expected target")
        golden = args.golden or args.fixtures.parent / "golden.json"
        runs, fixtures = check_artifacts(args.artifacts, targets, args.fixtures, golden,
                                        args.commit, args.sdk, args.runtime)
        require(args.producer_result == "success", f"producer matrix result: {args.producer_result!r}")
    except (OSError, ValueError) as error:
        print(f"Determinism comparison failed: {error}", file=sys.stderr)
        return 1
    print(f"Compared {fixtures} fixtures across {runs} independent runs from {len(targets)} targets.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
