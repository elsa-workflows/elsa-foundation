using CShells.Lifecycle;
using Elsa.Decisions.Persistence.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Decisions.Persistence.EntityFrameworkCore;

internal sealed record DecisionPersistenceComposition(IServiceCollection Services);

/// <summary>Detects late replacement conflicts or removal of an EF provider's CQS registrations.</summary>
internal sealed class DecisionPersistenceCompositionCheck(DecisionPersistenceComposition composition) : IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Validate(composition.Services, requireBoth: true);
        return Task.CompletedTask;
    }

    internal static void Validate(IServiceCollection services, bool requireBoth)
    {
        foreach (var contract in new[] { typeof(IDecisionReader), typeof(IDecisionWriter) })
        {
            var count = services.Count(item => item.ServiceType == contract && !item.IsKeyedService);
            if (count > 1 || requireBoth && count != 1)
                throw new InvalidOperationException($"Decisions persistence requires exactly one reader and writer; {contract.Name} has {count} registrations.");
        }
    }

    internal static void Register(IServiceCollection services)
    {
        if (services.Any(item => item.ServiceType == typeof(DecisionPersistenceComposition))) return;
        services.AddSingleton(new DecisionPersistenceComposition(services));
        services.AddShellInitializer<DecisionPersistenceCompositionCheck>(LifecyclePhase.Prepare, 0);
    }
}
