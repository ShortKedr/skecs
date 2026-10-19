English | [Русский](DEPLOYMENT.ru.md)

# Initial Fleet Deployment

Revision: `dispersed-front-v2`. Each concrete core uses the same C# deployment rules in desktop and web.

The old start sampled every escort in a strip only 32 units deep and 130 units
wide, without clearance checks. The new start uses a broader front with separate
depth: light four-ship packets, a rear escort screen and three separated carriers.
Packets are deployment positions, not forced AI groups. Existing tactics choose
their membership, targets and approach independently after the simulation starts.

Light packets retain broad approach lanes, but their centers, depth, orientation
and individual positions vary. Rear escorts and carriers also have staggered
positions. The actual formation no longer snaps to rows or repeats identical
squares. Clearance checks reserve at least 2.4 units between escort surfaces and
four units around carriers. These are deployment spacing choices, not changes to
collision geometry. Opposing fleets reflect X exactly; original birth order,
home assignments, class counts, weapons,
physics, production and corner warp waves are unchanged. Placement uses its own
`RandomStream(Seed ^ 0xD1B54A35)`, without advancing the combat/spawn RNG. The same
seed repeats the same layout; different seeds also change initial positions.

This is a game deployment heuristic, not a model of real space warfare. It
creates maneuver room at tick zero, but does not constrain ships to remain in
their lanes or promise that later battles cannot become dense. Different initial
positions intentionally change trajectories and checksums, not weapon balance.

## Reproduction And Limits

`BattleConfig.Deployment` defaults to `LegacyRandom` in the common configuration contract. Interactive hosts and the current `game` benchmark preset select `Tactical`. Desktop `--legacy-deployment` selects sequential random placement; deliberately dense stress runs can use `--ships 2048 --legacy-deployment`.

The startup planner allocates temporary arrays, not per-tick buffers. It first
reserves carrier clearance and assigns nearest slots in stable cell order on a
bounded safety lattice (12-unit spacing, at most 4096 slots, planning half-size
capped at 600). It then tries up to twelve off-grid displacements per escort,
checking each against every actual hull. A safe lattice position remains the
fallback if all attempts fail; very crowded starts can therefore be more regular.
These bounded all-hull checks cost O(S²) at startup, never on each simulation tick.
It rejects fleets that do not fit rather than silently shrinking collision
geometry or spawning overlaps. Increasing arena size helps up to the planning
cap; extreme dense scenes remain available through the legacy configuration.

`dotnet run --project SpaceBattle.Benchmarks -- measure --validate-deployment` checks placement and repeated replay
on the native SKECS core. `measure --validate-ports` compares the other native cores with SKECS at initial state and after every tick. `tools/web/check-deployment.mjs` verifies the production
browser deployment, live engagement, reset/replay and desktop/mobile captures.

Native validation covers geometry, irregularity and deterministic replay on the native SKECS core. Browser checks cover initial attributes, real combat, reset and self-hit exclusion on desktop and mobile viewports. Native/WASM bit-identical replay is not asserted.
