using Acornima;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.Core.Services;
using Elsa.Expressions.JavaScript.Core.Models;
using System.Text.RegularExpressions;

namespace Elsa.Expressions.JavaScript.Services;

/// <summary>Metadata-only JavaScript authoring assistance; it parses source but never loads Jint or evaluates it.</summary>
public sealed partial class JavaScriptExpressionToolingProvider : IExpressionToolingProvider
{
    public ExpressionToolingCapabilities DeclaredCapabilities { get; } = new(
        SupportsCompletions: true,
        SupportsHover: true,
        SupportsValidation: true,
        SupportsSymbolPaging: true,
        SupportsLazyMembers: false,
        MaximumSymbols: 500);

    public string ExpressionType => JavaScriptExpressionDescriptor.JavaScriptExpressionTypeName;
    public ExpressionToolingContractVersion SupportedVersion => ExpressionToolingContractVersion.V1;
    public ExpressionToolingCatalog? DeclaredCatalog => JavaScriptRuntimeProfile.DeclaredToolingCatalog;

    public ValueTask<ExpressionToolingOutcome<ExpressionToolingCapabilities>> GetCapabilitiesAsync(ExpressionToolingRequestScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var failure = EnsureCompatible<ExpressionToolingCapabilities>(scope);
        return ValueTask.FromResult(failure ?? ExpressionToolingOutcome<ExpressionToolingCapabilities>.Success(DeclaredCapabilities, SupportedVersion, scope.Document.DocumentRevision, scope.Context.ContextRevision));
    }

    public ValueTask<ExpressionToolingOutcome<ExpressionToolingItems>> GetCompletionsAsync(ExpressionCompletionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var incompatible = EnsureCompatible<ExpressionToolingItems>(request.Scope);
        if (incompatible is not null)
            return ValueTask.FromResult(incompatible);

        var context = ProjectContext(request.Scope.Context);
        var (source, cursor) = NormalizeEmptyAccessorCalls(request.Source, request.Cursor);
        var items = ExpressionToolingSymbolResolver.Complete(context, source, cursor)
            .OrderByDescending(symbol => MatchesExpectedResult(symbol, request.Scope.Context))
            .ThenBy(symbol => symbol.Label, StringComparer.OrdinalIgnoreCase)
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
        var incompatible = EnsureCompatible<ExpressionHover>(request.Scope);
        if (incompatible is not null)
            return ValueTask.FromResult(incompatible);

        var (source, position) = NormalizeEmptyAccessorCalls(request.Source, request.Position);
        var symbol = ExpressionToolingSymbolResolver.Resolve(ProjectContext(request.Scope.Context), source, position);
        if (symbol is null)
            return ValueTask.FromResult(ExpressionToolingOutcome<ExpressionHover>.SupportedEmpty(new ExpressionHover(string.Empty), SupportedVersion, request.Scope.Document.DocumentRevision, request.Scope.Context.ContextRevision));

        var contents = string.Join(Environment.NewLine, new[] { symbol.Label, symbol.Documentation }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return ValueTask.FromResult(ExpressionToolingOutcome<ExpressionHover>.Success(new ExpressionHover(contents), SupportedVersion, request.Scope.Document.DocumentRevision, request.Scope.Context.ContextRevision));
    }

    public ValueTask<ExpressionToolingOutcome<ExpressionDiagnosticSet>> ValidateAsync(ExpressionValidationRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var incompatible = EnsureCompatible<ExpressionDiagnosticSet>(request.Scope);
        if (incompatible is not null)
            return ValueTask.FromResult(incompatible);

        var diagnostics = ParseDiagnostics(request.Source, request.Scope.Document.DocumentRevision);
        var result = new ExpressionDiagnosticSet(diagnostics);
        return ValueTask.FromResult(diagnostics.Count == 0
            ? ExpressionToolingOutcome<ExpressionDiagnosticSet>.SupportedEmpty(result, SupportedVersion, request.Scope.Document.DocumentRevision, request.Scope.Context.ContextRevision)
            : ExpressionToolingOutcome<ExpressionDiagnosticSet>.Success(result, SupportedVersion, request.Scope.Document.DocumentRevision, request.Scope.Context.ContextRevision));
    }

    private ExpressionToolingOutcome<T>? EnsureCompatible<T>(ExpressionToolingRequestScope scope)
    {
        if (!string.Equals(scope.Document.ExpressionType, ExpressionType, StringComparison.OrdinalIgnoreCase))
            return ExpressionToolingOutcome<T>.Failure(ExpressionToolingOutcomeState.Incompatible, SupportedVersion, "expression-type");
        if (!scope.ContractVersion.IsCompatibleWith(SupportedVersion))
            return ExpressionToolingOutcome<T>.Failure(ExpressionToolingOutcomeState.Incompatible, SupportedVersion, "contract-version");
        return null;
    }

    private static bool MatchesExpectedResult(ExpressionToolingItem symbol, ExpressionAuthoringContext context) =>
        !string.IsNullOrWhiteSpace(context.ExpectedResultType) &&
        string.Equals(symbol.Detail, context.ExpectedResultType, StringComparison.Ordinal);

    private static ExpressionAuthoringContext ProjectContext(ExpressionAuthoringContext context)
    {
        var values = context.RootSymbols
            .Where(symbol => symbol.Kind is ExpressionSymbolKind.WorkflowInput or ExpressionSymbolKind.ActivityResult)
            .ToArray();
        var variables = context.RootSymbols
            .Where(symbol => symbol.Kind == ExpressionSymbolKind.Variable)
            .ToArray();
        var projected = context.RootSymbols
            .Where(symbol => symbol.Kind is ExpressionSymbolKind.Function or ExpressionSymbolKind.Namespace or ExpressionSymbolKind.Extension)
            .ToList();
        projected.Add(new(
            "javascript:args",
            JavaScriptRuntimeProfile.ArgumentsName,
            ExpressionSymbolKind.Namespace,
            new("Readonly arguments", ExpressionValueKind.Object, false, Members: values
                .Select(symbol => new ExpressionValueMember(
                    symbol.Name,
                    symbol.ValueShape ?? new(),
                    symbol.Documentation))
                .ToArray()),
            JavaScriptRuntimeProfile.ArgumentsDocumentation));
        if (variables.Length > 0)
        {
            projected.Add(new(
                "javascript:variables",
                JavaScriptRuntimeProfile.VariablesName,
                ExpressionSymbolKind.Namespace,
                new("Readonly variables", ExpressionValueKind.Object, false, Members: variables
                    .Select(symbol => new ExpressionValueMember(
                        symbol.Name,
                        symbol.ValueShape ?? new(),
                        symbol.Documentation))
                    .ToArray()),
                JavaScriptRuntimeProfile.VariablesDocumentation));
            projected.Add(JavaScriptRuntimeProfile.GetVariableFunction);
            var getterNames = new HashSet<string>(JavaScriptRuntimeProfile.ReservedVariableGetterNames, StringComparer.Ordinal);
            foreach (var variable in variables.OrderBy(symbol => symbol.Name, StringComparer.Ordinal))
            {
                var getterName = JavaScriptRuntimeProfile.GetGeneratedVariableGetterName(variable.Name);
                if (getterName is null || !getterNames.Add(getterName))
                    continue;

                projected.Add(new(
                    $"javascript:getter:{variable.SymbolId}",
                    getterName,
                    ExpressionSymbolKind.Function,
                    variable.ValueShape,
                    $"Reads the '{variable.Name}' workflow variable.",
                    [new($"{getterName}()", ReturnShape: variable.ValueShape)]));
            }
        }
        return context with { RootSymbols = projected };
    }

    private static (string Source, ExpressionToolingPosition Position) NormalizeEmptyAccessorCalls(
        string source,
        ExpressionToolingPosition position)
    {
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var lineIndex = Math.Clamp(position.Line, 0, Math.Max(0, lines.Length - 1));
        var line = lines[lineIndex];
        var character = Math.Clamp(position.Character, 0, line.Length);
        var prefix = line[..character];
        var normalizedPrefix = EmptyAccessorCallPattern().Replace(prefix, string.Empty);
        var normalizedLine = normalizedPrefix + EmptyAccessorCallPattern().Replace(line[character..], string.Empty);
        return (normalizedLine, new(0, normalizedPrefix.Length));
    }

    [GeneratedRegex(@"(?<=[A-Za-z0-9_$])\(\)(?=\.)", RegexOptions.CultureInvariant)]
    private static partial Regex EmptyAccessorCallPattern();

    private static IReadOnlyList<ExpressionDiagnostic> ParseDiagnostics(string source, string revision)
    {
        try
        {
            // Runtime evaluates the authored source as a parenthesized strict-mode expression.
            // Parse in the same grammar so the consequential-operation gate cannot approve a
            // statement body that Jint will reject (or reject a valid anonymous expression).
            var expression = new Parser().ParseExpression(source, strict: true);
            return FindAmbientCapabilityDiagnostics(expression, source, revision);
        }
        catch (ParseErrorException exception)
        {
            var line = Math.Max(0, exception.LineNumber - 1);
            var column = Math.Max(0, exception.Column);
            return
            [
                new(
                    "JavaScript/Syntax",
                    ExpressionDiagnosticSeverity.Error,
                    exception.Message,
                    revision,
                    new(
                        new(line, column),
                        new(line, column + 1)))
            ];
        }
    }
}
