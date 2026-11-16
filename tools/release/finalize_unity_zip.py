"""Add exact successful Unity Editor/backend results from hosted or local evidence."""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
import zipfile
from pathlib import Path

from unity_evidence import render_unity_matrix, verification_for
from verify_unity_zip import verify


def finalize(package: Path, provisional: Path, output: Path, license_file: Path,
             guide: Path, evidence_dir: Path | None, version: str, commit: str, run_id: str | None, *,
             local_report: dict | None = None, repo: Path | None = None) -> None:
    if provisional.resolve() == output.resolve():
        raise ValueError("Final ZIP output must differ from the tested provisional ZIP")
    # The input ZIP is exactly the binary that all six Unity jobs imported.
    verify(package, provisional, license_file, guide, version, commit)
    verification = verification_for(package, version, commit, evidence_dir, run_id, local_report, repo)
    with zipfile.ZipFile(provisional) as source:
        entries = {name: source.read(name) for name in ("lib/netstandard2.1/SKECS.dll", "LICENSE", "INSTALL.md")}
        manifest = json.loads(source.read("manifest.json"))
    entries["VERIFIED_UNITY.md"] = render_unity_matrix(version, commit, run_id, verification["unityChecks"])
    manifest["verification"] = verification
    manifest["files"] = {
        name: hashlib.sha256(contents).hexdigest()
        for name, contents in sorted(entries.items())
    }
    entries["manifest.json"] = (json.dumps(manifest, indent=2, sort_keys=True) + "\n").encode("utf-8")

    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, "w") as archive:
        for name, contents in sorted(entries.items()):
            info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(info, contents)
    verify(package, output, license_file, guide, version, commit, True, evidence_dir, run_id,
           local_report=local_report, repo=repo)
    print(f"PASS: finalized {output.name} with six exact Unity runtime results; DLL unchanged")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package", type=Path, required=True)
    parser.add_argument("--provisional", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--license", type=Path, default=Path("LICENSE"))
    parser.add_argument("--guide", type=Path, default=Path("docs/skecs/INSTALL.md"))
    parser.add_argument("--evidence-dir", type=Path)
    parser.add_argument("--version", required=True)
    parser.add_argument("--commit", required=True)
    parser.add_argument("--run-id")
    parser.add_argument("--local-report", type=Path, help="Draft verification JSON containing Unity check logs")
    parser.add_argument("--repo-root", type=Path, default=Path.cwd(), help="Checkout that contains the local Unity fixtures")
    args = parser.parse_args()
    try:
        local_report = None
        if args.local_report:
            local_report = json.loads(args.local_report.read_text(encoding="utf-8-sig"))
            if not isinstance(local_report, dict):
                raise ValueError("--local-report must contain a JSON object")
        finalize(args.package, args.provisional, args.output, args.license, args.guide,
                 args.evidence_dir, args.version, args.commit, args.run_id,
                 local_report=local_report, repo=args.repo_root)
    except (OSError, KeyError, ValueError, json.JSONDecodeError, zipfile.BadZipFile) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
