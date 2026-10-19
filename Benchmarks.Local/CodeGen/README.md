English | [Русский](README.ru.md)

# Benchmark source generation

Run from the workspace root:

```sh
dotnet run --project tools/BenchmarkTools -c Release -- generate
dotnet run --project tools/BenchmarkTools -c Release -- generate --check
dotnet test tools/BenchmarkTools.Tests/BenchmarkTools.Tests.csproj -c Release
```

The .NET 8 [generator](../../tools/BenchmarkTools/BenchmarkCodeGenerator.cs) uses only the standard library. `benchmark-model.json` defines the framework registry, base/stress operation catalogs and stress shape bit count. `Templates/` contains native C# API implementations. Edit these inputs, regenerate and review the generated C#.

`--check` verifies UTF-8 content, accepts LF or CRLF checkout line endings, and fails on missing or stale outputs without writing. Generation emits UTF-8 without BOM and LF consistently. Generated C# is checked in; ordinary builds do not run generation.

Eight template outputs plus the registry and scenario catalog produce ten files. The shared `ComparisonWorkload`, `StressContract`, `SkecsStressBackend` and `BenchmarkModel.g.cs` live in [SKECS.Benchmarks.Workloads](../../benchmarks/SKECS.Benchmarks.Workloads/SKECS.Benchmarks.Workloads.csproj), alongside the handwritten `StressWorkload`. The comparison executable and BenchmarkDotNet reference that .NET 8 class library. Other generated outputs live in `Benchmarks.Local` and `SKECS.Benchmarks`.

Expansion covers position/velocity initialization and movement, stress tags, pool/stash registration, shape seeding and inspection, framework factories, scenario lists and BenchmarkDotNet entry points. Native query processors, pool access, deferred commits, batch operations and entity handle semantics stay in their framework templates. Generation adds no dispatch inside timed loops.

The StaticEcs template integrates the author's supplied microbenchmark APIs: bulk creation, typed read/write processors, disabled component data lifecycle, batch churn/destruction, tags and the hot entity type. The supplied `SpaceBattle.Adapters/StaticAdapters.cs` stays preserved and excluded from that project's compilation; its microbenchmark integration is generated into `Benchmarks.Local/StaticAdapters.cs`.
