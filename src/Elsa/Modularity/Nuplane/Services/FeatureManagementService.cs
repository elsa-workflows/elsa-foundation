using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Exceptions;
using Elsa.Modularity.Core.Models;

namespace Elsa.Modularity.Nuplane.Services;

public sealed class FeatureManagementService(
    IShellFeatureConfigurationStore shellStore,
    IEnumerable<IFeatureCatalogContributor> contributors,
    IRuntimeFeatureCatalogRefresher runtimeFeatureCatalogRefresher,
    IShellReloader shellReloader) : IFeatureManagementService
{
    public async Task<FeatureCatalogResponse> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        var shell = await shellStore.LoadAsync(cancellationToken);
        return await BuildCatalogAsync(shell, cancellationToken);
    }

    public async Task<FeatureApplyResult> ApplyAsync(FeatureApplyRequest request, CancellationToken cancellationToken = default)
    {
        var current = await BuildCatalogAsync(await shellStore.LoadAsync(cancellationToken), cancellationToken);
        ValidateRequest(request, current);

        var changes = request.Features
            .Select(feature => new FeatureConfigurationChange(feature.Id, feature.Enabled, feature.Configuration.Clone()))
            .ToArray();

        ShellFeatureConfigurationSnapshot saved;
        try
        {
            saved = await shellStore.SaveAsync(request.Revision, changes, cancellationToken);
        }
        catch (FeatureCatalogRevisionConflictException)
        {
            throw;
        }

        var featureDescriptorCount = await runtimeFeatureCatalogRefresher.RefreshAsync(cancellationToken);
        var reloadedShellCount = await shellReloader.ReloadAsync(cancellationToken);
        var catalog = await BuildCatalogAsync(saved, cancellationToken);

        return new FeatureApplyResult(catalog, featureDescriptorCount, reloadedShellCount);
    }

    private async Task<FeatureCatalogResponse> BuildCatalogAsync(ShellFeatureConfigurationSnapshot shell, CancellationToken cancellationToken)
    {
        var context = new FeatureCatalogContributionContext(shell);

        foreach (var feature in shell.Features.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var builder = context.GetOrAdd(feature.Key);
            builder.Enabled = true;
            builder.DisplayName ??= feature.Key;
            builder.SourceKind = FeatureSourceKinds.Shell;
            builder.Configuration = feature.Value.Clone();
        }

        foreach (var contributor in contributors)
            await contributor.ContributeAsync(context, cancellationToken);

        var items = context.Items.Values
            .Select(x => x.ToItem())
            .OrderByDescending(x => x.Enabled)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new FeatureCatalogResponse(shell.Revision, items);
    }

    private static void ValidateRequest(FeatureApplyRequest request, FeatureCatalogResponse current)
    {
        if (string.IsNullOrWhiteSpace(request.Revision))
            throw new ArgumentException("Revision is required.", nameof(request));

        var currentById = current.Features.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var feature in request.Features)
        {
            if (string.IsNullOrWhiteSpace(feature.Id))
                throw new ArgumentException("Feature ID is required.", nameof(request));

            if (feature.Enabled && feature.Configuration.ValueKind is not System.Text.Json.JsonValueKind.Object)
                throw new ArgumentException($"Feature '{feature.Id}' configuration must be a JSON object.", nameof(request));

            if (!currentById.TryGetValue(feature.Id, out var currentFeature))
                continue;

            foreach (var setting in currentFeature.Settings)
            {
                if (!TryGetProperty(feature.Configuration, setting.Name, out var value))
                    continue;

                ValidateSettingValue(feature.Id, setting, value);
            }
        }
    }

    private static bool TryGetProperty(System.Text.Json.JsonElement configuration, string name, out System.Text.Json.JsonElement value)
    {
        if (configuration.ValueKind is System.Text.Json.JsonValueKind.Object && configuration.TryGetProperty(name, out value))
            return true;

        value = default;
        return false;
    }

    private static void ValidateSettingValue(string featureId, FeatureSettingDescriptor setting, System.Text.Json.JsonElement value)
    {
        var jsonType = (setting.JsonType ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(jsonType) || value.ValueKind is System.Text.Json.JsonValueKind.Null)
            return;

        var valid = jsonType switch
        {
            "string" => value.ValueKind is System.Text.Json.JsonValueKind.String,
            "boolean" => value.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False,
            "integer" => value.ValueKind is System.Text.Json.JsonValueKind.Number && value.TryGetInt64(out _),
            "number" => value.ValueKind is System.Text.Json.JsonValueKind.Number,
            "object" => value.ValueKind is System.Text.Json.JsonValueKind.Object,
            "array" => value.ValueKind is System.Text.Json.JsonValueKind.Array,
            _ => true
        };

        if (!valid)
            throw new ArgumentException($"Feature '{featureId}' setting '{setting.Name}' must be a JSON {jsonType} value.");
    }
}
