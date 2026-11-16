"""Pair the existing Churn dense workloads across two local .NET 8 SKECS.dll builds.

Usage: python run_legacy_pairs.py BASELINE_DLL CANDIDATE_DLL OUTPUT [--nuget-source PATH] [--logical-cpu N] [--smoke]
Use Python 3.10+ with the .NET 8 targeting packs available locally.
BASELINE_DLL must support Churn and precede placement. CANDIDATE_DLL must support placement.
OUTPUT is a new directory inside this project. The smoke run checks comparability only.
"""

import argparse
from collections import defaultdict
import hashlib
import json
import math
import os
from pathlib import Path
import shutil
import statistics
import subprocess


WORKSPACE = Path(__file__).resolve().parents[3]
CHURN_BENCHMARK = WORKSPACE / "tools" / "ChurnControlBenchmarks"
HARNESS = "ChurnControlBenchmarks"
SCENARIOS = ("get", "for-each", "chunks")
GROUPS = (
    {"name": "control", "firstSeed": 1973, "chunkCapacity": 256, "entities": 8192},
    {"name": "holdout", "firstSeed": 8191, "chunkCapacity": 128, "entities": 12288},
)
BUDGET = "16777216"
MODES = (
    ("baseline-off", "baseline", "off"),
    ("candidate-off", "candidate", "off"),
    ("baseline-churn", "baseline", BUDGET),
    ("candidate-churn", "candidate", BUDGET),
    ("candidate-churn+allowed-empty", "candidate", BUDGET),
)
COMPARISONS = (
    ("baseline-off", "candidate-off"),
    ("baseline-churn", "candidate-churn"),
    ("baseline-churn", "candidate-churn+allowed-empty"),
    ("candidate-churn", "candidate-churn+allowed-empty"),
)
GATES = {"P50": 1.03, "P99": 1.10}
PAIR_FIELDS = (
    "Scenario", "TargetRetainedBytes", "Runtime", "Os", "Architecture", "Cpu", "LogicalProcessors",
    "ServerGc", "Frequency", "EntitiesPerGroup", "ChunkCapacity", "Seed", "WarmupFrames",
    "SweepsPerFrame", "AffinitizedLogicalCpu", "InitialChecksum", "AfterWarmupChecksum",
)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def invoke(args, environment):
    result = subprocess.run(args, capture_output=True, text=True, env=environment)
    if result.returncode:
        raise RuntimeError(f"{args} returned {result.returncode}\n{result.stdout}\n{result.stderr}")
    return result.stdout


def prepare_harness(output, cores, package_source, environment):
    source = output / "harness"
    source.mkdir()
    program = (CHURN_BENCHMARK / "Program.cs").read_text(encoding="utf-8-sig")

    def replace(old, new):
        nonlocal program
        if program.count(old) != 1:
            raise RuntimeError(f"Churn benchmark changed at {old!r}")
        program = program.replace(old, new)

    replace("var workload = new Workload(scenario, entities, seed);", """var placementType = typeof(World).Assembly.GetType("SKECS.ComponentPlacementOptions");
if ((placementType is not null) != args[7].StartsWith("candidate-", StringComparison.Ordinal))
    throw new InvalidOperationException("Core lacks the requested placement capabilities for this arm.");
if (args[7].EndsWith("+allowed-empty", StringComparison.Ordinal))
{
    var positionType = typeof(Workload).GetNestedType("Position", System.Reflection.BindingFlags.NonPublic)!;
    var placement = placementType!.GetMethod("AllowAdaptiveStorage")!.MakeGenericMethod(positionType)
        .Invoke(Activator.CreateInstance(placementType), null)!;
    placement = placementType.GetMethod("WithPackedPools")!.Invoke(placement, [Type.EmptyTypes])!;
    options = (WorldOptions)typeof(WorldOptions).GetMethod("WithComponentPlacement")!.Invoke(options, [placement])!;
}
var logicalCpu = Environment.GetEnvironmentVariable("SKECS_PAIR_LOGICAL_CPU");
if (logicalCpu != null && OperatingSystem.IsWindows())
    using (var process = Process.GetCurrentProcess())
        process.ProcessorAffinity = (nint)(1L << int.Parse(logicalCpu, CultureInfo.InvariantCulture));
var workload = new Workload(scenario, entities, seed);""")
    replace("    var phase = workload.Step(world, frame);", """    var phase = workload.Step(world, frame);
    for (var sweep = 1; sweep < 4; sweep++) workload.Step(world, frame);""")
    replace("    Stopwatch.Frequency,", """    Stopwatch.Frequency,
    SweepsPerFrame = 4,
    AffinitizedLogicalCpu = logicalCpu,""")
    (source / "Program.cs").write_text(program, encoding="utf-8")
    for name in ("Workload.cs", f"{HARNESS}.csproj"):
        shutil.copyfile(CHURN_BENCHMARK / name, source / name)

    if not package_source.is_dir():
        package_source = output / "offline-source"
        package_source.mkdir()
    build = source / "built"
    invoke(["dotnet", "build", str(source / f"{HARNESS}.csproj"), "-c", "Release", "-o", str(build),
            "--source", str(package_source), "-p:NuGetAudit=false", "-m:1", "-nr:false",
            "-p:UseSharedCompilation=false", f"-p:SKECSCoreAssembly={cores['baseline']}"], environment)

    runners = {}
    for side, core in cores.items():
        run_dir = output / side
        shutil.copytree(build, run_dir)
        shutil.copyfile(core, run_dir / "SKECS.dll")
        runners[side] = run_dir / f"{HARNESS}.dll"
    return runners


def compare(baseline, candidate, frames):
    for field in PAIR_FIELDS:
        if baseline[field] != candidate[field]:
            raise RuntimeError(f"Pair differs in {field}")
    if any(len(run["Samples"]) != frames for run in (baseline, candidate)):
        raise RuntimeError("Pair has an unexpected measured frame count")
    for offset, (left, right) in enumerate(zip(baseline["Samples"], candidate["Samples"], strict=True)):
        frame = baseline["WarmupFrames"] + offset
        for field in ("Phase", "Checksum", "ManagedBytes"):
            if left[field] != right[field]:
                raise RuntimeError(f"Pair differs in {field} at frame {frame}")
        if left["Memory"]["Entities"] != right["Memory"]["Entities"]:
            raise RuntimeError(f"Pair differs in entity count at frame {frame}")
        if any(sample["FrameTicks"] < sample["TrimTicks"] for sample in (left, right)):
            raise RuntimeError(f"Invalid trim timing at frame {frame}")


def work_percentile(run, fraction):
    work_ticks = sorted(sample["FrameTicks"] - sample["TrimTicks"] for sample in run["Samples"])
    return work_ticks[math.ceil(len(work_ticks) * fraction) - 1]


def measure_group(plan, group, runners, output, environment):
    rounds, frames, warmup = (plan[key] for key in ("rounds", "frames", "warmup"))
    observations = defaultdict(list)
    for round_index in range(rounds):
        shift = round_index % len(MODES)
        sequence = MODES[shift:] + MODES[:shift]
        if round_index % 2:
            sequence = tuple(reversed(sequence))
        shift = round_index % len(SCENARIOS)
        for scenario in SCENARIOS[shift:] + SCENARIOS[:shift]:
            results = {}
            for key, side, target in sequence:
                invocation = ["dotnet", str(runners[side]), scenario, target, str(frames), str(warmup),
                              str(group["entities"]), str(group["chunkCapacity"]),
                              str(group["firstSeed"] + round_index), key]
                raw = invoke(invocation, environment)
                (output / f"{group['name']}-{round_index:02}-{scenario}-{key}.json").write_text(raw, encoding="utf-8")
                result = json.loads(raw)
                if result["CoreAssemblySha256"].lower() != plan["coreSha256"][side] or result["BuildId"] != key:
                    raise RuntimeError(f"Wrong benchmark assembly or arm for {key}")
                results[key] = result
            for left, right in COMPARISONS:
                compare(results[left], results[right], frames)
                metrics = {}
                if not plan["smoke"]:
                    for key, fraction in (("P50", .50), ("P99", .99)):
                        baseline = work_percentile(results[left], fraction)
                        if baseline == 0:
                            raise RuntimeError(f"Zero baseline timing for {scenario} {left} {key}")
                        metrics[key] = work_percentile(results[right], fraction) / baseline
                observations[(scenario, left, right)].append(metrics)
        print(f"Verified {group['name']} paired block {round_index + 1}/{rounds}", flush=True)

    comparisons = []
    for (scenario, baseline, candidate), runs in sorted(observations.items()):
        metrics = {}
        if not plan["smoke"]:
            for key, gate in GATES.items():
                values = sorted(sample[key] for sample in runs)
                # With 12 independent blocks the 10th order statistic is a 98.1% one-sided bound on the median.
                metrics[key] = {"median": statistics.median(values), "upper": values[9], "gate": gate,
                                "passes": values[9] <= gate, "ratios": values}
        comparisons.append({"group": group["name"], "scenario": scenario, "baseline": baseline, "candidate": candidate,
                            "verifiedMeasuredFrames": frames * rounds, "metrics": metrics})
    return comparisons


def run_suite(args, cores, output, environment):
    runners = prepare_harness(output, cores, args.nuget_source, environment)
    rounds, frames, warmup = (1, 8, 32) if args.smoke else (12, 256, 192)
    groups = [dict(group, entities=2048) for group in GROUPS] if args.smoke else GROUPS
    plan = {"smoke": args.smoke, "coreSha256": {side: digest(path) for side, path in cores.items()},
            "harnessSha256": digest(runners["baseline"]), "tieredCompilation": False,
            "rounds": rounds, "frames": frames, "warmup": warmup, "logicalCpu": args.logical_cpu,
            "groups": groups, "scenarios": SCENARIOS, "modes": MODES, "gates": GATES}
    (output / "plan.json").write_text(json.dumps(plan, indent=2) + "\n", encoding="utf-8")
    comparisons = [item for group in groups for item in measure_group(plan, group, runners, output, environment)]
    summary = dict(plan, comparisons=comparisons)
    (output / "summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print(f"Saved {len(comparisons)} verified comparisons to {output / 'summary.json'}")
    return 1 if any(not metric["passes"] for item in comparisons for metric in item["metrics"].values()) else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("baseline", type=Path)
    parser.add_argument("candidate", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--nuget-source", type=Path,
                        default=Path(os.environ.get("NUGET_PACKAGES", Path.home() / ".nuget" / "packages")))
    parser.add_argument("--logical-cpu", type=int)
    parser.add_argument("--smoke", action="store_true")
    args = parser.parse_args()
    output = args.output.resolve()
    cores = {side: getattr(args, side).resolve() for side in ("baseline", "candidate")}
    if any(path.name.lower() != "skecs.dll" or not path.is_file() for path in cores.values()):
        parser.error("Both core inputs must be existing SKECS.dll files")
    if output == WORKSPACE or not output.is_relative_to(WORKSPACE) or output.exists():
        parser.error("Output must be a new directory inside the workspace")
    if args.logical_cpu is not None and (os.name != "nt" or not 0 <= args.logical_cpu < 64):
        parser.error("The logical CPU must be in [0, 63] on Windows")
    output.mkdir(parents=True)
    environment = dict(os.environ, DOTNET_TieredCompilation="0", DOTNET_CLI_TELEMETRY_OPTOUT="1",
                       DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE="1", DOTNET_CLI_HOME=str(output / "dotnet-home"),
                       DOTNET_GENERATE_ASPNET_CERTIFICATE="false", DOTNET_ADD_GLOBAL_TOOLS_TO_PATH="false",
                       DOTNET_NOLOGO="true",
                       NUGET_PACKAGES=str(output / "packages"), NUGET_HTTP_CACHE_PATH=str(output / "nuget-http"))
    environment.pop("SKECS_PAIR_LOGICAL_CPU", None)
    if args.logical_cpu is not None:
        environment["SKECS_PAIR_LOGICAL_CPU"] = str(args.logical_cpu)
    return run_suite(args, cores, output, environment)


if __name__ == "__main__":
    raise SystemExit(main())
