using CShells.Features;
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
    }

    private static void RejectDuplicate<T>(IServiceCollection services)
    {
        if (services.Count(service => service.ServiceType == typeof(T)) > 1)
            throw new InvalidOperationException($"Multiple replacements of {typeof(T).Name} are not supported.");
    }
}
