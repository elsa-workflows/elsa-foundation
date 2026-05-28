using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Mapping.Core.Contracts;
using Elsa.Mediator.Core.Contracts;
using Elsa.Serialization.Core;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Models;
using System.Text.Json;

namespace Elsa3.Activities.Design.Import.Handlers;

public sealed class ImportActivityVersions(IObjectMapper mapper, IEnumerable<IActivityCollectionJsonSource> collectionSources, IPayloadSerializer payloadSerializer)
    : IDomainEventHandler<OnActivityVersionsReconciling>
{
    public async ValueTask Handle(OnActivityVersionsReconciling domainEvent, CancellationToken cancellationToken)
    {
        foreach(var source in collectionSources)
        {
            await using var versionsStream = await source.OpenStream(cancellationToken);
            var versionsDocument = await JsonDocument.ParseAsync(versionsStream, cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("Workflow version collection json stream is not a valid array");

            var definitions = payloadSerializer
                .Deserialize<IEnumerable<Elsa3WorkflowDefinition>>(versionsDocument.RootElement)
                .ToList();

            var enumerable = mapper.Map<IActivityDefinitionVersion>(definitions, cancellationToken);
            await foreach(var def in enumerable)
            {
                domainEvent.AddVersion(def);
            }
        }
    }
}
