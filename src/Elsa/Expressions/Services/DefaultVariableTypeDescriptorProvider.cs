using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.Services;

/// <summary>
/// Contributes the framework's built-in primitive <see cref="TypeDescriptor"/>s, keyed by bare aliases
/// (the reserved framework namespace). These are both the selectable picker entries (design-time) and,
/// via the seeding startup task, the resolvable alias↔CLR-type set (runtime).
/// </summary>
public sealed class DefaultVariableTypeDescriptorProvider : IVariableTypeDescriptorProvider
{
    private const string Category = "Primitives";

    private static readonly TypeDescriptor[] Descriptors =
    [
        new("String", typeof(string), "String", Category, "text"),
        new("Boolean", typeof(bool), "Boolean", Category, "checkbox"),
        new("Int32", typeof(int), "Integer", Category, "number"),
        new("Int64", typeof(long), "Long", Category, "number"),
        new("Double", typeof(double), "Double", Category, "number"),
        new("Decimal", typeof(decimal), "Decimal", Category, "number"),
        new("DateTime", typeof(DateTime), "Date/Time", Category, "date"),
        new("DateTimeOffset", typeof(DateTimeOffset), "Date/Time Offset", Category, "date"),
        new("TimeSpan", typeof(TimeSpan), "Duration", Category, "text"),
        new("Guid", typeof(Guid), "GUID", Category, "text"),
        new("Object", typeof(object), "Object", Category, "text")
    ];

    /// <inheritdoc />
    public IEnumerable<TypeDescriptor> GetDescriptors() => Descriptors;
}
