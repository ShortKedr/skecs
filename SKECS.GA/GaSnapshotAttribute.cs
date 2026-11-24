namespace SKECS.GA;

/// <summary>Declares the exact ordered snapshot payload read by a portable system.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class GaSnapshotAttribute : Attribute
{
    public GaSnapshotAttribute(params GaScalar[] fields)
    {
        if (fields is null)
            throw new ArgumentNullException(nameof(fields));
        if (fields.Length == 0)
            throw new ArgumentException("A system snapshot layout needs at least one field.", nameof(fields));
        Layout = new GaSnapshotLayout(fields);
    }

    public GaSnapshotLayout Layout { get; }
}
