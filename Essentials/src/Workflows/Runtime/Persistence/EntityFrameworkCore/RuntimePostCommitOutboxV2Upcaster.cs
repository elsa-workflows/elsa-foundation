using System.Text.Json;
using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>Preserves exact workflow bytes; predecessor families cannot claim the new source/recipient protocol.</summary>
[EfSchemaUpcaster("2.0.0", "3.0.0")]
internal sealed class RuntimePostCommitOutboxV2Upcaster : IEfSchemaUpcaster
{
    public RuntimePostCommitOutboxV2Upcaster() { }
    public EfSchemaRowContent Upcast(EfSchemaRowContent row)
    {
        var content = row[nameof(Entities.RuntimePostCommitOutboxEntity.ContentJson)] ?? throw new InvalidDataException("An outbox row has no content.");
        using var document = JsonDocument.Parse(content);
        if (document.RootElement.EnumerateObject().Any(property =>
            property.Name.Equals("savedStimulusStarts", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Equals("standaloneIngress", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Equals("subscriptionDeliveryProtocolVersion", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Equals("subscriptionDelivery", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("A predecessor outbox family cannot carry standalone/subscription delivery state.");
        return row;
    }
}
