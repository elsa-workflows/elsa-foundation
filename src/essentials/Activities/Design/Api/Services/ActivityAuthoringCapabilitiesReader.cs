using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Core.Contracts;

namespace Elsa.Activities.Design.Api.Services;

/// <summary>The authoring capabilities projection the Design endpoints dispatch to.</summary>
public sealed class ActivityAuthoringCapabilitiesReader(
    IActivityProviderRegistry providers,
    IActivityContractCapabilityCatalog contractTypes,
    IActivityTypeKeyPolicy typeKeys,
    IActivityAuthoringContextAsync context) : IActivityAuthoringCapabilitiesReader
{
    // Serializing the snapshot with fresh Web-default options would cache this assembly's type
    // metadata in the process-shared Web-defaults serializer cache and root the collectible
    // module context. The owner context avoids that; it is bound to plain Web defaults (strict
    // encoder, no wire converters) because the fingerprint bytes are a frozen contract.
    private static readonly ActivitiesDesignJsonContext FingerprintContext =
        new(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public async Task<ActivityAuthoringCapabilitiesView> GetAsync(GetActivityAuthoringCapabilities request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var providerViews = new List<ActivityProviderAuthoringCapabilityView>();
        foreach (var provider in providers.Providers.OrderBy(x => x.ProviderKey, StringComparer.Ordinal))
        {
            if (!await context.CanAuthorProviderAsync(provider.ProviderKey, cancellationToken))
                continue;

            providerViews.Add(new(
                provider.ProviderKey,
                provider.AuthoringCapabilities.DisplayName,
                provider.AuthoringCapabilities.ManifestSchemas
                    .OrderBy(x => x.SchemaVersion, StringComparer.Ordinal)
                    .Select(x => new ActivityProviderManifestSchemaCapabilityView(
                        x.SchemaVersion,
                        x.IsAuthorable,
                        x.MigratableFromSchemaVersions.Order(StringComparer.Ordinal).ToArray()))
                    .ToArray(),
                provider.AuthoringCapabilities.ContractConstraints.RequiredOutcomes
                    .OrderBy(x => x.ReferenceKey, StringComparer.Ordinal)
                    .Select(x => new ActivityOutcomeContractView(x.ReferenceKey, x.Name, x.IsEmitted, x.Description))
                    .ToArray()));
        }
        var typeViews = contractTypes.Types
            .OrderBy(x => x.Alias, StringComparer.Ordinal)
            .Select(x => new ActivityContractTypeCapabilityView(
                x.Alias,
                x.DisplayName,
                x.Category,
                x.DefaultEditor,
                x.SupportedCollectionKinds.Select(kind => kind.ToString()).Order(StringComparer.Ordinal).ToArray(),
                x.SupportsNull,
                x.SupportsDurability,
                x.CompatibleStorageDriverKeys.Order(StringComparer.Ordinal).ToArray()))
            .ToArray();
        var drivers = typeViews.SelectMany(x => x.CompatibleStorageDriverKeys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var snapshot = new ActivityAuthoringCapabilitiesSnapshot(
            ["1"],
            typeKeys.Rules,
            providerViews,
            typeViews,
            drivers);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, FingerprintContext.ActivityAuthoringCapabilitiesSnapshot);
        var fingerprint = $"sha256:{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}";
        return new ActivityAuthoringCapabilitiesView(
            snapshot.ContractSchemaVersions,
            snapshot.ActivityTypeKeyRules,
            snapshot.Providers,
            snapshot.Types,
            snapshot.StorageDriverKeys,
            fingerprint);
    }
}

/// <summary>The authoring capabilities seam.</summary>
public interface IActivityAuthoringCapabilitiesReader
{
    Task<ActivityAuthoringCapabilitiesView> GetAsync(GetActivityAuthoringCapabilities request, CancellationToken cancellationToken);
}
