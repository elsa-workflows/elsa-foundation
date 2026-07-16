using System.Text.Json;
using System.Text.Json.Nodes;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork;

public interface IPublishingGroundworkDocumentUpcaster
{
    string DocumentKind { get; }
    int FromVersion { get; }
    JsonObject Upcast(JsonObject content);
}

public sealed class PublishingGroundworkDocumentSerializer(IEnumerable<IPublishingGroundworkDocumentUpcaster>? upcasters = null)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyDictionary<string, int> CurrentVersions = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        [PublishingGroundworkStorageManifest.PublicationSlotDocumentKind] = 1,
        [PublishingGroundworkStorageManifest.PublicationRecordDocumentKind] = 1,
        [PublishingGroundworkStorageManifest.PublicationPolicyDocumentKind] = 1,
        [PublishingGroundworkStorageManifest.ProjectionIntentDocumentKind] = 1,
        [PublishingGroundworkStorageManifest.SnapshotReviewDocumentKind] = 1
    };
    private readonly IReadOnlyDictionary<(string Kind, int Version), IPublishingGroundworkDocumentUpcaster> _upcasters =
        (upcasters ?? []).ToDictionary(x => (x.DocumentKind, x.FromVersion));

    public (string SchemaVersion, string ContentJson) Serialize<T>(string documentKind, T document) =>
        (Stamp(CurrentFor(documentKind)), JsonSerializer.Serialize(document, Options));

    public T Deserialize<T>(DocumentEnvelope envelope)
    {
        var version = Parse(envelope.DocumentKind, envelope.SchemaVersion);
        var current = CurrentFor(envelope.DocumentKind);
        var content = JsonNode.Parse(envelope.ContentJson)?.AsObject()
            ?? throw new InvalidOperationException($"Publishing document '{envelope.DocumentKind}/{envelope.Id}' is not a JSON object.");
        while (version < current)
        {
            if (!_upcasters.TryGetValue((envelope.DocumentKind, version), out var upcaster))
                throw new InvalidOperationException($"No publishing upcaster exists for '{envelope.DocumentKind}' v{version} to v{version + 1}.");
            content = upcaster.Upcast(content);
            version++;
        }
        if (version != current)
            throw new InvalidOperationException($"Publishing document '{envelope.DocumentKind}' has unsupported future version v{version}; current is v{current}.");
        return content.Deserialize<T>(Options)
            ?? throw new InvalidOperationException($"Publishing document '{envelope.DocumentKind}/{envelope.Id}' deserialized to null.");
    }

    public static int CurrentFor(string documentKind) => CurrentVersions.TryGetValue(documentKind, out var version)
        ? version
        : throw new ArgumentException($"Unknown Publishing document kind '{documentKind}'.", nameof(documentKind));

    public static string Stamp(int version) => $"{version}.0.0";

    private static int Parse(string documentKind, string schemaVersion)
    {
        if (!Version.TryParse(schemaVersion, out var parsed) || parsed.Minor != 0 || parsed.Build != 0)
            throw new InvalidOperationException($"Publishing document '{documentKind}' has invalid schema version '{schemaVersion}'.");
        return parsed.Major;
    }
}
