using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Stores and retrieves payloads whose owning value envelope selects external storage.
/// Implementations route by storage profile and must make writes idempotent for the supplied owner key.
/// </summary>
public interface IExternalPayloadStore
{
    ValueTask<DurableValueExternalReference> WriteAsync(
        ExternalPayloadWriteRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<JsonElement> ReadAsync(
        DurableValueExternalReference reference,
        CancellationToken cancellationToken = default);
}

public sealed record ExternalPayloadWriteRequest(
    string WorkflowExecutionId,
    string OwnerKey,
    string StorageProfile,
    ValueTypeDescriptor Type,
    JsonElement Payload,
    ValueProtectionPolicy Policy);
