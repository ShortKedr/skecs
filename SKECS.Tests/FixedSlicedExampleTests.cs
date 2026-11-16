using SKECS.Numerics;

namespace SKECS.Tests;

public sealed class FixedSlicedExampleTests
{
    [Fact]
    public void DisplayKeepsTheLastCompletedPositionWhileFixedMovementIsPending()
    {
        using var world = new World();
        var ship = world.CreateEntity(Fixed.Zero, new Velocity(Fixed.FromInt32(3)));
        for (var i = 0; i < DeterministicRunner<Controls>.LogicalJobSize; i++)
            world.CreateEntity(Fixed.Zero, new Velocity(Fixed.Zero));

        var movement = new DeterministicQuery().With<Fixed>().With<Velocity>().ReadWrite<Fixed>().Read<Velocity>();
        var workload = new DeterministicWorkload<Controls>().AddSystem(SystemStage.Update, movement, new Move());
        using var runner = new DeterministicRunner<Controls>(world, workload);

        var displayedPosition = world.Get<Fixed>(ship);
        var controls = new Controls(Fixed.FromRatio(1, 2));
        runner.BeginTick(0, controls, Fixed.FromRatio(1, 4));
        Assert.Equal(DeterministicTickStatus.Pending, runner.Advance(1));
        Assert.Equal(Fixed.Zero, displayedPosition);
        Assert.Equal(DeterministicTickStatus.Completed, runner.Advance(1));
        displayedPosition = world.Get<Fixed>(ship);
        Assert.Equal(Fixed.FromRatio(3, 8), displayedPosition);
    }

    private readonly record struct Controls(Fixed Throttle);
    private readonly record struct Velocity(Fixed Value);

    private readonly struct Move : IDeterministicJobProcessor<Controls>
    {
        public void Execute(DeterministicJob<Controls> job)
        {
            for (var row = 0; row < job.Count; row++)
            {
                var displacement = job.Read<Velocity>(row).Value * job.Input.Throttle * job.Step;
                job.Write(row, job.Read<Fixed>(row) + displacement);
            }
        }
    }
}
