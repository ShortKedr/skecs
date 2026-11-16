using SKECS.Serialization;

namespace SKECS.Determinism;

internal static class ReplaySchema
{
    internal const uint TransformId = 1001;
    internal const uint MotionId = 1002;
    internal const uint MarkerId = 1003;
    internal const uint First = 1;
    internal const uint Second = 2;

    internal static readonly CodecRegistry Registry = new CodecRegistry()
        .Register(new ComponentSchema(TransformId, 1, new FieldSchema(First, FieldKind.Int32),
            new FieldSchema(Second, FieldKind.Int32)), new TransformCodec())
        .Register(new ComponentSchema(MotionId, 1, new FieldSchema(First, FieldKind.Int32),
            new FieldSchema(Second, FieldKind.Int32)), new MotionCodec())
        .Register(new ComponentSchema(MarkerId, 1, new FieldSchema(First, FieldKind.UInt8),
            new FieldSchema(Second, FieldKind.Int32)), new MarkerCodec());

    internal static EntityState Capture(World world, World.CheckpointView checkpoint, int key, Entity entity)
    {
        Transform? transform = null;
        Motion? motion = null;
        Marker? marker = null;
        var count = checkpoint.GetComponentCount(entity);
        for (var component = 0; component < count; component++)
        {
            var schema = Registry.GetSchema(checkpoint.GetComponentType(entity, component));
            FieldValue first = default;
            FieldValue second = default;
            Registry.VisitFields(world, entity, schema.Id, (field, value) =>
            {
                switch (field.Tag)
                {
                    case First: first = value; break;
                    case Second: second = value; break;
                    default: throw new InvalidDataException($"Unexpected replay field tag {schema.Id}/{field.Tag}.");
                }
            });

            switch (schema.Id)
            {
                case TransformId: transform = new Transform(first.AsInt32(), second.AsInt32()); break;
                case MotionId: motion = new Motion(first.AsInt32(), second.AsInt32()); break;
                case MarkerId: marker = new Marker(first.AsUInt8(), second.AsInt32()); break;
                default: throw new InvalidDataException($"Unexpected replay component {schema.Id}.");
            }
        }
        return new EntityState(key, entity.Id, entity.Version, transform, motion, marker);
    }

    private sealed class TransformCodec : IComponentCodec<Transform>
    {
        public void Write(in Transform value, ComponentWriter fields)
        {
            fields.Set(First, FieldValue.From(value.X));
            fields.Set(Second, FieldValue.From(value.Y));
        }

        public Transform Read(ComponentReader fields)
            => new(fields.Get(First).AsInt32(), fields.Get(Second).AsInt32());
    }

    private sealed class MotionCodec : IComponentCodec<Motion>
    {
        public void Write(in Motion value, ComponentWriter fields)
        {
            fields.Set(First, FieldValue.From(value.Dx));
            fields.Set(Second, FieldValue.From(value.Dy));
        }

        public Motion Read(ComponentReader fields)
            => new(fields.Get(First).AsInt32(), fields.Get(Second).AsInt32());
    }

    private sealed class MarkerCodec : IComponentCodec<Marker>
    {
        public void Write(in Marker value, ComponentWriter fields)
        {
            fields.Set(First, FieldValue.From(value.Kind));
            fields.Set(Second, FieldValue.From(value.Value));
        }

        public Marker Read(ComponentReader fields)
            => new(fields.Get(First).AsUInt8(), fields.Get(Second).AsInt32());
    }
}
