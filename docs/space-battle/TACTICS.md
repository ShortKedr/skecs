English | [Русский](TACTICS.ru.md)

# Layered Squad Tactics

Current AI revision: **layered-squads-v3**. Desktop and Mono AOT share C# code.
[DamageRules](../../SpaceBattle.Core/DamageRules.cs) is the source for the layered-weapon-damage-v1 damage profiles used by tactical scoring.

## Composition And Persistence

Groups contain 2-4 ships, Interceptor through Gunship. Team-specific
spatial snapshots feed greedy joint-cost recruitment, not combination enumeration
or a diversity quota. Mixed and homogeneous groups compete on usefulness.
Compatibility includes speed/range ratios, arrival delay, health and weapon/heat
readiness. Temporary screening/cooling preserves membership; death, docking,
class/team changes and large separation repair the roster and elect a survivor.
Periodic review replaces at most one member for a clear improvement. A shield
transition does not reset leases or pause useful fire while waiting for plasma.

[TacticsSettings](../../SpaceBattle.Core/TacticsSettings.cs) defaults are heuristics,
not proven balance: 120-tick reviews, 20% replacement improvement, minimum
speed/range ratios 0.45/0.35, 2.5-second arrival tolerance, 8-second engagement
ceiling. Target leases retain 120 ticks and the 35% switch threshold.

## Scoring And Movement

A bounded first-impact/continuous-rate model handles shield then hull through
real profiles, weapon readiness/cooldown, flight, hit probability, packet overkill
and own heat limits. Late participants do not contribute full DPS before arrival.
Return damage/expected losses and probabilistic plasma suppression are separate
from direct damage; suppression is capped at 25% cost benefit, not guaranteed
overheat or a reduction of HP. Flying allied shots receive at most 35% initial
confidence, attenuated by motion/heading; credited damage is capped at half each
remaining layer. Actual CCD and friendly fire are untouched.

Target-relative angular lanes use individual working ranges, short separation
and steering away from an ally crossing a firing ray. This reduces intended
overlap, but neither guarantees a clear path nor immunizes allies. Wounded ships
still screen behind healthier ships, recover and return through existing states.

## Budgets And Limits

Defaults: 16384 spatial cell/membership visits and 8192 target-cost evaluations
per phase, 2048 composition evaluations per review; 16 local targets, 8 rotating
global probes, 6 formation targets, 16 recruits and 4096 projectile intents.
Prepared indexes and reusable buffers avoid allocations in the hot path once their capacity has been reserved.
Recruitment filters assigned/ineligible rows before filling the bounded sample,
so an assigned prefix cannot permanently starve a dense cell. When spatial work
is exhausted, a constant-sized neighboring serial window allows stable progress.

Limited spatial queries are deterministic samples, not globally optimal nearest
or target searches. Stable traversal is followed by snapshot/serial sorting.
Current-velocity engagement bounds plus rotating global probes may still omit a
good target until a later review. On exhaustion, live leases and existing groups
are retained; sampled compatible free recruits provide a stable fallback.
Diagnostics count fallback explicitly.

These counters bound spatial visits and expensive score calls, not all AI CPU
time: compatibility/arrival prefilters and steering also do work. Snapshot/index
preparation is linear; two additional team indexes require roughly 0.5 MiB of
cell metadata plus population-sized buffers. Fully coupled dense physical
contacts still have a quadratic worst case; physics was not changed here.

Approximation limits: future acceleration, sustained discrete volleys, regen/
repair, changing heat and exact turret aim/obstruction. Large solo ships retain
catalog mount pairs, not arbitrary external gun edits. Cross-platform bitwise
determinism and a win in every scenario are not claimed.

## Validation

[Squad tests](../../SpaceBattle.Benchmarks/Measurement/SquadCompositionValidation.cs) cover
composition, compatibility, leases, death repair, cover, lanes and dense budgets.
[Scoring tests](../../SpaceBattle.Benchmarks/Measurement/TacticalScoringValidation.cs) cover
layer/arrival/heat estimates. Full checks and all-port replays remain required.


Run the complete native validation with the [benchmark CLI](../../SpaceBattle.Benchmarks/README.md). Current workload performance is published in the [benchmark report](REPORT.md).
