using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Activities.Design.Reconciliation.Core.Models;
using Elsa.Activities.Design.Reconciliation.Exceptions;
using Elsa.Events.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Serialization.Core;
using System.Text.Json;

namespace Elsa.Activities.Design.Reconciliation.Handlers;

/// <summary>
/// Handles <see cref="OnActivityVersionsReconciling"/> by pulling every registered
/// <see cref="IActivityReconciliationSource"/> from DI and contributing one
/// <c>IActivityDefinitionVersion</c> per entry. The handler is source-agnostic and
/// descriptor-type-agnostic: it validates that each entry carries a <c>DescriptorType</c> and a
/// descriptor payload, serialises the descriptor to opaque JSON, and stores
/// <c>(DescriptorType, DescriptorPayload)</c> on the version. It never resolves the descriptor type to
/// a CLR type (that happens only in the runtime feature that owns the type). No per-kind branch.
/// </summary>
public sealed class CollectActivityVersions(
    IQueries<ActivityDefinition> definitionQueries,
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

            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                var descriptorPayload = NormalizeDescriptor(entry, i);

                IActivityDefinition definition = await FindDefinition(entry.Id, cancellationToken)
                    ?? definitionFactory.Create(entry.ActivityTypeKey, entry.Category ?? string.Empty, entry.DisplayName, entry.Description, entry.Id);

                // The factory generates the version Id and the content Hash.
                var version = versionFactory.Create(
                    definition,
                    entry.Version,
                    entry.DescriptorType,
                    descriptorPayload,
                    source.SourceKind,
                    source.SourceId,
                    entry.Inputs,
                    entry.Outputs,
                    entry.Ports,
                    entry.ExecutionType);

                domainEvent.Versions.Add(version);
            }
        }
    }

    private async Task<ActivityDefinition?> FindDefinition(string? definitionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            return null;

        var filter = new ActivityDefinitionFilter { Id = definitionId };
        return await definitionQueries.Find(filter, cancellationToken);
    }

    /// <summary>
    /// Validates the entry and returns its descriptor as an opaque <see cref="JsonElement"/>. The
    /// descriptor may already be a <see cref="JsonElement"/> (JSON sources) or any object (CLR /
    /// Workflow sources), which is serialised here. No concrete descriptor type is resolved.
    /// </summary>
    private JsonElement NormalizeDescriptor(ActivityVersionReconciliationModel entry, int entryIndex)
    {
        if (string.IsNullOrWhiteSpace(entry.DescriptorType))
            throw new InvalidActivityVersionReconciliationEntryException(entryIndex, entry.ActivityTypeKey, entry.DescriptorType, $"'{nameof(entry.DescriptorType)}' is required.");

        var element = entry.Descriptor is JsonElement jsonElement
            ? jsonElement
            : payloadSerializer.SerializeToElement(entry.Descriptor);

        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new InvalidActivityVersionReconciliationEntryException(entryIndex, entry.ActivityTypeKey, entry.DescriptorType, $"'{nameof(entry.Descriptor)}' is required.");

        return element.Clone();
    }
}
