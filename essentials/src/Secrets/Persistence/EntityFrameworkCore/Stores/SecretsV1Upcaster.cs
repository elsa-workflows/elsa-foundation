using Elsa.Persistence.EntityFramework;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;

/// <summary>Lowercase v1 names are already valid in the case-preserving v2 format; no content changes.</summary>
[EfSchemaUpcaster("1.0.0", "2.0.0")]
internal sealed class SecretsV1Upcaster : IEfSchemaUpcaster
{
    public SecretsV1Upcaster() { }

    public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row;
}
