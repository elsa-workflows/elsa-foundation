using System.Text.Json;
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
    IEnumerable<IBuiltInAuthoringDescriptorProvider> builtInDescriptorProviders)
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

        var persisted = versions
            .Where(version => definitionsById.ContainsKey(version.DefinitionId))
            .Select(version => (Version: version, Definition: definitionsById[version.DefinitionId]))
            .Where(item => request.Availability == ActivityCatalogAvailability.All || addableKeys.Contains(item.Definition.ActivityTypeKey))
            .OrderBy(item => item.Definition.Category, StringComparer.Ordinal)
            .ThenBy(item => item.Definition.DisplayName, StringComparer.Ordinal)
            .ThenBy(item => item.Version.Id, StringComparer.Ordinal)
            .Select(item => ToView(item.Version, item.Definition, addableKeys.Contains(item.Definition.ActivityTypeKey)));

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
        bool available)
    {
        var structureFacet = version.DesignFacets.FirstOrDefault(IsStructureFacet);
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
                structure));
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
            input.UISpecifications);

    private static ActivityOutputDescriptorView ToView(OutputDefinition output) =>
        new(
            output.Name,
            output.Type.Alias,
            output.Type.CollectionKind,
            output.DisplayName,
            output.Description,
            output.Category,
            output.IsBrowsable ?? true);

    private static IEnumerable<ActivityPortDescriptorView> ToPorts(ActivityDesignFacet facet)
    {
        if (facet.Payload.ValueKind != JsonValueKind.Object ||
            !facet.Payload.TryGetProperty("ports", out var ports) ||
            ports.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var port in ports.EnumerateArray())
        {
            if (port.ValueKind != JsonValueKind.Object ||
                !port.TryGetProperty("name", out var nameProperty) ||
                string.IsNullOrWhiteSpace(nameProperty.GetString()))
                continue;

            var name = nameProperty.GetString()!;
            yield return new ActivityPortDescriptorView(
                name,
                ReadString(port, "displayName") ?? name,
                ReadString(port, "type"),
                ReadBoolean(port, "isBrowsable") ?? true);
        }
    }

    private static bool IsStructureFacet(ActivityDesignFacet facet) =>
        facet.Kind.Contains("structure", StringComparison.OrdinalIgnoreCase);

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool? ReadBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;
}
