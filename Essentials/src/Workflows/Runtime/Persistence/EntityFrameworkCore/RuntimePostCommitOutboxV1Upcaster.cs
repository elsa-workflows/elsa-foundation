using System.Text.Json;
using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>Preserves legacy absence; v1 never authorized the durable recipient protocol.</summary>
[EfSchemaUpcaster("1.0.0", "2.0.0")]
internal sealed class RuntimePostCommitOutboxV1Upcaster : IEfSchemaUpcaster
{
    public RuntimePostCommitOutboxV1Upcaster() { }

    public EfSchemaRowContent Upcast(EfSchemaRowContent row)
    {
        var content = row[nameof(Entities.RuntimePostCommitOutboxEntity.ContentJson)] ??
            throw new InvalidDataException("A legacy outbox row has no content.");
        using var document = JsonDocument.Parse(content);
        if (document.RootElement.EnumerateObject().Any(property =>
            property.Name.Equals("recipientSnapshot", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Equals("stimulusDeliveryProtocolVersion", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Equals("stimulusDeliverySourceScope", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("A legacy outbox family cannot carry recipient protocol state.");
        // Byte-for-byte identity and absent recipients are preserved, even for attempt zero or Delivered rows.
        return row;
    }
}
