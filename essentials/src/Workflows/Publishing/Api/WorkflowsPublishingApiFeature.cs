using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Api.AspNetCore;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Events.Core.Extensions;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Capabilities;
using Elsa.Workflows.Publishing.Api.Commands;
using Elsa.Workflows.Publishing.Api.Contracts;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Events;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MinimalEndpoints;

namespace Elsa.Workflows.Publishing.Api;

/// <summary>
/// The publishing surface bridges persisted Design metadata and canonical Runtime executable
/// contracts. It compiles descriptors and role-owned bindings without constructing live activities.
/// The feature is neither Design nor Runtime, which is why it may bridge their stable contracts
/// without breaking the Design/Runtime split.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Publishing")]
[ManifestFeatureCategory("API")]
[ShellFeature(
    name: "WorkflowsPublishingApi",
    DisplayName = "Workflows Publishing API",
    Description = "Bridge endpoints that compile persisted catalog metadata into canonical workflow executables.",
    DependsOn = new object[] { "WorkflowsPublishing", "ApiCapabilities" }
)]
public class WorkflowsPublishingApiFeature : IWebShellFeature, IPostConfigureShellServices
{
    private static readonly ServiceDescriptor WorkflowRuns = ServiceDescriptor.Scoped<IWorkflowTestRunStarter, StartWorkflowTestRunRequestHandler>();
    private static readonly ServiceDescriptor WorkflowWaits = ServiceDescriptor.Scoped<ITestRunWaitOperations, TestRunWaitOperations>();
    private static readonly ServiceDescriptor ActivityRuns = ServiceDescriptor.Scoped<IActivityDraftTestRunService, ActivityDraftTestRunService>();

    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.AddElsaEndpoints();
        // Transport authorization (HTTP-bound). Authorization is a transport concern; the engine is
        // authorization-free, so its context + the activity-draft services that consume it live here.
        services.AddHttpContextAccessor();
        services.AddDynamicEndpointApiExplorerRefresh();
        services.TryAddScoped<IActivityPublishingAuthorizationContext, HttpContextActivityPublishingAuthorizationContext>();
        services.TryAddScoped<IActivityDefinitionPublisher, ActivityDefinitionPublisher>();
        services.TryAddScoped<IActivitySourceVersionPublisher, SourceOwnedActivityVersionPublisher>();
        services.TryAdd(ActivityRuns);
        PublishingPersistenceFamilyBackend.TryAddInMemory<IActivityDraftTestRunStore, InMemoryActivityDraftTestRunStore>(
            services,
            PublishingPersistenceFamilyBackend.ActivityDraftTestRuns,
            PublishingPersistenceFamilyBackend.ActivityDraftTestRunContracts);
        services.TryAddSingleton<IActivityDraftTestRunCancellationPolicy, DefaultActivityDraftTestRunCancellationPolicy>();
        services.TryAddScoped<IActivityDraftDiffCandidateCompiler, ActivityDraftDiffCandidateCompiler>();
        services.TryAddScoped<IActivityUpgradePlanApplier, ApplyActivityUpgradePlanCommand>();
        services.TryAddScoped<RuntimeRequirementPreflight>();
        services.TryAddSingleton<IWorkflowTestRunStore, InMemoryWorkflowTestRunStore>();
        services.TryAdd(WorkflowWaits);

        // The workflow-publish + compile engine (compiler + collaborators, publication stores/activator,
        // the PublishWorkflow handler) is supplied by the WorkflowsPublishing engine feature via
        // DependsOn; publish is the one operation still dispatched through the mediator. Every other
        // operation's handling lives on its endpoint, backed by these module services.
        services.TryAdd(WorkflowRuns);
        services.TryAddScoped<Handlers.IPublicationSlotUnpublisher, Handlers.UnpublishPublicationSlotRequestHandler>();
        services.TryAddScoped<Handlers.IPublicationSlotRestorer, Handlers.RestorePublicationSlotRequestHandler>();
        services.TryAddScoped<IRuntimeRequirementPreflight>(provider => provider.GetRequiredService<RuntimeRequirementPreflight>());
        // Export targets are fan-in: a future folder or blob target contributes alongside the safe built-in
        // download target rather than replacing it.
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IWorkflowArtifactExportTarget, DownloadWorkflowArtifactExportTarget>());
        // Endpoint failure rendering and translation, keyed by the owner so hosts composing several
        // modules keep each module's own problem shapes.
        services.TryAddKeyedSingleton<IEndpointProblemWriter, Endpoints.WorkflowPublishingProblemWriter>(WorkflowsPublishingApi.OwnerId);
        services.TryAddKeyedSingleton<IEndpointExceptionTranslator, Endpoints.WorkflowPublishingExceptionTranslator>(WorkflowsPublishingApi.OwnerId);
        services.TryAddKeyedSingleton<IEndpointFaultRenderer, Endpoints.WorkflowPublishingFaultRenderer>(WorkflowsPublishingApi.OwnerId);
        // Keep the owner serializer contract ready for the Minimal API mapper. The effective host
        // options preserve the former FastEndpoints web defaults while routing Publishing types
        // through generated metadata before any general fallback resolver.
        services.ConfigureHttpJsonOptions(options => WorkflowsPublishingJsonOptions.Configure(options.SerializerOptions));
        services.AddApiCapability(PublishingApiCapabilities.BaseDeclaration);
        services.AddApiCapabilitySource<PublishingTestRunCapabilitySource>();
        // The conversion-profiles endpoint is served here (it reads IValueConversionProfileRegistry, supplied by
        // the engine) but advertised under the client's expressions capability; the source merges that relation
        // into elsa.api.expressions.
        services.AddApiCapabilitySource<ConversionProfilesCapabilitySource>();
        services.AddPermissionContributor<WorkflowPublishingPermissionContributor>();
    }

    public void PostConfigureServices(IServiceCollection services)
    {
        var workflowScope = Has<IWorkflowStartDispatcher>() && Has<IWorkflowTestScopeStore>();
        var legacyWorkflow = workflowScope && Has<IWorkflowExecutionStateStore>();
        var directWorkflow = workflowScope && Has<IWorkflowDirectTestRunDispatcher>();
        var legacyWaits = legacyWorkflow && Has<IBookmarkStateStore>() && Has<IActivityExecutionStateStore>() && Has<IBookmarkResumeDispatcher>();
        var directWaits = directWorkflow && Has<IWorkflowTestRunWaitControl>();
        var legacyActivity = legacyWorkflow && Has<IWorkflowExecutionActorProvider>() && Has<IActivityExecutionStateStore>() &&
            Has<IWorkflowExecutionPartitionAccessor>() && Has<IRuntimeExecutionIdGenerator>() && Has<IActivityDefinitionStore>() &&
            Has<IActivityDefinitionDraftStore>() && Has<IActivityDefinitionLayoutStore>();

        var workflow = Select(WorkflowRuns, legacyWorkflow || directWorkflow);
        var waits = Select(WorkflowWaits, workflow && (legacyWaits || directWaits)) && workflow;
        var activity = Select(ActivityRuns, legacyActivity);
        services.RemoveAll<PublishingTestRunRoutes>();
        services.AddSingleton(new PublishingTestRunRoutes(workflow, waits, activity));

        bool Has<T>() => services.Any(item => !item.IsKeyedService && item.ServiceType == typeof(T));

        bool Select(ServiceDescriptor fallback, bool available)
        {
            if (!available)
                services.Remove(fallback);
            else
                services.TryAdd(fallback);
            return services.Any(item => !item.IsKeyedService && item.ServiceType == fallback.ServiceType);
        }
    }

    public virtual void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) => WorkflowsPublishingApi.MapWorkflowsPublishingApi(
        endpoints, endpoints.ServiceProvider.GetService<PublishingTestRunRoutes>() ?? PublishingTestRunRoutes.All);
}
