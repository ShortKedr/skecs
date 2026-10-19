English | [Русский](THIRD_PARTY_NOTICES.ru.md)

# Third-Party Sources and Licenses

[Repository](README.md) | [All implementations and tests](SKECS.Benchmarks/README.md) | [Benchmark report](docs/space-battle/REPORT.md)

This repository compares five ECS implementations using shared workloads. The
vendored code remains the work of its respective authors. Comparison, research,
or naming a framework does not waive its license conditions or imply endorsement.
This inventory records the checked source licenses, not a guarantee of compliance
with every law, jurisdiction, patent, or a user's individual circumstances.

## Pinned Source Inventory

Only the runtime source subsets below and their exact original licenses are
distributed. They are ordinary repository files, not submodules, external links
standing in for code, or a required download. All included upstream files are
byte-identical to the pinned archives; copyright headers are preserved.

| Component | Authors / copyright in license | Terms | Included source | Exact license |
| --- | --- | --- | --- | --- |
| LeoECS Classic | 2012 - 2023 leopotam@yandex.ru | MIT-Red or separately negotiated proprietary/commercial license | [ecs/src](SKECS.Benchmarks/Vendor/Sources/ecs/src/) | [Original](SKECS.Benchmarks/Vendor/Sources/ecs/LICENSE.md), [pinned upstream](https://github.com/Leopotam/ecs/blob/1213c31c9ef8325d48e74a52b345c4db242d038b/LICENSE.md) |
| LeoECS Lite | 2012-2024 leopotam@yandex.ru | MIT-Red | [ecslite/src](SKECS.Benchmarks/Vendor/Sources/ecslite/src/) | [Original](SKECS.Benchmarks/Vendor/Sources/ecslite/LICENSE.md), [pinned upstream](https://github.com/Leopotam/ecslite/blob/5053141d7d663c6b1855c8409ac93dfac807b239/LICENSE.md) |
| Morpeh | 2025 Oleg Morozov, Igor Boyko, Matvey Reschikov | MIT | [Morpeh Core](SKECS.Benchmarks/Vendor/Sources/morpeh/Scellecs.Morpeh/Core/) | [Original](SKECS.Benchmarks/Vendor/Sources/morpeh/LICENSE.md), [pinned upstream](https://github.com/scellecs/morpeh/blob/9f6369384792a01d74df24efd35b08ae313e4f12/LICENSE.md) |
| StaticEcs | 2026 FFS | MIT | [StaticEcs/Src](SKECS.Benchmarks/Vendor/Sources/StaticEcs/Src/) | [Original](SKECS.Benchmarks/Vendor/Sources/StaticEcs/LICENSE.md), [pinned upstream](https://github.com/Felid-Force-Studios/StaticEcs/blob/23b1cf36d89497e8a126d38459d688e2b0a5dea3/LICENSE.md) |
| StaticPack | 2025 FFS | MIT | [StaticPack/Src](SKECS.Benchmarks/Vendor/Sources/StaticPack/Src/) | [Original](SKECS.Benchmarks/Vendor/Sources/StaticPack/LICENSE.md), [pinned upstream](https://github.com/Felid-Force-Studios/StaticPack/blob/7bfb6cf925901b9366dda884258d727a89fff76e/LICENSE.md) |

StaticPack is a dependency of StaticEcs, not another measured ECS. Exact repository
URLs, full commit IDs, file sizes, and SHA-256 hashes are recorded in the
[source manifest](SKECS.Benchmarks/Vendor/manifest.json).

## License Conditions

**MIT:** preserve each author's copyright and permission notice in all copies or
substantial portions, including any separately distributed binary/source package.
The original warranty/liability disclaimers remain in force. Do not remove or
replace these notices with a license for this repository's own code.

**MIT-Red is not ordinary MIT.** The pinned LeoECS licenses contain additional
conditions concerning support for military use of open-source software and
hostility or support for attacks toward Russians. They prohibit use when any
condition cannot be met, and require retaining the copyright and full conditions
in copies or substantial portions. The original Russian text is authoritative;
this short English description is not a replacement or a legal interpretation.
Users and redistributors must assess whether they meet those conditions. If they
cannot, they must not use the covered code under MIT-Red. Classic also describes
individually negotiated proprietary/commercial terms; no such permission is
claimed here. Do not label the complete comparison bundle unrestricted or
uniformly MIT-licensed. Obtain clarification from the rightsholder when needed.

## Local Adaptations

The upstream source files and licenses are unmodified. Our [microbenchmark
adapters](SKECS.Benchmarks/Comparison/Adapters.cs), [static adapters](SKECS.Benchmarks/Comparison/StaticAdapters.cs),
[stress adapters](SKECS.Benchmarks/Comparison/StressAdapters.cs), and [native SpaceBattle cores](SpaceBattle.Simulations/)
are separate comparison integrations, not upstream implementations or endorsed
performance recommendations.

[StaticLibrary.csproj](SKECS.Benchmarks/Vendor/StaticLibrary.csproj) compiles StaticEcs and
StaticPack runtime sources for .NET 8. [MorpehLibrary.csproj](SKECS.Benchmarks/Vendor/MorpehLibrary.csproj)
compiles the unchanged Morpeh Core for netstandard2.1, matching its upstream target
and Debug defines, without Unity/package/editor assets or generating a NuGet
package. These are local build wrappers, not edits to the ECS algorithms.
Packaging builds may have different binary hashes from historical measurements;
the retained benchmark provenance is not rewritten to claim otherwise.

## Other Dependencies

NuGet dependencies are declared in the project files and restored normally; their
package caches, binaries, SDKs, and native libraries are **not redistributed in
this source repository**. Examples include [Silk.NET 2.23.0](https://www.nuget.org/packages/Silk.NET.Windowing/2.23.0),
[StbImageSharp 2.30.16](https://www.nuget.org/packages/StbImageSharp/2.30.16),
[BenchmarkDotNet 0.14.0](https://www.nuget.org/packages/BenchmarkDotNet/0.14.0), and
the xUnit/test SDK/coverage tooling. This is not a complete transitive binary
license inventory. A future executable, NuGet package, or release archive needs
its own dependency/license review and required notices for everything bundled.
Generated frontend artwork has separate [provenance](SpaceBattle.Game/Assets/PROVENANCE.md).

The repository's original code (including SKECS, adapters, and tests),
documentation, data, and original artwork are licensed under the root
[MIT license](LICENSE). This grant applies only to material owned by the
project's rightsholder. Vendored sources and other third-party material retain
their own licenses and notices listed here and in their original files.

## Browser Distribution

The [WebAssembly demo](SpaceBattle.Web/README.md) distributes compiled SKECS,
SpaceBattle.Core, SpaceBattle.Simulations and SpaceBattle.Replay,
along with the pinned LeoECS Lite, LeoECS Classic, Morpeh, StaticEcs and StaticPack
runtimes described above,
.NET/Blazor 8.0.31 (MIT and upstream third-party notices), and Lucide 1.55.0
(ISC / MIT for Feather-derived icons). The static site ships the original license
texts, copyright notices and artwork provenance in its
[license directory](SpaceBattle.Web/wwwroot/licenses/), including a
[distribution inventory](SpaceBattle.Web/wwwroot/licenses/NOTICE.txt).
Production AOT output also retains the [Emscripten 3.1.34 license](SpaceBattle.Web/wwwroot/licenses/EMSCRIPTEN.txt)
and [author list](SpaceBattle.Web/wwwroot/licenses/EMSCRIPTEN-AUTHORS.txt) from the
.NET WebAssembly workload. Compiler/toolchain binaries remain local build tools,
not site assets.
The demo does not bundle native OpenGL dependencies or benchmark/test dependencies. The broader source comparison
licensing conditions above remain unchanged.

The project-owned [StaticEcs compatibility source](SKECS.Benchmarks/Vendor/Compatibility/StaticEcsMd5.cs)
provides equivalent managed MD5 for startup type GUID generation in WebAssembly,
where the platform MD5 provider is unavailable. Native builds retain the platform
provider. The pinned upstream sources are unmodified; the shim does not run in
simulation ticks.

## Verification

```sh
dotnet run --project SKECS.Benchmarks -c Release -- verify-vendor
dotnet test tools/BenchmarkSupport.Tests/BenchmarkSupport.Tests.csproj -c Release
```

The [native verifier](tools/BenchmarkSupport/VendorVerifier.cs) checks audited component metadata, lengths and SHA-256 hashes for every manifest file, including licenses, and the exact publishable Git inventory. It rejects unpinned C# files in the distributed runtime directories. [.gitattributes](.gitattributes) prevents line-ending conversion of hash-verified upstream files.

```sh
dotnet run --project SKECS.Benchmarks -c Release -- restore-vendor
```

The optional [restore implementation](SKECS.Benchmarks/Analysis/VendorRestore.cs) downloads pinned archives, verifies the selected runtime sources and original licenses before installing missing files, and refuses to overwrite changed local files. Downloading requires network access; remote repositories remain unchanged. A normal clone does not need this step. The license conditions above still apply.
