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
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Api.Capabilities;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Workflows.Publishing.Api.Commands;
using Elsa.Workflows.Publishing.Api.Contracts;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Events;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Publishing.Api;

/// <summary>
/// The publishing surface bridges persisted Design metadata and canonical Runtime executable
/// contracts. It compiles descriptors and role-owned bindings without constructing live activities.
/// The feature is neither Design nor Runtime, which is why it may bridge their stable contracts
/// without breaking §E2.2.
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
public class WorkflowsPublishingApiFeature : FastEndpointsFeatureBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        var assembly = GetType().Assembly;

        // Transport authorization (HTTP-bound). Authorization is a transport concern; the engine is
        // authorization-free, so its context + the activity-draft services that consume it live here.
        services.AddHttpContextAccessor();
        services.TryAddScoped<IActivityPublishingAuthorizationContext, HttpContextActivityPublishingAuthorizationContext>();
        services.TryAddScoped<IActivityDefinitionPublisher, ActivityDefinitionPublisher>();
        services.TryAddScoped<IActivitySourceVersionPublisher, SourceOwnedActivityVersionPublisher>();
        services.TryAddScoped<IActivityDraftTestRunService, ActivityDraftTestRunService>();
        services.TryAddSingleton<IActivityDraftTestRunStore, InMemoryActivityDraftTestRunStore>();
        services.TryAddSingleton<IActivityDraftTestRunCancellationPolicy, DefaultActivityDraftTestRunCancellationPolicy>();
        services.TryAddScoped<IActivityDraftDiffCandidateCompiler, ActivityDraftDiffCandidateCompiler>();
        services.TryAddScoped<IActivityUpgradePlanApplier, ApplyActivityUpgradePlanCommand>();
        services.TryAddScoped<RuntimeRequirementPreflight>();
        services.TryAddSingleton<IWorkflowTestRunStore, InMemoryWorkflowTestRunStore>();

        // The workflow-publish + compile engine (compiler + collaborators, publication stores/activator,
        // the PublishWorkflow handler) is supplied by the WorkflowsPublishing engine feature via DependsOn.
        services.AddRequestHandlersFrom(assembly);
        services.AddApiCapability(PublishingApiCapabilities.StaticDeclaration);
        // The conversion-profiles endpoint is served here (it reads IValueConversionProfileRegistry, supplied by
        // the engine) but advertised under the client's expressions capability; the source merges that relation
        // into elsa.api.expressions.
        services.AddApiCapabilitySource<ConversionProfilesCapabilitySource>();
        services.AddPermissionContributor<WorkflowPublishingPermissionContributor>();

        // FR-B-010a export targets are fan-in, never replacement: a folder writer or blob push arriving later must
        // contribute alongside the built-in download rather than displace it, so this is TryAddEnumerable and the
        // consumer resolves IEnumerable<> and selects by TargetId.
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IWorkflowArtifactExportTarget, DownloadWorkflowArtifactExportTarget>());
    }
}
