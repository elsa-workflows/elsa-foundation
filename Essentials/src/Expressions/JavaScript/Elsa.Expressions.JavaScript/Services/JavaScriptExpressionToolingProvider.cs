using Acornima;
using Acornima.Ast;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.Core.Services;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Models;
using System.Text.RegularExpressions;

namespace Elsa.Expressions.JavaScript.Services;

/// <summary>Metadata-only JavaScript authoring assistance; it parses source but never loads Jint or evaluates it.</summary>
internal sealed partial class JavaScriptExpressionToolingProvider(
    IJavaScriptDotNetUsageAnalyzer? dotNetUsage = null,
    IEnumerable<JavaScriptHostFunctionDeclaration>? hostFunctions = null) : IExpressionToolingProvider
{
    /// <summary>What each built-in takes and returns, for completions and hover (spec 1223 FR-006, FR-007, FR-026).</summary>
    private static readonly (string Name, string[] Parameters, string Documentation)[] BuiltInSignatures =
    [
        (JavaScriptBuiltInFunctions.NewGuid, [], "A new GUID, as its 36-character text."),
        (JavaScriptBuiltInFunctions.NewGuidString, [], "A new GUID, as its 36-character text."),
        (JavaScriptBuiltInFunctions.GetGuidString, [], "A new GUID, as its 36-character text."),
        (JavaScriptBuiltInFunctions.NewShortGuid, [], "A new GUID in Elsa 3's short form: Base64 of its bytes without '/', '+' and '=' (at most 22 characters)."),
        (JavaScriptBuiltInFunctions.GetShortGuid, [], "A new GUID in Elsa 3's short form: Base64 of its bytes without '/', '+' and '=' (at most 22 characters)."),
        (JavaScriptBuiltInFunctions.ParseGuid, ["text"], "The 36-character text of the GUID that text denotes, in any form .NET's Guid.Parse accepts; other text fails."),
        (JavaScriptBuiltInFunctions.StringToBase64, ["text"], "The standard Base64 of the text's UTF-8 bytes."),
        (JavaScriptBuiltInFunctions.StringFromBase64, ["base64"], "The UTF-8 text of standard Base64; invalid Base64 fails."),
    ];

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

    private static bool MatchesExpectedResult(ExpressionToolingItem symbol, ExpressionAuthoringContext context) =>
        !string.IsNullOrWhiteSpace(context.ExpectedResultType) &&
        string.Equals(symbol.Detail, context.ExpectedResultType, StringComparison.Ordinal);

    private ExpressionAuthoringContext ProjectContext(ExpressionAuthoringContext context)
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
            // No generated get<Name>() accessors: the runtime does not provide them (spec 1222 FR-036, ADR 0089 decision 6).
        }

        // The built-in functions and the registered function namespaces, on every JavaScript surface (spec 1223 FR-003).
        foreach (var (name, parameters, documentation) in BuiltInSignatures)
        {
            projected.Add(new(
                $"javascript:{name}",
                name,
                ExpressionSymbolKind.Function,
                Documentation: documentation,
                Signatures: [new($"{name}({string.Join(", ", parameters)})", parameters, new("String"))]));
        }

        foreach (var space in (hostFunctions ?? [])
                     .Select(function => function.Name)
                     .Where(name => name.Contains('.', StringComparison.Ordinal))
                     .GroupBy(name => name[..name.IndexOf('.', StringComparison.Ordinal)], StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            projected.Add(new(
                $"javascript:{space.Key}",
                space.Key,
                ExpressionSymbolKind.Namespace,
                new("Functions", ExpressionValueKind.Object, false, Members: space
                    .Select(name => new ExpressionValueMember(name[(space.Key.Length + 1)..], new(), $"Host function {name}."))
                    .ToArray()),
                $"Functions the host registered under '{space.Key}'."));
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

    private IReadOnlyList<ExpressionDiagnostic> ParseDiagnostics(string source, string revision)
    {
        try
        {
            // Share grammar selection with runtime without evaluating authored code.
            Node parsed = JavaScriptBindingSyntax.IsExpression(source)
                ? new Parser().ParseExpression(source, strict: true)
                : new Parser(new ParserOptions { AllowReturnOutsideFunction = true }).ParseScript(source, strict: true);
            return ReservedNameDiagnostics(parsed, revision).Concat(DotNetUsageDiagnostics(source, revision)).ToArray();
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

    /// <summary>
    /// Live .NET objects and types are not available under the default JSON-only strategy (spec 1223 FR-017 to FR-019):
    /// a .NET type reference or a PascalCase invocation on a value the expression did not define is refused at design
    /// time and publish.
    /// </summary>
    private IEnumerable<ExpressionDiagnostic> DotNetUsageDiagnostics(string source, string revision) =>
        (dotNetUsage?.Analyze(source) ?? [])
            .Select(usage => new ExpressionDiagnostic(
                "JavaScript/DotNetMemberUsage",
                ExpressionDiagnosticSeverity.Error,
                usage.Message,
                revision,
                usage.Range));

    /// <summary>
    /// <c>complete</c> is a reserved built-in name in every JavaScript surface (spec 1208 FR-007a, ADR 0045 §5): a binding
    /// that declares it, as a variable, function, class, parameter or catch binding, is refused at design time.
    /// </summary>
    private static IReadOnlyList<ExpressionDiagnostic> ReservedNameDiagnostics(Node root, string revision) =>
        JavaScriptReservedNames.CollectCompleteDeclarations(root)
            .Select(identifier => new ExpressionDiagnostic(
                "JavaScript/ReservedName",
                ExpressionDiagnosticSeverity.Error,
                $"'{JavaScriptScriptBuiltIns.Complete}' is a reserved built-in name and cannot be declared in an expression. Rename it.",
                revision,
                JavaScriptReservedNames.RangeOf(identifier)))
            .ToArray();
}
