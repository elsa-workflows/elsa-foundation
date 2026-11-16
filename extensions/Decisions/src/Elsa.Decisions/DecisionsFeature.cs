using CShells.Features;
using CShells.Lifecycle;
using Elsa.Decisions.Core;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Decisions;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Decisions")]
[ShellFeature(name: "Decisions", DisplayName = "Business Decisions", Description = "Bounded pure decision-table compilation and evaluation.")]
public class DecisionsFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        RejectDuplicate<IDecisionCompiler>(services);
        RejectDuplicate<IDecisionEvaluator>(services);
        services.TryAddScoped<IDecisionCompiler, DecisionEngine>();
        services.TryAddScoped<IDecisionEvaluator, DecisionEngine>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IShellInitializer>(new Cardinality(services)));
    }

    private sealed class Cardinality(IServiceCollection services) : IShellInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireOne<IDecisionCompiler>();
            RequireOne<IDecisionEvaluator>();
            return Task.CompletedTask;
        }

        private void RequireOne<T>()
        {
            var count = services.Count(service => service.ServiceType == typeof(T));
            if (count != 1)
                throw new InvalidOperationException($"Decision replacement contract '{typeof(T).FullName}' requires exactly one registration, but found {count}. Replace the existing implementation instead of adding another.");
        }
    }

    private static void RejectDuplicate<T>(IServiceCollection services)
    {
        if (services.Count(service => service.ServiceType == typeof(T)) > 1)
            throw new InvalidOperationException($"Multiple replacements of {typeof(T).Name} are not supported.");
    }
}
