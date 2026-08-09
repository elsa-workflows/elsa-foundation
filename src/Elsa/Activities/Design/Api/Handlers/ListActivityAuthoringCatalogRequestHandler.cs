using System.Text.Json;
using Elsa.Activities.Design.Api.Constants;
using Elsa.Activities.Design.Api.Contracts;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Handlers;

public sealed class ListActivityAuthoringCatalogRequestHandler(
    IActivityDefinitionStore definitionStore,
    IActivityDefinitionVersionStore versionStore,
    IActivityAvailabilityEvaluator availabilityEvaluator,
    IActivityAvailabilitySettingsStore settingsStore,
    IEnumerable<IBuiltInAuthoringDescriptorProvider> builtInDescriptorProviders,
    IActivityFeatureAttributionResolver featureAttributionResolver)
    : IRequestHandler<ListActivityAuthoringCatalog, ActivityAuthoringCatalogView>
{
    public async Task<ActivityAuthoringCatalogView> Handle(ListActivityAuthoringCatalog request, CancellationToken cancellationToken)
    {
        var definitions = await definitionStore.ListAsync(new ActivityDefinitionFilter(), cancellationToken);
        var definitionsById = definitions.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var versions = await versionStore.ListAsync(cancellationToken);
        var settings = await settingsStore.LoadAsync(ActivityAvailabilitySettings.HostDefaultScope, cancellationToken);
        var addableKeys = availabilityEvaluator.FilterAddable(definitions, settings)
            .Select(x => x.ActivityTypeKey)
            .ToHashSet(StringComparer.Ordinal);

        var persistedItems = versions
            .Where(version => definitionsById.ContainsKey(version.DefinitionId))
            .Select(version => (Version: version, Definition: definitionsById[version.DefinitionId]))
            .Where(item => request.Availability == ActivityCatalogAvailability.All || addableKeys.Contains(item.Definition.ActivityTypeKey))
            .OrderBy(item => item.Definition.Category, StringComparer.Ordinal)
            .ThenBy(item => item.Definition.DisplayName, StringComparer.Ordinal)
            .ThenBy(item => item.Version.Id, StringComparer.Ordinal)
            .ToArray();

        var persisted = new List<ActivityAuthoringDescriptorView>(persistedItems.Length);
        foreach (var item in persistedItems)
        {
            // Feature attribution is only meaningful for CLR-backed versions: their activity type key is
            // a well-known type alias that resolves to the providing feature's assembly. Other consumers
            // (e.g. graph activities) keep source provenance but carry no feature id.
            var featureId = string.Equals(item.Version.ConsumerKey, ActivityProvenanceConstants.ClrActivityConsumerKey, StringComparison.Ordinal)
                ? await featureAttributionResolver.ResolveProvidingFeatureAsync(item.Definition.ActivityTypeKey, cancellationToken)
                : null;
            persisted.Add(ToView(item.Version, item.Definition, addableKeys.Contains(item.Definition.ActivityTypeKey), featureId));
        }

        // Built-in engine intrinsics (Set Variable, Set Output, …) are code-owned and always addable: they
        // have no persisted catalog row and are never gated by the availability policy.
        var builtIns = builtInDescriptorProviders
            .SelectMany(provider => provider.GetDescriptors())
            .OrderBy(descriptor => descriptor.Category, StringComparer.Ordinal)
            .ThenBy(descriptor => descriptor.DisplayName, StringComparer.Ordinal);

        return new ActivityAuthoringCatalogView(persisted.Concat(builtIns).ToArray());
    }

    private static ActivityAuthoringDescriptorView ToView(
        Persistence.Core.Entities.ActivityDefinitionVersion version,
        Persistence.Core.Entities.ActivityDefinition definition,
        bool available,
        string? featureId)
    {
        if (!ActivityStructureDesignFacetReader.TryReadSingle(version.DesignFacets, out var structureFacet))
            throw new InvalidOperationException(
                $"Activity definition version '{version.Id}' declares more than one structure design facet.");
        var structure = structureFacet is null
            ? null
            : new ActivityAuthoringStructureView(structureFacet.Kind, structureFacet.SchemaVersion, structureFacet.Payload.Clone());

        return new ActivityAuthoringDescriptorView(
            version.Id,
            definition.ActivityTypeKey,
            version.Version,
            definition.DisplayName ?? definition.ActivityTypeKey,
            definition.Category,
            definition.Description,
            version.ExecutionType.ToString(),
            available,
            available ? null : "Excluded by the effective activity availability policy.",
            version.Inputs.Select(ToView).ToArray(),
            version.Outputs.Select(ToView).ToArray(),
            version.DesignFacets.SelectMany(ToPorts).ToArray(),
            structureFacet?.Payload.Clone(),
            new ActivityAuthoringTemplateView(
                "activity",
                version.Id,
                new Dictionary<string, ActivityArgumentValue>(StringComparer.Ordinal),
                new Dictionary<string, ActivityArgumentValue>(StringComparer.Ordinal),
                structure),
            Provenance: new ActivityAuthoringProvenanceView(version.SourceKind, version.SourceId, featureId));
    }

    private static ActivityInputDescriptorView ToView(InputDefinition input) =>
        new(
            input.ReferenceKey,
            input.Name,
            input.Type.Alias,
            input.Type.CollectionKind,
            input.DisplayName,
            input.Description,
            input.Order,
            input.Category,
            input.IsBrowsable ?? true,
            input.IsRequired,
            input.IsNullable,
            input.UiHint,
            input.DefaultValue,
            input.DefaultSyntax,
            input.UISpecifications,
            // Pre-G1 persisted rows carry HasStaticDefault=false even when an attribute default exists;
            // a non-null DefaultValue implies a static default regardless (see InputDefinition remarks).
            input.HasStaticDefault || input.DefaultValue is not null,
            input.EnumValues);

    private static ActivityOutputDescriptorView ToView(OutputDefinition output) =>
        new(
            output.Name,
            output.Type.Alias,
            output.Type.CollectionKind,
            output.DisplayName,
            output.Description,
            output.Category,
            output.IsBrowsable ?? true,
            output.IsRequired,
            output.ReferenceKey);

    // Parsing of the "ports" facet convention lives in ActivityPortDesignFacetReader so the served
    // catalog and the build-time contract fragments cannot disagree about an activity's ports
    // (one-projection rule, spec 149 / RFC #1191). This method only shapes the result into the view.
    private static IEnumerable<ActivityPortDescriptorView> ToPorts(ActivityDesignFacet facet) =>
        ActivityPortDesignFacetReader.Read(facet)
            .Select(port => new ActivityPortDescriptorView(
                port.Name,
                port.DisplayName,
                port.Type,
                port.IsBrowsable,
                port.ReferenceKey));
}
