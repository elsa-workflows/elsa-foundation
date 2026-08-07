using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Core;
using Elsa.Persistence.Core.DependencyInjection;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.DependencyInjection;

/// <summary>
/// Registers the Publishing stores against one named Groundwork target.
/// <para>
/// Publishing straddles the design and runtime lanes: it reads design material and writes runtime
/// executables. Its own documents (publication slots, records, policies, receipts) live in the target named
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

        lane.Replace<IPublicationSlotStore, GroundworkPublicationSlotStore>();
        lane.Replace<IPublicationRecordStore, GroundworkPublicationRecordStore>();
        lane.Replace<IPublicationPolicyStore, GroundworkPublicationPolicyStore>();
        lane.Replace<IPublicationProjectionIntentStore, GroundworkPublicationProjectionIntentStore>();
        lane.Replace<IPublicationSnapshotReviewStore, GroundworkPublicationSnapshotReviewStore>();
        lane.Replace<IActivityPublicationReceiptStore, GroundworkActivityPublicationReceiptStore>();
        lane.Replace<IActivityDraftTestRunStore, GroundworkActivityDraftTestRunStore>();
        return services;
    }
}
