using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Handlers;

public sealed class GetActivityAuthoringCapabilitiesHandler(
    IActivityProviderRegistry providers,
    IActivityContractCapabilityCatalog contractTypes,
    IActivityTypeKeyPolicy typeKeys,
    IActivityAuthoringContext context)
    : IRequestHandler<GetActivityAuthoringCapabilities, ActivityAuthoringCapabilitiesView>
{
    public Task<ActivityAuthoringCapabilitiesView> Handle(
        GetActivityAuthoringCapabilities request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var providerViews = providers.Providers
            .Where(x => context.CanAuthorProvider(x.ProviderKey))
            .OrderBy(x => x.ProviderKey, StringComparer.Ordinal)
            .Select(provider => new ActivityProviderAuthoringCapabilityView(
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
                    .ToArray()))
            .ToArray();
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
        var snapshot = new
        {
            contractSchemaVersions = new[] { "1" },
            activityTypeKeyRules = typeKeys.Rules,
            providers = providerViews,
            types = typeViews,
            storageDriverKeys = drivers
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var fingerprint = $"sha256:{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}";
        return Task.FromResult(new ActivityAuthoringCapabilitiesView(
            snapshot.contractSchemaVersions,
            typeKeys.Rules,
            providerViews,
            typeViews,
            drivers,
            fingerprint));
    }
}
