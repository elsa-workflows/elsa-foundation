using CShells.Features;
using CShells.Lifecycle;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Decisions.Workflows.Design;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Decisions")]
[ShellFeature(name: "DecisionsWorkflowsDesign", DisplayName = "Decision workflow publication", Description = "Pins authorized immutable decisions when workflows are published.", DependsOn = new object[] { "Decisions", "DecisionsWorkflowsRuntime", "WorkflowsPublishing" })]
public class DecisionsWorkflowsDesignFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.TryAddScoped<IDecisionPublicationAuthorizer, DecisionPublicationAuthorizer>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IExecutableCompilationSource, DecisionPinSource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IActivityInputSourceConstraintProvider, DecisionInputSourceConstraintProvider>());
        services.AddSingleton<IShellInitializer>(new Cardinality(services));
    }

    private sealed class Cardinality(IServiceCollection services) : IShellInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            if (services.Count(item => item.ServiceType == typeof(IDecisionPublicationAuthorizer)) != 1)
                throw new InvalidOperationException("Exactly one decision publication authorizer must be registered.");
            return Task.CompletedTask;
        }
    }
}
