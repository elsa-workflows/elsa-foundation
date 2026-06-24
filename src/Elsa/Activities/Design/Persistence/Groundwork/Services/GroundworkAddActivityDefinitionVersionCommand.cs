using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Serialization.Core;
using Groundwork.Documents.Store;

namespace Elsa.Activities.Design.Persistence.Groundwork.Services;

public sealed class GroundworkAddActivityDefinitionVersionCommand(IDocumentStore store, IPayloadSerializer payloadSerializer)
    : IAddCommand<ActivityDefinitionVersion>
{
    public Task Add(ActivityDefinitionVersion entity, CancellationToken cancellationToken = default)
    {
        var save = GroundworkDocumentWriter.ToSaveRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind,
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionCollection,
            ActivitiesDesignStorageManifest.SchemaVersion,
            entity,
            GroundworkActivitiesDesignDocumentSerialization.Create(payloadSerializer));

        return store.SaveAsync(save, cancellationToken);
    }
}
