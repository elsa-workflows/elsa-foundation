using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Core;
using Elsa.Persistence.Core.DependencyInjection;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Persistence.Groundwork.Services;
using Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.DependencyInjection;

/// <summary>
/// Registers the Publishing stores against one named Groundwork target.
/// <para>
/// Publishing straddles the design and runtime lanes: it reads design material and writes runtime
/// executables. Its own documents (publication records, policies, receipts) live in the target named
/// here. Reusable-activity publication commits design, runtime, and publishing kinds together, so those
/// lanes' targets have to agree for that path; plain workflow publish is already a compensating sequence and
/// works across targets.
/// </para>
/// </summary>
public static class GroundworkPublishingStoreRegistration
{
    public static IServiceCollection AddGroundworkPublishingStores(
        this IServiceCollection services,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddPersistenceCore();
        var lane = services.GroundworkLane(targetName);

        lane.Manifest<PublishingGroundworkStorageManifestSource>();
        services.TryAddSingleton<PublishingGroundworkDocumentSerializer>();

        // No publishing slot store: the activation ledger is the runtime family's IWorkflowActivationAuthority,
        // registered by AddGroundworkRuntimeStores (FR-B-006 — one physical ledger per engine).
        lane.Replace<IPublicationRecordStore, GroundworkPublicationRecordStore>();
        lane.Replace<IPublicationPolicyStore, GroundworkPublicationPolicyStore>();
        lane.Replace<IPublicationProjectionIntentStore, GroundworkPublicationProjectionIntentStore>();
        lane.Replace<IPublicationSnapshotReviewStore, GroundworkPublicationSnapshotReviewStore>();
        lane.Replace<IActivityPublicationReceiptStore, GroundworkActivityPublicationReceiptStore>();
        lane.Replace<IActivityDraftTestRunStore, GroundworkActivityDraftTestRunStore>();

        // Answers for the receipt intent a split publication records in the design commit. Registered
        // unkeyed: it addresses the publishing lane through GroundworkLaneStores rather than being handed a
        // store, because the design lane's redrive resolves it and has no business knowing this target.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IDesignPostCommitIntentDeliverer,
            ActivityPublicationReceiptIntentDeliverer>());
        return services;
    }
}
