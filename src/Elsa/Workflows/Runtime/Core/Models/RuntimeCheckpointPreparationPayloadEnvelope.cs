namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Versioned, canonical raw input retained by a prepared logical checkpoint.
/// The digest covers the UTF-8 bytes of <see cref="CanonicalPayload"/> exactly.
/// </summary>
public sealed record RuntimeCheckpointPreparationPayloadEnvelope(
    int Version,
    string CanonicalPayload,
    string PayloadSha256);
