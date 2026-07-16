using Elsa.Diagnostics.OpenTelemetry.Core.Models;

namespace Elsa.Diagnostics.OpenTelemetry.Core.Contracts;

/// <summary>
/// Additively observes an ingested OpenTelemetry batch after redaction and before diagnostics storage and
/// live-feed publication. Contributors are awaited sequentially in registration order and receive the same
/// redacted batch instance and server-authoritative ingestion context, both of which must be treated as read-only.
/// </summary>
/// <remarks>
/// Completion participates in ingestion acknowledgement: a contributor that promises durable handoff must not
/// complete until that handoff is durably accepted. An exception or cancellation stops later contributors and
/// prevents the batch from reaching the diagnostics store or live feed, allowing the transport to report that
/// ingestion was not accepted. Only authenticated claims in the ingestion context are authoritative; resource
/// attributes in the redacted batch remain untrusted telemetry evidence.
/// </remarks>
public interface IOpenTelemetryIngestionContributor
{
    ValueTask ContributeAsync(
        OpenTelemetryBatch redactedBatch,
        OpenTelemetryIngestionContext ingestionContext,
        CancellationToken cancellationToken = default);
}
