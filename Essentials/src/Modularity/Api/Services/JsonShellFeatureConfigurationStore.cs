using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CShells;
using Elsa.Modularity.Api.Options;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Exceptions;
using Elsa.Modularity.Core.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Elsa.Modularity.Api.Services;

public sealed class JsonShellFeatureConfigurationStore(
    IWebHostEnvironment environment,
    ShellSettings shellSettings,
    IOptions<FeatureManagementOptions> options,
    HostBuilderContext hostContext) : IShellFeatureConfigurationStore
{
    private static readonly byte[] s_processRevisionKey = RandomNumberGenerator.GetBytes(32);

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
        // Shell-scoped IConfiguration is CShells' read-only merged view. The standard host
        // context retains the actual root that its configuration blueprint reads on reload.
        if (hostContext.Configuration is not IConfigurationRoot hostConfiguration)
            throw new InvalidOperationException("The host configuration must support reload before feature configuration can be saved.");

        var document = await ReadDocumentAsync(cancellationToken);
        var current = CreateSnapshot(document);
        if (!string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal))
            throw new FeatureCatalogRevisionConflictException(expectedRevision, current.Revision);

        var accepted = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var feature in features.Where(x => x.Enabled).OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
        {
            accepted[feature.Id] = feature.Configuration.ValueKind == JsonValueKind.Object ? feature.Configuration.Clone() : s_emptyObject;
        }
        var replacement = JsonSerializer.SerializeToElement(accepted, Elsa.Serialization.Core.JsonBaseline.Options);
        var json = ReplaceFeatures(document.Document, replacement);
        using var saved = JsonDocument.Parse(json);
        var savedSnapshot = CreateSnapshot(new ShellDocument(document.Path, json, saved.RootElement.Clone()));
        await File.WriteAllTextAsync(document.Path, json + Environment.NewLine, cancellationToken);
        try
        {
            // Do not race the file watcher's asynchronous reload with the immediate shell reload.
            hostConfiguration.Reload();
        }
        catch (Exception error)
        {
            throw new InvalidOperationException("Feature configuration was saved, but host configuration reload failed. Inspect the host configuration and reload or restart before applying further changes.", error);
        }

        return savedSnapshot;
    }

    private async Task<ShellDocument> ReadDocumentAsync(CancellationToken cancellationToken)
    {
        var path = ResolvePath();
        if (!File.Exists(path))
            throw new InvalidOperationException($"Could not find shell configuration file at '{path}'.");

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        JsonElement document;
        try
        {
            using var parsed = JsonDocument.Parse(json);
            document = parsed.RootElement.Clone();
        }
        catch (JsonException e)
        {
            throw new InvalidOperationException("Shell configuration file is not valid JSON.", e);
        }

        if (document.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Shell configuration root must be a JSON object.");

        return new ShellDocument(path, json, document);
    }

    private ShellFeatureConfigurationSnapshot CreateSnapshot(ShellDocument document)
    {
        var featuresNode = GetObject(GetShellNode(document.Document), "Features");
        var features = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in featuresNode.EnumerateObject())
        {
            var name = property.Name;
            var configuration = property.Value;
            if (string.IsNullOrWhiteSpace(name) || configuration.ValueKind == JsonValueKind.Null)
                continue;

            if (configuration.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                if (configuration.GetBoolean())
                    features[name] = s_emptyObject;
                continue;
            }

            features[name] = configuration.ValueKind == JsonValueKind.Object
                ? configuration.Clone()
                : s_emptyObject;
        }

        var revision = CreateRevision(featuresNode);
        var shellConfiguration = GetOptionalConfigurationNode(document.Document);
        return new ShellFeatureConfigurationSnapshot(CurrentShellId, revision, features, shellConfiguration);
    }

    /// <summary>
    /// The shell's own <c>Configuration</c> node — the same one <c>EfMigrateOptions</c> documents as
    /// overriding the host's <c>Elsa:Persistence:EntityFramework:Migrate:Policy</c> — or an empty object
    /// when the shell declares none. Absence here is not an error: most
    /// shells override nothing.
    /// </summary>
    private JsonElement GetOptionalConfigurationNode(JsonElement document)
    {
        var shell = GetShellNode(document);
        return shell.TryGetProperty("Configuration", out var configuration) && configuration.ValueKind == JsonValueKind.Object
            ? configuration.Clone()
            : s_emptyObject;
    }

    private JsonElement GetShellNode(JsonElement document)
    {
        var shells = GetObject(GetObject(document, "CShells"), "Shells");
        return GetObject(shells, CurrentShellId, ignoreCase: true);
    }

    private static JsonElement GetObject(JsonElement parent, string propertyName, bool ignoreCase = false)
    {
        var property = parent.EnumerateObject().FirstOrDefault(property => string.Equals(property.Name, propertyName,
            ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        if (property.Value.ValueKind == JsonValueKind.Object) return property.Value;
        throw new InvalidOperationException($"Shell configuration must contain '{propertyName}'.");
    }

    private string ReplaceFeatures(JsonElement document, JsonElement replacement)
    {
        var path = new[] { "CShells", "Shells", CurrentShellId, "Features" };
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = Elsa.Serialization.Core.JsonBaseline.Options.Encoder })) WritePath(document, 0, writer);
        return Encoding.UTF8.GetString(stream.ToArray());

        void WritePath(JsonElement current, int depth, Utf8JsonWriter writer)
        {
            if (depth == path.Length) { writer.WriteRawValue(replacement.GetRawText(), skipInputValidation: true); return; }
            writer.WriteStartObject();
            var replaced = false;
            foreach (var property in current.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                if (!replaced && string.Equals(property.Name, path[depth], depth == 2 ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                { replaced = true; WritePath(property.Value, depth + 1, writer); }
                else writer.WriteRawValue(property.Value.GetRawText(), skipInputValidation: true);
            }
            writer.WriteEndObject();
        }
    }

    private string ResolvePath()
    {
        var path = Options.ShellsJsonPath;
        return Path.IsPathRooted(path)
            ? path
            : Path.Combine(environment.ContentRootPath, path);
    }

    /// <summary>
    /// The revision covers the raw features section, secret values included, so changing a secret still conflicts with
    /// a stale apply. It is keyed because the catalog shows everything else in that section: a plain hash would let a
    /// reader rebuild the section with each candidate secret and compare, which recovers a guessable one.
    /// </summary>
    private string CreateRevision(JsonElement value)
    {
        var key = string.IsNullOrWhiteSpace(Options.RevisionKey) ? s_processRevisionKey : Encoding.UTF8.GetBytes(Options.RevisionKey);
        return Convert.ToHexStringLower(Elsa.Serialization.Core.JsonHashInput.HashKeyed(value, key));
    }

    private FeatureManagementOptions Options => options.Value;

    private string CurrentShellId => shellSettings.Id.Name;

    private sealed record ShellDocument(string Path, string Json, JsonElement Document);
}
