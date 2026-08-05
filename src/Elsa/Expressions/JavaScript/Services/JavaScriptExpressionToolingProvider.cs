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
        if (values.Length > 0)
        {
            projected.Add(new(
                "javascript:args",
                "args",
                ExpressionSymbolKind.Namespace,
                new("Readonly arguments", ExpressionValueKind.Object, false, Members: values
                    .Select(symbol => new ExpressionValueMember(
                        symbol.Name,
                        symbol.ValueShape ?? new(),
                        symbol.Documentation))
                    .ToArray()),
                "Immutable expression parameters."));
        }
        if (variables.Length > 0)
        {
            projected.Add(new(
                "javascript:variables",
                "variables",
                ExpressionSymbolKind.Namespace,
                new("Readonly variables", ExpressionValueKind.Object, false, Members: variables
                    .Select(symbol => new ExpressionValueMember(
                        symbol.Name,
                        symbol.ValueShape ?? new(),
                        symbol.Documentation))
                    .ToArray()),
                "Immutable visible workflow variables."));
            projected.Add(new(
                "javascript:getVariable",
                "getVariable",
                ExpressionSymbolKind.Function,
                Documentation: "Reads a visible workflow variable by name.",
                Signatures:
                [
                    new("getVariable(name)", ["name"], new("Any"))
                ]));
            projected.AddRange(variables
                .Where(symbol => IsIdentifier(symbol.Name))
                .Select(symbol => new ExpressionSymbol(
                    $"javascript:getter:{symbol.SymbolId}",
                    $"get{char.ToUpperInvariant(symbol.Name[0])}{symbol.Name[1..]}",
                    ExpressionSymbolKind.Function,
                    symbol.ValueShape,
                    $"Reads the '{symbol.Name}' workflow variable.",
                    [new($"get{char.ToUpperInvariant(symbol.Name[0])}{symbol.Name[1..]}()", ReturnShape: symbol.ValueShape)])));
        }
        return context with { RootSymbols = projected };
    }

    private static bool IsIdentifier(string value) =>
        value.Length > 0 &&
        (char.IsLetter(value[0]) || value[0] is '_' or '$') &&
        value.Skip(1).All(character => char.IsLetterOrDigit(character) || character is '_' or '$');

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
            new Parser().ParseExpression(source, strict: true);
            return [];
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

    internal static IReadOnlyList<ExpressionDiagnostic> FindUnclosedDelimiters(string source, string revision, string code)
    {
        var pairs = new Dictionary<char, char> { ['('] = ')', ['['] = ']', ['{'] = '}' };
        var stack = new Stack<(char Character, int Offset)>();
        var lexicalState = JavaScriptLexicalState.Code;
        var escaped = false;
        for (var offset = 0; offset < source.Length; offset++)
        {
            var character = source[offset];
            var next = offset + 1 < source.Length ? source[offset + 1] : '\0';
            if (lexicalState == JavaScriptLexicalState.LineComment)
            {
                if (character == '\n') lexicalState = JavaScriptLexicalState.Code;
                continue;
            }
            if (lexicalState == JavaScriptLexicalState.BlockComment)
            {
                if (character == '*' && next == '/')
                {
                    lexicalState = JavaScriptLexicalState.Code;
                    offset++;
                }
                continue;
            }
            if (lexicalState != JavaScriptLexicalState.Code)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }
                if (character == '\\')
                {
                    escaped = true;
                    continue;
                }
                if (character == QuoteFor(lexicalState))
                    lexicalState = JavaScriptLexicalState.Code;
                continue;
            }

            if (character == '/' && next == '/')
            {
                lexicalState = JavaScriptLexicalState.LineComment;
                offset++;
            }
            else if (character == '/' && next == '*')
            {
                lexicalState = JavaScriptLexicalState.BlockComment;
                offset++;
            }
            else if (character == '\'')
                lexicalState = JavaScriptLexicalState.SingleQuotedString;
            else if (character == '"')
                lexicalState = JavaScriptLexicalState.DoubleQuotedString;
            else if (character == '`')
                lexicalState = JavaScriptLexicalState.TemplateString;
            else if (pairs.ContainsKey(character))
                stack.Push((character, offset));
            else if (pairs.ContainsValue(character) &&
                     (stack.Count == 0 || pairs[stack.Peek().Character] != character))
                return [Diagnostic(code, "Unexpected closing delimiter.", revision, source, offset)];
            else if (pairs.ContainsValue(character))
                stack.Pop();
        }

        if (lexicalState is JavaScriptLexicalState.SingleQuotedString or JavaScriptLexicalState.DoubleQuotedString or JavaScriptLexicalState.TemplateString)
            return [Diagnostic(code, "Unclosed string literal.", revision, source, Math.Max(0, source.Length - 1))];
        if (lexicalState == JavaScriptLexicalState.BlockComment)
            return [Diagnostic(code, "Unclosed block comment.", revision, source, Math.Max(0, source.Length - 1))];
        return stack.Count == 0
            ? []
            : [Diagnostic(code, "Unclosed delimiter.", revision, source, stack.Peek().Offset)];
    }

    private static char QuoteFor(JavaScriptLexicalState state) => state switch
    {
        JavaScriptLexicalState.SingleQuotedString => '\'',
        JavaScriptLexicalState.DoubleQuotedString => '"',
        JavaScriptLexicalState.TemplateString => '`',
        _ => '\0'
    };

    private static ExpressionDiagnostic Diagnostic(string code, string message, string revision, string source, int offset)
    {
        var position = ToPosition(source, offset);
        return new(code, ExpressionDiagnosticSeverity.Error, message, revision, new ExpressionToolingRange(position, position with { Character = position.Character + 1 }));
    }

    private static ExpressionToolingPosition ToPosition(string source, int offset)
    {
        var line = 0;
        var character = 0;
        for (var index = 0; index < Math.Min(offset, source.Length); index++)
        {
            if (source[index] == '\n')
            {
                line++;
                character = 0;
            }
            else
                character++;
        }
        return new(line, character);
    }

    private enum JavaScriptLexicalState { Code, SingleQuotedString, DoubleQuotedString, TemplateString, LineComment, BlockComment }
}
