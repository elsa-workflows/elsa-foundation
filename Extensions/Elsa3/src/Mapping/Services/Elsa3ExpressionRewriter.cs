using System.Collections;
using System.Reflection;
using System.Text.Json;
using Acornima;
using Acornima.Ast;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa3.Mapping.Internal;
using Fluid;

namespace Elsa3.Mapping.Services;

/// <summary>
/// Rewrites the narrowly supported Elsa 3 ambient expression surface into a portable expression.
/// All semantic decisions are made from a language syntax tree. Source edits use parser-provided
/// JavaScript ranges or a Liquid-aware token scanner, never regular expressions.
/// </summary>
public sealed class Elsa3ExpressionRewriter
{
    /// <summary>
    /// What to change in the Elsa 3 workflow when an expression assigns a value: Elsa 4 expressions are pure. In Elsa 3
    /// terms, because the engineer edits the Elsa 3 workflow; the reusable caveat is spec 246 D-7.
    /// </summary>
    private const string MutationGuidance =
        "Move the assignment into a Set Variable activity in the Elsa 3 workflow. In a reusable workflow, Elsa 4 cannot set variables yet (ELS3-ACTIVITY-REUSABLE-INTRINSIC-UNSUPPORTED).";

    public Elsa3ExpressionRewriteResult Rewrite(
        string language,
        string source,
        TypeReference resultType,
        Func<Elsa3AmbientReference, Elsa3ExpressionParameterResolution> resolve) =>
        Rewrite(language, source, resultType, resolve, Elsa3JavaScriptMode.Expression);

    /// <param name="mode">
    /// <see cref="Elsa3JavaScriptMode.Expression"/>: the result is one expression, as Elsa 4 evaluates it; a statement
    /// form is wrapped in a function returning its completion value (plan D-15). Every value that leaves it ends in
    /// <c>?? null</c>, because Elsa 3 returned null where Elsa 4 refuses undefined (spec 261).
    /// <see cref="Elsa3JavaScriptMode.Script"/>: a RunJavaScript body, whose completion value becomes a <c>return</c>
    /// (plan D-16).
    /// </param>
    public Elsa3ExpressionRewriteResult Rewrite(
        string language,
        string source,
        TypeReference resultType,
        Func<Elsa3AmbientReference, Elsa3ExpressionParameterResolution> resolve,
        Elsa3JavaScriptMode mode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(resultType);
        ArgumentNullException.ThrowIfNull(resolve);

        return language switch
        {
            var value when value.Equals("JavaScript", StringComparison.OrdinalIgnoreCase) =>
                CheckStrictMode(RewriteJavaScript(source, resultType, resolve, mode), mode),
            var value when value.Equals("Liquid", StringComparison.OrdinalIgnoreCase) =>
                RewriteLiquid(source, resultType, resolve),
            _ => Elsa3ExpressionRewriteResult.Failure(new Elsa3ExpressionRewriteIssue(
                Elsa3ImportDiagnosticCodes.UnsupportedShape,
                $"Expression language '{language}' has no registered Elsa 3 lowering provider.",
                "Register a syntax-tree based importer lowering provider or replace the expression before import.")),
        };
    }

    private static Elsa3ExpressionRewriteResult RewriteJavaScript(
        string source,
        TypeReference resultType,
        Func<Elsa3AmbientReference, Elsa3ExpressionParameterResolution> resolve,
        Elsa3JavaScriptMode mode)
    {
        Script script;
        try
        {
            // Elsa 3 parsed with return allowed outside a function, so `return x` at the top level is its result.
            script = new Parser(new ParserOptions { AllowReturnOutsideFunction = true }).ParseScript(source);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return Elsa3ExpressionRewriteResult.Failure(new Elsa3ExpressionRewriteIssue(
                Elsa3ImportDiagnosticCodes.UnsupportedShape,
                $"JavaScript could not be parsed for safe lowering: {exception.Message}",
                "Correct the JavaScript or replace it with a portable expression before import."));
        }

        var analyzer = new JavaScriptAnalyzer(resolve);
        analyzer.Visit(script);
        if (analyzer.Issues.Count > 0)
            return Elsa3ExpressionRewriteResult.Failure(analyzer.Issues);

        // Elsa 3 evaluated source as a statement script whose completion value is the result. Elsa 4 evaluates an
        // expression as one parenthesized expression and a script as a function body that must return.
        var edits = analyzer.Edits.ToList();
        var body = script.Body;
        var prelude = analyzer.Prelude;
        var topLevelReturns = new List<ReturnStatement>();
        CollectTopLevelReturns(script, topLevelReturns);
        if (topLevelReturns.Count > 0)
        {
            // Elsa 3 turned a bare `return;` (undefined) into null; Elsa 4 refuses an undefined result.
            foreach (var bare in topLevelReturns.Where(statement => statement.Argument is null))
                edits.Add(new SourceEdit(bare.Start + "return".Length, bare.Start + "return".Length, " null"));

            // An expression's returned value becomes null when it is undefined (spec 261 R-1).
            if (mode == Elsa3JavaScriptMode.Expression)
            {
                foreach (var returned in topLevelReturns.Where(statement => statement.Argument is not null))
                    AddNullCoalescing(edits, returned.Argument!.Start, returned.Argument.End);
            }

            // Every path ends in a return Elsa 3 would have returned: a trailing return keeps its value, a trailing
            // expression is Elsa 3's completion value and gets a return. Any other trailing statement could carry a
            // completion value that a return cannot reproduce (spec 247 research R-1).
            // A script's lost value only matters when its result is read, which the caller decides (as below).
            var preserved = body[^1] is ExpressionStatement or ReturnStatement;
            if (body[^1] is ExpressionStatement trailing)
            {
                if (mode == Elsa3JavaScriptMode.Expression)
                    AddNullCoalescing(edits, trailing.Start, ExpressionEnd(source, trailing));
                edits.Add(new SourceEdit(trailing.Start, trailing.Start, "return "));
            }

            if (mode == Elsa3JavaScriptMode.Script)
            {
                return Elsa3ExpressionRewriteResult.Success(
                    CreateDefinition("JavaScript", prelude + ApplyEdits(source, edits), resultType, analyzer.Parameters),
                    completionValueLost: !preserved);
            }

            if (!preserved)
            {
                return Elsa3ExpressionRewriteResult.Failure(DynamicIssue(
                    "JavaScript returns at the top level but ends with a statement that is neither a return nor an expression, so its Elsa 3 result cannot be preserved as one Elsa 4 expression."));
            }

            return Elsa3ExpressionRewriteResult.Success(CreateDefinition(
                "JavaScript", $"(function () {{\n{prelude}{ApplyEdits(source, edits)}\n}})()", resultType, analyzer.Parameters));
        }

        if (mode == Elsa3JavaScriptMode.Script)
        {
            var hasCompletion = body.Count > 0 && body[^1] is ExpressionStatement;
            if (hasCompletion)
                edits.Add(new SourceEdit(body[^1].Start, body[^1].Start, "return "));
            return Elsa3ExpressionRewriteResult.Success(
                CreateDefinition("JavaScript", prelude + ApplyEdits(source, edits), resultType, analyzer.Parameters),
                completionValueLost: body.Count > 0 && !hasCompletion);
        }

        if (body.Count == 0)
        {
            return Elsa3ExpressionRewriteResult.Failure(new Elsa3ExpressionRewriteIssue(
                Elsa3ImportDiagnosticCodes.UnsupportedShape,
                "JavaScript has no expression.",
                "Give the expression a value before import."));
        }

        if (body[^1] is not ExpressionStatement last)
        {
            return Elsa3ExpressionRewriteResult.Failure(DynamicIssue(
                "JavaScript ends with a statement rather than an expression, so its Elsa 3 completion value cannot be preserved as one Elsa 4 expression."));
        }

        if (body.Count == 1 && prelude.Length == 0)
        {
            // One expression, evaluated by Elsa 4 inside parentheses: drop its statement semicolon, and end with a
            // newline when a line comment trails, so the comment cannot swallow the closing parenthesis. Block
            // comments are harmless and kept.
            var end = ExpressionEnd(source, last);
            if (last.End > end)
                edits.Add(new SourceEdit(end, last.End, string.Empty));
            AddNullCoalescing(edits, last.Start, end);
            if (source.AsSpan(last.End).Contains("//", StringComparison.Ordinal))
                edits.Add(new SourceEdit(source.Length, source.Length, "\n"));
            return Elsa3ExpressionRewriteResult.Success(CreateDefinition("JavaScript", ApplyEdits(source, edits), resultType, analyzer.Parameters));
        }

        // Statements: an immediately invoked function that returns the last expression, Elsa 3's completion value.
        AddNullCoalescing(edits, last.Start, ExpressionEnd(source, last));
        edits.Add(new SourceEdit(last.Start, last.Start, "return "));
        var wrapped = $"(function () {{\n{prelude}{ApplyEdits(source, edits)}\n}})()";
        return Elsa3ExpressionRewriteResult.Success(CreateDefinition("JavaScript", wrapped, resultType, analyzer.Parameters));
    }

    /// <summary>
    /// Parses a converted source the way Elsa 4 will evaluate it: strict mode, as one expression in Expression mode and
    /// inside the RunJavaScript arrow wrapper in Script mode (spec 261 R-3). Elsa 3 ran sloppy mode, so what fails here
    /// cannot run in Elsa 4 and is refused. An Expression-mode source that is no longer an expression is refused too,
    /// because Elsa 4 would then run it as statements.
    /// </summary>
    private static Elsa3ExpressionRewriteResult CheckStrictMode(Elsa3ExpressionRewriteResult result, Elsa3JavaScriptMode mode)
    {
        if (!result.Succeeded)
            return result;

        var source = result.Definition!.Source;
        try
        {
            if (mode == Elsa3JavaScriptMode.Expression)
                new Parser().ParseExpression(source, strict: true);
            else
                new Parser(new ParserOptions { AllowReturnOutsideFunction = true }).ParseScript($"(() => {{ {source} }})()", strict: true);
            return result;
        }
        catch (ParseErrorException exception)
        {
            return Elsa3ExpressionRewriteResult.Failure(new Elsa3ExpressionRewriteIssue(
                Elsa3ImportDiagnosticCodes.UnsupportedShape,
                $"JavaScript only works in Elsa 3's sloppy mode; Elsa 4 runs it in strict mode, where it does not parse: {exception.Error.Description}.",
                "Rewrite it in the Elsa 3 workflow without sloppy-mode syntax: no with statement, no legacy octal literal such as 010 (write 0o10 or 8), no duplicate parameter names, no delete of a variable, and no reserved word such as let, static or yield as a name, before import."));
        }
    }

    private static Elsa3ExpressionRewriteResult RewriteLiquid(
        string source,
        TypeReference resultType,
        Func<Elsa3AmbientReference, Elsa3ExpressionParameterResolution> resolve)
    {
        var parser = new FluidParser();
        if (!parser.TryParse(source, out var template, out var error) || template is null)
        {
            return Elsa3ExpressionRewriteResult.Failure(new Elsa3ExpressionRewriteIssue(
                Elsa3ImportDiagnosticCodes.UnsupportedShape,
                $"Liquid could not be parsed for safe lowering: {error}",
                "Correct the Liquid template or replace it with a portable expression before import."));
        }

        var parameters = new Dictionary<string, ExpressionParameterBinding>(StringComparer.Ordinal);
        var issues = new List<Elsa3ExpressionRewriteIssue>();
        var requestedEdits = new List<LiquidRootEdit>();

        var syntax = EnumerateFluidSyntax(template).ToArray();
        if (syntax.Any(node => node.GetType().Name.Contains("Assign", StringComparison.OrdinalIgnoreCase) ||
                               node.GetType().Name.Contains("Capture", StringComparison.OrdinalIgnoreCase) ||
                               node.GetType().Name.Contains("Increment", StringComparison.OrdinalIgnoreCase) ||
                               node.GetType().Name.Contains("Decrement", StringComparison.OrdinalIgnoreCase)))
        {
            return Elsa3ExpressionRewriteResult.Failure(new Elsa3ExpressionRewriteIssue(
                Elsa3ImportDiagnosticCodes.ForbiddenCapability,
                "Liquid template performs assignment or mutation.",
                MutationGuidance));
        }

        foreach (var member in syntax.OfType<Fluid.Ast.MemberExpression>())
        {
            var segments = member.Segments;
            if (segments.Count == 0 || segments[0] is not Fluid.Ast.IdentifierSegment root)
                continue;

            var kind = root.Identifier switch
            {
                "Variables" => Elsa3AmbientReferenceKind.Variable,
                "Input" or "Inputs" => Elsa3AmbientReferenceKind.WorkflowRequest,
                "Output" or "Outputs" => Elsa3AmbientReferenceKind.ActivityResult,
                _ => (Elsa3AmbientReferenceKind?)null,
            };
            if (kind is null)
                continue;

            if (segments.Count < 2 || segments[1] is not Fluid.Ast.IdentifierSegment memberSegment)
            {
                issues.Add(DynamicIssue($"Liquid ambient root '{root.Identifier}' uses a computed or incomplete accessor."));
                continue;
            }

            var resolution = resolve(new Elsa3AmbientReference(kind.Value, memberSegment.Identifier));
            if (!resolution.Succeeded)
            {
                issues.Add(resolution.Issue!);
                continue;
            }

            if (!TryAddParameter(parameters, memberSegment.Identifier, resolution.Binding!, out var collision))
            {
                issues.Add(collision!);
                continue;
            }

            requestedEdits.Add(new LiquidRootEdit(root.Identifier, memberSegment.Identifier));
        }

        if (issues.Count > 0)
            return Elsa3ExpressionRewriteResult.Failure(issues);

        var scan = LiquidTokenScanner.Scan(source);
        var edits = new List<SourceEdit>();
        var used = new HashSet<int>();
        foreach (var requested in requestedEdits)
        {
            var match = scan.FirstOrDefault(candidate =>
                !used.Contains(candidate.Start) &&
                candidate.Root.Equals(requested.Root, StringComparison.Ordinal) &&
                candidate.Member.Equals(requested.Member, StringComparison.Ordinal));
            if (match is null)
            {
                issues.Add(new Elsa3ExpressionRewriteIssue(
                    Elsa3ImportDiagnosticCodes.UnsupportedShape,
                    $"Liquid syntax tree member '{requested.Root}.{requested.Member}' could not be correlated to a source token.",
                    "Simplify the Liquid expression or replace it with a portable expression before import."));
                continue;
            }

            used.Add(match.Start);
            edits.Add(new SourceEdit(match.Start, match.MemberStart, string.Empty));
        }

        if (issues.Count > 0)
            return Elsa3ExpressionRewriteResult.Failure(issues);

        return Elsa3ExpressionRewriteResult.Success(
            CreateDefinition("Liquid", ApplyEdits(source, edits), resultType, parameters));
    }

    /// <summary>The <c>return</c> statements outside every function, which Elsa 3 allowed and evaluated as the result.</summary>
    private static void CollectTopLevelReturns(Node node, List<ReturnStatement> returns)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child is IFunction)
                continue;
            if (child is ReturnStatement statement)
                returns.Add(statement);
            CollectTopLevelReturns(child, returns);
        }
    }

    /// <summary>
    /// Makes the value of <c>source[start..end]</c> null when it is undefined, as Elsa 3 did (spec 261 R-1). The parentheses
    /// keep the expression one unit, so <c>a || b</c> and <c>a, b</c> stay valid. Edits are applied in descending start
    /// order and, at one offset, in the order added: a replacement added earlier (an analyzer edit) keeps the opening
    /// parenthesis in front of it, and an insertion added later at the same offset (<c>return </c>) lands before it.
    /// </summary>
    private static void AddNullCoalescing(List<SourceEdit> edits, int start, int end)
    {
        edits.Add(new SourceEdit(start, start, "("));
        edits.Add(new SourceEdit(end, end, ") ?? null"));
    }

    /// <summary>
    /// Where an expression statement's expression ends in the source: before its semicolon, if it has one. The parser's
    /// expression range leaves out enclosing parentheses, so <c>({ a: 1 })</c> must be cut by the statement, not by it.
    /// </summary>
    private static int ExpressionEnd(string source, ExpressionStatement statement) =>
        statement.End > statement.Expression.End && source[statement.End - 1] == ';' ? statement.End - 1 : statement.End;

    private static ExpressionDefinition CreateDefinition(
        string language,
        string source,
        TypeReference resultType,
        IReadOnlyDictionary<string, ExpressionParameterBinding> parameters) =>
        new(
            language,
            source,
            resultType,
            parameters,
            JsonSerializer.SerializeToElement(new { }),
            ExpressionCapabilityProfiles.BindingPureV1);

    private static bool TryAddParameter(
        IDictionary<string, ExpressionParameterBinding> parameters,
        string name,
        ExpressionParameterBinding binding,
        out Elsa3ExpressionRewriteIssue? issue)
    {
        if (!parameters.TryGetValue(name, out var existing))
        {
            parameters.Add(name, binding);
            issue = null;
            return true;
        }

        if (Equals(existing, binding))
        {
            issue = null;
            return true;
        }

        issue = new Elsa3ExpressionRewriteIssue(
            Elsa3ImportDiagnosticCodes.AmbiguousReference,
            $"Expression parameter '{name}' resolves to more than one value source.",
            "Rename or split the ambient reads so that every portable parameter has one unambiguous source.");
        return false;
    }

    private static Elsa3ExpressionRewriteIssue DynamicIssue(string message) => new(
        Elsa3ImportDiagnosticCodes.DynamicAccessor,
        message,
        "Replace computed ambient access with a statically named workflow request, variable, or activity result.");

    private static string ApplyEdits(string source, IEnumerable<SourceEdit> edits)
    {
        var result = source;
        foreach (var edit in edits.OrderByDescending(x => x.Start))
            result = string.Concat(result.AsSpan(0, edit.Start), edit.Replacement, result.AsSpan(edit.End));
        return result;
    }

    private static IEnumerable<object> EnumerateFluidSyntax(object root)
    {
        var pending = new Stack<object>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        pending.Push(root);

        while (pending.TryPop(out var current))
        {
            if (!seen.Add(current))
                continue;
            yield return current;

            foreach (var property in current.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.GetIndexParameters().Length != 0 || property.GetMethod is null)
                    continue;
                var value = property.GetValue(current);
                if (value is null || value is string)
                    continue;

                if (value is IEnumerable values)
                {
                    foreach (var item in values)
                    {
                        if (item is not null && IsFluidSyntax(item.GetType()))
                            pending.Push(item);
                    }
                }
                else if (IsFluidSyntax(value.GetType()))
                    pending.Push(value);
            }
        }
    }

    private static bool IsFluidSyntax(Type type) =>
        type.Namespace?.StartsWith("Fluid.Ast", StringComparison.Ordinal) == true ||
        type.FullName?.Equals("Fluid.Parser.FluidTemplate", StringComparison.Ordinal) == true;

    private sealed class JavaScriptAnalyzer(
        Func<Elsa3AmbientReference, Elsa3ExpressionParameterResolution> resolve)
    {
        private static readonly HashSet<string> SetterCalls = new(StringComparer.Ordinal)
        {
            "setVariable", "setOutput", "setInput", "setResult",
        };
        /// <summary>
        /// Elsa 3 string helpers with an exact Elsa 4 equivalent, and the local function each call becomes. The
        /// whitespace class is .NET's <c>char.IsWhiteSpace</c> set, which differs from JavaScript's <c>\s</c>
        /// (spec 247 research R-6).
        /// </summary>
        private static readonly Dictionary<string, (string Name, string Declaration)> InlineHelpers = new(StringComparer.Ordinal)
        {
            ["isNullOrEmpty"] = ("__elsa3IsNullOrEmpty",
                "function __elsa3IsNullOrEmpty(v) { return v == null || v === \"\"; }"),
            ["isNullOrWhiteSpace"] = ("__elsa3IsNullOrWhiteSpace",
                "function __elsa3IsNullOrWhiteSpace(v) { return v == null || /^[\\t-\\r \\u0085\\u00A0\\u1680\\u2000-\\u200A\\u2028\\u2029\\u202F\\u205F\\u3000]*$/.test(v); }"),
        };

        private readonly SortedSet<string> _helpers = new(StringComparer.Ordinal);

        /// <summary>The local functions the rewritten source calls, one per line, or empty.</summary>
        public string Prelude => string.Concat(_helpers.Select(helper => InlineHelpers[helper].Declaration + "\n"));

        /// <summary>Elsa 3 built-ins that read instance metadata, and the parameter each one becomes.</summary>
        private static readonly Dictionary<string, (string Member, string Parameter)> InstanceMembers = new(StringComparer.Ordinal)
        {
            ["getCorrelationId"] = (WorkflowInstanceExpressionParameterBinding.Members.CorrelationId, "elsa3CorrelationId"),
            ["getWorkflowInstanceId"] = (WorkflowInstanceExpressionParameterBinding.Members.InstanceId, "elsa3WorkflowInstanceId"),
        };

        private static readonly HashSet<string> PureGlobalCalls = new(StringComparer.Ordinal)
        {
            "Boolean", "Number", "String", "parseFloat", "parseInt",
        };

        /// <summary>
        /// Globals Elsa 3 put into the engine that Elsa 4 does not have (or removes): CLR types, <c>Date</c> and the
        /// optional lodash and moment libraries. Reading or constructing them would fail at run time (plan D-14).
        /// </summary>
        private static readonly HashSet<string> ElsaThreeGlobals = new(StringComparer.Ordinal)
        {
            "Date", "DateTime", "DateTimeOffset", "TimeSpan", "Guid", "Random", "_", "moment",
        };

        private static Elsa3ExpressionRewriteIssue UnavailableGlobal(string name) => new(
            Elsa3ImportDiagnosticCodes.ForbiddenCapability,
            $"JavaScript uses '{name}', which the Elsa 4 JavaScript engine does not provide.",
            name switch
            {
                "Date" or "DateTime" or "DateTimeOffset" => TimeGuidance,
                "Guid" or "Random" => UniqueValueGuidance,
                _ => "Supply the value as a pinned input or compute it in an activity before import.",
            });

        private const string TimeGuidance =
            "Elsa 4 expressions have no clock. In the Elsa 3 workflow, replace a wait loop with a Delay activity, and pass the current time or date values in as workflow inputs, before import.";

        private const string UniqueValueGuidance =
            "Elsa 4 expressions have no random or unique values. In the Elsa 3 workflow, use getWorkflowInstanceId() when one unique value per run is enough, or pass the value in as a workflow input, before import.";

        private const string DataGuidance =
            "Elsa 4 values are JSON and expressions have no byte, stream or Base64 helpers. Move the conversion into an activity before import.";

        private const string InstanceGuidance =
            "Elsa 4 expressions can read only the correlation ID and the instance ID of a workflow instance. Pass the value in as a workflow input, or move the operation into an activity, before import.";

        private const string OutcomeGuidance =
            "Elsa 4 scripts have no outcomes. In the Elsa 3 workflow, route with a Decision or Switch activity whose conditions carry the script's logic, before import.";

        /// <summary>
        /// Elsa 3 host functions Elsa 4 cannot run the same, each with what to change in the Elsa 3 workflow
        /// (spec 247 contracts/javascript-conversion.md, "Refused"). The code stays VF-IMP-005.
        /// </summary>
        private static readonly Dictionary<string, (string Message, string Guidance)> RefusedHostFunctions = new(StringComparer.Ordinal)
        {
            ["setOutcome"] = ("JavaScript calls 'setOutcome', which sets an Elsa 3 script outcome.", OutcomeGuidance),
            ["setOutcomes"] = ("JavaScript calls 'setOutcomes', which sets Elsa 3 script outcomes.", OutcomeGuidance),
            ["newGuid"] = ("JavaScript calls 'newGuid', which generates a random value.", UniqueValueGuidance),
            ["newGuidString"] = ("JavaScript calls 'newGuidString', which generates a random value.", UniqueValueGuidance),
            ["newShortGuid"] = ("JavaScript calls 'newShortGuid', which generates a random value.", UniqueValueGuidance),
            ["getGuidString"] = ("JavaScript calls 'getGuidString', which generates a random value.", UniqueValueGuidance),
            ["getShortGuid"] = ("JavaScript calls 'getShortGuid', which generates a random value.", UniqueValueGuidance),
            ["toJson"] = ("JavaScript calls 'toJson'. Elsa 3 serialized with .NET, which escapes HTML characters and keeps .NET property casing, so no Elsa 4 form returns the same text.",
                "Use JSON.stringify(value) in the Elsa 3 workflow, or JSON.parse(JSON.stringify(value)) to copy a value, before import."),
            ["parseGuid"] = ("JavaScript calls 'parseGuid', which returns a .NET Guid that Elsa 4 values cannot carry.",
                "Keep the GUID as a string in the Elsa 3 workflow, and check its format in the script if needed, before import."),
            ["bytesToString"] = ("JavaScript calls 'bytesToString'.", DataGuidance),
            ["bytesFromString"] = ("JavaScript calls 'bytesFromString'.", DataGuidance),
            ["bytesToBase64"] = ("JavaScript calls 'bytesToBase64'.", DataGuidance),
            ["bytesFromBase64"] = ("JavaScript calls 'bytesFromBase64'.", DataGuidance),
            ["streamToBytes"] = ("JavaScript calls 'streamToBytes'.", DataGuidance),
            ["streamToBase64"] = ("JavaScript calls 'streamToBase64'.", DataGuidance),
            ["stringToBase64"] = ("JavaScript calls 'stringToBase64'.", DataGuidance),
            ["stringFromBase64"] = ("JavaScript calls 'stringFromBase64'.", DataGuidance),
            ["getConfig"] = ("JavaScript calls 'getConfig', which reads host configuration.",
                "Pass the setting in as a workflow input before import."),
            ["getWorkflowDefinitionId"] = ("JavaScript calls 'getWorkflowDefinitionId'.", InstanceGuidance),
            ["getWorkflowDefinitionVersionId"] = ("JavaScript calls 'getWorkflowDefinitionVersionId'.", InstanceGuidance),
            ["getWorkflowDefinitionVersion"] = ("JavaScript calls 'getWorkflowDefinitionVersion'.", InstanceGuidance),
            ["getWorkflowInstanceName"] = ("JavaScript calls 'getWorkflowInstanceName'.", InstanceGuidance),
            ["setWorkflowInstanceName"] = ("JavaScript calls 'setWorkflowInstanceName', which changes the workflow instance.",
                "Use a Set Name activity in the Elsa 3 workflow before import."),
            ["setCorrelationId"] = ("JavaScript calls 'setCorrelationId', which changes the workflow instance.",
                "Use a Correlate activity in the Elsa 3 workflow before import."),
            ["getLastResult"] = ("JavaScript calls 'getLastResult', which reads whichever activity ran last.",
                "Capture the producing activity's output into a variable and read that variable before import."),
        };

        /// <summary>
        /// The names declared in the scopes enclosing the node being visited, innermost last. A reference to one of them
        /// is a local value, not an Elsa 3 global. Scopes follow JavaScript: <c>var</c> and parameters belong to their
        /// function (or the script), <c>let</c>, <c>const</c>, classes and function declarations to their block, and a
        /// catch variable to its clause.
        /// </summary>
        private readonly List<HashSet<string>> _scopes = [];

        private bool IsDeclared(string name) => _scopes.Any(scope => scope.Contains(name));

        private bool IsNondeterministicMember(Node node) =>
            node is MemberExpression member && IsNondeterministicCall(member);

        private bool IsUnavailableGlobal(string name) => ElsaThreeGlobals.Contains(name) && !IsDeclared(name);

        /// <summary>
        /// The non-arrow functions (and class bodies) enclosing the node being visited, innermost last. Each owns its
        /// own <c>this</c> and <c>arguments</c>; arrow functions share their enclosing one's (spec 261 R-6).
        /// </summary>
        private readonly List<FunctionContext> _functions = [];

        public void Visit(Node node)
        {
            var scope = ScopeOf(node);
            if (scope is not null)
                _scopes.Add(scope);
            var function = node switch
            {
                IFunction and not ArrowFunctionExpression => new FunctionContext(((IFunction)node).Params),
                ClassBody => new FunctionContext([]),
                _ => null,
            };
            if (function is not null)
                _functions.Add(function);
            try
            {
                VisitNode(node);
            }
            finally
            {
                if (function is not null)
                {
                    _functions.RemoveAt(_functions.Count - 1);
                    // Elsa 3's sloppy functions kept arguments and simple parameters linked; Elsa 4's strict ones do not.
                    if (function.ReadsArguments)
                    {
                        foreach (var target in function.Writes)
                            Issues.Add(AliasedArgumentWrite(target));
                    }
                }
                if (scope is not null)
                    _scopes.RemoveAt(_scopes.Count - 1);
            }
        }

        private sealed class FunctionContext(IEnumerable<Node> parameters)
        {
            public HashSet<string> Parameters { get; } = CollectParameters(parameters);
            public bool ReadsArguments { get; set; }
            public SortedSet<string> Writes { get; } = new(StringComparer.Ordinal);

            private static HashSet<string> CollectParameters(IEnumerable<Node> parameters)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var parameter in parameters)
                    CollectBindings(parameter, names);
                return names;
            }
        }

        /// <summary>
        /// Records what Elsa 3's sloppy mode treated differently from Elsa 4's strict mode around <c>this</c> and
        /// <c>arguments</c> (spec 261 R-6): either refused here (outside every function) or noted on the enclosing
        /// function, which refuses its writes when it pops if it also read <c>arguments</c>.
        /// </summary>
        private void CheckThisAndArguments(Node node)
        {
            var function = _functions.Count > 0 ? _functions[^1] : null;
            if (node is ThisExpression && function is null)
                Issues.Add(TopLevelThis);

            if (node is Identifier { Name: "arguments" } && !IsDeclared("arguments"))
            {
                if (function is null)
                    Issues.Add(TopLevelArguments);
                else
                    function.ReadsArguments = true;
            }

            if (function is null)
                return;

            var target = node switch
            {
                AssignmentExpression assignment => assignment.Left,
                UpdateExpression update => update.Argument,
                _ => null,
            };
            if (target is MemberExpression { Object: Identifier { Name: "arguments" } })
                function.Writes.Add("arguments[…]");
            foreach (var written in WrittenNames(node).Where(function.Parameters.Contains))
                function.Writes.Add(written);
        }

        private static readonly Elsa3ExpressionRewriteIssue TopLevelThis = new(
            Elsa3ImportDiagnosticCodes.UnsupportedShape,
            "JavaScript uses 'this' outside a function. In Elsa 3 it was the script engine's global object, which Elsa 4 does not have.",
            "Read the value through getVariable or getInput, or a local variable, instead of this, before import.");

        private static readonly Elsa3ExpressionRewriteIssue TopLevelArguments = new(
            Elsa3ImportDiagnosticCodes.UnsupportedShape,
            "JavaScript uses 'arguments' outside a function. Elsa 3 failed there; Elsa 4 would return an empty value.",
            "Remove the reference to arguments in the Elsa 3 workflow before import.");

        private static Elsa3ExpressionRewriteIssue AliasedArgumentWrite(string target) => new(
            Elsa3ImportDiagnosticCodes.UnsupportedShape,
            $"JavaScript changes '{target}' in a function that reads 'arguments'. Elsa 3 kept arguments and parameters linked; Elsa 4 runs in strict mode, where they are not.",
            "Copy the value into a local let variable and change that, in the Elsa 3 workflow, before import.");

        private static HashSet<string>? ScopeOf(Node node)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            switch (node)
            {
                case Script script:
                    CollectVarDeclarations(script, names);
                    CollectLexicalDeclarations(script.Body, names);
                    break;
                case IFunction function:
                    if (function.Id is { } functionId)
                        CollectBindings(functionId, names);
                    foreach (var parameter in function.Params)
                        CollectBindings(parameter, names);
                    CollectVarDeclarations(function.Body, names);
                    break;
                case BlockStatement block:
                    CollectLexicalDeclarations(block.Body, names);
                    break;
                case SwitchStatement switchStatement:
                    CollectLexicalDeclarations(switchStatement.Cases.SelectMany(@case => @case.Consequent), names);
                    break;
                case CatchClause { Param: { } caught }:
                    CollectBindings(caught, names);
                    break;
                case ForStatement { Init: VariableDeclaration declaration }:
                    CollectLexicalDeclaration(declaration, names);
                    break;
                case ForInStatement { Left: VariableDeclaration declaration }:
                    CollectLexicalDeclaration(declaration, names);
                    break;
                case ForOfStatement { Left: VariableDeclaration declaration }:
                    CollectLexicalDeclaration(declaration, names);
                    break;
                case ClassExpression { Id: { } classId }:
                    CollectBindings(classId, names);
                    break;
                default:
                    return null;
            }

            return names;
        }

        /// <summary>The <c>var</c> declarations of a function or script body, which do not reach into nested functions.</summary>
        private static void CollectVarDeclarations(Node node, HashSet<string> names)
        {
            foreach (var child in node.ChildNodes)
            {
                if (child is IFunction)
                    continue;
                if (child is VariableDeclaration { Kind: VariableDeclarationKind.Var } declaration)
                {
                    foreach (var declarator in declaration.Declarations)
                        CollectBindings(declarator.Id, names);
                }

                CollectVarDeclarations(child, names);
            }
        }

        private static void CollectLexicalDeclarations(IEnumerable<Node> statements, HashSet<string> names)
        {
            foreach (var statement in statements)
            {
                switch (statement)
                {
                    case VariableDeclaration declaration:
                        CollectLexicalDeclaration(declaration, names);
                        break;
                    case FunctionDeclaration { Id: { } functionId }:
                        CollectBindings(functionId, names);
                        break;
                    case ClassDeclaration { Id: { } classId }:
                        CollectBindings(classId, names);
                        break;
                }
            }
        }

        private static void CollectLexicalDeclaration(VariableDeclaration declaration, HashSet<string> names)
        {
            if (declaration.Kind == VariableDeclarationKind.Var)
                return;
            foreach (var declarator in declaration.Declarations)
                CollectBindings(declarator.Id, names);
        }

        private static void CollectBindings(Node? pattern, HashSet<string> names)
        {
            switch (pattern)
            {
                case Identifier identifier:
                    names.Add(identifier.Name);
                    break;
                case ArrayPattern array:
                    foreach (var element in array.Elements)
                        CollectBindings(element, names);
                    break;
                case ObjectPattern objectPattern:
                    foreach (var property in objectPattern.Properties)
                        CollectBindings(property is Property { } bound ? bound.Value : property, names);
                    break;
                case AssignmentPattern assigned:
                    CollectBindings(assigned.Left, names);
                    break;
                case RestElement rest:
                    CollectBindings(rest.Argument, names);
                    break;
            }
        }

        public Dictionary<string, ExpressionParameterBinding> Parameters { get; } = new(StringComparer.Ordinal);
        public List<SourceEdit> Edits { get; } = [];
        public List<Elsa3ExpressionRewriteIssue> Issues { get; } = [];

        private void VisitNode(Node node)
        {
            if (node is AssignmentExpression assignment && ContainsAmbientRoot(assignment.Left) ||
                node is UpdateExpression update && ContainsAmbientRoot(update.Argument))
            {
                Issues.Add(new Elsa3ExpressionRewriteIssue(
                    Elsa3ImportDiagnosticCodes.ForbiddenCapability,
                    "JavaScript mutates an Elsa 3 ambient value.",
                    MutationGuidance));
                return;
            }

            // Elsa 3's sloppy mode turned a write to an undeclared name into a global; Elsa 4's strict mode throws
            // (spec 261 R-4). The parse cannot see it, so the scopes decide.
            foreach (var written in WrittenNames(node).Where(name => !IsDeclared(name)))
                Issues.Add(UndeclaredWrite(written));
            CheckThisAndArguments(node);

            // A function the script declares itself is a local call, not an Elsa 3 host function.
            if (node is CallExpression call && call.Callee is Identifier callee && !IsDeclared(callee.Name))
            {
                if (SetterCalls.Contains(callee.Name))
                {
                    Issues.Add(new Elsa3ExpressionRewriteIssue(
                        Elsa3ImportDiagnosticCodes.ForbiddenCapability,
                        $"JavaScript calls forbidden mutation helper '{callee.Name}'.",
                        MutationGuidance));
                    return;
                }

                var kind = callee.Name switch
                {
                    "getVariable" => Elsa3AmbientReferenceKind.Variable,
                    "getInput" => Elsa3AmbientReferenceKind.WorkflowRequest,
                    _ => (Elsa3AmbientReferenceKind?)null,
                };
                if (kind is not null)
                {
                    if (call.Arguments.Count != 1 || call.Arguments[0] is not Literal { Value: string name })
                    {
                        Issues.Add(DynamicIssue($"JavaScript helper '{callee.Name}' uses a computed or dynamic name."));
                        return;
                    }

                    AddResolvedEdit(call, kind.Value, name);
                    return;
                }

                if (callee.Name.Equals("getOutputFrom", StringComparison.Ordinal))
                {
                    Issues.Add(DynamicIssue("JavaScript helper 'getOutputFrom' cannot prove one producer statically."));
                    return;
                }

                if (InlineHelpers.TryGetValue(callee.Name, out var inline))
                {
                    if (call.Arguments.Count != 1 || call.Arguments[0] is SpreadElement)
                    {
                        Issues.Add(new Elsa3ExpressionRewriteIssue(
                            Elsa3ImportDiagnosticCodes.ForbiddenCapability,
                            $"JavaScript calls '{callee.Name}' with other than one argument.",
                            "Call it with exactly one argument before import."));
                        return;
                    }

                    if (IsDeclared(inline.Name))
                    {
                        Issues.Add(new Elsa3ExpressionRewriteIssue(
                            Elsa3ImportDiagnosticCodes.AmbiguousReference,
                            $"JavaScript declares '{inline.Name}', the name the converter gives Elsa 3's '{callee.Name}'.",
                            "Rename the declaration before import."));
                        return;
                    }

                    _helpers.Add(callee.Name);
                    Edits.Add(new SourceEdit(callee.Start, callee.End, inline.Name));
                    Visit(call.Arguments[0]);
                    return;
                }

                // Instance metadata reads become a declared WorkflowInstance parameter (spec 247 R-4).
                if (InstanceMembers.TryGetValue(callee.Name, out var instance))
                {
                    if (call.Arguments.Count != 0)
                    {
                        Issues.Add(new Elsa3ExpressionRewriteIssue(
                            Elsa3ImportDiagnosticCodes.ForbiddenCapability,
                            $"JavaScript calls '{callee.Name}' with arguments, which it does not take.",
                            "Call it without arguments before import."));
                        return;
                    }

                    // Elsa 3 registered built-ins and generated accessors in an order its source does not fix, so a
                    // variable or input whose accessor has the built-in's name leaves the call ambiguous (R-3).
                    if (resolve(new Elsa3AmbientReference(Elsa3AmbientReferenceKind.GeneratedAccessor, callee.Name["get".Length..])).Succeeded)
                    {
                        Issues.Add(new Elsa3ExpressionRewriteIssue(
                            Elsa3ImportDiagnosticCodes.AmbiguousReference,
                            $"JavaScript calls '{callee.Name}', which is both an Elsa 3 built-in and the generated accessor of a variable or input.",
                            $"Rename the variable or input so that '{callee.Name}' only means the built-in, before import."));
                        return;
                    }

                    var binding = new WorkflowInstanceExpressionParameterBinding(instance.Member);
                    if (!TryAddParameter(Parameters, instance.Parameter, binding, out var collision))
                    {
                        Issues.Add(collision!);
                        return;
                    }

                    Edits.Add(new SourceEdit(call.Start, call.End, $"args.{instance.Parameter}"));
                    return;
                }

                // Elsa 3 generated get{Name}() per variable and workflow input; the resolver decides which (spec 247 R-2).
                if (TryGetAccessorName(callee.Name, "get", out var accessor) &&
                    (call.Arguments.Count == 0 ||
                     resolve(new Elsa3AmbientReference(Elsa3AmbientReferenceKind.GeneratedAccessor, accessor)).Succeeded))
                {
                    if (call.Arguments.Count != 0)
                    {
                        Issues.Add(new Elsa3ExpressionRewriteIssue(
                            Elsa3ImportDiagnosticCodes.ForbiddenCapability,
                            $"JavaScript calls '{callee.Name}' with arguments, which no Elsa 3 generated accessor takes.",
                            "Call the accessor without arguments before import."));
                        return;
                    }

                    AddResolvedEdit(call, Elsa3AmbientReferenceKind.GeneratedAccessor, accessor);
                    return;
                }

                if (TryGetAccessorName(callee.Name, "set", out var setter) &&
                    resolve(new Elsa3AmbientReference(Elsa3AmbientReferenceKind.GeneratedAccessor, setter)).Succeeded)
                {
                    Issues.Add(new Elsa3ExpressionRewriteIssue(
                        Elsa3ImportDiagnosticCodes.ForbiddenCapability,
                        $"JavaScript calls generated setter '{callee.Name}', which mutates workflow state.",
                        "Use a Set Variable activity instead of the setter before import."));
                    return;
                }

                if (RefusedHostFunctions.TryGetValue(callee.Name, out var refused))
                {
                    Issues.Add(new Elsa3ExpressionRewriteIssue(Elsa3ImportDiagnosticCodes.ForbiddenCapability, refused.Message, refused.Guidance));
                    return;
                }

                if (!PureGlobalCalls.Contains(callee.Name))
                {
                    Issues.Add(new Elsa3ExpressionRewriteIssue(
                        Elsa3ImportDiagnosticCodes.ForbiddenCapability,
                        $"JavaScript calls unknown or unapproved host function '{callee.Name}'.",
                        "Replace the call with deterministic language syntax or move it into a graph-visible activity."));
                    return;
                }
            }

            if (node is NewExpression { Callee: Identifier constructed } && IsUnavailableGlobal(constructed.Name))
            {
                Issues.Add(UnavailableGlobal(constructed.Name));
                return;
            }

            if (node is MemberExpression { Object: Identifier global } && IsUnavailableGlobal(global.Name) &&
                !IsNondeterministicMember(node))
            {
                Issues.Add(UnavailableGlobal(global.Name));
                return;
            }

            // A bare reference too, such as Date.now without a call or moment passed as a value.
            if (node is Identifier bareGlobal && IsUnavailableGlobal(bareGlobal.Name))
            {
                Issues.Add(UnavailableGlobal(bareGlobal.Name));
                return;
            }

            // A non-computed object key is a name, not a reference.
            if (node is Property { Computed: false, Shorthand: false } objectProperty)
            {
                Visit(objectProperty.Value);
                return;
            }

            // A generated accessor passed around as a value has no static read to lower.
            if (node is Identifier accessorValue && !IsDeclared(accessorValue.Name) &&
                TryGetAccessorName(accessorValue.Name, "get", out var accessorValueName) &&
                resolve(new Elsa3AmbientReference(Elsa3AmbientReferenceKind.GeneratedAccessor, accessorValueName)).Succeeded)
            {
                Issues.Add(DynamicIssue($"JavaScript uses generated accessor '{accessorValue.Name}' as a value instead of calling it."));
                return;
            }

            if (node is Identifier { Name: "args" } && !IsDeclared("args"))
            {
                Issues.Add(new Elsa3ExpressionRewriteIssue(
                    Elsa3ImportDiagnosticCodes.ForbiddenCapability,
                    "JavaScript reads Elsa 3 evaluator arguments ('args'), which Elsa 4 replaces with the expression's own parameters.",
                    "Read the value through getVariable or getInput instead before import."));
                return;
            }

            if (node is CallExpression { Callee: MemberExpression { Object: Identifier helperRoot, Property: Identifier helper } calledMember } &&
                IsNondeterministicCall(calledMember))
            {
                Issues.Add(new Elsa3ExpressionRewriteIssue(
                    Elsa3ImportDiagnosticCodes.ForbiddenCapability,
                    $"JavaScript calls '{helperRoot.Name}.{helper.Name}', a nondeterministic time or random helper.",
                    helperRoot.Name == "Date" ? TimeGuidance : UniqueValueGuidance));
                return;
            }

            if (node is MemberExpression member && member.Object is Identifier root && !IsDeclared(root.Name) &&
                TryGetAmbientKind(root.Name, out var memberKind))
            {
                if (member.Computed || member.Property is not Identifier property)
                {
                    Issues.Add(DynamicIssue($"JavaScript ambient root '{root.Name}' uses a computed accessor."));
                    return;
                }

                AddResolvedEdit(member, memberKind, property.Name);
                return;
            }

            if (node is MemberExpression ordinaryMember)
            {
                Visit(ordinaryMember.Object);
                if (ordinaryMember.Computed)
                    Visit(ordinaryMember.Property);
                return;
            }

            if (node is Identifier bareIdentifier && !IsDeclared(bareIdentifier.Name) && TryGetAmbientKind(bareIdentifier.Name, out _))
            {
                Issues.Add(DynamicIssue($"JavaScript ambient root '{bareIdentifier.Name}' is not followed by a static member name."));
                return;
            }

            foreach (var child in node.ChildNodes)
                Visit(child);
        }

        /// <summary>
        /// The names a node writes as a plain binding: an assignment or destructuring target, an update, or the head of a
        /// <c>for…in</c>/<c>for…of</c> that declares nothing. Member targets are not names.
        /// </summary>
        private static IEnumerable<string> WrittenNames(Node node)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            switch (node)
            {
                case AssignmentExpression assignment:
                    CollectBindings(assignment.Left, names);
                    break;
                case UpdateExpression { Argument: Identifier updated }:
                    names.Add(updated.Name);
                    break;
                case ForInStatement { Left: not VariableDeclaration } forIn:
                    CollectBindings(forIn.Left, names);
                    break;
                case ForOfStatement { Left: not VariableDeclaration } forOf:
                    CollectBindings(forOf.Left, names);
                    break;
            }

            return names;
        }

        private static Elsa3ExpressionRewriteIssue UndeclaredWrite(string name) => new(
            Elsa3ImportDiagnosticCodes.UnsupportedShape,
            $"JavaScript assigns to '{name}', which it never declares. Elsa 3 created a global; Elsa 4 runs in strict mode, where the assignment fails.",
            $"Declare it with let in the Elsa 3 workflow (let {name} = …;) before import.");

        private void AddResolvedEdit(Node node, Elsa3AmbientReferenceKind kind, string name)
        {
            var resolution = resolve(new Elsa3AmbientReference(kind, name));
            if (!resolution.Succeeded)
            {
                Issues.Add(resolution.Issue!);
                return;
            }

            if (!TryAddParameter(Parameters, name, resolution.Binding!, out var collision))
            {
                Issues.Add(collision!);
                return;
            }

            Edits.Add(new SourceEdit(node.Start, node.End, FormatArgumentAccess(name)));
        }

        private static string FormatArgumentAccess(string name) =>
            IsJavaScriptIdentifier(name)
                ? $"args.{name}"
                : $"args[{JsonSerializer.Serialize(name)}]";

        private static bool IsJavaScriptIdentifier(string value) =>
            value.Length > 0 && (char.IsLetter(value[0]) || value[0] is '_' or '$') &&
            value.Skip(1).All(character => char.IsLetterOrDigit(character) || character is '_' or '$');

        /// <summary>
        /// The ambient object roots Elsa 3 JavaScript has: only <c>variables</c>. Elsa 3 has no <c>input</c> or
        /// <c>output</c> object, so those names are ordinary script locals.
        /// </summary>
        private static bool TryGetAmbientKind(string root, out Elsa3AmbientReferenceKind kind)
        {
            kind = Elsa3AmbientReferenceKind.Variable;
            return root.Equals("variables", StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether a mutation target reaches an ambient value: the <c>variables</c> or <c>args</c> root, or a value read
        /// through <c>getVariable</c>/<c>getInput</c> (Elsa 4 hands expressions frozen values).
        /// </summary>
        private bool ContainsAmbientRoot(Node node)
        {
            if (node is Identifier identifier && !IsDeclared(identifier.Name) &&
                (TryGetAmbientKind(identifier.Name, out _) || identifier.Name.Equals("args", StringComparison.Ordinal)))
                return true;
            if (node is CallExpression { Callee: Identifier reader } && !IsDeclared(reader.Name) &&
                (reader.Name is "getVariable" or "getInput" || TryGetAccessorName(reader.Name, "get", out _)))
                return true;
            foreach (var child in node.ChildNodes)
            {
                if (ContainsAmbientRoot(child))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Whether <paramref name="name"/> has the shape of an Elsa 3 generated accessor (<c>get{Name}</c> or
        /// <c>set{Name}</c>) rather than a built-in, and the <c>{Name}</c> part as written.
        /// </summary>
        private static bool TryGetAccessorName(string name, string prefix, out string accessor)
        {
            accessor = name.Length > prefix.Length ? name[prefix.Length..] : string.Empty;
            return accessor.Length > 0 && name.StartsWith(prefix, StringComparison.Ordinal) &&
                   !Elsa3JavaScriptNames.BuiltIns.Contains(name);
        }

        private bool IsNondeterministicCall(MemberExpression member) =>
            member.Object is Identifier root && !IsDeclared(root.Name) &&
            member.Property is Identifier property &&
            ((root.Name.Equals("Math", StringComparison.Ordinal) && property.Name.Equals("random", StringComparison.Ordinal)) ||
             (root.Name.Equals("Date", StringComparison.Ordinal) && property.Name.Equals("now", StringComparison.Ordinal)) ||
             (root.Name.Equals("crypto", StringComparison.Ordinal) && property.Name.Equals("randomUUID", StringComparison.Ordinal)));
    }

    private static class LiquidTokenScanner
    {
        public static IReadOnlyList<LiquidRootToken> Scan(string source)
        {
            var result = new List<LiquidRootToken>();
            var index = 0;
            string? suppressedBlock = null;
            while (index < source.Length - 1)
            {
                if (source[index] != '{' || source[index + 1] is not ('{' or '%'))
                {
                    index++;
                    continue;
                }

                var statementTag = source[index + 1] == '%';
                var contentStart = index + 2;
                var contentEnd = FindTagEnd(source, contentStart, statementTag ? '%' : '}');
                if (contentEnd < 0)
                    break;

                var tagName = statementTag ? ReadFirstTagIdentifier(source, contentStart, contentEnd) : null;
                if (suppressedBlock is not null)
                {
                    if (tagName?.Equals($"end{suppressedBlock}", StringComparison.OrdinalIgnoreCase) == true)
                        suppressedBlock = null;
                    index = contentEnd + 2;
                    continue;
                }

                if (tagName is "comment" or "raw")
                {
                    suppressedBlock = tagName;
                    index = contentEnd + 2;
                    continue;
                }

                ScanTag(source, contentStart, contentEnd, result);
                index = contentEnd + 2;
            }
            return result;
        }

        private static void ScanTag(string source, int start, int end, ICollection<LiquidRootToken> result)
        {
            var index = start;
            while (index < end)
            {
                if (source[index] is '\'' or '"')
                {
                    index = Math.Min(SkipQuoted(source, index), end);
                    continue;
                }
                if (!IsIdentifierStart(source[index]))
                {
                    index++;
                    continue;
                }

                var rootStart = index;
                var root = ReadIdentifier(source, ref index);
                var cursor = SkipWhitespace(source, index);
                if (cursor >= end || source[cursor] != '.')
                    continue;
                cursor = SkipWhitespace(source, cursor + 1);
                if (cursor >= end || !IsIdentifierStart(source[cursor]))
                    continue;
                var memberStart = cursor;
                var member = ReadIdentifier(source, ref cursor);
                result.Add(new LiquidRootToken(rootStart, memberStart, root, member));
            }
        }

        private static int FindTagEnd(string source, int index, char closingPrefix)
        {
            while (index < source.Length - 1)
            {
                if (source[index] is '\'' or '"')
                {
                    index = SkipQuoted(source, index);
                    continue;
                }
                if (source[index] == closingPrefix && source[index + 1] == '}')
                    return index;
                index++;
            }
            return -1;
        }

        private static string? ReadFirstTagIdentifier(string source, int start, int end)
        {
            var index = SkipWhitespace(source, start);
            if (index >= end || !IsIdentifierStart(source[index]))
                return null;
            return ReadIdentifier(source, ref index);
        }

        private static int SkipQuoted(string source, int index)
        {
            var quote = source[index++];
            while (index < source.Length)
            {
                if (source[index] == '\\')
                {
                    index += 2;
                    continue;
                }
                if (source[index++] == quote)
                    break;
            }
            return index;
        }

        private static int SkipWhitespace(string source, int index)
        {
            while (index < source.Length && char.IsWhiteSpace(source[index]))
                index++;
            return index;
        }

        private static string ReadIdentifier(string source, ref int index)
        {
            var start = index++;
            while (index < source.Length && (char.IsLetterOrDigit(source[index]) || source[index] is '_' or '-'))
                index++;
            return source[start..index];
        }

        private static bool IsIdentifierStart(char character) => char.IsLetter(character) || character == '_';
    }

    private sealed record SourceEdit(int Start, int End, string Replacement);
    private sealed record LiquidRootEdit(string Root, string Member);
    private sealed record LiquidRootToken(int Start, int MemberStart, string Root, string Member);
}

/// <summary>How an Elsa 3 JavaScript source is evaluated in Elsa 4.</summary>
public enum Elsa3JavaScriptMode
{
    /// <summary>An expression bound to an input: Elsa 4 evaluates one parenthesized expression.</summary>
    Expression,

    /// <summary>A RunJavaScript body: Elsa 4 runs it as a function body that must return its value.</summary>
    Script,
}

public enum Elsa3AmbientReferenceKind
{
    WorkflowRequest,
    Variable,
    ActivityResult,

    /// <summary>
    /// A call <c>get{Name}()</c> to a function Elsa 3 generated per variable or workflow input; <c>Name</c> is the
    /// callee without <c>get</c>, as written. The resolver decides which declaration Elsa 3 bound it to.
    /// </summary>
    GeneratedAccessor,
}

public sealed record Elsa3AmbientReference(Elsa3AmbientReferenceKind Kind, string Name);

public sealed class Elsa3ExpressionParameterResolution
{
    private Elsa3ExpressionParameterResolution(ExpressionParameterBinding? binding, Elsa3ExpressionRewriteIssue? issue)
    {
        Binding = binding;
        Issue = issue;
    }

    public bool Succeeded => Binding is not null;
    public ExpressionParameterBinding? Binding { get; }
    public Elsa3ExpressionRewriteIssue? Issue { get; }

    public static Elsa3ExpressionParameterResolution Success(ExpressionParameterBinding binding) => new(binding, null);
    public static Elsa3ExpressionParameterResolution Failure(Elsa3ExpressionRewriteIssue issue) => new(null, issue);
}

public sealed record Elsa3ExpressionRewriteIssue(string Code, string Message, string Guidance);

public sealed class Elsa3ExpressionRewriteResult
{
    private Elsa3ExpressionRewriteResult(ExpressionDefinition? definition, IReadOnlyList<Elsa3ExpressionRewriteIssue> issues)
    {
        Definition = definition;
        Issues = issues;
    }

    public bool Succeeded => Definition is not null && Issues.Count == 0;
    public ExpressionDefinition? Definition { get; }
    public IReadOnlyList<Elsa3ExpressionRewriteIssue> Issues { get; }

    /// <summary>
    /// Script mode: the body ends with a statement, so Elsa 3's completion value (for example of a trailing
    /// <c>if</c>) has no <c>return</c> in Elsa 4 and the converted script returns nothing.
    /// </summary>
    public bool CompletionValueLost { get; private init; }

    public static Elsa3ExpressionRewriteResult Success(ExpressionDefinition definition, bool completionValueLost = false) =>
        new(definition, []) { CompletionValueLost = completionValueLost };
    public static Elsa3ExpressionRewriteResult Failure(params Elsa3ExpressionRewriteIssue[] issues) => new(null, issues);
    public static Elsa3ExpressionRewriteResult Failure(IEnumerable<Elsa3ExpressionRewriteIssue> issues) => new(null, issues.ToArray());
}

public static class Elsa3ImportDiagnosticCodes
{
    public const string DanglingReference = "VF-IMP-001";
    public const string AmbiguousReference = "VF-IMP-002";
    public const string CrossFrameReference = "VF-IMP-003";
    public const string UnresolvableReferenceShape = "VF-IMP-004";
    public const string ForbiddenCapability = "VF-IMP-005";
    public const string DynamicAccessor = "VF-IMP-006";
    public const string UnsupportedShape = "VF-IMP-007";
    public const string IncompatibleValue = "VF-IMP-008";
    public const string LiveInstanceState = "VF-IMP-009";
}
