using CShells.Features;
using Elsa.Decisions.Persistence.Core;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Decisions.Design;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Decisions")]
[ShellFeature(name: "DecisionsDesign", DisplayName = "Decisions Design", Description = "Manages reusable decision assets, publication and saved cases.", DependsOn = new object[] { "Decisions" })]
public class DecisionsDesignFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Retain detailed test history", Description = "Retain protected test inputs and comparisons; disabled by default.", Category = "Privacy")]
    public bool RetainDetailedTestHistory { get; set; }

    public virtual void ConfigureServices(IServiceCollection services) => services.AddDecisionsDesign(new() { RetainDetailedTestHistory = RetainDetailedTestHistory });
}

public static class DecisionsDesignRegistration
{
    public static IServiceCollection AddDecisionsDesign(this IServiceCollection services, DecisionDesignOptions? options = null)
    {
        DecisionDesignCompositionCheck.Validate(services, requirePolicy: false);
        services.TryAddSingleton(options ?? new DecisionDesignOptions());
        services.TryAddScoped<IDecisionAccessPolicy, UnsupportedDecisionAccessPolicy>();
        services.TryAddScoped(sp => new DecisionDesignService(
            sp.GetService<IDecisionReader>() ?? new UnavailableDecisionPersistence(),
            sp.GetService<IDecisionWriter>() ?? new UnavailableDecisionPersistence(),
            sp.GetRequiredService<Elsa.Decisions.Core.IDecisionCompiler>(),
            sp.GetRequiredService<Elsa.Decisions.Core.IDecisionEvaluator>(),
            sp.GetRequiredService<IDecisionAccessPolicy>(), sp.GetRequiredService<DecisionDesignOptions>()));
        DecisionDesignCompositionCheck.Register(services);
        return services;
    }

    /// <summary>Installs a provider policy while preserving an explicit host replacement.</summary>
    public static IServiceCollection UseDecisionsAccessPolicy<TPolicy>(this IServiceCollection services) where TPolicy : class, IDecisionAccessPolicy
    {
        var policies = services.Where(x => x.ServiceType == typeof(IDecisionAccessPolicy) && !x.IsKeyedService).ToArray();
        if (policies.Length > 1) throw new InvalidOperationException("Decisions has competing access-policy replacements.");
        if (policies.Length == 0 || policies[0].ImplementationType == typeof(UnsupportedDecisionAccessPolicy))
            services.Replace(ServiceDescriptor.Scoped<IDecisionAccessPolicy, TPolicy>());
        return services;
    }
}

internal sealed class UnsupportedDecisionAccessPolicy : IDecisionAccessPolicy
{
    public ValueTask<bool> IsAllowedAsync(DecisionCaller caller, string permission, object? resource = null, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<bool>(new DecisionDesignException("unavailable", "This host has no supported decision authorization policy."));
}
