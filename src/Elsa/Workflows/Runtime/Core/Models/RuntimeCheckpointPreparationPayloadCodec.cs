using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Encodes and verifies the provider-neutral raw input retained for a prepared logical checkpoint.
/// </summary>
public sealed class RuntimeCheckpointPreparationPayloadCodec
{
    /// <summary>The only payload schema understood by this runtime version.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The digest algorithm used for <see cref="RuntimeCheckpointPreparationPayloadEnvelope.PayloadSha256"/>.</summary>
    public const string PayloadHashAlgorithm = "sha256";

    /// <summary>Length of the lowercase hexadecimal SHA-256 digest stored in an envelope.</summary>
    public const int PayloadSha256Length = 64;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Produces canonical bytes for the caller-owned raw proposal. Store-assigned provenance and the ownership fence
    /// are deliberately excluded because they are mutable provider bindings, not recovery input.
    /// </summary>
    public RuntimeCheckpointPreparationPayloadEnvelope Encode(RuntimeCheckpointPrepareRequest request)
    {
        ValidateRequest(request);

        var rawCheckpoint = new
        {
            request.Commit.Checkpoint.CheckpointId,
            request.Commit.Checkpoint.Name,
            request.Commit.Checkpoint.WorkflowExecutionId,
            request.Commit.Checkpoint.OccurredAt,
            request.Commit.Checkpoint.ActivityExecutionIds,
            request.Commit.Checkpoint.Metadata
        };
        var rawCommit = new
        {
            request.Commit.CommitId,
            Checkpoint = rawCheckpoint,
            request.Commit.StateChanges,
            request.Commit.PostCommitIntents,
            request.Commit.Metadata
        };
        var canonicalPayload = SerializeCanonical(
            request.SourceIdentity,
            request.OperationIdentity,
            request.RequestedExecutionContext,
            request.InitialPersistenceMode,
            request.RecoveryAuthority,
            rawCommit);

        return new RuntimeCheckpointPreparationPayloadEnvelope(
            CurrentVersion,
            canonicalPayload,
            ComputeSha256(canonicalPayload));
    }

    /// <summary>
    /// Verifies the envelope and returns a newly materialized request. The returned graph shares no mutable
    /// collections with the request that produced the envelope.
    /// </summary>
    public RuntimeCheckpointPrepareRequest Decode(RuntimeCheckpointPreparationPayloadEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Version != CurrentVersion)
            throw new ArgumentOutOfRangeException(nameof(envelope), envelope.Version, $"Only preparation-payload version {CurrentVersion} is supported.");
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.CanonicalPayload);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.PayloadSha256);
        if (envelope.PayloadSha256.Length != PayloadSha256Length)
            throw new ArgumentException($"The preparation payload digest must be {PayloadSha256Length} lowercase hexadecimal characters.", nameof(envelope));
        if (envelope.PayloadSha256.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("The preparation payload digest must contain only lowercase hexadecimal characters.", nameof(envelope));

        var actualHash = ComputeSha256(envelope.CanonicalPayload);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(envelope.PayloadSha256),
                Encoding.ASCII.GetBytes(actualHash)))
        {
            throw new ArgumentException("The preparation payload digest does not match its canonical payload.", nameof(envelope));
        }

        SerializedPayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<SerializedPayload>(envelope.CanonicalPayload, SerializerOptions)
                ?? throw new JsonException("The preparation payload is empty.");
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("The preparation payload is not valid JSON for the supported schema.", nameof(envelope), exception);
        }

        if (payload.SchemaVersion != envelope.Version)
            throw new ArgumentException("The preparation payload schema version does not match its envelope.", nameof(envelope));

        RuntimeCheckpointCommit rawCommit;
        try
        {
            rawCommit = payload.RawCommit.Deserialize<RuntimeCheckpointCommit>(SerializerOptions)
                ?? throw new JsonException("The preparation payload has no raw checkpoint commit.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or NotSupportedException)
        {
            throw new ArgumentException("The preparation payload contains an invalid raw checkpoint commit.", nameof(envelope), exception);
        }

        ArgumentNullException.ThrowIfNull(rawCommit.Checkpoint);
        ArgumentNullException.ThrowIfNull(rawCommit.StateChanges);

        var request = new RuntimeCheckpointPrepareRequest(
            rawCommit with
            {
                Checkpoint = rawCommit.Checkpoint with { Provenance = null },
                ExpectedFence = null
            },
            payload.SourceIdentity,
            payload.OperationIdentity,
            payload.RequestedExecutionContext,
            payload.InitialPersistenceMode,
            payload.RecoveryAuthority);
        ValidateRequest(request);

        // A valid hash is necessary but not sufficient: reject an alternate, non-canonical representation even if a
        // caller generated a matching digest for it. This preserves one byte representation for each raw request.
        var canonicalEnvelope = Encode(request);
        if (!StringComparer.Ordinal.Equals(canonicalEnvelope.CanonicalPayload, envelope.CanonicalPayload))
            throw new ArgumentException("The preparation payload is not in canonical form.", nameof(envelope));

        return request;
    }

    private static string SerializeCanonical(
        string sourceIdentity,
        string operationIdentity,
        RuntimeExecutionContextSnapshot requestedExecutionContext,
        RuntimeCheckpointPersistenceMode initialPersistenceMode,
        RuntimeCheckpointRecoveryAuthority? recoveryAuthority,
        object rawCommit)
    {
        var payload = new
        {
            SchemaVersion = CurrentVersion,
            SourceIdentity = sourceIdentity,
            OperationIdentity = operationIdentity,
            RequestedExecutionContext = requestedExecutionContext,
            InitialPersistenceMode = initialPersistenceMode,
            RecoveryAuthority = recoveryAuthority,
            RawCommit = rawCommit
        };
        var element = JsonSerializer.SerializeToElement(payload, SerializerOptions);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(element, writer);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                return;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonical(item, writer);
                writer.WriteEndArray();
                return;
            default:
                element.WriteTo(writer);
                return;
        }
    }

    private static string ComputeSha256(string payload) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    private static void ValidateRequest(RuntimeCheckpointPrepareRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Commit);
        ArgumentNullException.ThrowIfNull(request.Commit.Checkpoint);
        ArgumentNullException.ThrowIfNull(request.Commit.StateChanges);
        ArgumentNullException.ThrowIfNull(request.RequestedExecutionContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OperationIdentity);
        if (!Enum.IsDefined(request.InitialPersistenceMode))
            throw new ArgumentOutOfRangeException(nameof(request), request.InitialPersistenceMode, "The initial checkpoint persistence mode is invalid.");
    }

    private sealed record SerializedPayload(
        int SchemaVersion,
        string SourceIdentity,
        string OperationIdentity,
        RuntimeExecutionContextSnapshot RequestedExecutionContext,
        RuntimeCheckpointPersistenceMode InitialPersistenceMode,
        RuntimeCheckpointRecoveryAuthority? RecoveryAuthority,
        JsonElement RawCommit);
}
