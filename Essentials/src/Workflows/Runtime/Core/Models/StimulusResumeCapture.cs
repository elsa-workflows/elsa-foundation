using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Immutable physical wait generation and recipient facts; carries no independent delivery authority.</summary>
[JsonConverter(typeof(StimulusResumeCaptureJsonConverter))]
public sealed class StimulusResumeCapture
{
    public const int CurrentVersion = 1;

    public StimulusResumeCapture(long generation, WorkflowExecutableIdentity executable, StimulusDeliveryScope scope,
        WorkflowExecutableSourceProvenance? pinnedSource, IReadOnlyDictionary<string, string> metadata, string payloadFingerprint)
    {
        if (generation <= 0) throw new ArgumentOutOfRangeException(nameof(generation));
        ArgumentNullException.ThrowIfNull(executable); ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(metadata);
        foreach (var value in new[] { executable.ArtifactId, executable.DefinitionId, executable.DefinitionVersionId, executable.ArtifactVersion, executable.ArtifactHash })
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (payloadFingerprint is null || payloadFingerprint.Length != 64 || payloadFingerprint.Any(value => !char.IsAsciiHexDigitLower(value)))
            throw new ArgumentException("The bookmark payload fingerprint must be a lowercase SHA256 digest.", nameof(payloadFingerprint));
        if (metadata.Any(pair => pair.Value is null)) throw new ArgumentException("Captured bookmark metadata cannot contain null values.", nameof(metadata));
        if (pinnedSource is not null)
        {
            foreach (var value in new[] { pinnedSource.SourceReferenceId, pinnedSource.SourceKind, pinnedSource.SourceId,
                         pinnedSource.DefinitionId, pinnedSource.DefinitionVersionId, pinnedSource.ArtifactVersion })
                ArgumentException.ThrowIfNullOrWhiteSpace(value);
            foreach (var value in new[] { pinnedSource.SourceVersion, pinnedSource.PublicationId, pinnedSource.SlotId })
                if (value is not null) ArgumentException.ThrowIfNullOrWhiteSpace(value);
        }
        Generation = generation; Executable = executable; Scope = scope; PinnedSource = pinnedSource;
        Metadata = RuntimeModelMetadata.Snapshot(metadata); PayloadFingerprint = payloadFingerprint;
    }

    public long Generation { get; }
    public WorkflowExecutableIdentity Executable { get; }
    public StimulusDeliveryScope Scope { get; }
    public WorkflowExecutableSourceProvenance? PinnedSource { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
    public string PayloadFingerprint { get; }

    internal static string Fingerprint(BookmarkState bookmark) => Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes(bookmark.Payload is { } payload ? "json:" + payload.GetRawText() : "absent")));

    internal bool Matches(BookmarkState bookmark) => RuntimeModelMetadata.AreEqual(Metadata, bookmark.Metadata) &&
        PayloadFingerprint == Fingerprint(bookmark);

    internal bool Matches(WorkflowExecutionState execution, PersistenceScope scope) =>
        WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(Executable, execution.PinnedExecutable) &&
        Scope.Matches(execution, scope) && PinnedSource == execution.PinnedSource;

    internal bool IsEquivalentTo(StimulusResumeCapture? other) => other is not null && Generation == other.Generation &&
        Executable == other.Executable && Scope.PersistenceScope == other.Scope.PersistenceScope && Scope.TenantId == other.Scope.TenantId &&
        Scope.Partition == other.Scope.Partition && Scope.RunKind == other.Scope.RunKind && Scope.TestScope == other.Scope.TestScope &&
        PinnedSource == other.PinnedSource && RuntimeModelMetadata.AreEqual(Metadata, other.Metadata) && PayloadFingerprint == other.PayloadFingerprint;
}

internal sealed class StimulusResumeCaptureJsonConverter : JsonConverter<StimulusResumeCapture>
{
    public StimulusResumeCaptureJsonConverter() { }
    public override StimulusResumeCapture Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("A registration capture must be an object.");
        var properties = document.RootElement.EnumerateObject().ToArray();
        var names = new[] { "version", "generation", "executable", "scope", "pinnedSource", "metadata", "payloadFingerprint" };
        if (properties.Any(property => !names.Contains(property.Name, StringComparer.OrdinalIgnoreCase)) ||
            properties.GroupBy(property => property.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() != 1))
            throw new JsonException("A registration capture has unknown or duplicated fields.");
        var metadata = properties.SingleOrDefault(property => property.Name.Equals("metadata", StringComparison.OrdinalIgnoreCase));
        if (metadata.Value.ValueKind == JsonValueKind.Object && metadata.Value.EnumerateObject().GroupBy(property => property.Name, StringComparer.Ordinal).Any(group => group.Count() != 1))
            throw new JsonException("Captured bookmark metadata has duplicated keys.");
        var wire = document.RootElement.Deserialize<Wire>(options) ?? throw new JsonException("A registration capture must be an object.");
        if (wire.Version != StimulusResumeCapture.CurrentVersion) throw new JsonException("The registration capture version is unsupported.");
        try { return new(wire.Generation, wire.Executable, wire.Scope, wire.PinnedSource?.ToProvenance(), wire.Metadata, wire.PayloadFingerprint); }
        catch (ArgumentException exception) { throw new JsonException("The registration capture is invalid.", exception); }
    }

    public override void Write(Utf8JsonWriter writer, StimulusResumeCapture value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, new Wire(StimulusResumeCapture.CurrentVersion, value.Generation, value.Executable, value.Scope,
            value.PinnedSource is null ? null : StimulusStartRecipientJsonConverter.ProvenanceWire.From(value.PinnedSource), value.Metadata, value.PayloadFingerprint), options);

    private sealed record Wire([property: JsonRequired] int Version, [property: JsonRequired] long Generation,
        [property: JsonRequired] WorkflowExecutableIdentity Executable, [property: JsonRequired] StimulusDeliveryScope Scope,
        [property: JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] StimulusStartRecipientJsonConverter.ProvenanceWire? PinnedSource,
        [property: JsonRequired] IReadOnlyDictionary<string, string> Metadata, [property: JsonRequired] string PayloadFingerprint);
}
