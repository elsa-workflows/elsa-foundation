using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Api.FastEndpoints;
using Elsa.Mediator.Core.Extensions;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Workflows.Publishing.Api.Capabilities;

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
    DependsOn = new object[] { "WorkflowsRuntimeTriggers", "ApiCapabilities" }
)]
public class WorkflowsPublishingApiFeature : FastEndpointsFeatureBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        var assembly = GetType().Assembly;

        services.TryAddSingleton<IWorkflowExecutableStore, InMemoryWorkflowExecutableStore>();
        services.TryAddSingleton<IWorkflowExecutableSourceReferenceStore, InMemoryWorkflowExecutableSourceReferenceStore>();
        services.TryAddSingleton<IPublicationSlotStore, InMemoryPublicationSlotStore>();
        services.TryAddSingleton<IPublicationRecordStore, InMemoryPublicationRecordStore>();
        services.TryAddSingleton<IPublicationPolicyStore, InMemoryPublicationPolicyStore>();
        services.TryAddSingleton<IPublicationProjectionIntentStore, InMemoryPublicationProjectionIntentStore>();
        services.TryAddSingleton<IPublicationPolicyResolver, PublicationPolicyResolver>();
        services.TryAddSingleton<IPublicationProjectionPreparer, PublicationProjectionReconciler>();
        services.TryAddSingleton<IPublicationPreflightService, PublicationPreflightService>();
        services.TryAddSingleton<IPublicationActivator, PublicationActivator>();
        services.TryAddScoped<WorkflowPublicationPreflightReader>();
        services.TryAddSingleton<PublicationSnapshotReviewService>();
        // Fallback layout store for in-memory compositions; a design-persistence provider overrides this with its
        // own registration so the publish flow copies the real layout sidecar onto the source reference (ADR 0039).
        services.TryAddScoped<IWorkflowDefinitionVersionLayoutStore, EmptyWorkflowDefinitionVersionLayoutStore>();
        services.TryAddScoped<IActivityStructureService, DefaultActivityStructureService>();
        // W30b (#418): WorkflowExecutableCompiler decomposition collaborators. Registered at the compiler's own
        // scoped lifetime so each is independently resolvable, replaceable, and unit-testable.
        services.TryAddScoped<RuntimeInputBindingCompiler>();
        services.TryAddScoped<WorkflowExecutableHasher>();
        services.TryAddScoped<ActivityTreeProjector>();
        services.TryAddScoped<ExecutableNodeCompiler>();
        services.TryAddScoped<IWorkflowExecutableCompiler, WorkflowExecutableCompiler>();
        services.TryAddSingleton<IWorkflowTestRunStore, InMemoryWorkflowTestRunStore>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddRequestHandlersFrom(assembly);
        services.AddApiCapability(PublishingApiCapabilities.StaticDeclaration);
    }
}
