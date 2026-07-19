using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Serialization.Core;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Activities.Design.Persistence.Groundwork.Services;

/// <summary>
/// Groundwork (document) implementation of <see cref="IAddActivityDefinitionCommand"/>, the document-store
/// counterpart of the EF Core <c>AddActivityDefinitionCommand</c>. It stages the <c>activityDefinition</c> and
/// its first <c>activityDefinitionVersion</c> into one Groundwork <see cref="IDocumentUnitOfWork"/> and commits
/// them atomically, mirroring the EF command's single <c>SaveChangesAsync</c>. The definition serializes with the
/// plain web options; the rich version (authored projection collections) delegates to the host
/// <see cref="IPayloadSerializer"/>, matching the version read store.
/// </summary>
public sealed class GroundworkAddActivityDefinitionCommand(
    IDocumentStore store,
    IPayloadSerializer payloadSerializer,
    IPersistenceAccessContextAccessor accessContextAccessor)
    : IAddActivityDefinitionCommand
{
    public async Task Execute(ActivityDefinition definition, ActivityDefinitionVersion version, CancellationToken cancellation)
    {
        accessContextAccessor.Current.EnsureTenantScope(definition.TenantId);
        accessContextAccessor.Current.EnsureTenantScope(version.TenantId);

        var definitionSave = GroundworkDocumentWriter.ToTenantScopedSaveRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            ActivitiesDesignStorageManifest.ActivityDefinitionCollection,
            ActivitiesDesignStorageManifest.SchemaVersion,
            definition,
            GroundworkActivitiesDesignJson.Options,
            accessContextAccessor.Current);

        var versionSave = GroundworkDocumentWriter.ToTenantScopedSaveRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind,
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionCollection,
            ActivitiesDesignStorageManifest.SchemaVersion,
            version,
            GroundworkActivitiesDesignDocumentSerialization.Create(payloadSerializer),
            accessContextAccessor.Current);

        await store.SaveAllAsync(
            DocumentCommitScope.Of(
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind),
            [definitionSave, versionSave],
            cancellation);
    }
}
