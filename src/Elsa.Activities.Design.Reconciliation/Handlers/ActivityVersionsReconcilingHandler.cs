using Elsa.Activities.Design.Core.Contracts;
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
/// <see cref="IActivityReconciliationSource"/> from DI, validating each contributed entry (kind
/// known, descriptor structurally valid, kind/descriptor agree), and contributing one
/// <c>IActivityDefinitionVersion</c> per entry. The handler is source-agnostic — CLR assembly
/// scanning, JSON files, or any other source funnel through here identically. Validation throws
/// <c>InvalidActivityVersionReconciliationEntryException</c> with entry context — the reconciler
/// sees a domain-scoped failure, never a raw <c>JsonException</c>.
/// </summary>
public sealed class ActivityVersionsReconcilingHandler(
    IQueries<ActivityDefinition> definitionQueries,
    IActivityDefinitionHasher activityDefinitionHasher,
    IIdentityGenerator identityGenerator,
    IPayloadSerializer payloadSerializer,
    IEnumerable<IActivityReconciliationSource> sources,
    IImplementationDescriptorRegistry descriptorRegistry,
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
                var descriptor = DeserializeImplementationDescriptor(entry, i);

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
                    ImplementationDescriptor = descriptor,
                    ImplementationKind = descriptor.Kind,
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

    private IImplementationDescriptor DeserializeImplementationDescriptor(ActivityVersionReconciliationModel entry, int entryIndex)
    {
        if (string.IsNullOrWhiteSpace(entry.ImplementationKind))
            throw new InvalidActivityVersionReconciliationEntryException(entryIndex, entry.ActivityTypeKey, entry.ImplementationKind, $"'{nameof(entry.ImplementationKind)}' is required.");

        var descriptorElement = entry.ImplementationDescriptor is JsonElement jsonElement
            ? jsonElement
            : payloadSerializer.SerializeToElement(entry.ImplementationDescriptor);


        if (descriptorElement.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new InvalidActivityVersionReconciliationEntryException(entryIndex, entry.ActivityTypeKey, entry.ImplementationKind, $"'{nameof(entry.ImplementationDescriptor)}' is required.");

        var descriptorType = descriptorRegistry.Resolve(entry.ImplementationKind)
            ?? throw new InvalidActivityVersionReconciliationEntryException(
                entryIndex,
                entry.ActivityTypeKey,
                entry.ImplementationKind,
                $"No implementation-descriptor type is registered for kind '{entry.ImplementationKind}'. The owning module may not be installed, or the kind value is misspelled."
            );

        IImplementationDescriptor descriptor;
        try
        {
            descriptor = (IImplementationDescriptor)payloadSerializer.Deserialize(descriptorElement.GetRawText(), descriptorType);
        }
        catch (Exception inner)
        {
            throw new InvalidActivityVersionReconciliationEntryException(
                entryIndex,
                entry.ActivityTypeKey,
                entry.ImplementationKind,
                $"Failed to deserialise 'implementationDescriptor' into '{descriptorType.FullName}': {inner.Message}",
                inner
            );
        }

        if (!string.Equals(descriptor.Kind, entry.ImplementationKind, StringComparison.Ordinal))
        {
            throw new InvalidActivityVersionReconciliationEntryException(
                entryIndex,
                entry.ActivityTypeKey,
                entry.ImplementationKind,
                $"Kind mismatch: entry declares '{entry.ImplementationKind}' but the deserialised descriptor reports '{descriptor.Kind}'. The descriptor type's Kind property and the entry's '{nameof(entry.ImplementationKind)}' must agree."
            );
        }

        return descriptor;
    }
}
