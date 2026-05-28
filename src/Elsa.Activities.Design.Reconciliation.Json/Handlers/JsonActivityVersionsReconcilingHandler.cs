using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Activities.Design.Reconciliation.Json.Exceptions;
using Elsa.Activities.Design.Reconciliation.Json.Models;
using Elsa.Activities.Design.Reconciliation.Json.Options;
using Elsa.Activities.Design.Reconciliation.Json.Services;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Elsa.Activities.Design.Reconciliation.Json.Handlers;

/// <summary>
/// Handles <see cref="OnActivityVersionsReconciling"/> by reading the configured JSON file,
/// validating each entry (kind known, descriptor structurally valid, kind/descriptor agree),
/// and contributing one <c>IActivityDefinitionVersion</c> per entry. Validation throws
/// <c>InvalidJsonCatalogEntryException</c> with entry context — the reconciler sees a
/// domain-scoped failure, never a raw <c>JsonException</c>.
/// </summary>
public sealed class JsonActivityVersionsReconcilingHandler(
    IOptions<JsonReconciliationOptions> options,
    IPayloadSerializer payloadSerializer,
    IImplementationDescriptorRegistry descriptorRegistry,
    JsonActivityCatalogReader reader,
    ISystemClock clock)
    : IDomainEventHandler<OnActivityVersionsReconciling>
{
    public async ValueTask Handle(OnActivityVersionsReconciling domainEvent, CancellationToken cancellationToken)
    {
        var entries = await reader.ReadAsync(cancellationToken);
        var now = clock.UtcNow;
        var provisionedBy = Environment.MachineName;
        var sourceId = options.Value.SourceId;

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var descriptor = DeserializeImplementationDescriptor(entry, i);

            var definition = new JsonContributedDefinition(
                ActivityTypeKey: entry.Definition.ActivityTypeKey,
                SourceKind: JsonReconciliationConstants.SourceKind,
                SourceId: sourceId,
                ProvisionedAt: now,
                ProvisionedBy: provisionedBy,
                Category: entry.Definition.Category,
                DisplayName: entry.Definition.DisplayName,
                Description: entry.Definition.Description
            );

            var version = new JsonContributedVersion(
                Version: entry.Version,
                ActivityTypeKey: entry.Definition.ActivityTypeKey,
                ImplementationKind: descriptor.Kind,
                ImplementationDescriptor: descriptor,
                ExecutionType: entry.ExecutionType,
                Inputs: entry.Inputs ?? [],
                Outputs: entry.Outputs ?? [],
                Ports: entry.Ports ?? [],
                Definition: definition
            );

            domainEvent.Versions.Add(version);
        }
    }

    private IImplementationDescriptor DeserializeImplementationDescriptor(JsonCatalogEntry entry, int entryIndex)
    {
        var kind = entry.ImplementationKind;
        var activityTypeKey = entry.Definition?.ActivityTypeKey;

        if (string.IsNullOrWhiteSpace(kind))
            throw new InvalidJsonCatalogEntryException(entryIndex, activityTypeKey, kind, "'implementationKind' is required.");

        if (entry.ImplementationDescriptor.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new InvalidJsonCatalogEntryException(entryIndex, activityTypeKey, kind, "'implementationDescriptor' is required.");

        var descriptorType = descriptorRegistry.Resolve(kind)
            ?? throw new InvalidJsonCatalogEntryException(
                entryIndex, activityTypeKey, kind,
                $"No implementation-descriptor type is registered for kind '{kind}'. The owning module may not be installed, or the kind value is misspelled.");

        IImplementationDescriptor descriptor;
        try
        {
            descriptor = (IImplementationDescriptor)payloadSerializer.Deserialize(entry.ImplementationDescriptor.GetRawText(), descriptorType);
        }
        catch (Exception inner)
        {
            throw new InvalidJsonCatalogEntryException(
                entryIndex, activityTypeKey, kind,
                $"Failed to deserialise 'implementationDescriptor' into '{descriptorType.FullName}': {inner.Message}",
                inner);
        }

        if (!string.Equals(descriptor.Kind, kind, StringComparison.Ordinal))
            throw new InvalidJsonCatalogEntryException(
                entryIndex, activityTypeKey, kind,
                $"Kind mismatch: entry declares '{kind}' but the deserialised descriptor reports '{descriptor.Kind}'. The descriptor type's Kind property and the JSON 'implementationKind' must agree.");

        return descriptor;
    }
}
