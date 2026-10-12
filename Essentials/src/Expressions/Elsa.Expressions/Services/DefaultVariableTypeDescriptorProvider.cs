using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;

namespace Elsa.Expressions.Services;

/// <summary>
/// Contributes the framework's built-in primitive <see cref="TypeDescriptor"/>s, keyed by bare aliases
/// (the reserved framework namespace). These are both the selectable picker entries (design-time) and,
/// via the seeding startup task, the resolvable alias↔CLR-type set (runtime).
/// </summary>
public sealed class DefaultVariableTypeDescriptorProvider : IVariableTypeDescriptorProvider
{
    private const string Category = "Primitives";
    private static readonly IReadOnlySet<CollectionKind> CollectionKinds = new HashSet<CollectionKind>(Enum.GetValues<CollectionKind>());
    private static readonly IReadOnlySet<string> JsonStorage = new HashSet<string>(StringComparer.Ordinal) { "elsa.json" };

    private static readonly TypeDescriptor[] Descriptors =
    [
        Descriptor("String", typeof(string), "String", "text", true),
        Descriptor("Boolean", typeof(bool), "Boolean", "checkbox", false),
        Descriptor("Int32", typeof(int), "Integer", "number", false),
        Descriptor("Int64", typeof(long), "Long", "number", false),
        Descriptor("Double", typeof(double), "Double", "number", false),
        Descriptor("Decimal", typeof(decimal), "Decimal", "number", false),
        Descriptor("DateTime", typeof(DateTime), "Date/Time", "date", false),
        Descriptor("DateTimeOffset", typeof(DateTimeOffset), "Date/Time Offset", "date", false),
        Descriptor("TimeSpan", typeof(TimeSpan), "Duration", "text", false),
        Descriptor("Guid", typeof(Guid), "GUID", "text", false),
        Descriptor("Object", typeof(object), "Object", "text", true)
    ];

    /// <inheritdoc />
    public IEnumerable<TypeDescriptor> GetDescriptors() => Descriptors;

    private static TypeDescriptor Descriptor(string alias, Type type, string displayName, string editor, bool supportsNull) =>
        new(alias, type, displayName, Category, editor, CollectionKinds, supportsNull, true, JsonStorage);
}
