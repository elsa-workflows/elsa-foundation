using Elsa.Expressions.Core.Contracts;

namespace Elsa.Expressions.Services;

/// <summary>Deterministic expression-type router, intentionally separate from evaluator routing.</summary>
public sealed class ExpressionToolingProviderResolver : IExpressionToolingProviderResolver
{
    private readonly IReadOnlyDictionary<string, IExpressionToolingProvider> _providers;

    public ExpressionToolingProviderResolver(IEnumerable<IExpressionToolingProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToDictionary(
            provider => string.IsNullOrWhiteSpace(provider.ExpressionType)
                ? throw new InvalidOperationException("An expression tooling provider must declare an expression type.")
                : provider.ExpressionType,
            StringComparer.OrdinalIgnoreCase);
    }

    public IExpressionToolingProvider? Find(string expressionType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expressionType);
        return _providers.GetValueOrDefault(expressionType);
    }
}
