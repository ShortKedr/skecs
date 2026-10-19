English | [Русский](PHYSICS.ru.md)

# SpaceBattle physics

[Desktop](../../SpaceBattle.Game/README.md) | [Web](../../SpaceBattle.Web/README.md)

Each independent C# core implements circle-kernel-v1 in desktop and production Mono AOT WASM. Source links below show the SKECS implementation; corresponding implementations live in all five [core directories](ARCHITECTURE.md). Current-source measurements are in the [benchmark report](REPORT.md).

## Dynamics

[PhysicsService](../../SpaceBattle.Simulations/Skecs/PhysicsService.cs) stores one homogeneous
disk per ship: position, angle, linear/angular velocity, radius, mass and inertia
`I = 0.5 * mass * radius^2`. Normal circle impulses are radial and deliberately
do not create spin. Configurable Coulomb tangential friction uses a shared contact
point, contact-point velocity and the correct arm/inverse-inertia denominator.
It can exchange translational/rotational motion; there is no random spin.

[Settings](../../SpaceBattle.Core/PhysicsSettings.cs) are available through
`BattleConfig.Physics`. Defaults: restitution 0.1, friction 0.08, restitution
speed threshold 10 game units/s, eight velocity and eight positional iterations,
0.01-unit positional slop, 0.8 correction fraction and 0.5-unit maximum relative
correction per pair/iteration. Friction 0 recovers frictionless disk contacts,
not the old compound geometry. These settings are initial tuning, not a claim
of equivalent trajectories.

Velocity passes repeat over the complete coupled contact set when projection
introduces a new pair; resolving only that new pair would invalidate neighboring
constraints. Such rounds are bounded by the number of positional iterations.

Adaptive substeps limit ordinary ship travel to approximately half a radius;
the hard ceiling is eight. Extremely fast ship motion can still tunnel above
that ceiling: ships do not have an unbounded global time-of-impact solver.
Linear/angular velocities persist between steps without additional engine drag.
The existing steering damping remains outside the kernel.

Positional projection is inverse-mass weighted and changes positions only;
it does not inject corrective velocities or damage impulses. Unique begin-contact
events use canonical serial pairs and pre-solve point speed. The existing gameplay
rule remains: speed below 10 does no collision damage; above it, each hull receives
`(speed - 10) * 0.35` through the normal shield/armor path. Persistent contact is
not reported as a fresh impact every iteration. Docked/disabled ships are omitted
from contact, projectile and placement queries. Decorative particles and turrets
do not get ship bodies.

## Broadphase And CCD

[The grid](../../SpaceBattle.Simulations/Skecs/SpatialBoundsIndex.cs) inserts whole swept AABBs,
not just centers. It includes different radii, negative coordinates and cell
boundaries; query stamps deduplicate IDs. Large bounds use a bounded-membership
fallback, and long queries scan occupied cells rather than billions of empty ones.
Contact discovery is local; solver ordering is canonical by ship serial.

Targets retain every recorded substep endpoint. Projectile candidates use the
union of these target paths, while narrow tests map the projectile's tick segment
into each corresponding time interval. A target's excursion outside its final
cell is therefore not lost. The recorded path is piecewise linear between solver
outputs, including positional projection; it is not a reconstruction of an exact
continuous rigid-body trajectory through an instantaneous correction.

[Analytic tests](../../SpaceBattle.Simulations/Skecs/CircleSweepMath.cs) replace five offset rays:
disks solve relative segment vs the sum of radii; MachineGun/Cannon rounds use a
fixed-orientation rectangle with half-length `2 * Radius` and half-width `Radius`.
The rectangle-circle sum is exactly a rounded rectangle (side strips and corner
arcs), not a capsule or expanded box. Plasma, missiles, heavy rounds and fragments
use disks. The long raster trail is decorative, not physical length. Rectangle
heading is set before the tick's linear translation; there is no rotating-rectangle
sweep. Both shapes select earliest time, then lower serial for exactly equal times,
including tangency, start overlap and zero motion. Calculations use double
intermediates and a stable quadratic form; float positions still define the state.

Owner immunity remains ship-specific, including the parent of a turret. Teams do
not provide immunity; other allies/enemies can be hit. Sweeps run before
out-of-arena retirement so a fast round crossing a target and leaving the map
within the same tick does not silently skip that hit.

## Resource And Accuracy Limits

Reserved grid arrays and query buffers avoid per-query/per-step allocation after
body reservation. Contact buffers reserve all possible pairs up to a configurable
32,768-pair ceiling at ship creation, not inside the solver. Exceeding the explicit
per-phase/per-tick contact budget fails with a diagnostic instead of silently
dropping collisions or allocating unbounded memory. Spawn/removal/setup and
measurement serialization are not zero-allocation claims.

Dense real overlap still has an `O(N^2)` worst case; spatial partitioning only
removes unrelated candidates. Limited iterations can leave temporary penetration
in a pile, resolved over subsequent steps. Projection is not energy/momentum
conserving motion; conservation tests use contacts below the correction slop.
Replacing compound hulls with circles changes silhouette contacts, grazing hits,
spin, survivorship, waves and checksums. Fixed seed/order is verified within each
runtime; native/WASM bitwise determinism is not claimed.

## Verification And Measurements

[Kernel tests](../../SpaceBattle.Benchmarks/Measurement/PhysicsKernelValidation.cs) cover masses,
elastic momentum/energy, angular integration, friction torque/angular momentum,
contact lifecycle, docking, correction without explosions, dense stability and
reserved allocation. [Geometry tests](../../SpaceBattle.Benchmarks/Measurement/AnalyticSweepValidation.cs)
include 12,000 independent distance-oracle sweeps and 3,000 brute-force bounds
queries. Focused mechanics fixtures run on the native SKECS core. [Native replay checks](../../SpaceBattle.Benchmarks/Measurement/NativeReplayValidation.cs) compare the other native cores against SKECS at initial state and after every tick; validation and hashing run outside measured Step timers.

Run the [benchmark CLI](../../SpaceBattle.Benchmarks/README.md) with `validate space-battle` to execute the current checks. Measurements and raw evidence use the same CLI and remain separate from validation.

Solver background: [Erin Catto, Modeling and Solving Constraints](https://box2d.org/files/ErinCatto_ModelingAndSolvingConstraints_GDC2009.pdf). The implementation is project-authored C#.
