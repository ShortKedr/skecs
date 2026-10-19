English | [Русский](ARMAMENT.ru.md)

[Current tactics](TACTICS.md) | [Current benchmark](REPORT.md)

# Mounted Armament

Gameplay revision: `mounted-armament-v7`. Current AI is [layered-squads-v3](TACTICS.md).
| Class | Turret entities | Main damage | Main cooldown, seconds | Main range |
|---|---:|---:|---:|---:|
| Frigate | 2 | 60 | 12 | 170 |
| Destroyer | 2 | 80 | 14 | 170 |
| Light carrier | 4 | 480 | 18 | 220 |
| Fleet carrier | 6 | 640 | 22 | 220 |
| Foundry carrier | 8 | 800 | 26 | 220 |

Nominal cooldowns assume 60 ticks/s; low/high profiles scale them with the
existing churn multiplier: low uses 24/28/36/44/52 seconds, high uses
6/7/9/11/13 seconds, in table order. These are initial tuning values, not proven balance.
Small classes and Support keep their weapons. Platforms replace their old
primary/secondary pair with the main cannon and attached guns.

`SpawnShip` assigns a deterministic initial heavy charge delay of 50%-100% of
the actual churn-scaled cooldown. A local RNG derived from battle seed and ship
serial is independent of the battle RNG and does not consume its draws. Readiness
is relative to `birthTick`, including late-built and respawned ships: no free
first heavy volley. Turrets start ready as before. Subsequent heavy shots use
the full scaled cooldown.

## Independent Guns

Projectile collision excludes self hits for every weapon kind, including
mounted guns and heavy hull cannons. The swept collision query excludes every
fixture of the projectile's owner, including start-point overlaps. Current [circle physics](PHYSICS.md) uses analytic sweeps. The query continues searching for the nearest other ship: an owner cannot block
a shot aimed past it. Mounted projectiles use the parent ship as owner, not the
turret entity. Lifetime-safe ECS keys resolve a live owner to its unique ship
serial; recycled body/entity slots do not give another ship immunity.
Friendly fire is retained. Debris damage and physical hull contacts are unchanged.
The `SelfHits` telemetry field stays present for compatibility and remains zero.
Mounts are auxiliary weapons with independent damage, cooldowns and heat budgets.

| Class | Battery DPS |
|---|---:|
| Frigate / Destroyer | 10.42 |
| Light carrier | 20.83 |
| Fleet carrier | 31.25 |
| Foundry carrier | 41.67 |

These are raw `sum(damage * burst * 60 / cooldown)` values at nominal churn,
before aim, armor, shields, heat, range or target availability. Low/high profiles
retain their existing cooldown scaling. DPS is not measured damage delivered.

Both frontends render the guns using a dedicated [generated gun atlas](../../SpaceBattle.Game/Assets/PROVENANCE.md#gun-atlas).
Fixed pedestals and independently rotating heads have measured bearing pivots;
the existing recoil/heat presentation is retained. The heavy hull cannon has
its own larger sprite. This is a visual change only, not a gameplay revision.

Each turret has `MountedTurret`, `Transform`, `Weapon` and `Heat`. Paired mounts
use the parent hull's team RGB multiplier for both their base and weapon head;
the main heavy cannon uses the same paint. Geometry, recoil and heat cues are
unchanged. Browser hulls and guns cache both native palette variants at load.

Paired mounts
alternate auxiliary cannon (7 damage, 84-tick cooldown = 1.4 seconds nominal)
and plasma (3.25 damage, 36-tick cooldown = 0.6 seconds nominal).
Range is 120 for escorts, 150 for carriers. Heat capacity is
60, cooling 12/s, shot heat half the ordinary weapon's cost. They have no extra
physics body or independently damageable hull. Parent destruction removes all
mounts in the same lifecycle flush. Statistics include docked mounts separately.

Mounts follow parent translation/rotation. Each gun swivels independently at
5 rad/s, retaining its living, undocked in-range enemy. Reacquisition happens at
staggered `30 + slot` tick intervals: parent focus first, otherwise nearest enemy
with serial-order ties. Searches do not run every tick for every mount.

Aim tolerance is 0.08 rad for turrets and 0.12 rad for the hull cannon. Heavy
weapon steering follows predicted intercept. Its projectile speed is 170 and
radius 0.9; it is not hitscan or a guaranteed kill. Cyan impact effects do not
introduce area damage.

Every actual heavy-round collision produces a separate cyan contact blast,
including nonlethal hits: one stationary core at the swept impact point and
smaller dispersing light fragments. It expands over 6 ticks, holds full opacity
for 3 ticks, then fades within 36 ticks (0.6 seconds). Core radius scales with
shot damage, clamped to 3.6-7.2 world units before presentation expansion.
Both clients draw this effect above hulls and mounts; orange kind-2 ship
destruction stays separate. Misses, expiry and owner-excluded crossings do not
trigger it. The existing 12-particle base budget, multiplier and particle cap
remain in force; there is no added splash, debris, damage or camera shake.
Particle state participates in checksums. [Heavy-impact checks](../../tools/web/check-heavy-impact.mjs)
capture every tick of a real hit's lifetime and apply the existing contrast
budgets; focused native SKECS mechanics checks cover surviving/lethal hits.

All guns retain deterministic 75/25 precise/spread decisions. Mounted rounds
inherit parent linear velocity plus tangential mount velocity. Muzzles launch
outside the enclosing hull circle. Projectile owner is the parent for damage/
threat attribution and self-collision exclusion; other friendly and enemy hits
remain possible. Only attacking, undocked, non-overheated parents permit turret fire.
Turret heat is independent of the main gun; cooling continues while idle.
Docked mounts are hidden; retreating and screening platforms do not fire.

Target reasoning includes mount DPS with separate heat budgets. It is a
heuristic, not an exact cooldown/turning-time solver. Sorted shots and checksummed
mount state preserve replays across independent cores, not cross-platform float parity.

Runtime source links show the SKECS core; the other four cores own matching implementations. Sources: [catalog](../../SpaceBattle.Core/Components.cs),
[mount systems](../../SpaceBattle.Simulations/Skecs/ArmamentSystems.cs),
[combat](../../SpaceBattle.Simulations/Skecs/CombatSystems.cs),
[tests](../../SpaceBattle.Benchmarks/Measurement/ArmamentValidation.cs),
[collision filtering](../../SpaceBattle.Simulations/Skecs/PhysicsService.cs),
[owner regressions](../../SpaceBattle.Benchmarks/Measurement/ProjectileOwnerValidation.cs).

## Validation

[Armament tests](../../SpaceBattle.Benchmarks/Measurement/ArmamentValidation.cs) cover seeded spawn-charge bounds, late births, first-shot deadlines, turret fire during heavy charge, independent targets/heat/cooldowns, rotating attachments, inherited projectile velocity, hull alignment and lifecycle cleanup. [Owner regressions](../../SpaceBattle.Benchmarks/Measurement/ProjectileOwnerValidation.cs) cover self-exclusion, stale owner keys and friendly fire on the native SKECS core. [Concrete-core replay checks](../../SpaceBattle.Benchmarks/Measurement/NativeReplayValidation.cs) compare the other native cores with SKECS at initial state and after every tick.

The [browser armament check](../../tools/web/check-armament.mjs) checks AOT heavy-round travel and independent swivel. The [opening cadence check](../../tools/web/check-heavy-cadence.mjs) checks all three AOT profiles, ordinary combat during initial charge, eventual heavy fire and reset replays. These verify implementation rather than matchup balance or performance.
