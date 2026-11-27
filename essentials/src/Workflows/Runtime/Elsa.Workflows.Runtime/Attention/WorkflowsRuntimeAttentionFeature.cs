using CShells.Features;
using Elsa.Attention.Core;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Incidents;

namespace Elsa.Workflows.Runtime.Attention;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Attention")]
[ShellFeature(
    name: "WorkflowsRuntimeAttention",
    DisplayName = "Workflow Runtime Attention",
    Description = "Contributes complete workflow failure and incident conditions to Attention.",
    DependsOn = new object[] { "AttentionApi", "WorkflowsRuntimeApi" })]
[RuntimeDefaultRegistration]
public sealed class WorkflowsRuntimeAttentionFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.TryAddSingleton(new LegacyWorkflowRuntimeAttentionSelected());
        services.TryAddSingleton<IWorkflowRuntimeAttentionQuery>(provider =>
        {
            var executionStore = provider.GetRequiredService<IWorkflowExecutionStateStore>();
            var incidentStore = provider.GetRequiredService<IIncidentStateStore>();
            return executionStore is InMemoryWorkflowExecutionStateStore inMemoryExecutions &&
                   incidentStore is InMemoryIncidentStateStore inMemoryIncidents
                ? new InMemoryWorkflowRuntimeAttentionQuery(inMemoryExecutions, inMemoryIncidents, provider.GetService<TimeProvider>())
                : new UnavailableWorkflowRuntimeAttentionQuery();
        });
        AddContributor(services);
    }

    internal static void AddContributor(IServiceCollection services) =>
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAttentionContributor, WorkflowRuntimeAttentionContributor>());
}

internal sealed class LegacyWorkflowRuntimeAttentionSelected;
