using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Activities.Design.Reconciliation.Core.Models;
using Elsa.Activities.Design.Reconciliation.Exceptions;
using Elsa.Events.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Primitives.Contracts;
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
    IActivityDefinitionHasher activityDefinitionHasher,
    IIdentityGenerator identityGenerator,
    IPayloadSerializer payloadSerializer,
    IEnumerable<IActivityReconciliationSource> sources,
    ISystemClock clock)
    : IEventHandler<OnActivityVersionsReconciling>
{
    public async Task Handle(OnActivityVersionsReconciling domainEvent, CancellationToken cancellationToken)
    {
        foreach (var source in sources)
        {
            var entries = (await source.Read(cancellationToken)).ToArray();

            var now = clock.UtcNow;
            var provisionedBy = Environment.MachineName;

            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                var descriptorPayload = NormalizeDescriptor(entry, i);

                var definition = await FindDefinition(entry.Id, cancellationToken);
                definition ??= new ActivityDefinition
                {
                    Id = string.IsNullOrWhiteSpace(entry.Id)
                        ? identityGenerator.Generate()
                        : entry.Id,
                    ActivityTypeKey = entry.ActivityTypeKey,
                    Category = entry.Category ?? string.Empty,
                    Description = entry.Description,
                    DisplayName = entry.DisplayName,
                };

                var version = new ActivityDefinitionVersion(entry.Version, definition.Id, executionType: entry.ExecutionType)
                {
                    Definition = definition,
                    Id = identityGenerator.Generate(),
                    DescriptorType = entry.DescriptorType,
                    DescriptorPayload = descriptorPayload,
                    Inputs = entry.Inputs,
                    Outputs = entry.Outputs,
                    Ports = entry.Ports,
                    ReconciledAt = now,
                    ReconciledBy = provisionedBy,
                    SourceId = source.SourceId,
                    SourceKind = source.SourceKind
                };

                var hash = activityDefinitionHasher.Hash(definition, version);
                version.ReconcilliationHash = hash;

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
