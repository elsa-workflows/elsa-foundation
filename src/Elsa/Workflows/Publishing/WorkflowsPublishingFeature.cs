using CShells.Features;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Events.Core.Extensions;
using Elsa.Mediator.Core.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Reconciliation.Core;
using Elsa.Workflows.Publishing.Handlers;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Events;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Publishing;

/// <summary>
/// The endpoint-free publishing ENGINE feature. It owns the auth-free workflow-publish + compile
/// logic (the executable compiler and its collaborators, the publication activator/stores, and the
/// workflow-publish orchestration handler) so a runtime node can compose the publish capability
/// without mounting any HTTP endpoints. Authorization is a transport concern and lives in the
/// <c>WorkflowsPublishingApi</c> feature, not here.
/// </summary>
/// <remarks>
/// The Api feature obtains the engine by <c>DependsOn</c> composition (framework §2.11), not
/// inheritance: it is its own <c>IWebShellFeature</c> and declares
/// <c>DependsOn WorkflowsPublishing</c>.
/// </remarks>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Publishing")]
[ShellFeature(
    name: "WorkflowsPublishing",
    DisplayName = "Workflows Publishing",
    Description = "Endpoint-free engine that compiles designed workflow definitions into canonical executables.",
    // WorkflowDesignValidations: PublishWorkflowRequestHandler treats an absent expression validator as
    // "validation unavailable" rather than "nothing to validate", so publishing answers 503
    // expression-validation-unavailable before it ever reaches the compiler. Discovered by crashing (T126).
    DependsOn = new object[] { "WorkflowsRuntimeTriggers", "Events", "WorkflowDesignValidations" }
)]
public class WorkflowsPublishingFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        var assembly = GetType().Assembly;

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IActivityContractStorageDriverProvider, RuntimeActivityContractStorageDriverProvider>());
        services.TryAddSingleton<IWorkflowExecutableStore, InMemoryWorkflowExecutableStore>();
        services.TryAddSingleton<IWorkflowExecutableSourceReferenceStore, InMemoryWorkflowExecutableSourceReferenceStore>();
        services.TryAddScoped<IWorkflowExecutableSourceReferenceReader>(serviceProvider =>
            serviceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>());
        services.TryAddScoped<IExecutableActivityTemplateReader>(serviceProvider =>
            serviceProvider.GetRequiredService<IExecutableActivityTemplateStore>());
        // No publishing-family slot store: the activation ledger is the runtime's IWorkflowActivationAuthority
        // (FR-B-006). Registered here as a TryAdd fallback for the same reason the executable stores above are —
        // AddWorkflowRuntime() registers the same defaults, and the runtime Groundwork lane replaces the authority
        // outright when durable persistence is composed.
        services.TryAddSingleton<IWorkflowActivationAuthority, InMemoryWorkflowActivationAuthority>();
        services.TryAddScoped<IWorkflowActivationCoordinator, WorkflowActivationCoordinator>();
        // Same reason: the executable hasher moved to the runtime layer (FR-B-010), so the compiler
        // now depends on IWorkflowExecutableHasher, which only AddWorkflowRuntime() registers. A host
        // composing publishing standalone must still be able to construct the compiler.
        services.TryAddScoped<IWorkflowExecutableHasher, WorkflowExecutableHasher>();
        services.TryAddSingleton<IPublicationRecordStore, InMemoryPublicationRecordStore>();
        services.TryAddSingleton<IPublicationPolicyStore, InMemoryPublicationPolicyStore>();
        // Deterministic policies hold no request or persistence state and remain safe singletons.
        services.TryAddSingleton<IPublicationPolicyResolver, PublicationPolicyResolver>();
        services.TryAddSingleton<IPublicationPreflightService, PublicationPreflightService>();
        // Publishing operations consume provider-overridable stores. Durable providers register those stores as
        // scoped services, so their aggregators must share the request scope instead of capturing it globally.
        services.TryAddScoped<IPublicationActivator, PublicationActivator>();
        services.TryAddScoped<WorkflowPublicationPreflightReader>();
        services.TryAddScoped<PublicationSnapshotReviewService>();
        services.TryAddSingleton<IPublicationSnapshotReviewStore, InMemoryPublicationSnapshotReviewStore>();
        services.TryAddSingleton<IActivityPublicationReceiptStore, InMemoryActivityPublicationReceiptStore>();
        // Fallback layout store for in-memory compositions; a design-persistence provider overrides this with its
        // own registration so the publish flow copies the real layout sidecar onto the source reference (ADR 0039).
        services.TryAddScoped<IWorkflowDefinitionVersionLayoutStore, EmptyWorkflowDefinitionVersionLayoutStore>();
        services.TryAddScoped<IActivityStructureService, DefaultActivityStructureService>();
        // Permanent definition deletion must not strand a live publication: the guard is contributed into the
        // design-persistence delete commands and vetoes while a slot is active or a Published reference is live.
        // It is also the publication check permanent deletion requires, so a host that does not compose this
        // feature refuses the operation outright instead of deleting unverified (#1283).
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowDefinitionPermanentDeletionGuard, PublishedWorkflowDeletionGuard>());
        // W30b (#418): WorkflowExecutableCompiler decomposition collaborators. Registered at the compiler's own
        // scoped lifetime so each is independently resolvable, replaceable, and unit-testable.
        services.TryAddSingleton<IValueConversionProfileRegistry>(BuiltInValueConversionProfileRegistry.Instance);
        services.TryAddScoped<ValueConversionPlanResolver>();
        services.TryAddScoped<ActivityResultConversionPlanLinker>();
        services.TryAddScoped<RuntimeInputBindingCompiler>();
        services.TryAddScoped<RuntimeOutputCaptureCompiler>();
        services.TryAddScoped<ActivityTreeProjector>();
        services.TryAddScoped<WorkflowExecutableAuthoredInputsSidecar>();
        services.TryAddScoped<ExecutableNodeCompiler>();
        services.TryAddScoped<WorkflowExecutablePlacementSidecarContext>();
        services.TryAddSingleton<IActivityTemplateProviderCompilerRegistry, ActivityTemplateProviderCompilerRegistry>();
        services.TryAddSingleton<IActivityTemplateDependencyDiscovererRegistry, ActivityTemplateDependencyDiscovererRegistry>();
        services.TryAddSingleton<IActivityPlacementHasher, Sha256ActivityPlacementHasher>();
        services.TryAddScoped<ActivityTemplatePlacer>();
        services.TryAddScoped<IActivityTemplateCompiler, ActivityTemplateCompiler>();
        services.TryAddSingleton<IActivityTemplateAdmissionPolicy, AcceptAllActivityTemplateAdmissionPolicy>();
        services.TryAddScoped<IExecutableNodeMetadataEnricher, ExecutableNodeMetadataEnricher>();
        // FR-B-010 export producer. Scoped because it reads the source-reference reader above, which durable
        // providers register as scoped. No IWorkflowArtifactExportTarget is registered here: the seam is fan-in
        // (TryAddEnumerable) and its first implementation — the API download target — is contributed by the
        // transport feature, which is where a destination belongs. An engine composed without one still resolves
        // an empty IEnumerable, so the absence is a composition fact, not a missing dependency.
        services.TryAddScoped<IWorkflowArtifactClosureFactory, WorkflowArtifactClosureFactory>();
        services.AddEventHandler<ExecutableCompilationCollecting, CollectExecutableCompilation>();
        services.AddEventHandler<ExecutableNodeMetadataCollecting, CollectExecutableNodeMetadata>();
        // Publish-on-reconcile (spec 147): independent subscriber on the Design-side reconcile completion
        // event. Only acts on claims whose source opted in (PublishOnReconcile); never throws.
        services.AddEventHandler<WorkflowVersionsReconciled, PublishReconciledWorkflowVersions>();
        services.TryAddScoped<IWorkflowExecutableCompiler, WorkflowExecutableCompiler>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddRequestHandlersFrom(assembly);
    }
}
