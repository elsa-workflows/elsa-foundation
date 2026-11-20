using CShells.Lifecycle;
using Elsa.Decisions.Persistence.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Decisions.Design;

internal sealed record DecisionDesignComposition(IServiceCollection Services);

/// <summary>Validates the completed shell registrations before any startup work resolves replacements.</summary>
internal sealed class DecisionDesignCompositionCheck(DecisionDesignComposition composition) : IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Validate(composition.Services, requirePolicy: true);
        return Task.CompletedTask;
    }

    internal static void Validate(IServiceCollection services, bool requirePolicy)
    {
        foreach (var contract in new[] { typeof(IDecisionAccessPolicy), typeof(IDecisionReader), typeof(IDecisionWriter) })
        {
            var count = services.Count(item => item.ServiceType == contract && !item.IsKeyedService);
            if (count > 1 || requirePolicy && contract == typeof(IDecisionAccessPolicy) && count != 1)
                throw new InvalidOperationException($"Decisions Design requires exactly one access policy and at most one persistence reader/writer; {contract.Name} has {count} registrations.");
        }
    }

    internal static void Register(IServiceCollection services)
    {
        if (services.Any(item => item.ServiceType == typeof(DecisionDesignComposition))) return;
        services.AddSingleton(new DecisionDesignComposition(services));
        services.AddShellInitializer<DecisionDesignCompositionCheck>(LifecyclePhase.Prepare, 0);
    }
}
