using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;

namespace Acme;

// catalog:case-16 begin
public sealed record Invoice(string Number, decimal Amount, string Currency);

// Registers Invoice under the dotted alias acme.Invoice. Elsa 4 module aliases are dotted, so a variable typed
// with the bare Elsa 3 alias Invoice is changed to acme.Invoice in the Elsa 3 definition before conversion.
public sealed class AcmeVariableTypes : IVariableTypeDescriptorProvider
{
    public const string InvoiceAlias = "acme.Invoice";

    public IEnumerable<TypeDescriptor> GetDescriptors()
    {
        yield return new TypeDescriptor(
            InvoiceAlias,
            typeof(Invoice),
            "Invoice",
            "Acme",
            "json",
            new HashSet<CollectionKind> { CollectionKind.Single },
            SupportsNull: true,
            SupportsDurability: true,
            new HashSet<string>(StringComparer.Ordinal) { "elsa.json" });
    }
}
// catalog:case-16 end
