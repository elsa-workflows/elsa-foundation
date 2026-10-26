using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CShells;
using CShells.Lifecycle;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Exceptions;
using Elsa.Modularity.Core.Models;
using Microsoft.Extensions.Options;

namespace Elsa.Studio.Hosting.Nuplane;

internal sealed class ShellFeatureStore(
    IOptions<StudioHostingOptions> options,
    IConfigurationRoot configuration,
    IShellRegistry registry,
    StudioConfigurationMutationGate mutationGate) : IShellFeatureConfigurationStore
{
    private static readonly byte[] RevisionKey = RandomNumberGenerator.GetBytes(32);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private string ShellName => options.Value.ShellName;
    private string Prefix => $"CShells:Shells:{ShellName}:Features";

    public async Task<ShellFeatureConfigurationSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var settings = await ComposeAsync(cancellationToken);
        if (options.Value.ShellsFile is { } file && ConfigurationOwnership.IsExclusive(configuration, file, Prefix))
        {
            var document = await ReadAsync(file, cancellationToken);
            return Snapshot(Features(document), settings, ShellConfiguration(document));
        }
        return Snapshot(null, settings);
    }

    public async Task<ShellFeatureConfigurationSnapshot> SaveAsync(string expectedRevision, IReadOnlyList<FeatureConfigurationChange> features, CancellationToken cancellationToken = default)
    {
        await using var mutation = await mutationGate.AcquireAsync(cancellationToken);
        var file = options.Value.ShellsFile;
        if (file is null || !ConfigurationOwnership.IsExclusive(configuration, file, Prefix))
            throw new ConfigurationReadOnlyException("The selected shell configuration is host-owned or has configuration-provider overlays; local feature administration is read-only.");
        var settings = await ComposeAsync(cancellationToken);
        if (settings.FeatureConfigurators.Count > 0)
            throw new ConfigurationReadOnlyException("Code-first feature configurators control this shell; local feature administration is read-only.");
        var document = await ReadAsync(file, cancellationToken);
        var node = Features(document);
        var current = Snapshot(node, settings, ShellConfiguration(document));
        if (!string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal))
            throw new FeatureCatalogRevisionConflictException(expectedRevision, current.Revision);
        node.Clear();
        foreach (var feature in features.OrderBy(feature => feature.Id, StringComparer.OrdinalIgnoreCase))
            node[feature.Id] = feature.Enabled ? JsonNode.Parse(feature.Configuration.GetRawText()) : JsonValue.Create(false);
        try
        {
            await AtomicConfigWriter.WriteAtomicAsync(file, document.ToJsonString(JsonOptions) + Environment.NewLine, cancellationToken);
            configuration.Reload();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException)
        {
            throw new StudioHostingException($"Could not persist/reload feature configuration for Studio shell '{ShellName}'.", error);
        }
        return Snapshot(node, await ComposeAsync(cancellationToken), ShellConfiguration(document));
    }

    private async Task<ShellSettings> ComposeAsync(CancellationToken cancellationToken)
    {
        var provided = await registry.GetBlueprintAsync(ShellName, cancellationToken)
            ?? throw new StudioHostingException($"Studio shell '{ShellName}' has no configuration blueprint.");
        return await provided.Blueprint.ComposeAsync(cancellationToken);
    }

    private ShellFeatureConfigurationSnapshot Snapshot(JsonObject? node, ShellSettings settings, JsonElement shellConfiguration = default)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in settings.EnabledFeatures.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var raw = node?.FirstOrDefault(pair => string.Equals(pair.Key, id, StringComparison.OrdinalIgnoreCase)).Value;
            values[id] = raw is JsonObject value
                ? JsonSerializer.SerializeToElement(value)
                : JsonSerializer.SerializeToElement(ComposedConfiguration(settings, id));
        }
        var rawState = node?.ToJsonString() ?? JsonSerializer.Serialize(values);
        var revision = Convert.ToHexString(HMACSHA256.HashData(RevisionKey, Encoding.UTF8.GetBytes(rawState))).ToLowerInvariant();
        return new(ShellName, revision, values, shellConfiguration);
    }

    private static JsonObject ComposedConfiguration(ShellSettings settings, string id)
    {
        var root = new JsonObject();
        var prefix = id + ":";
        foreach (var (key, value) in settings.ConfigurationData.Where(pair => pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            var parts = key[prefix.Length..].Split(':');
            var parent = root;
            foreach (var part in parts[..^1])
            {
                if (parent[part] is not JsonObject child) parent[part] = child = new JsonObject();
                parent = child;
            }
            parent[parts[^1]] = JsonSerializer.SerializeToNode(value);
        }
        return root;
    }

    private JsonObject Shell(JsonObject document) => Child(Child(Child(document, "CShells"), "Shells"), ShellName);
    private JsonObject Features(JsonObject document) => Child(Shell(document), "Features");
    private JsonElement ShellConfiguration(JsonObject document) =>
        Shell(document).FirstOrDefault(pair => string.Equals(pair.Key, "Configuration", StringComparison.OrdinalIgnoreCase)).Value is JsonObject node
            ? JsonSerializer.SerializeToElement(node) : default;

    private JsonObject Child(JsonObject parent, string key) =>
        parent.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value as JsonObject
            ?? throw new StudioHostingException($"Studio shell '{ShellName}' requires an object '{key}' in its managed configuration file.");

    private static async Task<JsonObject> ReadAsync(string file, CancellationToken cancellationToken)
    {
        try
        {
            return JsonNode.Parse(await File.ReadAllTextAsync(file, cancellationToken), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                ?? throw new StudioHostingException("Studio shell configuration must be a JSON object.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new StudioHostingException($"Could not read Studio shell configuration '{file}'.", error);
        }
    }
}
