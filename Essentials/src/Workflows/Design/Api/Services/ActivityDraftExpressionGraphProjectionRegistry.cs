using Elsa.Tasks.Core;
using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Workflows.Design.Api.Services;

/// <summary>Selects one provider strategy; constructing the registry never reads or projects a draft.</summary>
internal sealed class ActivityDraftExpressionGraphProjectionRegistry
{
    private readonly IReadOnlyDictionary<string, IActivityDraftExpressionGraphProjection> _strategies;

    public ActivityDraftExpressionGraphProjectionRegistry(IEnumerable<IActivityDraftExpressionGraphProjection> strategies)
    {
        var byKey = new Dictionary<string, IActivityDraftExpressionGraphProjection>(StringComparer.Ordinal);
        foreach (var strategy in strategies)
        {
            if (string.IsNullOrWhiteSpace(strategy.ProviderKey))
                throw new InvalidOperationException($"Activity draft expression graph strategy '{strategy.GetType().FullName}' has a blank provider key.");
            if (!byKey.TryAdd(strategy.ProviderKey, strategy))
                throw new InvalidOperationException($"Activity draft expression graph strategies have duplicate provider key '{strategy.ProviderKey}'; select one IActivityDraftExpressionGraphProjection per provider.");
        }
        _strategies = byKey;
    }

    public IActivityDraftExpressionGraphProjection? Find(string providerKey) => _strategies.GetValueOrDefault(providerKey);
}

/// <summary>Validates the scoped strategy registry before the shell accepts requests.</summary>
internal sealed class ValidateActivityDraftExpressionGraphProjectionsStartupTask(
    IEnumerable<IActivityDraftExpressionGraphProjection> strategies) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = new ActivityDraftExpressionGraphProjectionRegistry(strategies);
        return Task.CompletedTask;
    }
}
