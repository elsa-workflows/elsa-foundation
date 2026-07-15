using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Api.FastEndpoints;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Mediator.Core.Extensions;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Api.Commands;
using Elsa.Workflows.Publishing.Core.Contracts;
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
    DependsOn = new object[] { "WorkflowsRuntimeTriggers" }
)]
public class WorkflowsPublishingApiFeature : FastEndpointsFeatureBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        var assembly = GetType().Assembly;

        services.TryAddSingleton<IWorkflowExecutableStore, InMemoryWorkflowExecutableStore>();
        services.TryAddSingleton<IWorkflowExecutableSourceReferenceStore, InMemoryWorkflowExecutableSourceReferenceStore>();
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
        services.TryAddScoped<ExecutableNodeCompiler>();
        services.TryAddScoped<WorkflowExecutablePlacementSidecarContext>();
        services.TryAddSingleton<IActivityTemplateProviderCompilerRegistry, ActivityTemplateProviderCompilerRegistry>();
        services.TryAddSingleton<IActivityTemplateDependencyDiscovererRegistry, ActivityTemplateDependencyDiscovererRegistry>();
        services.TryAddSingleton<IActivityPlacementHasher, Sha256ActivityPlacementHasher>();
        services.TryAddScoped<ActivityTemplatePlacer>();
        services.TryAddScoped<IActivityTemplateCompiler, ActivityTemplateCompiler>();
        services.TryAddScoped<IActivityDefinitionPublisher, ActivityDefinitionPublisher>();
        services.TryAddScoped<IActivityDraftDiffCandidateCompiler, ActivityDraftDiffCandidateCompiler>();
        services.TryAddScoped<IActivityUpgradePlanApplier, ApplyActivityUpgradePlanCommand>();
        services.TryAddSingleton<IActivityTemplateAdmissionPolicy, AcceptAllActivityTemplateAdmissionPolicy>();
        services.TryAddScoped<IWorkflowExecutableCompiler, WorkflowExecutableCompiler>();
        // Read-only projection of the artifact + reference stores into the executables list/detail views the
        // Studio Executable Inspector consumes (#598 P1). Self-contained: depends only on the two runtime stores.
        services.TryAddScoped<WorkflowExecutableInspector>();
        services.TryAddSingleton<IWorkflowTestRunStore, InMemoryWorkflowTestRunStore>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddRequestHandlersFrom(assembly);
    }
}
