using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Etl.Core;

/// <summary>A read-only envelope for the canonical serialized runnable ETL pipeline.</summary>
/// <remarks>
/// This type preserves an artifact snapshot; it does not verify the content hash, compile the pipeline, or validate
/// its schema. Those policies belong to the design and runtime features.
/// </remarks>
public sealed class EtlArtifact
{
    /// <summary>The retained v1 file-pipeline format; connector artifacts use <see cref="EtlArtifactV2Json.FormatVersion"/>.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>The maximum serialized size of a complete runnable artifact envelope.</summary>
    public const int MaximumByteLength = 256 * 1024;

    /// <summary>Creates an artifact envelope and clones the supplied JSON document.</summary>
    [JsonConstructor]
    public EtlArtifact(
        int formatVersion,
        string artifactId,
        string contentHash,
        string definitionId,
        int definitionVersion,
        JsonElement pipeline)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);

        FormatVersion = formatVersion;
        ArtifactId = artifactId;
        ContentHash = contentHash;
        DefinitionId = definitionId;
        DefinitionVersion = definitionVersion;
        Pipeline = pipeline.Clone();
    }

    /// <summary>The artifact envelope format version.</summary>
    [JsonInclude]
    [JsonRequired]
    public int FormatVersion { get; private set; }

    /// <summary>The immutable published artifact identifier.</summary>
    [JsonInclude]
    [JsonRequired]
    public string ArtifactId { get; private set; }

    /// <summary>The content hash supplied by the design feature.</summary>
    [JsonInclude]
    [JsonRequired]
    public string ContentHash { get; private set; }

    /// <summary>The authored definition identifier from which this artifact was published.</summary>
    [JsonInclude]
    [JsonRequired]
    public string DefinitionId { get; private set; }

    /// <summary>The published definition version.</summary>
    [JsonInclude]
    [JsonRequired]
    public int DefinitionVersion { get; private set; }

    /// <summary>A cloned canonical pipeline JSON document.</summary>
    [JsonInclude]
    [JsonRequired]
    public JsonElement Pipeline { get; private set; }
}
