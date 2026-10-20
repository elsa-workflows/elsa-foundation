using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Design.Stores;
using Elsa.Etl.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Stores;

/// <summary>Provider-neutral persistence encoding and integrity checks for ETL authoring records.</summary>
internal static class EtlDefinitionRecordCodec
{
    private static readonly JsonSerializerOptions ArtifactJsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public static ScopeIdentity Scope(EtlScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        EnsureIdentity(scope.TenantId, "tenant id");
        EnsureIdentity(scope.PersistencePartition, "persistence partition");
        return new(
            EfRelationalIdentity.HashLengthFramed(scope.TenantId, scope.PersistencePartition),
            EfRelationalIdentity.Encode(scope.TenantId),
            EfRelationalIdentity.Encode(scope.PersistencePartition));
    }

    public static DefinitionIdentity Definition(string definitionId)
    {
        EnsureIdentity(definitionId, "definition id");
        return new(EfRelationalIdentity.Hash(definitionId), EfRelationalIdentity.Encode(definitionId));
    }

    public static string WritePipeline(EtlPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        return EtlArtifactJson.Write(pipeline).GetRawText();
    }

    public static string HashPipeline(string json)
    {
        using var document = JsonDocument.Parse(json);
        return EtlArtifactJson.Hash(document.RootElement);
    }

    public static EtlDefinitionDraft ReadDraft(EtlDefinitionDraftRecord row, EtlScope scope, string definitionId)
    {
        EnsureSchemaVersion(row.SchemaVersion);
        EnsureIdentityResidual(row.TenantId, scope.TenantId, "tenant id");
        EnsureIdentityResidual(row.PersistencePartition, scope.PersistencePartition, "persistence partition");
        EnsureIdentityResidual(row.DefinitionId, definitionId, "definition id");
        if (!StringComparer.Ordinal.Equals(row.ScopeKey, Scope(scope).Key) ||
            !StringComparer.Ordinal.Equals(row.DefinitionKey, Definition(definitionId).Key) ||
            !StringComparer.Ordinal.Equals(row.PipelineHash, HashPipeline(row.PipelineJson)))
            throw new InvalidDataException("The ETL draft row does not match its scoped identity or content hash.");

        EtlPipeline pipeline;
        using (var document = JsonDocument.Parse(row.PipelineJson))
            pipeline = EtlArtifactJson.Read(document.RootElement);
        if (!StringComparer.Ordinal.Equals(WritePipeline(pipeline), row.PipelineJson))
            throw new InvalidDataException("The ETL draft row is not in the canonical pipeline representation.");
        DateTimeOffset updatedAt;
        try
        {
            updatedAt = new DateTimeOffset(row.UpdatedAtUtcTicks, TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("The ETL draft row has an invalid update timestamp.", exception);
        }

        if (row.Revision <= 0 || row.LastPublishedVersion < 0)
            throw new InvalidDataException("The ETL draft row has an invalid revision or publication version.");
        return new EtlDefinitionDraft(definitionId, row.Revision, row.LastPublishedVersion, pipeline, updatedAt);
    }

    public static ValidatedArtifact ValidateArtifact(string definitionId, EtlArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        EnsureIdentity(definitionId, "definition id");
        if (artifact.FormatVersion != EtlArtifact.CurrentFormatVersion ||
            artifact.DefinitionVersion <= 0 ||
            string.IsNullOrWhiteSpace(artifact.ArtifactId) ||
            artifact.ArtifactId.Length > 128 || artifact.DefinitionId.Length > 256 || artifact.ContentHash.Length != 64 ||
            !StringComparer.Ordinal.Equals(artifact.DefinitionId, definitionId))
            throw new ArgumentException("The published ETL artifact envelope has invalid format or provenance.", nameof(artifact));

        // Inspect the existing UTF-8 span before materializing a typed graph or a raw-text copy.
        if (artifact.Pipeline.ValueKind != JsonValueKind.Object ||
            JsonMarshal.GetRawUtf8Value(artifact.Pipeline).Length > EtlArtifact.MaximumByteLength)
            throw new ArgumentException("The published ETL artifact exceeds the supported byte limit.", nameof(artifact));

        EtlPipeline pipeline;
        string canonicalPipeline;
        try
        {
            pipeline = EtlArtifactJson.Read(artifact.Pipeline);
            canonicalPipeline = WritePipeline(pipeline);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new ArgumentException("The published ETL artifact does not contain a supported typed pipeline.", nameof(artifact), exception);
        }

        var pipelineHash = HashPipeline(canonicalPipeline);
        if (!StringComparer.Ordinal.Equals(artifact.Pipeline.GetRawText(), canonicalPipeline) ||
            !StringComparer.Ordinal.Equals(artifact.ContentHash, pipelineHash))
            throw new ArgumentException("The published ETL artifact pipeline is not canonical or its content hash is invalid.", nameof(artifact));

        var artifactJson = JsonSerializer.Serialize(artifact, ArtifactJsonOptions);
        if (Encoding.UTF8.GetByteCount(artifactJson) > EtlArtifact.MaximumByteLength)
            throw new ArgumentException("The published ETL artifact exceeds the supported byte limit.", nameof(artifact));
        return new(artifact, pipeline, canonicalPipeline, pipelineHash, artifactJson, HashText(artifactJson));
    }

    public static EtlArtifact ReadPublished(EtlPublishedArtifactRecord row, EtlScope scope, string definitionId)
    {
        EnsureSchemaVersion(row.SchemaVersion);
        if (row.ArtifactJson.Length > EtlArtifact.MaximumByteLength || Encoding.UTF8.GetByteCount(row.ArtifactJson) > EtlArtifact.MaximumByteLength)
            throw new InvalidDataException("The retained ETL artifact exceeds the supported byte limit.");
        EnsureIdentityResidual(row.TenantId, scope.TenantId, "tenant id");
        EnsureIdentityResidual(row.PersistencePartition, scope.PersistencePartition, "persistence partition");
        EnsureIdentityResidual(row.DefinitionId, definitionId, "definition id");
        if (!StringComparer.Ordinal.Equals(row.ScopeKey, Scope(scope).Key) ||
            !StringComparer.Ordinal.Equals(row.DefinitionKey, Definition(definitionId).Key) ||
            !StringComparer.Ordinal.Equals(row.ArtifactEnvelopeHash, HashText(row.ArtifactJson)))
            throw new InvalidDataException("The published ETL artifact row does not match its scoped identity or envelope hash.");

        var artifact = JsonSerializer.Deserialize<EtlArtifact>(row.ArtifactJson, ArtifactJsonOptions)
            ?? throw new InvalidDataException("The published ETL artifact row contains an empty artifact envelope.");
        var validated = ValidateArtifact(definitionId, artifact);
        if (artifact.DefinitionVersion != row.DefinitionVersion ||
            !StringComparer.Ordinal.Equals(validated.PipelineHash, row.PipelineHash) ||
            !StringComparer.Ordinal.Equals(validated.ArtifactJson, row.ArtifactJson))
            throw new InvalidDataException("The published ETL artifact row does not match its version or pipeline hash.");
        return artifact;
    }

    public static string HashText(string value) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static void EnsureIdentity(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > EtlDesignEfModule.IdentityMaximumLength)
            throw new ArgumentException($"The ETL {label} must be nonblank and at most {EtlDesignEfModule.IdentityMaximumLength} UTF-16 code units.", label);
    }

    private static void EnsureIdentityResidual(string encoded, string expected, string label)
    {
        string decoded;
        try
        {
            decoded = EfRelationalIdentity.Decode(encoded);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException($"The ETL row has an invalid encoded {label}.", exception);
        }

        if (!StringComparer.Ordinal.Equals(decoded, expected))
            throw new InvalidDataException($"The ETL row does not match its {label} scope.");
    }

    private static void EnsureSchemaVersion(string value) => EfSchemaVersion.EnsureReadable(EtlDesignEfModule.Chain, value);

    public sealed record ScopeIdentity(string Key, string TenantId, string PersistencePartition);
    public sealed record DefinitionIdentity(string Key, string DefinitionId);
    public sealed record ValidatedArtifact(
        EtlArtifact Artifact,
        EtlPipeline Pipeline,
        string PipelineJson,
        string PipelineHash,
        string ArtifactJson,
        string ArtifactEnvelopeHash);
}
