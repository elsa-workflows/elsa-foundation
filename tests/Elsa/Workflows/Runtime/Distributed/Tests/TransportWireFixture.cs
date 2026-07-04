using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Models;

namespace Elsa.Workflows.Runtime.Distributed.Tests;

/// <summary>
/// Builds the single canonical <see cref="ExecutionCommandTransportItem"/> whose serialized shape is frozen as the v1
/// wire format. The golden fixture drift test compares this against the committed <c>Fixtures/v1</c> file, and the
/// durable (Groundwork) transport follow-up must persist exactly this shape.
/// </summary>
internal static class TransportWireFixture
{
    public const string Kind = "executionCommandTransport";

    private static readonly DateTimeOffset FixedTimestamp = new(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static ExecutionCommandTransportItem CanonicalItem()
    {
        using var payloadDocument = JsonDocument.Parse("""{"bookmarkId":"bm-1"}""");

        var command = new WorkflowExecutionCommand(
            CommandId: "cmd-1",
            WorkflowExecutionId: "wf-1",
            Kind: WorkflowExecutionCommandKind.ResumeBookmark,
            EnqueuedAt: FixedTimestamp,
            Payload: payloadDocument.RootElement.Clone(),
            Metadata: new Dictionary<string, string> { ["origin"] = "node-b" });

        var envelope = new WorkflowExecutionCommandEnvelope(
            envelopeId: "env-1",
            workflowExecutionId: "wf-1",
            command: command,
            idempotencyKey: "idem-1",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: FixedTimestamp,
            sequence: 1,
            metadata: new Dictionary<string, string> { ["origin"] = "node-b" });

        return new ExecutionCommandTransportItem(
            transportItemId: "transport:wf-1:1",
            workflowExecutionId: "wf-1",
            envelope: envelope,
            sequence: 1,
            enqueuedAt: FixedTimestamp,
            deliveryAttemptCount: 1,
            leasedByOwnerId: "node-a",
            leaseExpiresAt: FixedTimestamp.AddSeconds(30));
    }
}
