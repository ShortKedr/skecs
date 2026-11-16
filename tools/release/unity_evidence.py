"""Validate the six package-backed Unity consumer results for one release run."""

from __future__ import annotations

import json
import hashlib
import re
import zipfile
from pathlib import Path


UNITY_KINDS = tuple(
    f"unity-{branch}-{backend}"
    for branch in ("2022.3", "6")
    for backend in ("Editor", "Mono", "IL2CPP")
)
VERSION_PATTERN = {
    "2022.3": re.compile(r"2022\.3\.\d+[abcfpx]\d+"),
    "6": re.compile(r"6000\.\d+\.\d+[abcfpx]\d+"),
}


def load_unity_evidence(directory: Path, version: str, commit: str, run_id: str,
                        package: Path) -> list[dict[str, str]]:
    if not run_id.isdigit() or not re.fullmatch(r"[0-9a-fA-F]{40}", commit):
        raise ValueError("Unity evidence needs a numeric run ID and full source commit")
    package_sha = hashlib.sha256(package.read_bytes()).hexdigest()
    records = []
    for kind in UNITY_KINDS:
        path = directory / f"{kind}.json"
        if not path.is_file():
            raise ValueError(f"Missing verified Unity run: {path}")
        entry = json.loads(path.read_text(encoding="utf-8"))
        expected = {
            "kind": kind,
            "packageVersion": version,
            "sourceCommit": commit.lower(),
            "packageSha256": package_sha,
            "runId": run_id,
            "result": "passed",
        }
        for field, value in expected.items():
            if entry.get(field) != value:
                raise ValueError(f"{kind}: {field} must be {value!r}, got {entry.get(field)!r}")
        branch, backend = kind.removeprefix("unity-").rsplit("-", 1)
        runtime = entry.get("runtime", "")
        if not isinstance(runtime, str) or not runtime.startswith("Unity ") or not runtime.endswith(f" ({backend})"):
            raise ValueError(f"{kind}: runtime lacks exact Unity Editor/backend: {runtime!r}")
        editor_version = runtime.removeprefix("Unity ").removesuffix(f" ({backend})")
        if not VERSION_PATTERN[branch].fullmatch(editor_version):
            raise ValueError(f"{kind}: expected a full {branch} Editor patch, got {editor_version!r}")
        os_name = entry.get("os", "")
        if not isinstance(os_name, str) or not os_name.startswith("Windows ") or any(char in os_name for char in "|\n\r"):
            raise ValueError(f"{kind}: expected a single-line Windows runner OS")
        records.append({
            "branch": branch,
            "editorVersion": editor_version,
            "backend": backend,
            "os": os_name,
            "result": "passed",
        })
    return records


def load_local_unity_evidence(report: dict, repo: Path, commit: str, package: Path) -> list[dict[str, str]]:
    if not isinstance(report, dict) or report.get("headCommit") != commit.lower():
        raise ValueError("Local Unity report source commit differs from the requested commit")
    checks = report.get("checks")
    if not isinstance(checks, list) or not all(isinstance(check, dict) for check in checks):
        raise ValueError("Local Unity report needs a checks array")
    fixtures = (repo / "artifacts" / "release-consumers").resolve()
    if not fixtures.is_relative_to(repo.resolve()):
        raise ValueError("Unity fixtures escape the checkout")

    with zipfile.ZipFile(package) as archive:
        dll = archive.read("lib/netstandard2.1/SKECS.dll")
    records = []
    used = set()
    for kind in UNITY_KINDS:
        branch, backend = kind.removeprefix("unity-").rsplit("-", 1)
        matching = [check for check in checks if check.get("id") == kind.lower()]
        if len(matching) != 1:
            raise ValueError(f"{kind}: expected one successful Unity check")
        check = matching[0]
        exit_code = check.get("exitCode")
        if check.get("status") != "passed" or type(exit_code) is not int or exit_code != 0:
            raise ValueError(f"{kind}: Unity check did not pass")
        player = backend != "Editor"
        log = local_log(repo, fixtures, check.get("log"), "player.log" if player else "editor.log")
        editor_log = local_log(repo, fixtures, check.get("buildLog"), "editor.log") if player else log
        if editor_log.parent != log.parent or log.parent in used:
            raise ValueError(f"{kind}: expected a distinct fixture containing both logs")
        used.add(log.parent)

        plugin = (log.parent / "project" / "Assets" / "Plugins" / "SKECS.dll").resolve()
        if not plugin.is_relative_to(log.parent) or plugin.read_bytes() != dll:
            raise ValueError(f"{kind}: fixture SKECS.dll differs from the package DLL")
        editor_bytes = editor_log.read_bytes()
        editor_content = editor_bytes.decode("utf-8", errors="replace")
        version = engine_version(editor_content)
        os_match = re.search(r"^OS: '(Windows [^|'\r\n]+)'", editor_content, re.MULTILINE)
        if not VERSION_PATTERN[branch].fullmatch(version) or os_match is None:
            raise ValueError(f"{kind}: editor log lacks the exact {branch} release or Windows OS")
        editor_marker = "SKECS_UNITY_EDITOR_OK" if not player else f"SKECS_UNITY_BUILD_OK {backend}"
        if not has_marker(editor_content, editor_marker):
            raise ValueError(f"{kind}: editor log lacks {editor_marker}")
        record = {"branch": branch, "editorVersion": version, "backend": backend,
                  "os": os_match.group(1), "result": "passed"}
        if player:
            content = log.read_bytes()
            runtime = content.decode("utf-8", errors="replace")
            if engine_version(runtime) != version or not has_marker(runtime, "SKECS_UNITY_OK"):
                raise ValueError(f"{kind}: player log lacks a matching version or runtime success marker")
            record["buildLogSha256"] = hashlib.sha256(editor_bytes).hexdigest()
            record["logSha256"] = hashlib.sha256(content).hexdigest()
        else:
            record["logSha256"] = hashlib.sha256(editor_bytes).hexdigest()
        records.append(record)
    return records


def local_log(repo: Path, fixtures: Path, name: object, filename: str) -> Path:
    if not isinstance(name, str) or Path(name).is_absolute() or ".." in Path(name).parts:
        raise ValueError(f"Expected a relative {filename} path")
    path = (repo / name).resolve()
    if path.name != filename or path.parent.parent != fixtures or not path.parent.name.startswith("unity-"):
        raise ValueError(f"Expected {filename} from an isolated Unity release fixture: {name}")
    return path


def engine_version(log: str) -> str:
    match = re.search(r"^Initialize engine version: (\S+)", log, re.MULTILINE)
    return match.group(1) if match else ""


def has_marker(log: str, marker: str) -> bool:
    return any(line.strip() == marker for line in log.splitlines())


def verification_for(package: Path, version: str, commit: str, evidence_dir: Path | None,
                     run_id: str | None, local_report: dict | None, repo: Path | None) -> dict:
    result = {"packageSha256": hashlib.sha256(package.read_bytes()).hexdigest()}
    if local_report is not None:
        if evidence_dir is not None or run_id is not None or repo is None:
            raise ValueError("Local Unity evidence needs the report and checkout, without hosted evidence")
        result["source"] = "local"
        result["unityChecks"] = load_local_unity_evidence(local_report, repo, commit, package)
    else:
        if evidence_dir is None or run_id is None:
            raise ValueError("Finalized ZIP needs local Unity evidence or the hosted evidence directory and run ID")
        result["runId"] = run_id
        result["unityChecks"] = load_unity_evidence(evidence_dir, version, commit, run_id, package)
    return result


def render_unity_matrix(version: str, commit: str, run_id: str | None, records: list[dict[str, str]]) -> bytes:
    lines = [
        f"# Verified Unity consumers for SKECS {version}",
        "",
        "The SKECS.dll in this ZIP is byte-for-byte the netstandard2.1 assembly in the release NuGet package.",
        f"Source commit: `{commit.lower()}`.",
        (f"Evidence: https://github.com/ShortKedr/skecs/actions/runs/{run_id}." if run_id is not None else
         f"Evidence: local Windows smoke runs recorded in verification-{version}.json. Log SHA-256 digests are in manifest.json."),
        "",
        "| Unity branch | Exact Editor version | Backend exercised | Runner OS | Result |",
        "| --- | --- | --- | --- | --- |",
    ]
    for item in records:
        lines.append(
            f"| {item['branch']} | {item['editorVersion']} | {item['backend']} | {item['os']} | Passed |"
        )
    lines.extend(("", "Editor means an Editor smoke run; Mono and IL2CPP mean Windows players built and run.", ""))
    return "\n".join(lines).encode("utf-8")
