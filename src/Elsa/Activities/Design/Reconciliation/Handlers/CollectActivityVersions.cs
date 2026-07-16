using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Activities.Design.Reconciliation.Core.Models;
using Elsa.Activities.Design.Reconciliation.Exceptions;
using Elsa.Activities.Design.Reconciliation.Services;
using Elsa.Events.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Serialization.Core;
using System.Text.Json;

namespace Elsa.Activities.Design.Reconciliation.Handlers;

/// <summary>
/// Handles <see cref="OnActivityVersionsReconciling"/> by pulling every registered
/// <see cref="IActivityReconciliationSource"/> from DI and contributing one
/// <c>IActivityDefinitionVersion</c> per entry. The handler is source-agnostic and
/// provider- and consumer-agnostic: it validates stable provider/consumer keys and schemas, serialises
/// the Runtime descriptor to opaque JSON, and never resolves a CLR type. No per-kind branch.
/// </summary>
public sealed class CollectActivityVersions(
    IActivityDefinitionStore definitionStore,
    IActivityDefinitionFactory definitionFactory,
    IActivityDefinitionVersionFactory versionFactory,
    IPayloadSerializer payloadSerializer,
    IEnumerable<IActivityReconciliationSource> sources)
    : IEventHandler<OnActivityVersionsReconciling>
{
    public async Task Handle(OnActivityVersionsReconciling domainEvent, CancellationToken cancellationToken)
    {
        foreach (var source in sources)
        {
            var entries = (await source.Read(cancellationToken)).ToArray();

            // Batch the per-entry definition lookup (issue #417 item 1): one IN read for every entry Id in
            // this source instead of a FindAsync per entry. This is a pure read — the handler never persists,
            // so no in-loop mutation is needed here; two entries sharing a missing Id both fabricate distinct
            // in-memory definitions exactly as before, and the reconciler dedupes them on write.
            var definitionsById = await PrefetchDefinitionsById(entries, cancellationToken);

            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                var descriptorPayload = NormalizeDescriptor(entry, i);

                var definitionId = StableDefinitionId(entry);
                IActivityDefinition definition = FindDefinition(definitionsById, definitionId)
                    ?? definitionFactory.Create(entry.ActivityTypeKey, entry.Category ?? string.Empty, entry.DisplayName, entry.Description, definitionId);

                // Stable ids keep unchanged activity type/logical version pairs resolvable across
                // catalog rebuilds; the factory still owns the content Hash.
                var version = versionFactory.Create(
                    definition,
                    entry.Version,
                    entry.ProviderKey,
                    entry.ProviderSchemaVersion,
                    entry.ConsumerKey,
                    entry.ConsumerSchemaVersion,
                    descriptorPayload,
                    source.SourceKind,
                    source.SourceId,
                    entry.Inputs,
                    entry.Outputs,
                    entry.DesignFacets,
                    entry.ExecutionType,
                    ActivityCatalogStableIds.VersionId(entry.ActivityTypeKey, entry.Version));

                domainEvent.Versions.Add(version);
            }
        }
    }

    private async Task<Dictionary<string, ActivityDefinition>> PrefetchDefinitionsById(
        ActivityVersionReconciliationModel[] entries,
        CancellationToken cancellationToken)
    {
        var ids = entries
            .Select(e => e.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
            return new Dictionary<string, ActivityDefinition>();

        var definitions = await definitionStore.ListAsync(new ActivityDefinitionFilter { Ids = ids }, cancellationToken);

        // First-wins on duplicate Ids mirrors the previous FindAsync (first match) semantics; Id is a
        // surrogate key so duplicates are not expected in practice.
        var byId = new Dictionary<string, ActivityDefinition>();
        foreach (var definition in definitions)
            byId.TryAdd(definition.Id, definition);

        return byId;
    }

    private static ActivityDefinition? FindDefinition(Dictionary<string, ActivityDefinition> definitionsById, string? definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            return null;

        return definitionsById.GetValueOrDefault(definitionId);
    }

    private static string? StableDefinitionId(ActivityVersionReconciliationModel entry) =>
        !string.IsNullOrWhiteSpace(entry.Id)
            ? entry.Id
            : ActivityCatalogStableIds.DefinitionId(entry.ActivityTypeKey);

    /// <summary>
    /// Validates the entry and returns its descriptor as an opaque <see cref="JsonElement"/>. The
    /// descriptor may already be a <see cref="JsonElement"/> (JSON sources) or any object (CLR /
    /// Workflow sources), which is serialised here. No concrete descriptor type is resolved.
    /// </summary>
    private JsonElement NormalizeDescriptor(ActivityVersionReconciliationModel entry, int entryIndex)
    {
        if (string.IsNullOrWhiteSpace(entry.ProviderKey))
            throw Invalid(entry, entryIndex, $"'{nameof(entry.ProviderKey)}' is required.");
        if (string.IsNullOrWhiteSpace(entry.ProviderSchemaVersion))
            throw Invalid(entry, entryIndex, $"'{nameof(entry.ProviderSchemaVersion)}' is required.");
        if (string.IsNullOrWhiteSpace(entry.ConsumerKey))
            throw Invalid(entry, entryIndex, $"'{nameof(entry.ConsumerKey)}' is required.");
        if (string.IsNullOrWhiteSpace(entry.ConsumerSchemaVersion))
            throw Invalid(entry, entryIndex, $"'{nameof(entry.ConsumerSchemaVersion)}' is required.");

        var element = entry.Descriptor is JsonElement jsonElement
            ? jsonElement
            : payloadSerializer.SerializeToElement(entry.Descriptor);

        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw Invalid(entry, entryIndex, $"'{nameof(entry.Descriptor)}' is required.");

        return element.Clone();
    }

    private static InvalidActivityVersionReconciliationEntryException Invalid(
        ActivityVersionReconciliationModel entry,
        int entryIndex,
        string message) => new(entryIndex, entry.ActivityTypeKey, entry.ConsumerKey, message);
}
