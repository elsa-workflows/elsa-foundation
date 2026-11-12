using Elsa.Persistence.EntityFramework;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;

/// <summary>Preserves existing IAM content when issuer-qualified login integrity columns become available.</summary>
[EfSchemaUpcaster("1.0.0", "1.1.0")]
public sealed class IdentityIamV1Upcaster : IEfSchemaUpcaster
{
    public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row;
}
