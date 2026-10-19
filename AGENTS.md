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

## Independent SpaceBattle implementations

SpaceBattle runs a separate concrete simulation in
`SpaceBattle.Simulations/{Skecs,LeoEcsLite,LeoEcsClassic,Morpeh,StaticEcs}`. Each implementation owns its framework's
native entities, components, queries, systems, physics and lifecycle. Do not
route production simulation through `Battle<TStore>`, `IEcsStore`, adapters,
shared generic systems, or a common simulation implementation.

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
