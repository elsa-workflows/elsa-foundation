using Elsa.Mapping.Core.Contracts;
using Elsa.Serialization.Core;
using Elsa.Activities.Design.Provisioning.Core;
using Elsa3.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Elsa.Mediator.Core.Contracts;

namespace Elsa3.Activities.Design.Import.Handlers;

public sealed class ImportActivityVersions(IObjectMapper mapper, IEnumerable<IWorkflowCollectionSource> collectionSources, IPayloadSerializer payloadSerializer) 
    : IDomainEventHandler<OnActivityVersionsProvisioning>
{
    public async ValueTask Handle(OnActivityVersionsProvisioning domainEvent, CancellationToken cancellationToken)
    {
        foreach(var source in collectionSources)
        {
            await using var versionsStream = await source.OpenStream(cancellationToken);
            var versionsDocument = await JsonDocument.ParseAsync(versionsStream, cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("Workflow version collection json stream is not a valid array");

            var definitions = payloadSerializer
                .Deserialize<IEnumerable<Elsa3WorkflowDefinition>>(versionsDocument.RootElement)
                .ToList();

            var enumerable = mapper.Map<IWorkflowDefinitionVersion>(definitions, cancellationToken);
            await foreach(var def in enumerable)
            {
                domainEvent.Versions.Add(def);
            }
        }
    }
}
