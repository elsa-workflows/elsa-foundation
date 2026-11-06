using Elsa.Expressions.Core.Models;

namespace Elsa.Workflows.Design.Core.Contracts;

/// <summary>
/// Single-implementation, scoped replacement for Design expression operations over an authoritative location.
/// The owning feature rejects duplicate registrations at startup; optional absence is valid.
/// HTTP owners also fail closed with Unavailable when no or multiple implementations are composed.
/// </summary>
public interface IExpressionToolingOperations
{
    ValueTask<ExpressionToolingOutcome<ExpressionAuthoringContext>> ResolveContextAsync(ResolveExpressionAuthoringContextRequest request, ExpressionAuthoringAuthorization authorization, CancellationToken cancellationToken);
    ValueTask<ExpressionToolingOutcome<ExpressionToolingItems>> SearchSymbolsAsync(ResolveExpressionAuthoringContextRequest request, ExpressionAuthoringAuthorization authorization, CancellationToken cancellationToken);
    ValueTask<ExpressionToolingOutcome<ExpressionToolingItems>> CompleteAsync(ResolveExpressionAuthoringContextRequest request, string source, ExpressionToolingPosition cursor, ExpressionAuthoringAuthorization authorization, CancellationToken cancellationToken);
    ValueTask<ExpressionToolingOutcome<ExpressionHover>> HoverAsync(ResolveExpressionAuthoringContextRequest request, string source, ExpressionToolingPosition position, ExpressionAuthoringAuthorization authorization, CancellationToken cancellationToken);
    ValueTask<ExpressionToolingOutcome<ExpressionDiagnosticSet>> ValidateAsync(ResolveExpressionAuthoringContextRequest request, string source, ExpressionAuthoringAuthorization authorization, CancellationToken cancellationToken);
}
