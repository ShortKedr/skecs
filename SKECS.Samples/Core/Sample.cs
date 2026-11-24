using SKECS;

namespace Skecs.Samples.Core;

internal static class Sample
{
    public static Task RunAsync(string[] args)
    {
        if (args.Length != 0)
            throw new ArgumentException("The core sample takes no arguments.");

        using var world = new World(new WorldOptions(chunkCapacity: 128));
        var entity = world.CreateEntity(new Position(0, 0), new Velocity(3, 1));

        world.AddSystem(SystemStage.Update, static current =>
        {
            var move = new Move();
            current.Query<Position, Velocity>().ForEach(ref move);
        });

        for (var tick = 1; tick <= 5; tick++)
        {
            world.Run(SystemStage.Update);
            var position = world.Get<Position>(entity);
            Console.WriteLine($"Tick {tick}: ({position.X}, {position.Y})");
        }

        if (world.Get<Position>(entity) != new Position(15, 5))
            throw new InvalidOperationException("Expected the entity to finish at (15, 5).");

        return Task.CompletedTask;
    }

    private readonly record struct Position(int X, int Y);
    private readonly record struct Velocity(int X, int Y);

    private struct Move : IQueryProcessor<Position, Velocity>
    {
        public void Execute(Entity entity, ref Position position, ref Velocity velocity) =>
            position = new Position(position.X + velocity.X, position.Y + velocity.Y);
    }
}
