using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.Core.Services;
using Elsa.Expressions.Liquid.Models;
using Fluid;

namespace Elsa.Expressions.Liquid.Services;

/// <summary>Safe Liquid syntax and metadata assistance. It never constructs a Fluid context or renders a template.</summary>
public sealed class LiquidExpressionToolingProvider : IExpressionToolingProvider
{
    private readonly LiquidExpressionProfile profile;

    public LiquidExpressionToolingProvider() : this(LiquidExpressionProfile.Default)
    {
    }

    public LiquidExpressionToolingProvider(LiquidExpressionProfile profile) =>
        this.profile = profile ?? throw new ArgumentNullException(nameof(profile));

    public ExpressionToolingCapabilities DeclaredCapabilities { get; } = new(
        SupportsCompletions: true,
        SupportsHover: true,
        SupportsValidation: true,
        SupportsSymbolPaging: true,
        SupportsLazyMembers: false,
        MaximumSymbols: 500);

    public ExpressionToolingCatalog DeclaredCatalog => profile.ToolingCatalog;
    public string ExpressionType => LiquidExpressionDescriptor.TypeName;
    public ExpressionToolingContractVersion SupportedVersion => ExpressionToolingContractVersion.V1;

    public ValueTask<ExpressionToolingOutcome<ExpressionToolingCapabilities>> GetCapabilitiesAsync(
        ExpressionToolingRequestScope scope,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var failure = Compatible<ExpressionToolingCapabilities>(scope);
        return ValueTask.FromResult(failure ?? ExpressionToolingOutcome<ExpressionToolingCapabilities>.Success(
            DeclaredCapabilities, SupportedVersion, scope.Document.DocumentRevision, scope.Context.ContextRevision));
    }

    public ValueTask<ExpressionToolingOutcome<ExpressionToolingItems>> GetCompletionsAsync(
        ExpressionCompletionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var failure = Compatible<ExpressionToolingItems>(request.Scope);
        if (failure is not null) return ValueTask.FromResult(failure);

        var cursor = LiquidCursorContextAnalyzer.Analyze(request.Source, request.Cursor);
        IEnumerable<ExpressionToolingItem> candidates = cursor.Mode switch
        {
            LiquidCursorMode.Filter => CompleteProfileSymbols(request.Scope.Context, ExpressionSymbolKind.Filter, cursor.Prefix(request.Source)),
            LiquidCursorMode.Tag => CompleteProfileSymbols(request.Scope.Context, ExpressionSymbolKind.Tag, cursor.Prefix(request.Source)),
            LiquidCursorMode.Value or LiquidCursorMode.TagValue => CompleteValues(request.Scope.Context, request.Source, request.Cursor),
            _ => []
        };

        var items = candidates
            .OrderByDescending(item => MatchesExpectedResult(item, request.Scope.Context))
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, request.Scope.Context.Capabilities.MaximumSymbols))
            .ToArray();
        var result = new ExpressionToolingItems(items);
        return ValueTask.FromResult(items.Length == 0
            ? ExpressionToolingOutcome<ExpressionToolingItems>.SupportedEmpty(result, SupportedVersion, request.Scope.Document.DocumentRevision, request.Scope.Context.ContextRevision)
            : ExpressionToolingOutcome<ExpressionToolingItems>.Success(result, SupportedVersion, request.Scope.Document.DocumentRevision, request.Scope.Context.ContextRevision));
    }

    public ValueTask<ExpressionToolingOutcome<ExpressionHover>> GetHoverAsync(
        ExpressionHoverRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var failure = Compatible<ExpressionHover>(request.Scope);
        if (failure is not null) return ValueTask.FromResult(failure);

        var cursor = LiquidCursorContextAnalyzer.Analyze(request.Source, request.Position);
        ExpressionToolingItem? symbol = null;
        if ((cursor.Mode is LiquidCursorMode.Filter or LiquidCursorMode.Tag) && cursor.TokenStart != cursor.TokenEnd)
        {
            var kind = cursor.Mode == LiquidCursorMode.Filter ? ExpressionSymbolKind.Filter : ExpressionSymbolKind.Tag;
            var name = request.Source[cursor.TokenStart..cursor.TokenEnd];
            symbol = FindProfileSymbol(request.Scope.Context, kind, name);
        }
        else if ((cursor.Mode is LiquidCursorMode.Value or LiquidCursorMode.TagValue) && cursor.TokenStart != cursor.TokenEnd)
        {
            symbol = ExpressionToolingSymbolResolver.Resolve(
                ValueContext(request.Scope.Context), request.Source, request.Position);
        }

        var range = cursor.TokenRange(request.Source);
        var hover = new ExpressionHover(symbol is null ? string.Empty : HoverContents(symbol), symbol is null ? null : range);
        return ValueTask.FromResult(symbol is null
            ? ExpressionToolingOutcome<ExpressionHover>.SupportedEmpty(hover, SupportedVersion, request.Scope.Document.DocumentRevision, request.Scope.Context.ContextRevision)
            : ExpressionToolingOutcome<ExpressionHover>.Success(hover, SupportedVersion, request.Scope.Document.DocumentRevision, request.Scope.Context.ContextRevision));
    }

    public ValueTask<ExpressionToolingOutcome<ExpressionDiagnosticSet>> ValidateAsync(
        ExpressionValidationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var failure = Compatible<ExpressionDiagnosticSet>(request.Scope);
        if (failure is not null) return ValueTask.FromResult(failure);

        var diagnostics = new List<ExpressionDiagnostic>();
        if (!profile.CreateParser().TryParse(request.Source, out _, out var error))
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

    private static IEnumerable<ExpressionToolingItem> CompleteValues(
        ExpressionAuthoringContext context,
        string source,
        ExpressionToolingPosition cursor) =>
        ExpressionToolingSymbolResolver.Complete(ValueContext(context), source, cursor);

    private static IEnumerable<ExpressionToolingItem> CompleteProfileSymbols(
        ExpressionAuthoringContext context,
        ExpressionSymbolKind kind,
        string prefix) => context.RootSymbols
        .Where(symbol => symbol.Kind == kind && symbol.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        .Select(ToItem);

    private static ExpressionToolingItem? FindProfileSymbol(
        ExpressionAuthoringContext context,
        ExpressionSymbolKind kind,
        string name)
    {
        var symbol = context.RootSymbols.FirstOrDefault(candidate =>
            candidate.Kind == kind && string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        return symbol is null ? null : ToItem(symbol);
    }

    private static ExpressionAuthoringContext ValueContext(ExpressionAuthoringContext context) =>
        context with
        {
            RootSymbols = context.RootSymbols
                .Where(symbol => symbol.Kind is not (ExpressionSymbolKind.Filter or ExpressionSymbolKind.Tag))
                .ToArray()
        };

    private static ExpressionToolingItem ToItem(ExpressionSymbol symbol) =>
        new(
            symbol.Name,
            symbol.Signatures?.FirstOrDefault()?.Display ?? symbol.ValueShape?.DisplayName ??
            (symbol.Kind == ExpressionSymbolKind.Tag ? "Liquid tag" : "Liquid filter"),
            symbol.Documentation,
            symbol.Name,
            symbol.Kind);

    private static string HoverContents(ExpressionToolingItem symbol) => string.Join(
        Environment.NewLine,
        new[] { symbol.Label, symbol.Detail, symbol.Documentation }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

    private static bool MatchesExpectedResult(ExpressionToolingItem symbol, ExpressionAuthoringContext context) =>
        !string.IsNullOrWhiteSpace(context.ExpectedResultType) &&
        string.Equals(symbol.Detail, context.ExpectedResultType, StringComparison.Ordinal);

    private ExpressionToolingOutcome<T>? Compatible<T>(ExpressionToolingRequestScope scope) =>
        !string.Equals(scope.Document.ExpressionType, ExpressionType, StringComparison.OrdinalIgnoreCase)
            ? ExpressionToolingOutcome<T>.Failure(ExpressionToolingOutcomeState.Incompatible, SupportedVersion, "expression-type")
            : !scope.ContractVersion.IsCompatibleWith(SupportedVersion)
                ? ExpressionToolingOutcome<T>.Failure(ExpressionToolingOutcomeState.Incompatible, SupportedVersion, "contract-version")
                : null;
}
