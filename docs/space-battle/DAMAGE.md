English | [Русский](DAMAGE.ru.md)

# Layered Weapon Damage

Current source revision: **`layered-weapon-damage-v1`**. Desktop and production
Mono AOT WASM use the same [C# rules](../../SpaceBattle.Core/DamageRules.cs).

## Profiles

| Weapon | Shield multiplier | Hull multiplier |
| --- | ---: | ---: |
| Plasma | 1.5 | 1 |
| MachineGun, Cannon, HeavyCannon | 1 | 1.5 |
| Missile | 1 | 1 |
| Collisions and debris | 1 | 1 |

The 1.5 specialization is **initial tuning, not validated balance**. No weapon
base damage, cadence, heat limit, projectile geometry, faction neutrality or
owner immunity changes. Mounted rounds use their actual gun's weapon kind.
Hull armor is the existing fractional reduction; no new armor classes exist.

`BattleConfig.Damage` holds immutable settings. Coefficients must be finite and
positive. `DamageRules.ForWeapon` is the only weapon-to-profile mapping. For example:

```csharp
var config = new BattleConfig {
    Damage = new DamageSettings { Missile = new DamageProfile(1.2f, 1.1f) }
};
```

The existing plasma shield-to-heat rate remains 1.8 and is not a balance knob in
this change. To adjust plasma specialization, use `new DamageSettings().Plasma
with { ShieldMultiplier = 1.6f }`, retaining that heat field.

## One Hit

For base damage `D`, current shield `S`, shield multiplier `Ms` and hull multiplier `Mh`:

1. Spend `min(D, S / Ms)` base damage on the shield.
2. Remove the spent base damage times `Ms` from the shield.
3. Pass only the remaining base damage into the hull.
4. Hull damage is `remaining * Mh * (1 - armor)`.

Removed shield is clamped to available shield solely to avoid a floating-point
round-trip overshoot. Plasma adds `actual removed shield * 1.8` heat, never heat
for hull damage. Damage still queues the existing combat/shield-break/death states.
The legacy `Damage(..., plasma: true, ...)` uses the plasma profile; false uses
neutral damage. `DamageWeapon` is the explicit weapon API used by all projectile hits.

| Base 100, armor 0.2 | Shield before | Shield removed | Hull damage | Plasma heat |
| --- | ---: | ---: | ---: | ---: |
| Plasma | 75 | 75 | 40 | 135 |
| Kinetic | 75 | 75 | 30 | 0 |
| Missile | 75 | 75 | 20 | 0 |
| Plasma | 0 | 0 | 80 | 0 |
| Kinetic | 0 | 0 | 120 | 0 |

## AI Estimate

The [layered squad model](TACTICS.md) evaluates shield and hull damage separately, using real weapon profiles, readiness, flight time, hit probability and heat budgets. It includes staggered arrivals and bounded estimates of return damage, suppression and allied shots.

This is a target-selection heuristic. It does not simulate future acceleration, regeneration, every discrete volley, exact turret obstruction or arbitrary runtime changes to mounted guns. Actual hits use the emitted projectile's own weapon profile.

## Verification

[Headless checks](../../SpaceBattle.Benchmarks/Measurement/Validation.cs) cover layer accounting, damage/AI estimates and adapter replays. Run them with the [benchmark CLI](../../SpaceBattle.Benchmarks/README.md):

```sh
dotnet run --project SpaceBattle.Benchmarks -c Release -- validate space-battle
```

The controlled `measure --damage-study` command compares equal base-DPS compositions and shielded/unshielded targets. Save diagnostic evidence under ignored `artifacts/tooling/tmp`; these fixtures do not establish fleet balance.

The production [browser checks](../../tools/web/check-web.mjs) inspect actual AOT layer outputs and revision, combat, deterministic reset and rendering. Revision metadata is exported by both frontends and headless reports.
