using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace Elsa.Studio.Web;

internal sealed class StudioShellFeatureConfigurationStore(
    IHostEnvironment environment,
    IConfigurationRoot configuration)
{
    private const string FileName = "shells.json";
    private const string ShellId = "Default";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<StudioShellFeatureConfigurationSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var document = await ReadDocumentAsync(cancellationToken);
        return CreateSnapshot(document);
    }

    public async Task<StudioShellFeatureConfigurationSnapshot> SaveAsync(
        string expectedRevision,
        IReadOnlyList<FeatureManagementApplyItemRequest> features,
        CancellationToken cancellationToken = default)
    {
        var document = await ReadDocumentAsync(cancellationToken);
        var current = CreateSnapshot(document);
        if (!string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal))
            throw new StudioFeatureCatalogRevisionConflictException(expectedRevision, current.Revision);

        var featuresNode = GetFeaturesNode(document.Document, create: true);
        featuresNode.Clear();

        foreach (var feature in features.Where(feature => feature.Enabled).OrderBy(feature => feature.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(feature.Id))
                continue;

            var configuration = feature.Configuration.ValueKind is JsonValueKind.Object
                ? JsonNode.Parse(feature.Configuration.GetRawText()) ?? new JsonObject()
                : new JsonObject();

            featuresNode[feature.Id] = configuration;
        }

        var json = document.Document.ToJsonString(JsonOptions);
        await File.WriteAllTextAsync(document.Path, json + Environment.NewLine, cancellationToken);
        if (configuration is IConfigurationRoot root)
            root.Reload();

        return CreateSnapshot(new StudioShellDocument(document.Path, document.Document));
    }

    private async Task<StudioShellDocument> ReadDocumentAsync(CancellationToken cancellationToken)
    {
        var path = ResolvePath();
        if (!File.Exists(path))
            throw new InvalidOperationException($"Could not find Studio shell configuration file at '{path}'.");

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken));
        }
        catch (JsonException e)
        {
            throw new InvalidOperationException("Studio shell configuration file is not valid JSON.", e);
        }

        if (node is not JsonObject document)
            throw new InvalidOperationException("Studio shell configuration root must be a JSON object.");

        return new StudioShellDocument(path, document);
    }

    private StudioShellFeatureConfigurationSnapshot CreateSnapshot(StudioShellDocument document)
    {
        var featuresNode = GetFeaturesNode(document.Document, create: false);
        var features = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, configuration) in featuresNode)
        {
            if (string.IsNullOrWhiteSpace(name) || configuration is null)
                continue;

            if (configuration is JsonValue disabledValue &&
                disabledValue.TryGetValue<bool>(out var disabled) &&
                !disabled)
            {
                continue;
            }

            if (configuration is JsonValue enabledValue &&
                enabledValue.TryGetValue<bool>(out var enabled) &&
                enabled)
            {
                features[name] = EmptyJsonObject();
                continue;
            }

            var element = JsonSerializer.Deserialize<JsonElement>(configuration.ToJsonString());
            features[name] = element.ValueKind is JsonValueKind.Object
                ? element.Clone()
                : EmptyJsonObject();
        }

        return new StudioShellFeatureConfigurationSnapshot(CreateRevision(featuresNode.ToJsonString(JsonOptions)), features);
    }

    private static JsonObject GetFeaturesNode(JsonObject document, bool create)
    {
        var shells = GetObject(document, "CShells", create);
        shells = GetObject(shells, "Shells", create);
        var shell = GetObjectCaseInsensitive(shells, ShellId, create);
        return GetObject(shell, "Features", create);
    }

    private static JsonObject GetObject(JsonObject parent, string propertyName, bool create)
    {
        if (parent[propertyName] is JsonObject existing)
            return existing;

        if (!create)
            throw new InvalidOperationException($"Studio shell configuration must contain '{propertyName}'.");

        var created = new JsonObject();
        parent[propertyName] = created;
        return created;
    }

    private static JsonObject GetObjectCaseInsensitive(JsonObject parent, string propertyName, bool create)
    {
        var existingName = parent.Select(x => x.Key).FirstOrDefault(x => string.Equals(x, propertyName, StringComparison.OrdinalIgnoreCase));
        if (existingName is not null && parent[existingName] is JsonObject existing)
            return existing;

        if (!create)
            throw new InvalidOperationException($"Studio shell configuration must contain shell '{propertyName}'.");

        var created = new JsonObject();
        parent[propertyName] = created;
        return created;
    }

    private string ResolvePath() => Path.Combine(environment.ContentRootPath, FileName);

    private static JsonElement EmptyJsonObject()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private static string CreateRevision(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private sealed record StudioShellDocument(string Path, JsonObject Document);
}

internal sealed record StudioShellFeatureConfigurationSnapshot(
    string Revision,
    IReadOnlyDictionary<string, JsonElement> Features);

internal sealed class StudioFeatureCatalogRevisionConflictException(string expectedRevision, string actualRevision) : Exception
{
    public string ExpectedRevision { get; } = expectedRevision;
    public string ActualRevision { get; } = actualRevision;
}
