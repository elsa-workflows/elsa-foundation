using System.Security.Claims;
using CShells.Features;
using Elsa.Decisions.Workflows.Runtime;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Decisions.Workflows.Api;

/// <summary>Protects exact exported decision material and retained native/model value disclosure.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Decisions")]
[ManifestFeatureCategory("API")]
[ShellFeature(name: "DecisionsWorkflowsApi", DisplayName = "Decisions workflow disclosure", Description = "Applies decision source and read grants to workflow export and execution disclosure.", DependsOn = new object[] { "DecisionsWorkflowsRuntime", "WorkflowsRuntimeApi", "WorkflowsPublishingApi" })]
public class DecisionsWorkflowsApiFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.TryAddScoped<DecisionWorkflowResources>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IPermissionResourceHandler, DecisionWorkflowResourceHandler>());
        services.TryAddScoped<IWorkflowLibraryVisibilityPolicy, DecisionLibraryVisibilityPolicy>();
    }
}

/// <summary>This contribution never filters workflow-definition inventory; another resource policy needs its own query policy.</summary>
internal sealed class DecisionLibraryVisibilityPolicy(IEnumerable<IPermissionResourceHandler> handlers) : IWorkflowLibraryVisibilityPolicy
{
    public ValueTask<WorkflowLibraryReadScope> GetScopeAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (handlers.Any(handler => handler is not DecisionWorkflowResourceHandler))
            return ValueTask.FromException<WorkflowLibraryReadScope>(new DecisionInvocationException("visibility-unavailable", "The host must supply query visibility for its additional workflow resource policies."));
        return ValueTask.FromResult(new WorkflowLibraryReadScope("tenant-action-v1"));
    }
}
