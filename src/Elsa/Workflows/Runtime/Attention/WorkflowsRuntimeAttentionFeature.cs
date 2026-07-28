using CShells.Features;
using Elsa.Attention.Core;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Workflows.Runtime.Attention;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Attention")]
[ShellFeature(
    name: "WorkflowsRuntimeAttention",
    DisplayName = "Workflow Runtime Attention",
    Description = "Contributes complete workflow failure and incident conditions to Attention.",
    DependsOn = new object[] { "AttentionApi", "WorkflowsRuntimeApi" })]
public sealed class WorkflowsRuntimeAttentionFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.TryAddSingleton<IWorkflowRuntimeAttentionQuery>(provider =>
        {
            var executionStore = provider.GetRequiredService<IWorkflowExecutionStateStore>();
            var incidentStore = provider.GetRequiredService<IIncidentStateStore>();
            return executionStore is InMemoryWorkflowExecutionStateStore inMemoryExecutions &&
                   incidentStore is InMemoryIncidentStateStore inMemoryIncidents
                ? new InMemoryWorkflowRuntimeAttentionQuery(inMemoryExecutions, inMemoryIncidents, provider.GetService<TimeProvider>())
                : new UnavailableWorkflowRuntimeAttentionQuery();
        });
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAttentionContributor, WorkflowRuntimeAttentionContributor>());
    }
}
