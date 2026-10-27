using System.Text.Json;
using System.Text.Json.Nodes;

namespace Elsa.Studio.Hosting.Nuplane;

internal sealed class ManagementConfigurationStore(StudioNuplaneSettings settings, IConfigurationRoot configuration, StudioConfigurationMutationGate gate)
{
    public const string RetentionPrefix = "Studio:PackageManagement:RetentionPolicy";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public bool CanWrite(string prefix) => ConfigurationOwnership.IsExclusive(configuration, settings.ManagementFile, prefix);

    public RetentionPolicyState ReadRetention()
    {
        var section = configuration.GetSection(RetentionPrefix);
        var selected = section.GetChildren().Any(child => child.Key.Equals("KeepNewestInactiveVersionsPerPackage", StringComparison.OrdinalIgnoreCase));
        if (!selected && configuration.GetSection("Nuplane:CleanupPolicy").GetChildren().Any())
            return new(null, "LegacyUnsupported");
        try
        {
            var count = selected ? section.GetValue<int?>("KeepNewestInactiveVersionsPerPackage") : null;
            if (count < 0) throw new StudioHostingException("Studio's inactive-version retention count must be nonnegative or null.");
            return new(count, "Supported");
        }
        catch (InvalidOperationException error)
        {
            throw new StudioHostingException("Studio's inactive-version retention policy is invalid.", error);
        }
    }

    public Task MutateAsync(string prefix, Action<JsonObject> mutate, CancellationToken cancellationToken) =>
        MutateIfAsync(prefix, document => { mutate(document); return true; }, cancellationToken);

    public async Task<bool> MutateIfAsync(string prefix, Func<JsonObject, bool> mutate, CancellationToken cancellationToken)
    {
        await using var lease = await gate.AcquireAsync(cancellationToken);
        if (!CanWrite(prefix))
            throw new ConfigurationReadOnlyException("The management file is not the exclusive configuration source for this setting.");
        var document = File.Exists(settings.ManagementFile)
            ? JsonNode.Parse(await File.ReadAllTextAsync(settings.ManagementFile, cancellationToken), new JsonNodeOptions { PropertyNameCaseInsensitive = true })?.AsObject() ?? new JsonObject()
            : new JsonObject();
        if (!mutate(document)) return false;
        await AtomicConfigWriter.WriteAtomicAsync(settings.ManagementFile, document.ToJsonString(JsonOptions) + Environment.NewLine, cancellationToken);
        AtomicConfigWriter.ReloadAfterSave(configuration, "Studio management configuration");
        return true;
    }

    public Task SaveRetentionAsync(int? count, CancellationToken cancellationToken) => MutateAsync(RetentionPrefix, document =>
    {
        var studio = Object(document, "Studio");
        var management = Object(studio, "PackageManagement");
        management["RetentionPolicy"] = new JsonObject { ["KeepNewestInactiveVersionsPerPackage"] = count };
    }, cancellationToken);

    public static JsonObject Object(JsonObject parent, string key)
    {
        if (parent[key] is JsonObject child) return child;
        var created = new JsonObject();
        parent[key] = created;
        return created;
    }
}

internal sealed record RetentionPolicyState(int? KeepNewestInactiveVersionsPerPackage, string Status);
