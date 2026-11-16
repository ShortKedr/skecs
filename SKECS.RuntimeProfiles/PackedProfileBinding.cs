namespace SKECS.RuntimeProfiles;

/// <summary>The stable identity and layout required by a packed placement profile.</summary>
public sealed record PackedProfileComponent(string Assembly, string Type, int Size, int Alignment, string SchemaFingerprint)
{
    internal void Validate()
    {
        ProfileIdentity.RequireText(Assembly, nameof(Assembly));
        ProfileIdentity.RequireText(Type, nameof(Type));
        ProfileIdentity.RequireSha256(SchemaFingerprint, nameof(SchemaFingerprint));
        if (Size <= 0 || Alignment != Math.Min(16, Size & -Size))
            throw new ArgumentException("Invalid packed component geometry.");
    }
}

/// <summary>A component registered by application code for profile validation.</summary>
public sealed class PackedProfileBinding
{
    private PackedProfileBinding(Type type, PackedProfileComponent description)
    {
        RuntimeType = type;
        Description = description;
    }

    public PackedProfileComponent Description { get; }
    internal Type RuntimeType { get; }

    public static unsafe PackedProfileBinding Create<T>(string schemaFingerprint) where T : unmanaged
    {
        var type = typeof(T);
        var size = sizeof(T);
        // Match the core's unmanaged column alignment for the qualified build.
        var description = new PackedProfileComponent(type.Assembly.GetName().Name!, type.FullName!, size,
            Math.Min(16, size & -size), schemaFingerprint);
        description.Validate();
        return new PackedProfileBinding(type, description);
    }

    internal static ComponentPlacementOptions Resolve(PackedProfileComponent[] selected,
        ComponentPlacementOptions permissions, IReadOnlyList<PackedProfileBinding> bindings)
    {
        if (selected.Length == 0) throw new InvalidDataException("A packed profile requires at least one component.");
        var registry = new Dictionary<(string Assembly, string Type), PackedProfileBinding>();
        foreach (var binding in bindings)
        {
            if (binding == null) throw new ArgumentException("A packed profile binding cannot be null.", nameof(bindings));
            var description = binding.Description;
            var key = (description.Assembly, description.Type);
            if (registry.ContainsKey(key)) throw new ArgumentException($"Duplicate application binding for {description.Type}.", nameof(bindings));
            registry.Add(key, binding);
        }

        var types = new Type[selected.Length];
        var seen = new HashSet<Type>();
        for (var i = 0; i < selected.Length; i++)
        {
            var component = selected[i];
            component.Validate();
            if (!registry.TryGetValue((component.Assembly, component.Type), out var binding) || binding.Description != component)
                throw new InvalidDataException($"Packed component {component.Type} does not match a registered type, geometry and schema.");
            if (!seen.Add(binding.RuntimeType) || !permissions.CanUsePackedPool(binding.RuntimeType))
                throw new InvalidDataException($"Packed component {component.Type} is duplicated or ineligible.");
            types[i] = binding.RuntimeType;
        }
        return permissions.WithPackedPools(types);
    }
}
