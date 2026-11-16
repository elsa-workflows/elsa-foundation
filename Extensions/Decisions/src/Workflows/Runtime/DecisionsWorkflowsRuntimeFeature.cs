using CShells.Features;
using CShells.Lifecycle;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Decisions.Workflows.Runtime;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Decisions")]
[ShellFeature(name: "DecisionsWorkflowsRuntime", DisplayName = "Decisions in workflows", Description = "Evaluates frozen decision artifacts using the existing workflow execution authority and checkpoint lifecycle.", DependsOn = new object[] { "Decisions", "WorkflowsRuntimeResumption", "FoundationIdentityAbstractions" })]
public class DecisionsWorkflowsRuntimeFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.TryAddScoped<IDecisionWorkflowAuthorizer, DecisionWorkflowAuthorizer>();
        services.AddSingleton<IShellInitializer>(new Cardinality(services));
    }

    private sealed class Cardinality(IServiceCollection services) : IShellInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            if (services.Count(item => item.ServiceType == typeof(IDecisionWorkflowAuthorizer)) != 1)
                throw new InvalidOperationException("Exactly one decision workflow authorizer must be registered.");
            return Task.CompletedTask;
        }
    }
}
