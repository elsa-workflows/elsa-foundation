using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;

namespace Acme.Elsa3MigrationFixture;

/// <summary>
/// Registers the fixture export's custom variable type under its Elsa 3 type name, <see cref="Alias"/>, so the converter
/// finds it in Elsa 4's type registry and no longer reports it as unregistered.
/// </summary>
internal sealed class OrderDtoTypeDescriptorProvider : IVariableTypeDescriptorProvider
{
    /// <summary>The Elsa 3 type name of the fixture's custom variable type.</summary>
    public const string Alias = "Acme.Orders.OrderDto";

    private static readonly IReadOnlySet<CollectionKind> CollectionKinds = new HashSet<CollectionKind> { CollectionKind.Single };
    private static readonly IReadOnlySet<string> JsonStorage = new HashSet<string>(StringComparer.Ordinal) { "elsa.json" };

    public IEnumerable<TypeDescriptor> GetDescriptors()
    {
        yield return new TypeDescriptor(
            Alias,
            typeof(Orders.OrderDto),
            "Order",
            "Acme",
            "json",
            CollectionKinds,
            SupportsNull: true,
            SupportsDurability: true,
            JsonStorage);
    }
}
