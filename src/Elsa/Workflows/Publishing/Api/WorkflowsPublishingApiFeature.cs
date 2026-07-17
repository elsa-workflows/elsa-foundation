using CShells.Features;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Events.Core.Extensions;
using Elsa.Api.FastEndpoints;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Mediator.Core.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Api.Capabilities;
using Elsa.Workflows.Publishing.Api.Commands;
using Elsa.Workflows.Publishing.Api.Contracts;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Events;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Publishing.Api;

/// <summary>
/// The publishing surface — today a single bridge over the activity-construction seam, tomorrow the
/// seed of the compile-and-publish domain. Its endpoints read a persisted activity definition (the
/// Design seam) and invoke <c>IActivityFactory</c> (the Runtime seam) to materialise a live
/// <c>IActivity</c>. The feature depends only on the two seams' <c>.Core</c> contracts; it is neither
/// Design nor Runtime, which is why it may bridge them without breaking §E2.2.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Publishing")]
[ManifestFeatureCategory("API")]
[ShellFeature(
    name: "WorkflowsPublishingApi",
    DisplayName = "Workflows Publishing API",
    Description = "Bridge endpoints that construct a live activity from a persisted catalog row (the construction seam).",
    DependsOn = new object[] { "WorkflowsRuntimeTriggers", "ApiCapabilities", "Events" }
)]
public class WorkflowsPublishingApiFeature : FastEndpointsFeatureBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        var assembly = GetType().Assembly;

        services.AddHttpContextAccessor();
        services.TryAddScoped<IActivityPublishingAuthorizationContext, HttpContextActivityPublishingAuthorizationContext>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IActivityContractStorageDriverProvider, RuntimeActivityContractStorageDriverProvider>());
        services.TryAddSingleton<IWorkflowExecutableStore, InMemoryWorkflowExecutableStore>();
        services.TryAddSingleton<IWorkflowExecutableSourceReferenceStore, InMemoryWorkflowExecutableSourceReferenceStore>();
        services.TryAddScoped<IWorkflowExecutableSourceReferenceReader>(serviceProvider =>
            serviceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>());
        services.TryAddScoped<IExecutableActivityTemplateReader>(serviceProvider =>
            serviceProvider.GetRequiredService<IExecutableActivityTemplateStore>());
        services.TryAddSingleton<IPublicationSlotStore, InMemoryPublicationSlotStore>();
        services.TryAddSingleton<IPublicationRecordStore, InMemoryPublicationRecordStore>();
        services.TryAddSingleton<IPublicationPolicyStore, InMemoryPublicationPolicyStore>();
        services.TryAddSingleton<IPublicationProjectionIntentStore, InMemoryPublicationProjectionIntentStore>();
        // Deterministic policies hold no request or persistence state and remain safe singletons.
        services.TryAddSingleton<IPublicationPolicyResolver, PublicationPolicyResolver>();
        services.TryAddSingleton<IPublicationPreflightService, PublicationPreflightService>();
        // Publishing operations consume provider-overridable stores. Durable providers register those stores as
        // scoped services, so their aggregators must share the request scope instead of capturing it globally.
        services.TryAddScoped<IPublicationProjectionPreparer, PublicationProjectionReconciler>();
        services.TryAddScoped<IPublicationActivator, PublicationActivator>();
        services.TryAddScoped<WorkflowPublicationPreflightReader>();
        services.TryAddScoped<PublicationSnapshotReviewService>();
        services.TryAddSingleton<IPublicationSnapshotReviewStore, InMemoryPublicationSnapshotReviewStore>();
        // Fallback layout store for in-memory compositions; a design-persistence provider overrides this with its
        // own registration so the publish flow copies the real layout sidecar onto the source reference (ADR 0039).
        services.TryAddScoped<IWorkflowDefinitionVersionLayoutStore, EmptyWorkflowDefinitionVersionLayoutStore>();
        services.TryAddScoped<IActivityStructureService, DefaultActivityStructureService>();
        // W30b (#418): WorkflowExecutableCompiler decomposition collaborators. Registered at the compiler's own
        // scoped lifetime so each is independently resolvable, replaceable, and unit-testable.
        services.TryAddScoped<RuntimeInputBindingCompiler>();
        services.TryAddScoped<RuntimeOutputCaptureCompiler>();
        services.TryAddScoped<WorkflowExecutableHasher>();
        services.TryAddScoped<ActivityTreeProjector>();
        services.TryAddScoped<WorkflowExecutableAuthoredInputsSidecar>();
        services.TryAddScoped<ExecutableNodeCompiler>();
        services.TryAddScoped<WorkflowExecutablePlacementSidecarContext>();
        services.TryAddSingleton<IActivityTemplateProviderCompilerRegistry, ActivityTemplateProviderCompilerRegistry>();
        services.TryAddSingleton<IActivityTemplateDependencyDiscovererRegistry, ActivityTemplateDependencyDiscovererRegistry>();
        services.TryAddSingleton<IActivityPlacementHasher, Sha256ActivityPlacementHasher>();
        services.TryAddScoped<ActivityTemplatePlacer>();
        services.TryAddScoped<IActivityTemplateCompiler, ActivityTemplateCompiler>();
        services.TryAddScoped<IActivityDefinitionPublisher, ActivityDefinitionPublisher>();
        services.TryAddScoped<IActivitySourceVersionPublisher, SourceOwnedActivityVersionPublisher>();
        services.TryAddScoped<IActivityDraftTestRunPublisher, ActivityDraftTestRunPublisher>();
        services.TryAddScoped<IActivityDraftDiffCandidateCompiler, ActivityDraftDiffCandidateCompiler>();
        services.TryAddScoped<IActivityUpgradePlanApplier, ApplyActivityUpgradePlanCommand>();
        services.TryAddSingleton<IActivityTemplateAdmissionPolicy, AcceptAllActivityTemplateAdmissionPolicy>();
        services.TryAddScoped<IExecutableNodeMetadataEnricher, ExecutableNodeMetadataEnricher>();
        services.AddEventHandler<OnExecutableCompilationCollecting, CollectExecutableCompilation>();
        services.AddEventHandler<OnExecutableNodeMetadataCollecting, CollectExecutableNodeMetadata>();
        services.TryAddScoped<IWorkflowExecutableCompiler, WorkflowExecutableCompiler>();
        services.TryAddScoped<RuntimeRequirementPreflight>();
        services.TryAddSingleton<IWorkflowTestRunStore, InMemoryWorkflowTestRunStore>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddRequestHandlersFrom(assembly);
        services.AddApiCapability(PublishingApiCapabilities.StaticDeclaration);
    }
}
