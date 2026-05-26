using Elsa.Mapping.Core.Contracts;
using Elsa.Mediator.Core.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Provisioning.Core.Events;
using Elsa3.Models;
using Elsa3.Workflows.Design.Import.Contracts;
using System.Text.Json;

namespace Elsa3.Workflows.Design.Import.Handlers;

public sealed class ImportWorkflows(IObjectMapper mapper, IEnumerable<IWorkflowCollectionJsonSource> collectionSources, IPayloadSerializer payloadSerializer)
    : IDomainEventHandler<OnWorkflowVersionsProvisioning>
{
    public async ValueTask Handle(OnWorkflowVersionsProvisioning domainEvent, CancellationToken cancellationToken)
    {
        foreach (var source in collectionSources)
        {
            await using var versionsStream = await source.OpenStream(cancellationToken);
            var versionsDocument = await JsonDocument.ParseAsync(versionsStream, cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("Workflow version collection json stream is not a valid array");

            var definitions = payloadSerializer
                .Deserialize<IEnumerable<Elsa3WorkflowDefinition>>(versionsDocument.RootElement)
                .ToList();

            var enumerable = mapper.Map<IWorkflowDefinitionVersion>(definitions, cancellationToken);
            await foreach (var def in enumerable)
            {
                domainEvent.Versions.Add(def);
            }
        }
    }
}
