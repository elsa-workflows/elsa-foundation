using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;

/// <summary>The optional scalar fingerprint adds no meaning to receipt/test-run documents.</summary>
[EfSchemaUpcaster("1", "2")]
internal sealed class PublishingLedgerV1Upcaster : IEfSchemaUpcaster
{
    public PublishingLedgerV1Upcaster() { }
    public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row;
}
