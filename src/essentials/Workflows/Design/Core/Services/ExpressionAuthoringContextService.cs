using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Workflows.Design.Core.Services;

/// <summary>
/// Coordinates Design-owned context sources and optional selected-provider profile metadata. The
/// service accepts only a location and never accepts a client-supplied symbol catalog as authority.
/// </summary>
public sealed class ExpressionAuthoringContextService : IExpressionAuthoringContextService
{
    private const int MaximumPageSize = 500;
    private const int MaximumProfileSymbols = 500;
    private const int MaximumCombinedSymbols = 5_500;
    private const string ProfileUnavailableCode = "profile-unavailable";

    private readonly IEnumerable<IExpressionAuthoringContextSource> _sources;
    private readonly IExpressionToolingProviderResolver? _providerResolver;
    private readonly IReadOnlyList<IExpressionAuthoringSymbolFilter> _symbolFilters;

    /// <summary>Creates a context service with the legacy source-only composition.</summary>
    public ExpressionAuthoringContextService(IEnumerable<IExpressionAuthoringContextSource> sources)
        : this(sources, null, null)
    {
    }

    /// <summary>Creates a context service with optional selected-provider profile composition.</summary>
    public ExpressionAuthoringContextService(
        IEnumerable<IExpressionAuthoringContextSource> sources,
        IExpressionToolingProviderResolver? providerResolver = null,
        IEnumerable<IExpressionAuthoringSymbolFilter>? symbolFilters = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = sources;
        _providerResolver = providerResolver;
        _symbolFilters = symbolFilters?.ToArray() ?? [];
    }

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
        foreach (var source in _sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExpressionAuthoringContext? context;
            try
            {
                context = await source.TryResolveAsync(request, authorization, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is not (
                OutOfMemoryException or
                StackOverflowException or
                AccessViolationException))
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
            try
            {
                context = await ComposeDeclaredCatalogAsync(context, request.ExpressionType, authorization, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is not (
                OutOfMemoryException or
                StackOverflowException or
                AccessViolationException))
            {
                return ProfileUnavailable();
            }

            cancellationToken.ThrowIfCancellationRequested();
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
            cancellationToken.ThrowIfCancellationRequested();
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

    private async ValueTask<ExpressionAuthoringContext> ComposeDeclaredCatalogAsync(
        ExpressionAuthoringContext context,
        string expressionType,
        ExpressionAuthoringAuthorization authorization,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_providerResolver is null)
            return context;

        cancellationToken.ThrowIfCancellationRequested();
        var provider = _providerResolver.Find(expressionType);
        cancellationToken.ThrowIfCancellationRequested();
        if (provider is null)
            return context;
        if (!string.Equals(provider.ExpressionType, expressionType, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected tooling provider does not match the requested expression type.");

        var catalog = provider.DeclaredCatalog;
        cancellationToken.ThrowIfCancellationRequested();
        if (catalog is null)
            return context;
        if (string.IsNullOrWhiteSpace(catalog.Revision) || catalog.Symbols is null || catalog.Symbols.Count > MaximumProfileSymbols)
            throw new InvalidOperationException("The declared tooling profile is invalid.");

        var profileIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in catalog.Symbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate is null || string.IsNullOrWhiteSpace(candidate.SymbolId) || !profileIds.Add(candidate.SymbolId))
                throw new InvalidOperationException("The declared tooling profile contains an invalid symbol identity.");
        }

        var filteredProfile = new List<ExpressionSymbol>(catalog.Symbols.Count);
        foreach (var candidate in catalog.Symbols.OrderBy(symbol => symbol.SymbolId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExpressionSymbol? filtered = candidate;
            foreach (var symbolFilter in _symbolFilters)
            {
                if (filtered is null)
                    break;
                filtered = await symbolFilter.FilterAsync(filtered, context, authorization, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (filtered is null)
                    break;
                if (!string.Equals(filtered.SymbolId, candidate.SymbolId, StringComparison.Ordinal))
                    throw new InvalidOperationException("A symbol policy filter changed a declared profile identity.");
            }

            if (filtered is not null)
                filteredProfile.Add(filtered);
        }

        var sourceLimit = MaximumCombinedSymbols - filteredProfile.Count;
        var combinedCapacity = filteredProfile.Count + Math.Min(sourceLimit, context.RootSymbols.Count);
        var combined = new List<ExpressionSymbol>(combinedCapacity);
        combined.AddRange(filteredProfile);
        foreach (var symbol in context.RootSymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (profileIds.Contains(symbol.SymbolId))
                continue;
            combined.Add(symbol);
            if (combined.Count == filteredProfile.Count + sourceLimit)
                break;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return context with
        {
            RootSymbols = combined.ToArray(),
            ContextRevision = ComposeRevision("context", context.ContextRevision, catalog.Revision),
            SymbolCatalogRevision = ComposeRevision("symbols", context.SymbolCatalogRevision, catalog.Revision)
        };
    }

    private static ExpressionToolingOutcome<ExpressionAuthoringContext> ProfileUnavailable() =>
        ExpressionToolingOutcome<ExpressionAuthoringContext>.Failure(
            ExpressionToolingOutcomeState.Unavailable,
            ExpressionToolingContractVersion.V1,
            ProfileUnavailableCode);

    private static string ComposeRevision(string kind, string sourceRevision, string profileRevision)
    {
        var material = string.Concat(
            "expression-tooling-", kind, "-profile-v1\n",
            sourceRevision.Length.ToString(CultureInfo.InvariantCulture), ":", sourceRevision,
            profileRevision.Length.ToString(CultureInfo.InvariantCulture), ":", profileRevision);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }
}
