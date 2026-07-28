using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Workflows.Design.Core.Services;

/// <summary>
/// Coordinates Design-owned context contributors. The service accepts only a location and always
/// filters the contributor result again, preventing arbitrary client symbol catalog injection.
/// </summary>
public sealed class ExpressionAuthoringContextService(IEnumerable<IExpressionAuthoringContextSource> sources)
    : IExpressionAuthoringContextService
{
    private const int MaximumPageSize = 500;

    public async ValueTask<ExpressionToolingOutcome<ExpressionAuthoringContext>> ResolveAsync(
        ResolveExpressionAuthoringContextRequest request,
        ExpressionAuthoringAuthorization authorization,
        CancellationToken cancellationToken)
        => await ResolveCoreAsync(request, authorization, cancellationToken, pageSymbols: true);

    public async ValueTask<ExpressionToolingOutcome<ExpressionAuthoringContext>> ResolveForProviderAsync(
        ResolveExpressionAuthoringContextRequest request,
        ExpressionAuthoringAuthorization authorization,
        CancellationToken cancellationToken)
        => await ResolveCoreAsync(request, authorization, cancellationToken, pageSymbols: false);

    private async ValueTask<ExpressionToolingOutcome<ExpressionAuthoringContext>> ResolveCoreAsync(
        ResolveExpressionAuthoringContextRequest request,
        ExpressionAuthoringAuthorization authorization,
        CancellationToken cancellationToken,
        bool pageSymbols)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!authorization.IsAuthorized)
            return ExpressionToolingOutcome<ExpressionAuthoringContext>.Failure(ExpressionToolingOutcomeState.Unauthorized, ExpressionToolingContractVersion.V1);
        if (!request.ContractVersion.IsCompatibleWith(ExpressionToolingContractVersion.V1))
            return ExpressionToolingOutcome<ExpressionAuthoringContext>.Failure(ExpressionToolingOutcomeState.Incompatible, ExpressionToolingContractVersion.V1, "contract-version");
        if (string.IsNullOrWhiteSpace(request.WorkflowDraftId) || string.IsNullOrWhiteSpace(request.NodeId) || string.IsNullOrWhiteSpace(request.PropertyKey) || string.IsNullOrWhiteSpace(request.ExpressionType))
            return ExpressionToolingOutcome<ExpressionAuthoringContext>.Failure(ExpressionToolingOutcomeState.Incompatible, ExpressionToolingContractVersion.V1, "invalid-location");
        if (request.Skip < 0 || request.Take is < 1 or > MaximumPageSize || request.Search?.Length > 256)
            return ExpressionToolingOutcome<ExpressionAuthoringContext>.Failure(ExpressionToolingOutcomeState.Incompatible, ExpressionToolingContractVersion.V1, "invalid-page");

        var sourceFailed = false;
        foreach (var source in sources)
        {
            ExpressionAuthoringContext? context;
            try
            {
                context = await source.TryResolveAsync(request, authorization, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                sourceFailed = true;
                continue;
            }
            if (context is null)
                continue;
            if (!string.Equals(context.Document.WorkflowDraftId, request.WorkflowDraftId, StringComparison.Ordinal) ||
                !string.Equals(context.Document.NodeId, request.NodeId, StringComparison.Ordinal) ||
                !string.Equals(context.Document.PropertyKey, request.PropertyKey, StringComparison.Ordinal) ||
                !string.Equals(context.Document.ExpressionType, request.ExpressionType, StringComparison.Ordinal))
                return ExpressionToolingOutcome<ExpressionAuthoringContext>.Failure(ExpressionToolingOutcomeState.Unavailable, ExpressionToolingContractVersion.V1, "invalid-context-source");
            if (request.ContextRevision is not null && !string.Equals(request.ContextRevision, context.ContextRevision, StringComparison.Ordinal))
                return ExpressionToolingOutcome<ExpressionAuthoringContext>.Failure(ExpressionToolingOutcomeState.Stale, ExpressionToolingContractVersion.V1, documentRevision: context.Document.DocumentRevision, contextRevision: context.ContextRevision);

            var search = request.Search?.Trim();
            var symbols = pageSymbols
                ? context.RootSymbols
                    .Where(symbol => string.IsNullOrEmpty(search) || symbol.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                    .Skip(request.Skip)
                    .Take(request.Take)
                    .ToArray()
                : context.RootSymbols;
            var bounded = context with { RootSymbols = symbols };
            return symbols.Count == 0
                ? ExpressionToolingOutcome<ExpressionAuthoringContext>.SupportedEmpty(bounded, ExpressionToolingContractVersion.V1, bounded.Document.DocumentRevision, bounded.ContextRevision)
                : ExpressionToolingOutcome<ExpressionAuthoringContext>.Success(bounded, ExpressionToolingContractVersion.V1, bounded.Document.DocumentRevision, bounded.ContextRevision);
        }

        return ExpressionToolingOutcome<ExpressionAuthoringContext>.Failure(
            ExpressionToolingOutcomeState.Unavailable,
            ExpressionToolingContractVersion.V1,
            sourceFailed ? "context-source-failed" : "context-unavailable");
    }
}
