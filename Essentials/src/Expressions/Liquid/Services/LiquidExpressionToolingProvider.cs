using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.Core.Services;
using Elsa.Expressions.Liquid.Models;
using Fluid;

namespace Elsa.Expressions.Liquid.Services;

/// <summary>Safe Liquid syntax and metadata assistance. It never constructs a Fluid context or renders a template.</summary>
public sealed class LiquidExpressionToolingProvider : IExpressionToolingProvider
{
    private static readonly string[] BuiltInFilters = new TemplateOptions().Filters
        .Select(filter => filter.Key)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();

    private static readonly string[] BuiltInTags = new FluidParser().RegisteredTags.Keys
        .Where(name => name != "#")
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();

    public ExpressionToolingCapabilities DeclaredCapabilities { get; } = new(
        SupportsCompletions: true,
        SupportsHover: true,
        SupportsValidation: true,
        SupportsSymbolPaging: true,
        SupportsLazyMembers: false,
        MaximumSymbols: 500);

    public string ExpressionType => LiquidExpressionDescriptor.TypeName;
    public ExpressionToolingContractVersion SupportedVersion => ExpressionToolingContractVersion.V1;

    public ValueTask<ExpressionToolingOutcome<ExpressionToolingCapabilities>> GetCapabilitiesAsync(ExpressionToolingRequestScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var failure = Compatible<ExpressionToolingCapabilities>(scope);
        return ValueTask.FromResult(failure ?? ExpressionToolingOutcome<ExpressionToolingCapabilities>.Success(DeclaredCapabilities, SupportedVersion, scope.Document.DocumentRevision, scope.Context.ContextRevision));
    }

    public ValueTask<ExpressionToolingOutcome<ExpressionToolingItems>> GetCompletionsAsync(ExpressionCompletionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var failure = Compatible<ExpressionToolingItems>(request.Scope);
        if (failure is not null) return ValueTask.FromResult(failure);
        var prefix = Prefix(request.Source, request.Cursor);
        var symbols = ExpressionToolingSymbolResolver.Complete(request.Scope.Context, request.Source, request.Cursor)
            .OrderByDescending(symbol => MatchesExpectedResult(symbol, request.Scope.Context))
            .ThenBy(symbol => symbol.Label, StringComparer.OrdinalIgnoreCase);
        var liquidItems = BuiltInTags
            .Where(tag => tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(tag => new ExpressionToolingItem(tag, "Liquid tag", InsertText: tag, Kind: ExpressionSymbolKind.Tag));
        var filters = BuiltInFilters
            .Where(filter => filter.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(filter => new ExpressionToolingItem(filter, "Liquid filter", InsertText: filter, Kind: ExpressionSymbolKind.Filter));
        var items = symbols
            .Concat(liquidItems)
            .Concat(filters)
            .Take(request.Scope.Context.Capabilities.MaximumSymbols)
            .ToArray();
        var result = new ExpressionToolingItems(items);
        return ValueTask.FromResult(items.Length == 0
            ? ExpressionToolingOutcome<ExpressionToolingItems>.SupportedEmpty(result, SupportedVersion, request.Scope.Document.DocumentRevision, request.Scope.Context.ContextRevision)
            : ExpressionToolingOutcome<ExpressionToolingItems>.Success(result, SupportedVersion, request.Scope.Document.DocumentRevision, request.Scope.Context.ContextRevision));
    }

    public ValueTask<ExpressionToolingOutcome<ExpressionHover>> GetHoverAsync(ExpressionHoverRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var failure = Compatible<ExpressionHover>(request.Scope);
        if (failure is not null) return ValueTask.FromResult(failure);
        var symbol = ExpressionToolingSymbolResolver.Resolve(request.Scope.Context, request.Source, request.Position);
        var content = symbol is null ? string.Empty : string.Join(Environment.NewLine, new[] { symbol.Label, symbol.Documentation }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var hover = new ExpressionHover(content);
        return ValueTask.FromResult(symbol is null
            ? ExpressionToolingOutcome<ExpressionHover>.SupportedEmpty(hover, SupportedVersion, request.Scope.Document.DocumentRevision, request.Scope.Context.ContextRevision)
            : ExpressionToolingOutcome<ExpressionHover>.Success(hover, SupportedVersion, request.Scope.Document.DocumentRevision, request.Scope.Context.ContextRevision));
    }

    public ValueTask<ExpressionToolingOutcome<ExpressionDiagnosticSet>> ValidateAsync(ExpressionValidationRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var failure = Compatible<ExpressionDiagnosticSet>(request.Scope);
        if (failure is not null) return ValueTask.FromResult(failure);
        var diagnostics = new List<ExpressionDiagnostic>();
        if (!new FluidParser().TryParse(request.Source, out _, out var error))
            diagnostics.Add(new(
                "Liquid/Syntax",
                ExpressionDiagnosticSeverity.Error,
                error,
                request.Scope.Document.DocumentRevision));
        var result = new ExpressionDiagnosticSet(diagnostics);
        return ValueTask.FromResult(diagnostics.Count == 0
            ? ExpressionToolingOutcome<ExpressionDiagnosticSet>.SupportedEmpty(result, SupportedVersion, request.Scope.Document.DocumentRevision, request.Scope.Context.ContextRevision)
            : ExpressionToolingOutcome<ExpressionDiagnosticSet>.Success(result, SupportedVersion, request.Scope.Document.DocumentRevision, request.Scope.Context.ContextRevision));
    }

    private ExpressionToolingOutcome<T>? Compatible<T>(ExpressionToolingRequestScope scope) =>
        !string.Equals(scope.Document.ExpressionType, ExpressionType, StringComparison.OrdinalIgnoreCase)
            ? ExpressionToolingOutcome<T>.Failure(ExpressionToolingOutcomeState.Incompatible, SupportedVersion, "expression-type")
            : !scope.ContractVersion.IsCompatibleWith(SupportedVersion)
                ? ExpressionToolingOutcome<T>.Failure(ExpressionToolingOutcomeState.Incompatible, SupportedVersion, "contract-version")
                : null;

    private static string Prefix(string source, ExpressionToolingPosition position)
    {
        var offset = ToOffset(source, position);
        var start = offset;
        while (start > 0 && (char.IsLetterOrDigit(source[start - 1]) || source[start - 1] == '_')) start--;
        return source[start..offset];
    }

    private static int ToOffset(string source, ExpressionToolingPosition position)
    {
        if (!position.IsValid)
            return 0;
        var line = 0;
        var offset = 0;
        while (offset < source.Length && line < position.Line)
        {
            if (source[offset++] == '\n') line++;
        }
        return Math.Clamp(offset + position.Character, 0, source.Length);
    }

    private static bool MatchesExpectedResult(ExpressionToolingItem symbol, ExpressionAuthoringContext context) =>
        !string.IsNullOrWhiteSpace(context.ExpectedResultType) &&
        string.Equals(symbol.Detail, context.ExpectedResultType, StringComparison.Ordinal);
}
