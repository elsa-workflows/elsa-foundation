using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Elsa.Studio.Web;

internal sealed class StudioShellFeatureConfigurationStore(
    IHostEnvironment environment,
    IConfigurationRoot configuration,
    ILogger<StudioShellFeatureConfigurationStore>? logger = null)
{
    private const string FileName = "shells.json";
    private const string ShellId = "Default";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    // Serializes read-modify-write cycles against shells.json across the process so concurrent saves
    // can't interleave and clobber each other's revisions. Shared with the module-management writers
    // via ConfigFileWriter so all mutations of on-disk shell/management config are mutually exclusive.
    private static readonly SemaphoreSlim WriteGate = ConfigFileWriter.WriteGate;

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
        // Hold the process-wide gate across the whole read-modify-write cycle so the revision check and
        // the subsequent write are atomic with respect to other config mutations. Without this, two
        // concurrent saves could both read the same revision, both pass the conflict check, and the
        // second write would silently overwrite the first.
        await WriteGate.WaitAsync(cancellationToken);
        try
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

                var featureConfiguration = feature.Configuration.ValueKind is JsonValueKind.Object
                    ? JsonNode.Parse(feature.Configuration.GetRawText()) ?? new JsonObject()
                    : new JsonObject();

                featuresNode[feature.Id] = featureConfiguration;
            }

            var json = document.Document.ToJsonString(JsonOptions);
            await ConfigFileWriter.WriteAtomicAsync(document.Path, json + Environment.NewLine, cancellationToken);

            // Reload the configuration root explicitly rather than relying solely on the
            // reloadOnChange:true file watcher registered in Program.cs. The watcher fires
            // asynchronously and is debounced, so without this deterministic reload a caller reading
            // configuration immediately after SaveAsync could observe stale values. The redundant
            // watcher reload that follows shortly after is harmless (it re-parses identical content).
            configuration.Reload();

            return CreateSnapshot(new StudioShellDocument(document.Path, document.Document));
        }
        finally
        {
            WriteGate.Release();
        }
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

    // Interprets the "Features" section of shells.json into the set of enabled features and their
    // configuration objects. The on-disk feature value contract, in precedence order, is:
    //   1. bool false   -> feature is DISABLED (skipped; not present in the snapshot).
    //   2. bool true     -> feature is ENABLED with no configuration (mapped to an empty object).
    //   3. JSON object   -> feature is ENABLED, the object is its configuration.
    //   4. any other scalar (string/number/null/array) -> currently coerced to ENABLED-with-empty-config.
    //      This is lenient by design so a malformed value never disables a feature silently, but it is
    //      almost certainly an authoring mistake, so we log a warning to surface it.
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
            if (element.ValueKind is JsonValueKind.Object)
            {
                features[name] = element.Clone();
            }
            else
            {
                logger?.LogWarning(
                    "Feature '{Feature}' in {FileName} has an unexpected value kind '{ValueKind}'. Expected a bool or a configuration object; treating it as enabled with empty configuration.",
                    name,
                    FileName,
                    element.ValueKind);
                features[name] = EmptyJsonObject();
            }
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
