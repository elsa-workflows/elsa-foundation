using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>Preserves predecessor bytes while the existing fleet protocol gates semantic reservation writers.</summary>
/// <remarks>Binding projection content is an opaque fingerprint, not JSON. This transition changes no document shape.</remarks>
[EfSchemaUpcaster("1.0.0", "1.1.0")]
internal sealed class IngressOwnershipV1Upcaster : IEfSchemaUpcaster
{
    public IngressOwnershipV1Upcaster() { }
    public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row;
}
