using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CShells;
using Elsa.Modularity.Api.Options;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Exceptions;
using Elsa.Modularity.Core.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace Elsa.Modularity.Api.Services;

public sealed class JsonShellFeatureConfigurationStore(
    IWebHostEnvironment environment,
    ShellSettings shellSettings,
    IOptions<FeatureManagementOptions> options) : IShellFeatureConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly JsonElement s_emptyObject = CreateEmptyObject();
    private static JsonElement CreateEmptyObject() { using var d = JsonDocument.Parse("{}"); return d.RootElement.Clone(); }

    public async Task<ShellFeatureConfigurationSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var document = await ReadDocumentAsync(cancellationToken);
        return CreateSnapshot(document);
    }

    public async Task<ShellFeatureConfigurationSnapshot> SaveAsync(
        string expectedRevision,
        IReadOnlyList<FeatureConfigurationChange> features,
        CancellationToken cancellationToken = default)
    {
        var document = await ReadDocumentAsync(cancellationToken);
        var current = CreateSnapshot(document);
        if (!string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal))
            throw new FeatureCatalogRevisionConflictException(expectedRevision, current.Revision);

        var featuresNode = GetFeaturesNode(document.Document, create: true);
        featuresNode.Clear();

        foreach (var feature in features.Where(x => x.Enabled).OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
        {
            var configuration = feature.Configuration.ValueKind is JsonValueKind.Object
                ? JsonNode.Parse(feature.Configuration.GetRawText()) ?? new JsonObject()
                : new JsonObject();

            featuresNode[feature.Id] = configuration;
        }

        var json = document.Document.ToJsonString(JsonOptions);
        await File.WriteAllTextAsync(document.Path, json + Environment.NewLine, cancellationToken);

        return CreateSnapshot(new ShellDocument(document.Path, json, document.Document));
    }

    private async Task<ShellDocument> ReadDocumentAsync(CancellationToken cancellationToken)
    {
        var path = ResolvePath();
        if (!File.Exists(path))
            throw new InvalidOperationException($"Could not find shell configuration file at '{path}'.");

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException e)
        {
            throw new InvalidOperationException("Shell configuration file is not valid JSON.", e);
        }

        if (node is not JsonObject document)
            throw new InvalidOperationException("Shell configuration root must be a JSON object.");

        return new ShellDocument(path, json, document);
    }

    private ShellFeatureConfigurationSnapshot CreateSnapshot(ShellDocument document)
    {
        var featuresNode = GetFeaturesNode(document.Document, create: false);
        var features = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, configuration) in featuresNode)
        {
            if (string.IsNullOrWhiteSpace(name) || configuration is null)
                continue;

            if (configuration is JsonValue jsonBool && jsonBool.TryGetValue<bool>(out var flagEnabled))
            {
                if (flagEnabled)
                    features[name] = s_emptyObject;
                continue;
            }

            var element = JsonSerializer.Deserialize<JsonElement>(configuration.ToJsonString());
            features[name] = element.ValueKind is JsonValueKind.Object
                ? element.Clone()
                : s_emptyObject;
        }

        var revision = CreateRevision(featuresNode.ToJsonString(JsonOptions));
        return new ShellFeatureConfigurationSnapshot(CurrentShellId, revision, features);
    }

    private JsonObject GetFeaturesNode(JsonObject document, bool create)
    {
        var shells = GetObject(document, "CShells", create);
        shells = GetObject(shells, "Shells", create);
        var shell = GetObjectCaseInsensitive(shells, CurrentShellId, create);
        return GetObject(shell, "Features", create);
    }

    private static JsonObject GetObject(JsonObject parent, string propertyName, bool create)
    {
        if (parent[propertyName] is JsonObject existing)
            return existing;

        if (!create)
            throw new InvalidOperationException($"Shell configuration must contain '{propertyName}'.");

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
            throw new InvalidOperationException($"Shell configuration must contain shell '{propertyName}'.");

        var created = new JsonObject();
        parent[propertyName] = created;
        return created;
    }

    private string ResolvePath()
    {
        var path = Options.ShellsJsonPath;
        return Path.IsPathRooted(path)
            ? path
            : Path.Combine(environment.ContentRootPath, path);
    }

    private static string CreateRevision(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private FeatureManagementOptions Options => options.Value;

    private string CurrentShellId => shellSettings.Id.Name;

    private sealed record ShellDocument(string Path, string Json, JsonObject Document);
}
