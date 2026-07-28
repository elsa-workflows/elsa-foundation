using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork.Tests;

public sealed class StructuredLogsGroundworkStorageSchemaTests
{
    [Fact]
    public void Schema_declares_bound_stream_projected_indexes_and_append_trim_ledgers()
    {
        var binding = new StructuredLogStoreBinding("tenant", "scope", "logs");
        var stream = StructuredLogsGroundworkStorageSchema.CreateStream(binding);

        Assert.Equal(binding.StreamId, stream.Stream.Value);
        Assert.Equal(
            StructuredLogsGroundworkStorageSchema.RequiredIndexFields.Order(StringComparer.Ordinal),
            stream.Fields.Select(x => x.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            new[] { "diagnostic-append-idempotency", "diagnostic-trim-idempotency" },
            StructuredLogsGroundworkStorageSchema.RequiredOperationLedgers.Order(StringComparer.Ordinal));
    }
}
