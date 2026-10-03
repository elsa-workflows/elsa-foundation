using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.Core.Contracts;

/// <summary>
/// Supplies language-specific authoring assistance from a Design-filtered metadata context.
/// Implementations must not evaluate source or access runtime values.
/// </summary>
public interface IExpressionToolingProvider
{
    string ExpressionType { get; }
    ExpressionToolingContractVersion SupportedVersion { get; }
    ExpressionToolingCapabilities DeclaredCapabilities => new();
    /// <summary>Optional immutable profile metadata; never a policy-filtered authority by itself.</summary>
    ExpressionToolingCatalog? DeclaredCatalog => null;

    ValueTask<ExpressionToolingOutcome<ExpressionToolingCapabilities>> GetCapabilitiesAsync(ExpressionToolingRequestScope scope, CancellationToken cancellationToken);
    ValueTask<ExpressionToolingOutcome<ExpressionToolingItems>> GetCompletionsAsync(ExpressionCompletionRequest request, CancellationToken cancellationToken);
    ValueTask<ExpressionToolingOutcome<ExpressionHover>> GetHoverAsync(ExpressionHoverRequest request, CancellationToken cancellationToken);
    ValueTask<ExpressionToolingOutcome<ExpressionDiagnosticSet>> ValidateAsync(ExpressionValidationRequest request, CancellationToken cancellationToken);
}
