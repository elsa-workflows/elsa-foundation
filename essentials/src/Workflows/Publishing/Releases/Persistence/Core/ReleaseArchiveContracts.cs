using Elsa.Persistence.Core;
using Elsa.Workflows.Publishing.Releases.Core;

namespace Elsa.Workflows.Publishing.Releases.Persistence.Core;

/// <summary>Only a confirmed return establishes creation or recovery of an existing capture.</summary>
public enum ReleaseArchiveCreateOutcome
{
    Unknown = 0,
    Created = 1,
    Existing = 2
}

/// <summary>Archive transport validation never establishes semantic validity, publisher origin or activation authority.</summary>
public enum ReleaseArchiveValidationLevel
{
    Unknown = 0,
    TransportOnly = 1
}

public sealed record ReleaseArchiveCreateReceipt(string CaptureId, string ReleaseDigest, ReleaseArchiveCreateOutcome Outcome);

/// <summary>A local provider observation, not a source-retention or owner-validation claim.</summary>
public sealed record ReleaseArchiveStoreCapabilities(string Provider, bool DurableCapture, bool SchemaReady);

/// <summary>An immutable snapshot; the transport returns copies of all archived byte arrays.</summary>
public interface IReleaseArchiveSnapshot
{
    SourceScope Scope { get; }
    string CaptureId { get; }
    DateTimeOffset CreatedAt { get; }
    ReleaseArchiveValidationLevel ValidationLevel { get; }
    IReleaseBundleTransport Transport { get; }
}

/// <summary>Scoped backend replacement contract. Reads only the trusted scope selected by server composition.</summary>
public interface IReleaseArchiveReader
{
    ValueTask<IReleaseArchiveSnapshot?> FindAsync(string captureId, CancellationToken cancellationToken = default);
}

/// <summary>Scoped backend replacement contract. Creates a complete write-once capture, or recovers the first representation of equal logical content.</summary>
public interface IReleaseArchiveWriter
{
    /// <remarks>The input stream remains caller-owned. Reconcile an ambiguous commit using the same capture ID.</remarks>
    ValueTask<ReleaseArchiveCreateReceipt> CreateAsync(string captureId, Stream zip, CancellationToken cancellationToken = default);
}

/// <summary>Scoped backend replacement contract for capability observations; select coherently with the reader and writer.</summary>
public interface IReleaseArchiveStoreCapabilities
{
    ValueTask<ReleaseArchiveStoreCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default);
}

/// <summary>A stable value-free archive refusal; protected bytes and scope identities are absent from its message.</summary>
public class ReleaseArchiveStoreException(string code, Exception? innerException = null)
    : Exception("The release archive operation was refused (" + code + ").", innerException)
{
    public string Code { get; } = code;
}

/// <summary>A commit reached the provider; cancellation or a lost response cannot confirm whether it became durable.</summary>
public sealed class ReleaseArchiveCommitOutcomeUnknownException(Exception innerException)
    : ReleaseArchiveStoreException("archive_commit_outcome_unknown", innerException);
