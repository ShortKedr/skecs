using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SpaceBattle.Simulations.LeoEcsClassic;


/// <summary>Circle ship dynamics and analytic projectile CCD in game units, owned by this framework core.</summary>
internal sealed class PhysicsService : IDisposable
{
    private sealed class ShipBody(int serial, Transform transform, float radius, float mass, float inertia)
    {
        public readonly int Serial = serial;
        public readonly float Radius = radius, Mass = mass, Inertia = inertia;
        public readonly float InverseMass = 1 / mass, InverseInertia = 1 / inertia;
        public Vector2 Position = transform.Position, Velocity;
        public float Angle = transform.Angle, AngularVelocity;
        public bool Enabled = true;
        public readonly Vector2[] Path = new Vector2[9];
        public int PathSegments;
        public double MinX, MinY, MaxX, MaxY, FatMinX, FatMinY, FatMaxX, FatMaxY;
    }
    private struct Constraint
    {
        public int First, Second;
        public Vector2 Normal, FirstArm, SecondArm;
        public float Bias, NormalImpulse, TangentImpulse;
        public Vector2 Tangent;
        public float NormalMass;
        public double TangentMass;
    }
    private readonly record struct CandidatePair(int First, int Second);
    private sealed class SerialComparer(PhysicsService owner) : IComparer<int>
    {
        public int Compare(int a, int b) => owner.ships[a]!.Serial.CompareTo(owner.ships[b]!.Serial);
    }
    private readonly List<ShipBody?> ships = new();
    private readonly Stack<int> freeIds = new();
    private readonly List<int> active = new(), candidates = new();
    private readonly List<Constraint> constraints = new();
    private readonly SpatialBoundsIndex grid;
    private readonly List<CollisionEvent> contacts = new();
    private readonly IReadOnlyList<CollisionEvent> contactView;
    private readonly SerialComparer comparer;
    private readonly Comparison<int> compareSerial;
    private readonly HashSet<int> liveSerials = new();
    private readonly List<ShipBody> replayBodies = new();
    private readonly List<ulong> replayPairs = new();
    private HashSet<ulong> previousPairs = new(), currentPairs = new();
    private readonly HashSet<ulong> constraintPairs = new();
    private bool disposed, membershipDirty = true, gridDirty = true;
    private int sweepSegments = 1;
    private const int SweepCellCount = 64;
    private const double SweepMargin = 1e-3;
    private SweepRows sweepCellRows;
    private (double MinX, double MinY, double MaxX, double MaxY)[] sweepBodyBounds = [];
    private double sweepMinX, sweepMinY, sweepMaxX, sweepMaxY, sweepScaleX, sweepScaleY;
    private int sweepBodyCount;
    private long sweeps, emptySweeps, hitCandidates, narrowTests;
    private double contactCandidatesMs, solverMs, hitCandidatesMs, hitNarrowMs;
    private PhysicsWorkStatistics diagnostics;
    private CandidatePair[] potentialPairs = Array.Empty<CandidatePair>();
    private int potentialCount;
    private int[] broadBodies = [], broadOrder = [], broadOpen = [], broadSorted = [], broadIndexOfBody = [], broadStamp = [], broadStart = [];
    private int[] broadSeconds = [];
    private int broadSortedCount, broadEpoch;
    private double[] broadMinX = [], broadMinY = [], broadMaxX = [], broadMaxY = [];
    private ulong[] broadPairs = [];
    private readonly List<ulong> pairKeys = new();
    private bool candidateCacheValid, candidateFallback;

    public PhysicsSettings Settings { get; }
    public PhysicsService(PhysicsSettings? settings = null)
    {
        Settings = settings ?? new(); Settings.Validate();
        grid = new(cellSize: Settings.ContactCellSize, denseIds: true);
        grid.CollectDiagnostics = Settings.CollectDiagnostics;
        grid.CollectTimings = Settings.CollectTimings;
        comparer = new(this); compareSerial = comparer.Compare; contactView = contacts.AsReadOnly();
    }
    public IReadOnlyList<CollisionEvent> Contacts => contactView;
    public int Bodies { get; private set; }
    public int Fixtures => Bodies;
    public PhysicsQueryStatistics QueryStatistics => new(sweeps, emptySweeps, 0, 0, hitCandidates, narrowTests);
    public PhysicsTimingStatistics Timings => new(contactCandidatesMs, solverMs, hitCandidatesMs, hitNarrowMs);
    public PhysicsDiagnosticStatistics Diagnostics => new(diagnostics, grid.Diagnostics);

    // Observe future-affecting physics state with gameplay serials, independent of
    // recycled native body slots. Broadphase/solver scratch is rebuilt each Step.
    internal void AppendReplayState(List<uint> state)
    {
        replayBodies.Clear();
        foreach (var body in ships) if (body is not null) replayBodies.Add(body);
        replayBodies.Sort(static (first, second) => first.Serial.CompareTo(second.Serial));
        state.Add((uint)Bodies);
        foreach (var body in replayBodies)
        {
            state.Add((uint)body.Serial);
            state.Add(BitConverter.SingleToUInt32Bits(body.Radius));
            state.Add(BitConverter.SingleToUInt32Bits(body.Mass));
            state.Add(BitConverter.SingleToUInt32Bits(body.Inertia));
            state.Add(BitConverter.SingleToUInt32Bits(body.InverseMass));
            state.Add(BitConverter.SingleToUInt32Bits(body.InverseInertia));
        }
        // currentPairs becomes previousPairs at the next Step and suppresses
        // repeated begin-contact damage. Pair keys already contain ship serials.
        state.Add((uint)currentPairs.Count);
        replayPairs.Clear();
        foreach (var pair in currentPairs) replayPairs.Add(pair);
        replayPairs.Sort();
        foreach (var pair in replayPairs)
        {
            state.Add((uint)(pair >> 32));
            state.Add((uint)pair);
        }
    }

    public int AddShip(int serial, Transform transform, Collider collider)
    {
        ThrowIfDisposed(); ValidateTransform(transform);
        var inertia = (float)(0.5 * collider.Mass * collider.Radius * collider.Radius);
        if (!float.IsFinite(collider.Radius) || collider.Radius <= 0 || !float.IsFinite(collider.Mass) || collider.Mass <= 0 ||
            !float.IsFinite(inertia) || inertia <= 0 || !float.IsFinite(1 / collider.Mass) || !float.IsFinite(1 / inertia))
            throw new ArgumentOutOfRangeException(nameof(collider));
        if (!liveSerials.Add(serial)) throw new ArgumentException("Duplicate live ship serial.", nameof(serial));
        var body = new ShipBody(serial, transform, collider.Radius, collider.Mass, inertia);
        body.Path[0] = transform.Position;
        var id = freeIds.Count > 0 ? freeIds.Pop() : ships.Count;
        if (id == ships.Count) ships.Add(body); else ships[id] = body;
        Bodies++; membershipDirty = gridDirty = true;
        grid.Reserve(ships.Count);
        active.EnsureCapacity(ships.Count); candidates.EnsureCapacity(ships.Count);
        var pairCapacity = (int)Math.Min((long)ships.Count * (ships.Count - 1) / 2, Settings.MaximumContactPairs);
        constraints.EnsureCapacity(pairCapacity); contacts.EnsureCapacity(pairCapacity);
        previousPairs.EnsureCapacity(pairCapacity); currentPairs.EnsureCapacity(pairCapacity);
        constraintPairs.EnsureCapacity(pairCapacity); pairKeys.EnsureCapacity(pairCapacity);
        if (broadBodies.Length < ships.Count)
        {
            var bodies = Math.Max(ships.Count, broadBodies.Length * 2);
            Array.Resize(ref broadBodies, bodies); Array.Resize(ref broadOrder, bodies); Array.Resize(ref broadOpen, bodies);
            Array.Resize(ref broadSorted, bodies); Array.Resize(ref broadIndexOfBody, bodies); Array.Resize(ref broadStamp, bodies);
            Array.Resize(ref broadStart, bodies + 1);
            Array.Resize(ref broadMinX, bodies); Array.Resize(ref broadMinY, bodies);
            Array.Resize(ref broadMaxX, bodies); Array.Resize(ref broadMaxY, bodies);
            Array.Resize(ref broadPairs, checked(bodies * (bodies - 1) / 2)); Array.Resize(ref broadSeconds, broadPairs.Length);
        }
        var candidateCapacity = (int)Math.Min((long)ships.Count * (ships.Count - 1) / 2, Settings.MaximumCandidatePairs);
        if (candidateCapacity > potentialPairs.Length)
            Array.Resize(ref potentialPairs, (int)Math.Min(Settings.MaximumCandidatePairs,
                Math.Max(candidateCapacity, Math.Max(16L, (long)potentialPairs.Length * 2))));
        return id;
    }
    public void RemoveShip(int body)
    {
        ThrowIfDisposed();
        if ((uint)body >= (uint)ships.Count) throw new ArgumentOutOfRangeException(nameof(body));
        if (ships[body] is not { } ship) return;
        liveSerials.Remove(ship.Serial); ships[body] = null; freeIds.Push(body); Bodies--;
        membershipDirty = gridDirty = true;
    }
    public void SetShip(int body, in Transform transform, in Motion motion, bool enabled)
    {
        var ship = GetBody(body); ValidateTransform(transform);
        if (!Finite(motion.Velocity) || !float.IsFinite(motion.AngularVelocity))
            throw new ArgumentOutOfRangeException(nameof(motion));
        ship.Position = transform.Position; ship.Angle = transform.Angle;
        ship.Velocity = motion.Velocity; ship.AngularVelocity = motion.AngularVelocity; ship.Enabled = enabled;
        ship.Path[0] = ship.Position; ship.PathSegments = 0;
        gridDirty = true;
    }
    public (Vector2 Position, float Angle, Vector2 Velocity, float AngularVelocity) GetShip(int body)
    {
        var ship = GetBody(body);
        return (ship.Position, ship.Angle, ship.Velocity, ship.AngularVelocity);
    }
    public (float Mass, float Inertia) GetMassProperties(int body)
    {
        var ship = GetBody(body); return (ship.Mass, ship.Inertia);
    }
    public void Step(float dt)
    {
        ThrowIfDisposed();
        if (!float.IsFinite(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
        contacts.Clear(); sweeps = emptySweeps = hitCandidates = narrowTests = 0;
        contactCandidatesMs = solverMs = hitCandidatesMs = hitNarrowMs = 0;
        diagnostics = default; grid.ResetDiagnostics();
        EnsureActive();
        float travel = 0;
        foreach (var id in active)
        {
            var body = ships[id]!;
            body.Path[0] = body.Position; body.PathSegments = 0;
            if (body.Enabled) travel = Math.Max(travel, body.Velocity.Length() * dt / body.Radius);
        }
        if (dt == 0) { sweepSegments = 1; gridDirty = true; return; }
        (previousPairs, currentPairs) = (currentPairs, previousPairs); ClearPairs(currentPairs);
        var substeps = (int)Math.Clamp(Math.Ceiling(travel * 2.0), 1, Settings.MaximumSubsteps);
        sweepSegments = substeps;
        if (Settings.CollectDiagnostics) diagnostics.Substeps = substeps;
        var h = dt / substeps;
        for (var phase = 0; phase < substeps; phase++)
        {
            foreach (var id in active)
            {
                var body = ships[id]!;
                if (!body.Enabled) continue;
                var position = body.Position + body.Velocity * h;
                var angle = body.Angle + body.AngularVelocity * h;
                if (!Finite(position) || !float.IsFinite(angle)) throw new InvalidOperationException("Non-finite integration result.");
                body.Position = position; body.Angle = angle;
            }
            constraints.Clear(); ClearPairs(constraintPairs);
            candidateCacheValid = candidateFallback = false;
            BuildContacts();
            SolveVelocities(0);
            for (var iteration = 0; iteration < Settings.PositionIterations; iteration++)
            {
                var start = Stamp();
                if (Settings.CollectDiagnostics) diagnostics.PositionIterations++;
                var moved = CorrectPositions(); AddSolverTime(start);
                if (!moved) break;
                var firstNew = constraints.Count;
                BuildContacts();
                if (constraints.Count != firstNew)
                {
                    if (Settings.CollectDiagnostics) diagnostics.RepeatedVelocityPasses++;
                    SolveVelocities(0);
                }
            }
            foreach (var id in active)
            {
                var body = ships[id]!; body.Path[phase + 1] = body.Position; body.PathSegments = phase + 1;
            }
        }
        contacts.Sort(static (a, b) => a.FirstSerial != b.FirstSerial
            ? a.FirstSerial.CompareTo(b.FirstSerial) : a.SecondSerial.CompareTo(b.SecondSerial));
        if (Settings.CollectDiagnostics) diagnostics.Sorts++;
        gridDirty = true;
    }
    private void BuildContacts()
    {
        if (!Settings.CacheContactCandidates) { BuildContactsUncached(); return; }
        var start = Stamp();
        if (Settings.CollectDiagnostics) diagnostics.ContactBuilds++;
        var rebuild = !candidateCacheValid || !ContactBoundsStillCovered();
        if (!candidateFallback && rebuild) BuildPotentialPairs();
        if (candidateFallback)
        {
            // Budget overflow retains the complete exact broad phase, never truncates pairs.
            if (Settings.CollectDiagnostics) { diagnostics.CandidateFallbacks++; diagnostics.ContactBuilds--; }
            if (Settings.CollectTimings) contactCandidatesMs += Elapsed(start);
            BuildContactsUncached(); return;
        }
        else if (!rebuild && Settings.CollectDiagnostics) diagnostics.ReusedContactBuilds++;

        var narrowStart = Stamp();
        var next = 0;
        for (var i = 0; i < potentialCount; i++)
        {
            var pair = potentialPairs[i];
            if (Settings.CollectDiagnostics) diagnostics.CandidatePairsVisited++;
            var a = ships[pair.First]!; var b = ships[pair.Second]!;
            // Match the original tight AABB filter as well as its float narrow geometry.
            if (a.MinX > b.MaxX || a.MaxX < b.MinX || a.MinY > b.MaxY || a.MaxY < b.MinY ||
                !TryAddContact(pair.First, pair.Second)) potentialPairs[next++] = pair;
        }
        potentialCount = next;
        if (Settings.CollectTimings)
        {
            diagnostics.ContactNarrowMs += Elapsed(narrowStart);
            contactCandidatesMs += Elapsed(start);
        }
    }
    private void CurrentContactBounds(ShipBody body) => SpatialBoundsIndex.SweptBounds(body.Position, body.Position,
        body.Radius + Settings.PositionSlop, out body.MinX, out body.MinY, out body.MaxX, out body.MaxY);

    private bool ContactBoundsStillCovered()
    {
        foreach (var id in active)
        {
            var body = ships[id]!; if (!body.Enabled) continue;
            CurrentContactBounds(body);
            if (body.MinX < body.FatMinX || body.MinY < body.FatMinY ||
                body.MaxX > body.FatMaxX || body.MaxY > body.FatMaxY) return false;
        }
        return true;
    }
    // Pairs of enabled bodies whose fat bounds overlap, ordered by the serials of the first and the second body:
    // the active list is in serial order, so ordering by the active indices gives the serial order.
    private void BuildPotentialPairs()
    {
        potentialCount = 0;
        if (Settings.CollectDiagnostics) diagnostics.CandidateCacheBuilds++;
        var count = 0;
        if (++broadEpoch == int.MaxValue) { Array.Clear(broadStamp); broadEpoch = 1; }
        foreach (var id in active)
        {
            var body = ships[id]!; if (!body.Enabled) continue;
            CurrentContactBounds(body);
            body.FatMinX = Math.BitDecrement(body.MinX - Settings.ContactCandidateMargin);
            body.FatMinY = Math.BitDecrement(body.MinY - Settings.ContactCandidateMargin);
            body.FatMaxX = Math.BitIncrement(body.MaxX + Settings.ContactCandidateMargin);
            body.FatMaxY = Math.BitIncrement(body.MaxY + Settings.ContactCandidateMargin);
            broadBodies[count] = id; broadIndexOfBody[id] = count; broadStamp[id] = broadEpoch;
            broadMinX[count] = body.FatMinX; broadMinY[count] = body.FatMinY;
            broadMaxX[count] = body.FatMaxX; broadMaxY[count] = body.FatMaxY;
            count++;
        }
        // Bodies barely move between builds: start from the previous x order and finish with an insertion sort.
        var ordered = 0;
        for (var k = 0; k < broadSortedCount; k++)
        {
            var id = broadSorted[k];
            if (broadStamp[id] != broadEpoch) continue;
            broadStamp[id] = -broadEpoch; broadOrder[ordered++] = broadIndexOfBody[id];
        }
        for (var index = 0; index < count; index++)
            if (broadStamp[broadBodies[index]] == broadEpoch) broadOrder[ordered++] = index;
        for (var k = 1; k < count; k++)
        {
            var current = broadOrder[k]; var key = broadMinX[current]; var j = k - 1;
            while (j >= 0 && broadMinX[broadOrder[j]] > key) { broadOrder[j + 1] = broadOrder[j]; j--; }
            broadOrder[j + 1] = current;
        }
        for (var k = 0; k < count; k++) broadSorted[k] = broadBodies[broadOrder[k]];
        broadSortedCount = count;
        int pairs = 0, open = 0;
        for (var k = 0; k < count; k++)
        {
            var current = broadOrder[k]; var kept = 0;
            for (var o = 0; o < open; o++)
            {
                var other = broadOpen[o];
                if (broadMaxX[other] < broadMinX[current]) continue;
                broadOpen[kept++] = other;
                if (broadMinY[other] > broadMaxY[current] || broadMaxY[other] < broadMinY[current]) continue;
                broadPairs[pairs++] = other < current ? (ulong)other << 32 | (uint)current : (ulong)current << 32 | (uint)other;
            }
            open = kept; broadOpen[open++] = current;
        }
        Array.Clear(broadStart, 0, count + 1);
        for (var p = 0; p < pairs; p++) broadStart[(int)(broadPairs[p] >> 32) + 1]++;
        for (var first = 0; first < count; first++) { broadStart[first + 1] += broadStart[first]; broadOpen[first] = broadStart[first]; }
        for (var p = 0; p < pairs; p++) broadSeconds[broadOpen[(int)(broadPairs[p] >> 32)]++] = (int)(uint)broadPairs[p];
        for (var first = 0; first < count; first++)
        {
            int begin = broadStart[first], end = broadStart[first + 1];
            for (var k = begin + 1; k < end; k++)
            {
                var second = broadSeconds[k]; var j = k - 1;
                while (j >= begin && broadSeconds[j] > second) { broadSeconds[j + 1] = broadSeconds[j]; j--; }
                broadSeconds[j + 1] = second;
            }
            for (var k = begin; k < end; k++)
            {
                var firstId = broadBodies[first]; var secondId = broadBodies[broadSeconds[k]];
                if (constraintPairs.Contains(Pair(ships[firstId]!.Serial, ships[secondId]!.Serial))) continue;
                if (potentialCount == potentialPairs.Length)
                {
                    candidateFallback = true; candidateCacheValid = false; return;
                }
                potentialPairs[potentialCount++] = new(firstId, secondId);
            }
        }
        candidateCacheValid = true;
    }
    private void ClearPairs(HashSet<ulong> pairs)
    {
        pairKeys.Clear();
        foreach (var key in pairs) pairKeys.Add(key);
        foreach (var key in pairKeys) pairs.Remove(key);
    }
    private void BuildContactsUncached()
    {
        var start = Stamp(); grid.Clear();
        if (Settings.CollectDiagnostics) diagnostics.ContactBuilds++;
        foreach (var id in active)
        {
            var body = ships[id]!;
            if (body.Enabled) grid.Add(id, body.Position, body.Position, body.Radius + Settings.PositionSlop);
        }
        grid.Seal();
        foreach (var firstId in active)
        {
            var a = ships[firstId]!; if (!a.Enabled) continue;
            var queryStart = Stamp();
            grid.Query(a.Position, a.Position, a.Radius + Settings.PositionSlop, candidates, sort: false);
            candidates.Sort(compareSerial);
            if (Settings.CollectTimings) diagnostics.ContactQueryMs += Elapsed(queryStart);
            var narrowStart = Stamp();
            if (Settings.CollectDiagnostics) { diagnostics.ContactCandidates += candidates.Count; diagnostics.Sorts++; }
            foreach (var secondId in candidates)
            {
                var b = ships[secondId]!;
                if (b.Serial <= a.Serial) continue;
                TryAddContact(firstId, secondId);
            }
            if (Settings.CollectTimings) diagnostics.ContactNarrowMs += Elapsed(narrowStart);
        }
        if (Settings.CollectTimings) contactCandidatesMs += Elapsed(start);
    }
    private bool TryAddContact(int firstId, int secondId)
    {
        var a = ships[firstId]!; var b = ships[secondId]!;
        if (Settings.CollectDiagnostics) diagnostics.ContactTests++;
        var sum = (double)a.Radius + b.Radius;
        var delta = b.Position - a.Position;
        if ((double)delta.X * delta.X + (double)delta.Y * delta.Y > (sum + Settings.PositionSlop) * (sum + Settings.PositionSlop)) return false;
        var key = Pair(a.Serial, b.Serial);
        if (Settings.CollectDiagnostics) diagnostics.ContactPairs++;
        if (constraints.Count >= Settings.MaximumContactPairs && !constraintPairs.Contains(key))
            throw new InvalidOperationException("Physics contact budget exceeded; increase MaximumContactPairs or reduce density.");
        if (!constraintPairs.Add(key)) return true;
        if (Settings.CollectDiagnostics) diagnostics.NewConstraints++;
        var distance = Math.Sqrt((double)delta.X * delta.X + (double)delta.Y * delta.Y);
        var normal = distance > 0 ? new Vector2((float)(delta.X / distance), (float)(delta.Y / distance)) : Vector2.UnitX;
        var point = a.Position + normal * (float)((distance + a.Radius - b.Radius) * 0.5);
        var armA = point - a.Position; var armB = point - b.Position;
        var relative = PointVelocity(b.Velocity, b.AngularVelocity, armB) - PointVelocity(a.Velocity, a.AngularVelocity, armA);
        var speed = Vector2.Dot(relative, normal);
        var tangent = default(Vector2); double denominator = 0;
        if (Settings.Friction > 0)
        {
            tangent = new Vector2(-normal.Y, normal.X);
            var ra = Cross(armA, tangent); var rb = Cross(armB, tangent);
            denominator = (double)a.InverseMass + b.InverseMass + (double)ra * ra * a.InverseInertia + (double)rb * rb * b.InverseInertia;
        }
        constraints.Add(new() { First = firstId, Second = secondId, Normal = normal, FirstArm = armA, SecondArm = armB,
            Bias = speed < -Settings.RestitutionThreshold ? -Settings.Restitution * speed : 0,
            Tangent = tangent, NormalMass = a.InverseMass + b.InverseMass, TangentMass = denominator });
        if (!currentPairs.Contains(key) && currentPairs.Count >= Settings.MaximumContactPairs)
            throw new InvalidOperationException("Physics per-tick contact budget exceeded.");
        if (currentPairs.Add(key) && !previousPairs.Contains(key))
        {
            if (contacts.Count >= Settings.MaximumContactPairs) throw new InvalidOperationException("Physics begin-contact budget exceeded.");
            contacts.Add(new(a.Serial, b.Serial, relative.Length(), point));
        }
        return true;
    }
    private void SolveVelocities(int first)
    {
        var start = Stamp();
        if (Settings.CollectDiagnostics) { diagnostics.VelocityPasses++; diagnostics.VelocityConstraintVisits += (long)(constraints.Count - first) * Settings.VelocityIterations; }
        // This method and ApplyImpulse only mutate values; no list growth or structural calls occur.
        var values = CollectionsMarshal.AsSpan(constraints);
        var count = values.Length;
        for (var iteration = 0; iteration < Settings.VelocityIterations; iteration++)
            for (var i = first; i < count; i++)
            {
                ref var c = ref values[i]; var a = ships[c.First]!; var b = ships[c.Second]!;
                var relative = PointVelocity(b.Velocity, b.AngularVelocity, c.SecondArm) - PointVelocity(a.Velocity, a.AngularVelocity, c.FirstArm);
                var impulse = (c.Bias - Vector2.Dot(relative, c.Normal)) / c.NormalMass;
                var accumulated = Math.Max(0, c.NormalImpulse + impulse);
                ApplyImpulse(a, b, c.Normal * (accumulated - c.NormalImpulse), c.FirstArm, c.SecondArm, false);
                c.NormalImpulse = accumulated;
                if (Settings.Friction > 0)
                {
                    var tangent = c.Tangent;
                    relative = PointVelocity(b.Velocity, b.AngularVelocity, c.SecondArm) - PointVelocity(a.Velocity, a.AngularVelocity, c.FirstArm);
                    var limit = Settings.Friction * c.NormalImpulse;
                    var friction = (float)Math.Clamp(c.TangentImpulse - Vector2.Dot(relative, tangent) / c.TangentMass, -limit, limit);
                    ApplyImpulse(a, b, tangent * (friction - c.TangentImpulse), c.FirstArm, c.SecondArm, true);
                    c.TangentImpulse = friction;
                }
            }
        AddSolverTime(start);
    }
    private bool CorrectPositions()
    {
        var moved = false;
        if (Settings.CollectDiagnostics) diagnostics.PositionConstraintVisits += constraints.Count;
        foreach (ref readonly var c in CollectionsMarshal.AsSpan(constraints))
        {
            var a = ships[c.First]!; var b = ships[c.Second]!; var delta = b.Position - a.Position;
            var distance = Math.Sqrt((double)delta.X * delta.X + (double)delta.Y * delta.Y);
            var penetration = (double)a.Radius + b.Radius - distance - Settings.PositionSlop;
            if (penetration <= 0.0001f) continue;
            var normal = distance > 0 ? new Vector2((float)(delta.X / distance), (float)(delta.Y / distance)) : c.Normal;
            var correction = (float)(Math.Min(Settings.MaximumCorrection, penetration * Settings.CorrectionFraction) / ((double)a.InverseMass + b.InverseMass));
            a.Position -= normal * (correction * a.InverseMass); b.Position += normal * (correction * b.InverseMass);
            moved = true;
        }
        return moved;
    }
    public bool Sweep(Vector2 from, Vector2 to, float radius, out int targetSerial, out Vector2 impact)
        => Sweep(from, to, radius, -1, out targetSerial, out impact);
    public bool Sweep(Vector2 from, Vector2 to, float radius, int ignoredSerial, out int targetSerial, out Vector2 impact)
        => SweepCore(from, to, radius, default, 0, ignoredSerial, false, true, out targetSerial, out impact);
    internal bool SweepUnfiltered(Vector2 from, Vector2 to, float radius, out int targetSerial, out Vector2 impact, int ignoredSerial = -1)
        => SweepCore(from, to, radius, default, 0, ignoredSerial, false, false, out targetSerial, out impact);
    public bool SweepRectangle(Vector2 from, Vector2 to, Vector2 halfExtents, float angle, int ignoredSerial, out int targetSerial, out Vector2 impact)
        => SweepCore(from, to, Math.BitIncrement(Math.Sqrt((double)halfExtents.X * halfExtents.X + (double)halfExtents.Y * halfExtents.Y)),
            halfExtents, angle, ignoredSerial, true, true, out targetSerial, out impact);
    private bool SweepCore(Vector2 from, Vector2 to, double radius, Vector2 extents, float angle, int ignoredSerial,
        bool rectangle, bool filtered, out int targetSerial, out Vector2 impact)
    {
        ThrowIfDisposed();
        if (!Finite(from) || !Finite(to) || !double.IsFinite(radius) || radius < 0 ||
            rectangle && (!Finite(extents) || extents.X < 0 || extents.Y < 0 || !float.IsFinite(angle)))
            throw new ArgumentOutOfRangeException(nameof(radius));
        EnsureActive(); sweeps++;
        var start = Stamp();
        if (filtered)
        {
            EnsureSweepGrid();
            grid.QueryBounds(Math.BitDecrement(Math.Min(from.X, to.X) - radius), Math.BitDecrement(Math.Min(from.Y, to.Y) - radius),
                Math.BitIncrement(Math.Max(from.X, to.X) + radius), Math.BitIncrement(Math.Max(from.Y, to.Y) + radius), candidates);
        }
        else { candidates.Clear(); candidates.AddRange(active); }
        if (Settings.CollectTimings) hitCandidatesMs += Elapsed(start);
        start = Stamp();
        targetSerial = -1; impact = to; double best = double.PositiveInfinity;
        var eligible = 0;
        var segments = sweepSegments;
        double shotDx = 0, shotDy = 0, cosine = 0, sine = 0;
        var prepared = false;
        foreach (var id in candidates)
        {
            var body = ships[id]!;
            if (!body.Enabled || body.Serial == ignoredSerial) continue;
            hitCandidates++; eligible++;
            if (Settings.CollectDiagnostics) diagnostics.CcdCandidates++;
            if (!prepared)
            {
                shotDx = ((double)to.X - from.X) / segments;
                shotDy = ((double)to.Y - from.Y) / segments;
                if (rectangle) { cosine = Math.Cos(angle); sine = Math.Sin(angle); }
                prepared = true;
            }
            for (var phase = 0; phase < segments; phase++)
            {
                // This interval cannot precede its start; equality still participates in serial ties.
                if (phase != 0 && (double)phase / segments > best)
                {
                    if (Settings.CollectDiagnostics) diagnostics.PrunedCcdIntervals += segments - phase;
                    break;
                }
                var targetFrom = body.PathSegments == 0 ? body.Position : body.Path[phase];
                var targetTo = body.PathSegments == 0 ? body.Position : body.Path[phase + 1];
                var shotX = from.X + shotDx * phase; var shotY = from.Y + shotDy * phase;
                var rx = shotX - targetFrom.X; var ry = shotY - targetFrom.Y;
                var dx = shotDx - ((double)targetTo.X - targetFrom.X);
                var dy = shotDy - ((double)targetTo.Y - targetFrom.Y);
                narrowTests++;
                if (Settings.CollectDiagnostics) diagnostics.CcdNarrowTests++;
                double local;
                var hit = rectangle
                    ? CircleSweepMath.TryRectangleRelative(-rx, -ry, -dx, -dy, extents, cosine, sine, body.Radius, out local)
                    : CircleSweepMath.TryCircleRelative(rx, ry, dx, dy, (double)body.Radius + radius, out local);
                if (!hit) continue;
                var fraction = (phase + local) / segments;
                if (fraction > best || fraction == best && body.Serial >= targetSerial) continue;
                best = fraction; targetSerial = body.Serial;
                var centerX = from.X + ((double)to.X - from.X) * fraction;
                var centerY = from.Y + ((double)to.Y - from.Y) * fraction;
                var targetX = targetFrom.X + ((double)targetTo.X - targetFrom.X) * local;
                var targetY = targetFrom.Y + ((double)targetTo.Y - targetFrom.Y) * local;
                var nx = centerX - targetX; var ny = centerY - targetY;
                if (rectangle)
                {
                    var localX = targetX - centerX; var localY = targetY - centerY;
                    var x = Math.Clamp(localX * cosine + localY * sine, -extents.X, extents.X);
                    var y = Math.Clamp(-localX * sine + localY * cosine, -extents.Y, extents.Y);
                    var normalX = centerX + x * cosine - y * sine - targetX;
                    var normalY = centerY + x * sine + y * cosine - targetY;
                    if (normalX != 0 || normalY != 0) { nx = normalX; ny = normalY; }
                }
                var length = Math.Sqrt(nx * nx + ny * ny);
                if (length == 0) { nx = 1; ny = 0; length = 1; }
                impact = new((float)(targetX + nx / length * body.Radius), (float)(targetY + ny / length * body.Radius));
            }
        }
        if (eligible == 0) emptySweeps++;
        if (Settings.CollectTimings) hitNarrowMs += Elapsed(start);
        return double.IsFinite(best);
    }
    public bool CanSpawn(Vector2 position, float radius)
    {
        ThrowIfDisposed();
        if (!Finite(position) || !float.IsFinite(radius) || radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
        EnsureSweepGrid(); grid.Query(position, position, radius, candidates);
        foreach (var id in candidates)
        {
            var body = ships[id]!; if (!body.Enabled) continue;
            var delta = body.Position - position; var sum = (double)radius + body.Radius;
            if ((double)delta.X * delta.X + (double)delta.Y * delta.Y < sum * sum) return false;
        }
        return true;
    }
    private void EnsureSweepGrid()
    {
        if (!gridDirty) return;
        EnsureActive(); grid.Clear();
        if (sweepBodyBounds.Length < active.Count) Array.Resize(ref sweepBodyBounds, active.Count);
        sweepBodyCount = 0;
        foreach (var id in active)
        {
            var body = ships[id]!; if (!body.Enabled) continue;
            double minX = body.Position.X, maxX = minX, minY = body.Position.Y, maxY = minY;
            for (var p = 0; p <= body.PathSegments; p++)
            {
                minX = Math.Min(minX, body.Path[p].X); maxX = Math.Max(maxX, body.Path[p].X);
                minY = Math.Min(minY, body.Path[p].Y); maxY = Math.Max(maxY, body.Path[p].Y);
            }
            var bounds = (minX - body.Radius, minY - body.Radius, maxX + body.Radius, maxY + body.Radius);
            grid.AddBounds(id, bounds.Item1, bounds.Item2, bounds.Item3, bounds.Item4);
            sweepBodyBounds[sweepBodyCount++] = bounds;
        }
        grid.Seal();
        MarkSweepCells();
        gridDirty = false;
    }
    private void MarkSweepCells()
    {
        sweepCellRows = default;
        sweepMinX = sweepMinY = double.PositiveInfinity; sweepMaxX = sweepMaxY = double.NegativeInfinity;
        for (var i = 0; i < sweepBodyCount; i++)
        {
            var bounds = sweepBodyBounds[i];
            sweepMinX = Math.Min(sweepMinX, bounds.MinX); sweepMinY = Math.Min(sweepMinY, bounds.MinY);
            sweepMaxX = Math.Max(sweepMaxX, bounds.MaxX); sweepMaxY = Math.Max(sweepMaxY, bounds.MaxY);
        }
        sweepScaleX = sweepMaxX > sweepMinX ? SweepCellCount / (sweepMaxX - sweepMinX) : 0;
        sweepScaleY = sweepMaxY > sweepMinY ? SweepCellCount / (sweepMaxY - sweepMinY) : 0;
        for (var i = 0; i < sweepBodyCount; i++)
        {
            var bounds = sweepBodyBounds[i];
            var columns = SweepColumns(SweepCell(bounds.MinX, sweepMinX, sweepScaleX), SweepCell(bounds.MaxX, sweepMinX, sweepScaleX));
            var top = SweepCell(bounds.MaxY, sweepMinY, sweepScaleY);
            for (var row = SweepCell(bounds.MinY, sweepMinY, sweepScaleY); row <= top; row++) sweepCellRows[row] |= columns;
        }
    }
    private static int SweepCell(double value, double minimum, double scale)
    {
        var cell = (value - minimum) * scale;
        return cell <= 0 ? 0 : cell >= SweepCellCount - 1 ? SweepCellCount - 1 : (int)cell;
    }
    private static ulong SweepColumns(int first, int last) => (ulong.MaxValue >> (SweepCellCount - 1 - last)) & (ulong.MaxValue << first);
    public SweepCells SweepCellMask()
    {
        ThrowIfDisposed();
        if (gridDirty) EnsureSweepGrid();
        return new() { Rows = sweepCellRows, Occupied = sweepBodyCount > 0, MinX = sweepMinX, MinY = sweepMinY,
            MaxX = sweepMaxX, MaxY = sweepMaxY, ScaleX = sweepScaleX, ScaleY = sweepScaleY };
    }
    public void CountEmptySweep() { sweeps++; emptySweeps++; }
    // False only when Sweep would find no candidate: the margin covers the one-ulp widening of Sweep's query bounds
    // and SweepCell is monotonic, so any grid bounds overlapping them share a marked cell.
    [InlineArray(SweepCellCount)] internal struct SweepRows { private ulong first; }
    internal struct SweepCells
    {
        public SweepRows Rows;
        public bool Occupied;
        public double MinX, MinY, MaxX, MaxY, ScaleX, ScaleY;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool MayHit(Vector2 from, Vector2 to, float radius) => MayHit(from, to, (double)radius);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool MayHitRectangle(Vector2 from, Vector2 to, Vector2 halfExtents, float angle) =>
            !Finite(halfExtents) || halfExtents.X < 0 || halfExtents.Y < 0 || !float.IsFinite(angle) ||
            MayHit(from, to, Math.Sqrt((double)halfExtents.X * halfExtents.X + (double)halfExtents.Y * halfExtents.Y));
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private readonly bool MayHit(Vector2 from, Vector2 to, double radius)
        {
            if (!Finite(from) || !Finite(to) || !double.IsFinite(radius) || radius < 0) return true;
            var reach = radius + SweepMargin;
            var queryMinX = Math.Min(from.X, to.X) - reach; var queryMaxX = Math.Max(from.X, to.X) + reach;
            var queryMinY = Math.Min(from.Y, to.Y) - reach; var queryMaxY = Math.Max(from.Y, to.Y) + reach;
            if (!Occupied || MinX > queryMaxX || MaxX < queryMinX || MinY > queryMaxY || MaxY < queryMinY) return false;
            var columns = SweepColumns(SweepCell(queryMinX, MinX, ScaleX), SweepCell(queryMaxX, MinX, ScaleX));
            var top = SweepCell(queryMaxY, MinY, ScaleY);
            for (var row = SweepCell(queryMinY, MinY, ScaleY); row <= top; row++)
                if ((Rows[row] & columns) != 0) return true;
            return false;
        }
    }
    private void EnsureActive()
    {
        if (!membershipDirty) return;
        active.Clear();
        for (var id = 0; id < ships.Count; id++) if (ships[id] is not null) active.Add(id);
        active.Sort(compareSerial); membershipDirty = false;
    }
    private ShipBody GetBody(int id)
    {
        ThrowIfDisposed();
        if ((uint)id >= (uint)ships.Count || ships[id] is not { } body) throw new ArgumentOutOfRangeException(nameof(id));
        return body;
    }
    private static void ApplyImpulse(ShipBody a, ShipBody b, Vector2 impulse, Vector2 armA, Vector2 armB, bool angular)
    {
        a.Velocity -= impulse * a.InverseMass; b.Velocity += impulse * b.InverseMass;
        if (angular) { a.AngularVelocity -= Cross(armA, impulse) * a.InverseInertia; b.AngularVelocity += Cross(armB, impulse) * b.InverseInertia; }
    }
    private static Vector2 PointVelocity(Vector2 velocity, float angular, Vector2 arm) => velocity + new Vector2(-arm.Y, arm.X) * angular;
    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
    private static ulong Pair(int a, int b) => ((ulong)(uint)a << 32) | (uint)b;
    private static bool Finite(Vector2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);
    private static void ValidateTransform(Transform t)
    {
        if (!Finite(t.Position) || !float.IsFinite(t.Angle)) throw new ArgumentOutOfRangeException(nameof(t));
    }
    private long Stamp() => Settings.CollectTimings ? Stopwatch.GetTimestamp() : 0;
    private static double Elapsed(long start) => (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    private void AddSolverTime(long start) { if (Settings.CollectTimings) solverMs += Elapsed(start); }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
    public void Dispose()
    {
        if (disposed) return;
        ships.Clear(); freeIds.Clear(); active.Clear(); candidates.Clear(); constraints.Clear(); contacts.Clear(); liveSerials.Clear();
        previousPairs.Clear(); currentPairs.Clear(); constraintPairs.Clear(); grid.Clear(); Bodies = 0; disposed = true;
        potentialPairs = Array.Empty<CandidatePair>(); potentialCount = 0;
    }
}
