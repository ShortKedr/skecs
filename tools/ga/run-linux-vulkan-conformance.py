#!/usr/bin/env python3
"""Run SG01–SG09 with locally built Vulkan bridge and portable .NET fixture assemblies."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import sys


FIXTURES = [
    ("movement", "MovementConformance", "boundaries", 48_030, 6_500_000),
    ("target-read", "TargetReadConformance", "matrix", 64_040, 6_500_000),
    ("thermal-inventory", "ThermalInventoryConformance", "matrix", 128_080, 6_500_000),
    ("shot-burst-capacity", "ShotBurstConformance", "matrix", 96_060, 13_000_000),
    ("multi-hit-damage", "MultiHitConformance", "matrix", 48_030, 6_500_000),
    ("carrier-capacity", "CarrierCapacityConformance", "boundaries", 48_300, 1_700_000),
    ("spatial-parent", "SpatialParentConformance", "matrix", 64_040, 6_500_000),
    ("service-continuation", "ServiceContinuationConformance", None, 111, 56_832),
    ("host-lifecycle", "HostLifecycleConformance", None, 588, 150_528),
]


def digest(file):
    return hashlib.sha256(file.read_bytes()).hexdigest()


def run_logged(args, log, environment, workdir):
    with log.open("wb") as stream:
        result = subprocess.run(args, cwd=workdir, env=environment,
                                stdout=stream, stderr=subprocess.STDOUT, check=False)
    text = log.read_text(encoding="utf-8", errors="replace")
    if result.returncode or re.search(r"Validation Error|\bVUID-", text):
        raise RuntimeError(f"Command failed or reported a Vulkan validation error. See {log}")
    return text


def require_validation(text, enabled, log):
    if enabled and not re.search(r"Insert instance layer.*VK_LAYER_KHRONOS_validation", text):
        raise RuntimeError(f"Khronos validation was requested but was not loaded. See {log}")


def find_inputs(artifacts, assemblies):
    inputs = []
    for name, project, profile, short_rows, long_rows in FIXTURES:
        matches = sorted((artifacts / name / "bundles").glob(f"*/{name}.spv"))
        if len(matches) != 1:
            raise ValueError(f"Expected one Vulkan artifact for {name} in {artifacts}")
        shader = matches[0]
        stem = shader.with_suffix("")
        manifest_path = stem.with_suffix(".manifest.json")
        content = stem.with_suffix(".content-id").read_text().strip()
        manifest = json.loads(manifest_path.read_bytes())
        if (digest(manifest_path) != content or manifest["Contract"]["SystemId"] != name
                or manifest["Toolchain"]["TargetProfile"] != "spirv_1_3"
                or manifest["Toolchain"]["Format"] != 0 or digest(shader) != manifest["ArtifactDigest"]
                or digest(stem.with_suffix(".layout")) != manifest["ReflectionDigest"]
                or digest(stem.with_suffix(".source-map.json")) != manifest["SourceMapDigest"]):
            raise ValueError(f"Vulkan provenance or payload mismatch for {name}")
        assembly = (assemblies / project / "release" / f"{project}.dll").resolve(strict=True)
        inputs.append((name, profile, short_rows, long_rows, shader, assembly, content, manifest))
    return inputs


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artifacts", type=Path, required=True, help="Root containing <system>/bundles/<bundle>")
    parser.add_argument("--assemblies", type=Path, required=True, help="MSBuild artifacts bin directory")
    parser.add_argument("--native-dir", type=Path, required=True, help="CMake output with native probe and bridge")
    parser.add_argument("--startup", type=Path, required=True, help="SPIR-V used by the native binding test")
    parser.add_argument("--startup-sha256", required=True)
    parser.add_argument("--output", type=Path, required=True, help="New evidence directory")
    parser.add_argument("--dotnet", type=Path, help="Use a local .NET executable")
    parser.add_argument("--device-index", type=int, default=0)
    parser.add_argument("--validation", action="store_true", help="Require VK_LAYER_KHRONOS_validation")
    parser.add_argument("--software", action="store_true", help="Diagnostic CPU Vulkan lane")
    parser.add_argument("--endurance", action="store_true")
    args = parser.parse_args()

    if sys.platform != "linux" or not 0 <= args.device_index <= 0xffffffff:
        parser.error("Use this command on Linux with a nonnegative Vulkan device index.")
    host = args.dotnet or shutil.which("dotnet")
    if not host:
        parser.error("Set --dotnet or add the .NET executable to PATH.")
    dotnet = Path(host).resolve(strict=True)
    startup = args.startup.resolve(strict=True)
    if digest(startup) != args.startup_sha256.lower():
        parser.error("The startup SPIR-V does not match its expected SHA-256.")
    probe = (args.native_dir / "ga-native-probe").resolve(strict=True)
    bridge = (args.native_dir / "libga-vulkan-native.so").resolve(strict=True)
    inputs = find_inputs(args.artifacts, args.assemblies)
    output = args.output.resolve()
    output.mkdir()
    workdir = output / "temp"
    workdir.mkdir()

    environment = os.environ.copy()
    for key in ("TMPDIR", "XDG_CACHE_HOME", "XDG_CONFIG_HOME", "DOTNET_CLI_HOME"):
        environment[key] = str(workdir)
    environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    environment["DOTNET_NOLOGO"] = "1"
    if args.validation:
        environment["VK_INSTANCE_LAYERS"] = "VK_LAYER_KHRONOS_validation"
        environment["VK_LOADER_DEBUG"] = "layer"

    native_args = [probe, startup, "--device-index", str(args.device_index)]
    lane = "software" if args.software else "physical"
    if args.software:
        native_args.append("--software")
    startup_log = output / "startup.txt"
    result = run_logged(native_args, startup_log, environment, workdir)
    require_validation(result, args.validation, startup_log)
    identity = re.search(
        rf"deviceType={lane} deviceIndex={args.device_index} vkDeviceType=([0-9]+) "
        r"pci=([0-9a-f]+:[0-9a-f]+) uuid=([0-9a-f]{32}) "
        r"apiVersionRaw=(0x[0-9a-f]{8}) driverVersionRaw=(0x[0-9a-f]{8})", result)
    if not identity or int(identity[1]) not in ({4} if args.software else {1, 2}):
        raise RuntimeError(f"Native binding and readback did not attest the selected device. See {startup_log}")

    runtime_log = output / "dotnet-runtimes.txt"
    run_logged([dotnet, "--list-runtimes"], runtime_log, environment, workdir)
    fixtures = []
    for name, profile, short_rows, long_rows, shader, assembly, content, manifest in inputs:
        if args.endurance:
            profile = "long" if profile else "endurance"
        if name == "movement":
            cpu_log = output / "movement-cpu.txt"
            cpu = run_logged([dotnet, assembly, "cpu", "boundaries"], cpu_log, environment, workdir)
            if not re.search(r"deviceRows=0\b.*exactQ32_32=passed allocator=passed", cpu):
                raise RuntimeError(f"The CPU control did not report exact Movement parity. See {cpu_log}")

        command = [dotnet, assembly, "vulkan", shader.parent.resolve(), bridge, str(args.device_index)]
        if profile:
            command.append(profile)
        log = output / f"{name}.txt"
        result = run_logged(command, log, environment, workdir)
        require_validation(result, args.validation, log)
        rows = long_rows if args.endurance else short_rows
        managed_identity = f"({identity[2]}, {identity[3]})"
        identities = [line for line in result.splitlines() if
                      "api=Vulkan" in line and managed_identity in line
                      and re.search(rf"\b(contentId|artifact)={content}\b", line)]
        if not identities or not re.search(rf"\bdeviceRows={rows}\b", result):
            raise RuntimeError(f"The managed fixture lost device, artifact or row count evidence. See {log}")
        fixtures.append({"id": name, "profile": profile, "rows": rows, "device": identities[0],
                         "contentId": content, "contract": manifest["Contract"], "toolchain": manifest["Toolchain"],
                         "artifactSha256": digest(shader), "assemblySha256": digest(assembly), "traceSha256": digest(log)})
        print(f"{name} {lane} Vulkan passed {rows} rows", flush=True)

    receipt = {"system": platform.platform(), "architecture": platform.machine(),
               "wsl": "microsoft" in platform.release().lower(), "lane": lane, "deviceIndex": args.device_index,
               "pci": identity[2], "uuid": identity[3], "apiVersionRaw": identity[4], "driverVersionRaw": identity[5],
               "validationRequired": args.validation, "bridgeSha256": digest(bridge), "probeSha256": digest(probe),
               "startupSha256": digest(startup), "startupTraceSha256": digest(startup_log),
               "dotNetRuntimeInventorySha256": digest(runtime_log), "movementCpuTraceSha256": digest(cpu_log),
               "fixtures": fixtures}
    (output / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    try:
        main()
    except (OSError, RuntimeError, ValueError) as error:
        sys.exit(str(error))
