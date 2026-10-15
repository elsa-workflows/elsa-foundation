using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;

namespace Elsa.Connections.Expressions;

/// <summary>Registers <see cref="ConnectionReference"/> as a well-known dotted alias.</summary>
public sealed class ConnectionTypeDescriptorProvider : IVariableTypeDescriptorProvider
{
    private static readonly IReadOnlySet<CollectionKind> CollectionKinds = new HashSet<CollectionKind> { CollectionKind.Single };
    private static readonly IReadOnlySet<string> JsonStorage = new HashSet<string>(StringComparer.Ordinal) { "elsa.json" };

    public IEnumerable<TypeDescriptor> GetDescriptors()
    {
        yield return new TypeDescriptor(
            typeof(ConnectionReference).FullName!,
            typeof(ConnectionReference),
            "Connection reference",
            "Connections",
            "connection-picker",
            CollectionKinds,
            SupportsNull: true,
            SupportsDurability: true,
            JsonStorage);
    }
}
