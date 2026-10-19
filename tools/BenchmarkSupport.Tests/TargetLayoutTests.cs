using BenchmarkDotNet.Running;
using SKECS.BenchmarkTools;
using SpaceBattle.Benchmarks;

namespace SKECS.BenchmarkTools.Tests;

public sealed class TargetLayoutTests
{
    [Fact]
    public void SpaceBattleTargetsContainOnlyNativeSimulationImplementations()
    {
        foreach (var assembly in new[] { typeof(BattleCli).Assembly, typeof(SpaceBattle.Simulations.Skecs.Battle).Assembly, typeof(SpaceBattle.ReplayHash).Assembly })
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name is "SpaceBattle.Adapters" or "SpaceBattle.SKECS");
        var contracts = typeof(SpaceBattle.BattleConfig).Assembly;
        Assert.DoesNotContain(contracts.GetTypes(), type => type.Name is "IEcsStore" or "PhysicsService" || type.Name.StartsWith("Battle`", StringComparison.Ordinal) || type.Name.StartsWith("NativeStore`", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownMeasurementOptionsCannotStartARealCampaign()
        => Assert.Equal(1, global::MeasurementProgram.Run(["--unsupported"]));

    [Fact]
    public void MergedBdnSuitesPreserveEveryCaseAndDoNotOverlap()
    {
        var standard = Cases("standard");
        var extended = Cases("extended");
        Assert.Equal(34, standard.Count);
        Assert.Equal(51, extended.Count);
        Assert.True(standard.SetEquals(MicroValidation.ExpectedCurrent()));
        Assert.True(extended.SetEquals(MicroValidation.ExpectedLegacy(Workspace.FindRoot())));
        Assert.Empty(standard.Intersect(extended));
        Assert.Equal(EcsBenchmarkSuites.Types("standard").Concat(EcsBenchmarkSuites.Types("extended")).ToHashSet(), EcsBenchmarkSuites.Types("all").ToHashSet());
    }

    [Fact]
    public void EcsTargetDoesNotReferenceGameOrSpaceBattleBenchmarks()
    {
        Assert.DoesNotContain(typeof(EcsBenchmarkCli).Assembly.GetReferencedAssemblies(), reference => reference.Name!.StartsWith("SpaceBattle", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(BattleCli).Assembly.GetReferencedAssemblies(), reference => reference.Name == "SKECS.Benchmarks");
        Assert.DoesNotContain(typeof(Workspace).Assembly.GetReferencedAssemblies(), reference => reference.Name is "SKECS.Benchmarks" or "SpaceBattle.Benchmarks");
    }

    [Fact]
    public async Task TargetCommandsRejectTheOtherTargetsSuites()
    {
        Assert.Equal(1, await EcsBenchmarkCli.RunAsync(["validate", "space-battle"]));
        Assert.Equal(1, await BattleCli.RunAsync(["validate", "comparison"]));
        Assert.Equal(1, await EcsBenchmarkCli.RunAsync(["run", "all", "--include-desktop"]));
        Assert.Equal(1, await EcsBenchmarkCli.RunAsync(["run", "all", "--ticks", "120"]));
    }

    [Fact]
    public async Task CompositionAnalysisRequiresExplicitSavedInputs()
    {
        Assert.Equal(1, await BattleCli.RunAsync(["report", "composition"]));
        Assert.Equal(1, await BattleCli.RunAsync(["verify", "composition"]));
    }

    [Fact]
    public async Task EmptyBdnSelectionFailsInsteadOfReportingSuccess()
    {
        Assert.Equal(1, await EcsBenchmarkCli.RunAsync(["bdn", "--suite", "standard", "--filter", "*MissingConsolidationCase*", "--job", "Dry", "--inProcess", "--artifacts", "artifacts/benchmarks/invalid-bdn-selection"]));
    }

    static HashSet<MicroValidation.BdnKey> Cases(string suite)
    {
        var cases = new HashSet<MicroValidation.BdnKey>();
        foreach (var type in EcsBenchmarkSuites.Types(suite))
        foreach (var benchmark in BenchmarkConverter.TypeToBenchmarks(type).BenchmarksCases)
        {
            int count = Convert.ToInt32(benchmark.Parameters.Items.Single(p => p.Name == "EntityCount").Value);
            var padding = benchmark.Parameters.Items.SingleOrDefault(p => p.Name == "Padding");
            Assert.True(cases.Add(new(type.Name, benchmark.Descriptor.WorkloadMethod.Name, count, padding is null ? null : Convert.ToInt32(padding.Value))));
        }
        return cases;
    }
}
