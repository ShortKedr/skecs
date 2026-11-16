# Agent guidance

**Keep only current documentation in Git. Do not commit history/story documents,
work logs, dated report archives, or previous versions of documents in either
repository. Update the canonical document in place and use Git history for
earlier versions; remove superseded documents and their unused assets.**

This workspace keeps the independent private repository at `./skecs-private/`.
Before writing plans, specifications, or documentation for the maintainer and
agents, read `skecs-private/AGENTS.md` ([private agent instructions](https://github.com/ShortKedr/skecs-private/blob/main/AGENTS.md)).
The private checkout is always a directory at the root of this workspace and is ignored by
the public repository; it is not a submodule.

Keep repository operations in sync: when committing changes in the public repository,
also commit the related changes in `skecs-private/`. When pushing the public repository,
also push `skecs-private/` without a separate reminder. The workspace's prohibition on
changing remote state still applies: do not push either repository while that prohibition
is in force.

Private repository: [ShortKedr/skecs-private](https://github.com/ShortKedr/skecs-private)
(restricted access).

## Release documentation and migrations

Product documentation is written only for a specific released package version,
never for an unversioned working branch or an unreleased API. Every manual,
example and API reference must identify that exact version. Build API reference
and validate examples against the matching release artifact or immutable release
source, not the current checkout. Keep release documentation available at stable
versioned URLs with a version selector, as in Unity package documentation. Only
include versions for which release documentation is actually available; do not
invent versions or silently substitute another version's page. Search, navigation
and language switching must stay within the selected release.

Maintain one canonical source for each document. Keep older release documentation
in immutable release sources or generated site artifacts, not duplicate historical
Markdown trees in Git. Release documentation is a supported product surface, not
a dated work log. Documentation corrections may update the relevant release's
canonical source without changing which package/API it describes.

When upgrading between releases requires migration, the target release's
documentation must include an EN/RU migration article. State source and target
versions, breaking changes, automated steps, manual steps and validation. Link
it from installation, release notes and the versioned documentation navigation.

When a migration can be performed programmatically, implement and test the
automation in the separate `skecs-migration/` module. This is a project-wide
requirement, not only a documentation task. Consumer migration tools must use
C#/.NET and the existing .NET toolchain, without requiring Python or another
language runtime. Provide a non-mutating preview and
an explicit apply mode, preserve unrelated consumer code, and document remaining
manual work and how to validate the migrated project. Do not replace feasible
automation with instructions to edit files manually.

## Independent SpaceBattle implementations

SpaceBattle runs a separate concrete simulation in
`SpaceBattle.Simulations/{Skecs,LeoEcsLite,LeoEcsClassic,Morpeh,StaticEcs}`. Each implementation owns its framework's
native entities, components, queries, systems, physics and lifecycle. Do not
route simulation through `Battle<TStore>`, `IEcsStore`, adapters,
shared generic systems, or a common simulation implementation. Do not retain or
reintroduce a legacy shared SpaceBattle engine, adapter stores or duplicated
legacy simulation helpers for tests, compatibility or reference. Keep
`SpaceBattle.Core` limited to shared data/configuration and presentation contracts.
Detailed mechanics tests target the native SKECS core. Validate the other native
cores externally against SKECS at initial state and after every tick instead of
maintaining a parallel mechanics-test suite or legacy test engine.

Keep gameplay exactly equivalent across all five cores. **A logic change in one
core must be applied to every core, and the matching desktop, web and headless
implementations must be updated too.** Frontends select a concrete implementation
once; they must not introduce a shared simulation adapter or per-tick framework
abstraction. Shared data contracts, assets and low-level graphics helpers are
allowed.

Replay hashing and tick comparison belong to software outside the simulation
cores (`SpaceBattle.Replay` and the benchmark host). Validate initial state and
every tick against the SKECS state hash. Stop the tick timer and allocation
counter before state capture, hashing and comparison. Keep full per-tick hashes
in local benchmark evidence and reject divergent runs before publishing results.
Rerun the five-framework campaign after this architecture or gameplay changes;
never relabel shared-core measurements as native-core results. Follow the
private SpaceBattle report contract when regenerating both report languages.
