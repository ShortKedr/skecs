"""Verify a Unity ZIP against the exact NuGet package, source and runtime evidence."""

import argparse
import hashlib
import json
import sys
import zipfile
from pathlib import Path

from unity_evidence import render_unity_matrix, verification_for


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def verify(package: Path, unity_zip: Path, license_file: Path, guide: Path,
           version: str, commit: str, finalized: bool = False,
           evidence_dir: Path | None = None, run_id: str | None = None, *,
           local_report: dict | None = None, repo: Path | None = None) -> None:
    expected = {"lib/netstandard2.1/SKECS.dll", "LICENSE", "INSTALL.md", "manifest.json"}
    if finalized:
        expected.add("VERIFIED_UNITY.md")
    with zipfile.ZipFile(package) as source, zipfile.ZipFile(unity_zip) as archive:
        if source.testzip() or archive.testzip():
            raise ValueError("Package or Unity ZIP is corrupt")
        if set(archive.namelist()) != expected or len(archive.namelist()) != len(expected):
            raise ValueError(f"Unity ZIP must contain exactly {sorted(expected)}")
        dll = archive.read("lib/netstandard2.1/SKECS.dll")
        if dll != source.read("lib/netstandard2.1/SKECS.dll"):
            raise ValueError("Unity DLL differs from the package netstandard2.1 asset")
        if archive.read("LICENSE") != license_file.read_bytes():
            raise ValueError("Unity LICENSE differs from the repository")
        if archive.read("INSTALL.md") != guide.read_bytes():
            raise ValueError("Unity installation guide differs from the repository")
        manifest = json.loads(archive.read("manifest.json"))
        for key, value in (("package", "ShortKedr.SKECS"), ("version", version),
                           ("sourceCommit", commit.lower()), ("framework", "netstandard2.1")):
            if manifest.get(key) != value:
                raise ValueError(f"Unity manifest {key} mismatch")
        if manifest.get("files") != {name: sha(archive.read(name)) for name in expected - {"manifest.json"}}:
            raise ValueError("Unity manifest file checksums do not match ZIP contents")
        if manifest.get("assemblyReferences") != ["netstandard"]:
            raise ValueError(f"Unexpected Unity DLL assembly references: {manifest.get('assemblyReferences')}")
        if finalized:
            verification = verification_for(package, version, commit, evidence_dir, run_id, local_report, repo)
            if manifest.get("verification") != verification:
                raise ValueError("Finalized Unity manifest verification differs from release evidence")
            expected_matrix = render_unity_matrix(version, commit, run_id, verification["unityChecks"])
            if archive.read("VERIFIED_UNITY.md") != expected_matrix:
                raise ValueError("Verified Unity matrix differs from release evidence")
        elif "verification" in manifest:
            raise ValueError("Provisional Unity ZIP cannot claim runtime verification")
    print(f"PASS: {unity_zip.name}: source and manifest match {package.name}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package", type=Path, required=True)
    parser.add_argument("--unity-zip", type=Path, required=True)
    parser.add_argument("--license", type=Path, default=Path("LICENSE"))
    parser.add_argument("--guide", type=Path, default=Path("docs/skecs/INSTALL.md"))
    parser.add_argument("--version", required=True)
    parser.add_argument("--commit", required=True)
    parser.add_argument("--final", action="store_true")
    parser.add_argument("--evidence-dir", type=Path)
    parser.add_argument("--run-id")
    parser.add_argument("--local-report", type=Path, help="Local verification JSON containing Unity check logs")
    parser.add_argument("--repo-root", type=Path, default=Path.cwd(), help="Checkout that contains the local Unity fixtures")
    args = parser.parse_args()
    try:
        local_report = None
        if args.local_report:
            local_report = json.loads(args.local_report.read_text(encoding="utf-8-sig"))
            if not args.final or not isinstance(local_report, dict):
                raise ValueError("--local-report requires --final and a JSON object")
        verify(args.package, args.unity_zip, args.license, args.guide,
               args.version, args.commit, args.final, args.evidence_dir, args.run_id,
               local_report=local_report, repo=args.repo_root)
    except (OSError, KeyError, ValueError, zipfile.BadZipFile, json.JSONDecodeError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
