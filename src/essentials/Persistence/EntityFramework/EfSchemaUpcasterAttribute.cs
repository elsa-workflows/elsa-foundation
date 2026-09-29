using Elsa.Persistence.Schema;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Names the version an <see cref="IEfSchemaUpcaster"/> reads and the one it produces (spec 180, FR-003): exactly one
/// version of one family into its immediate successor.
/// </summary>
/// <remarks>
/// Constant-argument-only, so <see cref="EfSchemaFamilyCatalog"/> reads a family's chain as metadata without running
/// any of its code, as it reads the family itself. Versions are opaque labels: nothing parses or orders them except the
/// chain they form (FR-004).
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EfSchemaUpcasterAttribute(string from, string to) : Attribute
{
    /// <summary>The version of the content this upcaster reads.</summary>
    public string From { get; } = from;

    /// <summary>The version it produces: <see cref="From"/>'s immediate successor.</summary>
    public string To { get; } = to;
}
