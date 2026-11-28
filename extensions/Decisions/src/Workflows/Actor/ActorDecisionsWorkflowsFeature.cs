using System.Security.Claims;
using CShells.Features;
using Elsa.Decisions.Workflows.Api;
using Elsa.Decisions.Workflows.Design;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Contracts;
using Kernel.Native;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Decisions.Workflows.Actor;

/// <summary>Registers decision authoring, immutable workflow pins, execution and controlled disclosure for Actor Foundation.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Decisions")]
[ManifestFeatureCategory("Workflows")]
[ShellFeature(name: "ActorDecisionsWorkflows", DisplayName = "Actor Decision Workflows",
    Description = "Evaluates exact published decisions in Actor Foundation workflows.",
    DependsOn = new object[] { "DecisionsApi", "ActivitiesRuntimeHosting",
        "WorkflowsRuntimeKernelHosting", "WorkflowsRuntimeKernelRetainedApi", "WorkflowsPublishingApi" })]
public sealed class ActorDecisionsWorkflowsFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.TryAddScoped<INativeDecisions, ActorDecisionRunner>();
        services.TryAddScoped<IDecisionPublicationAuthorizer, DecisionPublicationAuthorizer>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IExecutableCompilationSource, DecisionPinSource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IActivityInputSourceConstraintProvider, DecisionInputSourceConstraintProvider>());
        services.TryAddScoped<DecisionWorkflowResources>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IPermissionResourceHandler, DecisionWorkflowResourceHandler>());
        services.TryAddScoped<IWorkflowLibraryVisibilityPolicy, DecisionLibraryVisibilityPolicy>();
    }
}

/// <summary>The Decision resource handler protects values and source. It leaves the workflow definition inventory unchanged.</summary>
internal sealed class DecisionLibraryVisibilityPolicy(IEnumerable<IPermissionResourceHandler> handlers) : IWorkflowLibraryVisibilityPolicy
{
    public ValueTask<WorkflowLibraryReadScope> GetScopeAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (handlers.Any(handler => handler is not DecisionWorkflowResourceHandler))
            throw new InvalidOperationException("The host must supply query visibility for its additional workflow resource policies.");
        return ValueTask.FromResult(new WorkflowLibraryReadScope("tenant-action-v1"));
    }
}
