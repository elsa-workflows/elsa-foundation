using System.Collections;
using System.Reflection;
using System.Text.Json;
using Acornima;
using Acornima.Ast;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Primitives.Models;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Internal;
using Fluid;

namespace Elsa3.Mapping.Services;

/// <summary>
/// Rewrites the narrowly supported Elsa 3 ambient expression surface into a portable expression.
/// All semantic decisions are made from a language syntax tree. Source edits use parser-provided
/// JavaScript ranges or a Liquid-aware token scanner, never regular expressions.
/// <para>
/// A converted JavaScript source is checked last by the shared .NET-usage analysis (spec 1223 FR-016, FR-017), with
/// the values the conversion knows were live .NET objects in Elsa 3; a refusal is <c>VF-IMP-012</c>. Local date and time
/// follows the converter's date interpretation (FR-021 to FR-024, FR-034).
/// </para>
/// </summary>
/// <param name="dotNetUsage">The default JavaScript strategy's .NET-usage analysis.</param>
/// <param name="options">The converter's settings; the default refuses zone-dependent date usage.</param>
public sealed class Elsa3ExpressionRewriter(IJavaScriptDotNetUsageAnalyzer dotNetUsage, Elsa3ConversionOptions? options = null)
{
    private readonly Elsa3ConversionOptions _options = Validated(options ?? new());

    private static Elsa3ConversionOptions Validated(Elsa3ConversionOptions options)
    {
        options.Validate();
        return options;
    }

    /// <summary>How Elsa 3 local date and time is read (spec 1223 FR-034).</summary>
    public Elsa3DateInterpretation DateInterpretation => _options.DateInterpretation;

    /// <summary>The Elsa 3 server's IANA zone under <see cref="Elsa3DateInterpretation.SourceTimeZone"/>.</summary>
    public string? SourceTimeZone => _options.SourceTimeZone;

    /// <summary>
    /// What to change in the Elsa 3 workflow when an expression assigns a value: Elsa 4 expressions are pure. In Elsa 3
    /// terms, because the engineer edits the Elsa 3 workflow; the reusable caveat is spec 246 D-7.
    /// </summary>
    private const string MutationGuidance =
        "Move the assignment into a Set Variable activity in the Elsa 3 workflow. In a reusable workflow, Elsa 4 cannot set variables yet (ELS3-ACTIVITY-REUSABLE-INTRINSIC-UNSUPPORTED).";

    /// <summary>
    /// What to change when an input expression writes a variable (spec 1231 FR-033): Elsa 4 input expressions only read,
    /// and a Run JavaScript script is where a step writes variables (its setters convert there).
    /// </summary>
    private const string VariableWriteGuidance =
        "Move the assignment into a Set Variable activity, or into a Run JavaScript activity, whose script can set variables, in the Elsa 3 workflow. " +
        "In a reusable workflow, Elsa 4 cannot set variables yet (ELS3-ACTIVITY-REUSABLE-INTRINSIC-UNSUPPORTED).";

    /// <summary>
    /// A Run JavaScript script in a reusable workflow cannot write the workflow's variables in Elsa 4 yet: a graph activity's
    /// variables are a closed scope a step cannot write (#4805), so the converted script would fail every run.
    /// </summary>
    private const string ReusableVariableWriteGuidance =
        "Elsa 4 cannot set a reusable workflow's variables from a script yet. Return the value from the script and capture the result, " +
        "or keep the workflow non-reusable, before import.";

    public Elsa3ExpressionRewriteResult Rewrite(
        string language,
        string source,
        TypeReference resultType,
        Func<Elsa3AmbientReference, Elsa3ExpressionParameterResolution> resolve) =>
        Rewrite(language, source, resultType, resolve, Elsa3JavaScriptMode.Expression);

    /// <param name="mode">
    /// <see cref="Elsa3JavaScriptMode.Expression"/>: the result is one expression, as Elsa 4 evaluates it; a statement
    /// form is wrapped in a function returning its completion value (plan D-15). Every value that leaves it ends in
    /// <c>?? null</c>, because Elsa 3 returned null where Elsa 4 refuses undefined (spec 262).
    /// <see cref="Elsa3JavaScriptMode.Script"/>: a RunJavaScript body, whose completion value becomes a <c>return</c>
    /// (plan D-16).
    /// </param>
    /// <param name="scriptOutcomes">
    /// Script mode: the RunJavaScript node's Elsa 3 possible outcomes, so <c>setOutcome</c> / <c>setOutcomes</c> can be
    /// converted to the returned <c>complete(result, outcomes)</c> form (spec 1208 FR-027). Without it the setters are refused.
    /// </param>
    public Elsa3ExpressionRewriteResult Rewrite(
        string language,
        string source,
        TypeReference resultType,
        Func<Elsa3AmbientReference, Elsa3ExpressionParameterResolution> resolve,
        Elsa3JavaScriptMode mode,
        Elsa3ScriptOutcomeContext? scriptOutcomes = null,
        bool scriptWritesVariables = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(resultType);
        ArgumentNullException.ThrowIfNull(resolve);

        return language switch
        {
            var value when value.Equals("JavaScript", StringComparison.OrdinalIgnoreCase) =>
                RewriteCheckedJavaScript(source, resultType, resolve, mode, scriptOutcomes, scriptWritesVariables),
            var value when value.Equals("Liquid", StringComparison.OrdinalIgnoreCase) =>
                RewriteLiquid(source, resultType, resolve),
            _ => Elsa3ExpressionRewriteResult.Failure(new Elsa3ExpressionRewriteIssue(
                Elsa3ImportDiagnosticCodes.UnsupportedShape,
                $"Expression language '{language}' has no registered Elsa 3 lowering provider.",
                "Register a syntax-tree based importer lowering provider or replace the expression before import.")),
        };
    }

    private Elsa3ExpressionRewriteResult RewriteCheckedJavaScript(
        string source,
        TypeReference resultType,
        Func<Elsa3AmbientReference, Elsa3ExpressionParameterResolution> resolve,
        Elsa3JavaScriptMode mode,
        Elsa3ScriptOutcomeContext? scriptOutcomes,
        bool scriptWritesVariables)
    {
        var result = CheckStrictMode(RewriteJavaScript(source, resultType, resolve, mode, scriptOutcomes, scriptWritesVariables, out var analyzer), mode);
        if (!result.Succeeded || analyzer is null)
            return result;

        // Spec 1223 FR-016 to FR-018: the converted source, with the values that were live .NET objects in Elsa 3.
        var usages = dotNetUsage.Analyze(
            result.Definition!.Source,
            scriptBody: mode == Elsa3JavaScriptMode.Script,
            new JavaScriptDotNetUsageContext(analyzer.KnownDotNetValues));
        if (usages is { Count: > 0 })
        {
            return Elsa3ExpressionRewriteResult.Failure(usages.Select(usage => new Elsa3ExpressionRewriteIssue(
                Elsa3ImportDiagnosticCodes.DotNetMemberUsage,
                usage.Message,
                "Rewrite the expression in the Elsa 3 workflow with JavaScript or an Elsa 4 built-in function, before import.")));
        }

        return result.WithConversionNotes(analyzer.Warnings, analyzer.AppliedDateInterpretation, analyzer.MayAccessNativeCompletion);
    }

    private Elsa3ExpressionRewriteResult RewriteJavaScript(
        string source,
        TypeReference resultType,
        Func<Elsa3AmbientReference, Elsa3ExpressionParameterResolution> resolve,
        Elsa3JavaScriptMode mode,
        Elsa3ScriptOutcomeContext? scriptOutcomes,
        bool scriptWritesVariables,
        out JavaScriptAnalyzer? analyzerUsed)
    {
        analyzerUsed = null;
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

        var analyzer = new JavaScriptAnalyzer(resolve)
        {
            AllowsOutcomeSetters = mode == Elsa3JavaScriptMode.Script && scriptOutcomes is not null,
            Mode = mode,
            WritesVariables = scriptWritesVariables,
            TrailingStatement = script.Body.Count > 0 ? script.Body[^1] : null,
            Source = source,
            DateInterpretation = _options.DateInterpretation,
            SourceTimeZone = _options.SourceTimeZone,
        };
        analyzerUsed = analyzer;
        analyzer.CollectReassignedNames(script);
        if (mode == Elsa3JavaScriptMode.Script && !scriptWritesVariables && analyzer.ReusableScriptMayWrite(script))
            return Elsa3ExpressionRewriteResult.Failure(new Elsa3ExpressionRewriteIssue(
                Elsa3ImportDiagnosticCodes.ForbiddenCapability,
                "JavaScript mutates, or passes a variable to an opaque helper in, a reusable workflow's script.",
                ReusableVariableWriteGuidance));
        analyzer.Visit(script);
        if (analyzer.Issues.Count > 0)
            return Elsa3ExpressionRewriteResult.Failure(analyzer.Issues);

        // Elsa 4 reserves 'complete' in every JavaScript surface, setter or not: a script or expression that declares it
        // would import and then fail draft validation (Scripts/ReservedName, JavaScript/ReservedName), so the import
        // refuses it with the rename up front.
        if (analyzer.DeclaresComplete)
        {
            return Elsa3ExpressionRewriteResult.Failure(new Elsa3ExpressionRewriteIssue(
                Elsa3ImportDiagnosticCodes.ForbiddenCapability,
                "JavaScript declares its own 'complete', which Elsa 4 reserves in every JavaScript surface for the returned completion form 'complete(result, outcomes)' (and which 'setOutcome' converts to).",
                "Rename the script's 'complete' in the Elsa 3 workflow before import."));
        }

        var outcomes = analyzer.CallsOutcomeSetter ? PlanScriptOutcomes(analyzer, scriptOutcomes!) : null;
        if (outcomes is { Issue: { } outcomeIssue })
            return Elsa3ExpressionRewriteResult.Failure(outcomeIssue);
        var selection = outcomes?.SelectionExpression;

        // Elsa 3 evaluated source as a statement script whose completion value is the result. Elsa 4 evaluates an
        // expression as one parenthesized expression and a script as a function body that must return.
        var edits = analyzer.Edits.ToList();
        var body = script.Body;
        var prelude = analyzer.Prelude + (outcomes?.Prelude ?? string.Empty);
        var topLevelReturns = new List<ReturnStatement>();
        CollectTopLevelReturns(script, topLevelReturns);
        if (topLevelReturns.Count > 0)
        {
            // Elsa 3 turned a bare `return;` (undefined) into null; Elsa 4 refuses an undefined result.
            foreach (var bare in topLevelReturns.Where(statement => statement.Argument is null))
                edits.Add(new SourceEdit(bare.Start + "return".Length, bare.Start + "return".Length,
                    selection is null ? " null" : $" complete(null, {selection})"));

            // A converted setter: every value the script returns carries the pending selection (FR-027).
            if (selection is not null)
            {
                foreach (var returned in topLevelReturns.Where(statement => statement.Argument is not null))
                    AddCompletion(edits, returned.Argument!.Start, returned.Argument.End, selection);
            }

            // An expression's returned value becomes null when it is undefined (spec 262 R-1).
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
                if (selection is not null)
                    AddCompletion(edits, trailing.Start, ExpressionEnd(source, trailing), selection);
                edits.Add(new SourceEdit(trailing.Start, trailing.Start, "return "));
            }

            if (mode == Elsa3JavaScriptMode.Script)
            {
                return Elsa3ExpressionRewriteResult.SuccessReading(analyzer.HasComputedVariableRead, 
                    CreateDefinition("JavaScript", EndScript(prelude + ApplyEdits(source, edits) + FinalCompletion(selection)), resultType, analyzer.Parameters),
                    completionValueLost: !preserved,
                    scriptOutcomes: outcomes?.Conversion);
            }

            if (!preserved)
            {
                return Elsa3ExpressionRewriteResult.Failure(DynamicIssue(
                    "JavaScript returns at the top level but ends with a statement that is neither a return nor an expression, so its Elsa 3 result cannot be preserved as one Elsa 4 expression."));
            }

            return Elsa3ExpressionRewriteResult.SuccessReading(analyzer.HasComputedVariableRead, CreateDefinition(
                "JavaScript", $"(function () {{\n{prelude}{ApplyEdits(source, edits)}\n}})()", resultType, analyzer.Parameters));
        }

        if (mode == Elsa3JavaScriptMode.Script)
        {
            var hasCompletion = body.Count > 0 && body[^1] is ExpressionStatement;
            if (hasCompletion)
            {
                if (selection is not null)
                    AddCompletion(edits, body[^1].Start, ExpressionEnd(source, (ExpressionStatement)body[^1]), selection);
                edits.Add(new SourceEdit(body[^1].Start, body[^1].Start, "return "));
            }
            return Elsa3ExpressionRewriteResult.SuccessReading(analyzer.HasComputedVariableRead, 
                CreateDefinition("JavaScript", EndScript(prelude + ApplyEdits(source, edits) + FinalCompletion(selection)), resultType, analyzer.Parameters),
                completionValueLost: body.Count > 0 && !hasCompletion,
                scriptOutcomes: outcomes?.Conversion);
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
            return Elsa3ExpressionRewriteResult.SuccessReading(analyzer.HasComputedVariableRead, CreateDefinition("JavaScript", ApplyEdits(source, edits), resultType, analyzer.Parameters));
        }

        // Statements: an immediately invoked function that returns the last expression, Elsa 3's completion value.
        AddNullCoalescing(edits, last.Start, ExpressionEnd(source, last));
        edits.Add(new SourceEdit(last.Start, last.Start, "return "));
        var wrapped = $"(function () {{\n{prelude}{ApplyEdits(source, edits)}\n}})()";
        return Elsa3ExpressionRewriteResult.SuccessReading(analyzer.HasComputedVariableRead, CreateDefinition("JavaScript", wrapped, resultType, analyzer.Parameters));
    }

    /// <summary>
    /// Parses a converted source the way Elsa 4 will evaluate it: strict mode, as one expression in Expression mode and
    /// inside the RunJavaScript arrow wrapper in Script mode (spec 262 R-3). Elsa 3 ran sloppy mode, so what fails here
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

    private static bool IsLiquidIdentifier(string value) =>
        value.Length > 0 && (char.IsLetter(value[0]) || value[0] == '_') &&
        value.Skip(1).All(character => char.IsLetterOrDigit(character) || character is '_' or '-');

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

        // A loop variable named `variables` would capture the converted `variables.X` reads inside it (spec 1222 FR-028).
        if (syntax.OfType<Fluid.Ast.ForStatement>().Any(loop => loop.Identifier == "variables"))
        {
            return Elsa3ExpressionRewriteResult.Failure(new Elsa3ExpressionRewriteIssue(
                Elsa3ImportDiagnosticCodes.AmbiguousReference,
                "Liquid declares its own 'variables' loop variable, and converted variable reads in its scope would read the loop item instead of the workflow's variables.",
                "Rename the loop variable in the Elsa 3 workflow before import."));
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

            // Spec 1222 FR-005, FR-026: a variable read keeps a variables root, spelled as the variable is declared.
            if (resolution.VariableName is { } variableName && resolution.Binding is VariableExpressionParameterBinding variable)
            {
                var pinned = new VariableExpressionParameterBinding(variable.DeclaringScopeNodeId, variable.VariableKey, ExpressionParameterSurfaces.Variables);
                if (!TryAddParameter(parameters, ExpressionParameterSurfaces.VariablesKey(variableName), pinned, out var variableCollision))
                {
                    issues.Add(variableCollision!);
                    continue;
                }

                requestedEdits.Add(new LiquidRootEdit(root.Identifier, memberSegment.Identifier,
                    IsLiquidIdentifier(variableName) ? $"variables.{variableName}" : $"variables[{JsonSerializer.Serialize(variableName)}]"));
                continue;
            }

            // A lowered input or output becomes a bare root; one named `variables` would be the variables root instead.
            if (memberSegment.Identifier == "variables")
            {
                issues.Add(new Elsa3ExpressionRewriteIssue(
                    Elsa3ImportDiagnosticCodes.AmbiguousReference,
                    $"Liquid reads '{root.Identifier}.variables', which in Elsa 4 would be the workflow's variables root, not this value.",
                    "Rename the input or output in the Elsa 3 workflow before import."));
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
            edits.Add(requested.Replacement is { } replacement
                ? new SourceEdit(match.Start, match.MemberStart + requested.Member.Length, replacement)
                : new SourceEdit(match.Start, match.MemberStart, string.Empty));
        }

        if (issues.Count > 0)
            return Elsa3ExpressionRewriteResult.Failure(issues);

        return Elsa3ExpressionRewriteResult.Success(
            CreateDefinition("Liquid", ApplyEdits(source, edits), resultType, parameters));
    }

    private const string PendingOutcomes = "__elsa3Outcomes";

    private sealed record ScriptOutcomePlan(
        string? Prelude,
        string? SelectionExpression,
        Elsa3ScriptOutcomeConversion? Conversion,
        Elsa3ExpressionRewriteIssue? Issue);

    /// <summary>
    /// Plans the conversion of Elsa 3 <c>setOutcome</c> / <c>setOutcomes</c> (spec 1208 FR-027, Q7). Each call replaces a
    /// pending selection held in a converter-owned local, as Elsa 3's did; every value the script returns becomes
    /// <c>complete(value, pending)</c>, so a script that sets nothing still completes with <c>Done</c>. Literal names
    /// join the node's outcome set. A computed name is faithful only with a known set: a name outside it selects a
    /// converter-declared catch-all with no connection, so that path ends as it did in Elsa 3. With no known set the
    /// conversion is refused; a warning would not make it faithful.
    /// </summary>
    private static ScriptOutcomePlan PlanScriptOutcomes(JavaScriptAnalyzer analyzer, Elsa3ScriptOutcomeContext context)
    {
        var literals = analyzer.OutcomeLiterals.Distinct(StringComparer.Ordinal).ToArray();
        var known = (context.PossibleOutcomes ?? []).Concat(literals).Distinct(StringComparer.Ordinal).ToArray();
        string? catchAll = null;
        if (analyzer.HasComputedOutcome)
        {
            if (context.PossibleOutcomes is not { Count: > 0 })
            {
                return new(null, null, null, new Elsa3ExpressionRewriteIssue(
                    Elsa3ImportDiagnosticCodes.ForbiddenCapability,
                    "JavaScript calls 'setOutcome' or 'setOutcomes' with a computed name, and the activity lists no possible outcomes, so Elsa 4 cannot know which outcomes the script selects.",
                    "List every outcome the script can set in the activity's Possible Outcomes in the Elsa 3 workflow, before import."));
            }

            catchAll = Elsa3ScriptOutcomeConversion.CatchAllOutcome;
            while (known.Contains(catchAll, StringComparer.Ordinal))
                catchAll += " (Elsa 3)";
        }

        var prelude =
            $"let {PendingOutcomes};\n" +
            $"function __elsa3SetOutcome(outcome) {{ {PendingOutcomes} = [outcome]; }}\n" +
            $"function __elsa3SetOutcomes(outcomes) {{ {PendingOutcomes} = Array.from(outcomes); }}\n";
        var selection = PendingOutcomes;
        if (catchAll is not null)
        {
            prelude += "function __elsa3RouteOutcomes(outcomes) { if (outcomes === undefined) return undefined; " +
                       $"const known = {JsonSerializer.Serialize(known)}; " +
                       $"return outcomes.map(outcome => known.includes(outcome) ? outcome : {JsonSerializer.Serialize(catchAll)}); }}\n";
            selection = $"__elsa3RouteOutcomes({PendingOutcomes})";
        }

        return new(prelude, selection, new Elsa3ScriptOutcomeConversion(literals, catchAll)
        {
            HasInvalidLiteralSelection = analyzer.HasInvalidLiteralOutcomeSelection,
            HasComputedSelectionSet = analyzer.HasComputedOutcomeSet
        }, null);
    }

    /// <summary>
    /// Wraps <c>source[start..end]</c> as the result of <c>complete((result), selection)</c>. Parenthesized, so a comma
    /// expression (<c>return a, b;</c>) stays one argument whose value is its last operand, as Elsa 3 returned it.
    /// </summary>
    private static void AddCompletion(List<SourceEdit> edits, int start, int end, string selection)
    {
        edits.Add(new SourceEdit(start, start, "complete(("));
        edits.Add(new SourceEdit(end, end, $"), {selection})"));
    }

    /// <summary>A script that ends without returning still completes with its pending selection.</summary>
    private static string FinalCompletion(string? selection) =>
        selection is null ? string.Empty : $"\nreturn complete(undefined, {selection});";

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
    /// Makes the value of <c>source[start..end]</c> null when it is undefined, as Elsa 3 did (spec 262 R-1). The parentheses
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

    /// <summary>
    /// Ends a RunJavaScript body with a newline when its last line holds a <c>//</c>. Elsa 4 wraps the body on one line, so
    /// a trailing line comment would swallow the end of the wrapper; Elsa 3 ran the body as is (spec 262 R-5). A
    /// <c>//</c> inside a string on that line also gets the newline, which is harmless.
    /// </summary>
    private static string EndScript(string script) =>
        script.AsSpan(script.LastIndexOf('\n') + 1).Contains("//", StringComparison.Ordinal) ? script + "\n" : script;

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
            ExpressionCapabilityProfiles.BindingReadonlyV1);

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
        /// <summary>Script mode with a known node: <c>setOutcome</c> / <c>setOutcomes</c> are converted, not refused.</summary>
        public bool AllowsOutcomeSetters { get; init; }

        /// <summary>
        /// Both modes read variables through the Elsa 4 <c>variables</c> root (spec 1222 FR-026, FR-027): an input
        /// expression from its input snapshot, a RunJavaScript body over its variable session (FR-026a, FR-033). A
        /// computed read keeps its form and is served by the run-time lookup.
        /// </summary>
        public Elsa3JavaScriptMode Mode { get; init; }

        /// <summary>
        /// Whether a Run JavaScript body may write variables: everywhere but in a reusable workflow, whose variables a step
        /// cannot write yet (#4805). Input expressions never write.
        /// </summary>
        public bool WritesVariables { get; init; }

        /// <summary>
        /// Spec 1231 FR-032: a Run JavaScript body converts Elsa 3's variable setters and member changes to writes through
        /// <c>variables</c>.
        /// </summary>
        private bool ConvertsVariableWrites => Mode == Elsa3JavaScriptMode.Script && WritesVariables;

        /// <summary>The script's last top-level statement, which becomes its <c>return</c>: a converted setter there keeps returning nothing.</summary>
        public Node? TrailingStatement { get; init; }

        // Setter calls that stand alone as a statement (not the trailing one), whose value nothing reads.
        private readonly HashSet<CallExpression> _statementCalls = new(ReferenceEqualityComparer.Instance);

        /// <summary>The source being converted, for quoting the offending expression in a diagnostic.</summary>
        public string Source { get; init; } = string.Empty;

        /// <summary>How Elsa 3 local date and time is read (spec 1223 FR-034).</summary>
        public Elsa3DateInterpretation DateInterpretation { get; init; }

        /// <summary>The Elsa 3 server's IANA zone under <see cref="Elsa3DateInterpretation.SourceTimeZone"/>.</summary>
        public string? SourceTimeZone { get; init; }

        /// <summary>Behaviour warnings of a successful conversion: a rewritten <c>toJson</c>, a date assumed UTC.</summary>
        public List<Elsa3ExpressionRewriteIssue> Warnings { get; } = [];

        /// <summary>Whether the date interpretation decided how an expression converted.</summary>
        public bool AppliedDateInterpretation { get; private set; }

        /// <summary>
        /// The Elsa 4 read paths (<c>variables.x</c>, <c>args.x</c>) of values that were live .NET objects in Elsa 3, for
        /// the .NET-usage analysis (FR-016).
        /// </summary>
        public Dictionary<string, JavaScriptDotNetValueKind> KnownDotNetValues { get; } = new(StringComparer.Ordinal);

        private bool _usesZonedHelper;

        /// <summary>Reads rewritten from a date-typed Elsa 3 value to a <c>Date</c> (FR-032): known dates.</summary>
        private readonly HashSet<Node> _dateReads = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// The names each scope declares as an alias of a known date (FR-022), keyed by that scope: an inner declaration
        /// of the same name is another variable. Only a name the script never assigns again is an alias; a reassigned
        /// name is not classified (a value reached indirectly, the recorded gap), so no guess about which assignment ran
        /// is needed.
        /// </summary>
        private readonly Dictionary<HashSet<string>, HashSet<string>> _dateAliases = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// Per declaring scope, the names the script assigns again after declaring them: an assignment, an update, a
        /// second initialized declaration of the same var (var d = new Date(); ... var d = 'x'), or a var loop head.
        /// Keyed by the same scope sets the visit uses, so a name in another function or block is another variable.
        /// </summary>
        private readonly Dictionary<HashSet<string>, HashSet<string>> _reassigned = new(ReferenceEqualityComparer.Instance);

        /// <summary>Each scope node's declared names, computed once, so the pre-scan and the visit share the same sets.</summary>
        private readonly Dictionary<Node, HashSet<string>?> _scopeCache = new(ReferenceEqualityComparer.Instance);

        private HashSet<string>? CachedScope(Node node)
        {
            if (!_scopeCache.TryGetValue(node, out var scope))
                _scopeCache[node] = scope = ScopeOf(node);
            return scope;
        }

        public void CollectReassignedNames(Node root)
        {
            var scopes = new List<HashSet<string>>();
            var initialized = new Dictionary<HashSet<string>, HashSet<string>>(ReferenceEqualityComparer.Instance);
            Collect(root);

            void Collect(Node node)
            {
                var scope = CachedScope(node);
                if (scope is not null)
                    scopes.Add(scope);
                foreach (var name in WrittenNames(node))
                    Mark(name);
                switch (node)
                {
                    // A var loop head assigns the function's var on every iteration.
                    case ForInStatement { Left: VariableDeclaration { Kind: VariableDeclarationKind.Var } loopHead }:
                        MarkBindings(loopHead);
                        break;
                    case ForOfStatement { Left: VariableDeclaration { Kind: VariableDeclarationKind.Var } loopHead }:
                        MarkBindings(loopHead);
                        break;
                    // A second initialized declaration of the same variable assigns it again.
                    case VariableDeclarator { Init: not null } declarator:
                        var names = new HashSet<string>(StringComparer.Ordinal);
                        CollectBindings(declarator.Id, names);
                        foreach (var name in names)
                        {
                            if (Resolve(name) is not { } declaring)
                                continue;
                            if (!initialized.TryGetValue(declaring, out var seen))
                                initialized[declaring] = seen = new HashSet<string>(StringComparer.Ordinal);
                            if (!seen.Add(name))
                                Mark(name);
                        }


                        break;
                }

                foreach (var child in node.ChildNodes)
                    Collect(child);
                if (scope is not null)
                    scopes.RemoveAt(scopes.Count - 1);
            }

            HashSet<string>? Resolve(string name)
            {
                for (var index = scopes.Count - 1; index >= 0; index--)
                {
                    if (scopes[index].Contains(name))
                        return scopes[index];
                }

                return null;
            }

            void Mark(string name)
            {
                if (Resolve(name) is not { } declaring)
                    return;
                if (!_reassigned.TryGetValue(declaring, out var names))
                    _reassigned[declaring] = names = new HashSet<string>(StringComparer.Ordinal);
                names.Add(name);
            }

            void MarkBindings(VariableDeclaration declaration)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var declarator in declaration.Declarations)
                    CollectBindings(declarator.Id, names);
                foreach (var name in names)
                    Mark(name);
            }
        }

        /// <summary>The innermost visible scope that declares <paramref name="name"/>, or null for a global.</summary>
        private HashSet<string>? DeclaringScope(string name)
        {
            for (var index = _scopes.Count - 1; index >= 0; index--)
            {
                if (_scopes[index].Contains(name))
                    return _scopes[index];
            }

            return null;
        }

        private bool IsDateAlias(string name) =>
            DeclaringScope(name) is { } scope && _dateAliases.TryGetValue(scope, out var aliases) && aliases.Contains(name);

        /// <summary>Whether the converted source reads variables by a computed name, which publish serves by the run-time fallback.</summary>
        public bool HasComputedVariableRead { get; private set; }

        public bool CallsOutcomeSetter { get; private set; }
        public bool HasComputedOutcome { get; private set; }
        public bool HasComputedOutcomeSet { get; private set; }
        public bool HasInvalidLiteralOutcomeSelection { get; private set; }
        public List<string> OutcomeLiterals { get; } = [];
        public bool DeclaresComplete { get; private set; }
        public bool MayAccessNativeCompletion { get; private set; }

        private void RecordOutcomeSetter(CallExpression call, string name)
        {
            CallsOutcomeSetter = true;
            var argument = call.Arguments.Count == 1 ? call.Arguments[0] : null;
            if (name == "setOutcome" && argument is Literal { Value: string literal })
            {
                OutcomeLiterals.Add(literal);
                return;
            }

            if (name == "setOutcomes" && argument is ArrayExpression array &&
                array.Elements.All(element => element is Literal { Value: string }))
            {
                var names = array.Elements.Select(element => (string)((Literal)element!).Value!).Distinct(StringComparer.Ordinal).ToArray();
                OutcomeLiterals.AddRange(names);
                HasInvalidLiteralOutcomeSelection |= names.Contains("Done", StringComparer.Ordinal) && names.Any(value => value != "Done");
                return;
            }

            HasComputedOutcome = true;
            HasComputedOutcomeSet |= name == "setOutcomes";
        }

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
            ["isNullOrEmpty"] = ("__isNullOrEmpty",
                "function __isNullOrEmpty(v) { return v == null || v === \"\"; }"),
            ["isNullOrWhiteSpace"] = ("__isNullOrWhiteSpace",
                "function __isNullOrWhiteSpace(v) { return v == null || /^[\\t-\\r \\u0085\\u00A0\\u1680\\u2000-\\u200A\\u2028\\u2029\\u202F\\u205F\\u3000]*$/.test(v); }"),
        };

        private readonly SortedSet<string> _helpers = new(StringComparer.Ordinal);

        /// <summary>The local functions the rewritten source calls, one per line, or empty.</summary>
        public string Prelude =>
            string.Concat(_helpers.Select(helper => InlineHelpers[helper].Declaration + "\n")) +
            (_usesZonedHelper ? Elsa3ZonedDateHelper.Declaration(SourceTimeZone!) + "\n" : string.Empty);

        /// <summary>Elsa 3 built-ins that read instance metadata, and the parameter each one becomes.</summary>
        private static readonly Dictionary<string, (string Member, string Parameter)> InstanceMembers = new(StringComparer.Ordinal)
        {
            ["getCorrelationId"] = (WorkflowInstanceExpressionParameterBinding.Members.CorrelationId, "workflowCorrelationId"),
            ["getWorkflowInstanceId"] = (WorkflowInstanceExpressionParameterBinding.Members.InstanceId, "workflowInstanceId"),
        };

        private static readonly HashSet<string> PureGlobalCalls = new(StringComparer.Ordinal)
        {
            "Boolean", "Number", "String", "parseFloat", "parseInt",
        };

        /// <summary>
        /// Globals Elsa 3 put into the engine that Elsa 4 does not have: CLR types and the optional lodash and moment
        /// libraries. Reading or constructing them would fail at run time (plan D-14). <c>Date</c> is native JavaScript
        /// and converts (spec 1223 FR-014).
        /// </summary>
        private static readonly HashSet<string> ElsaThreeGlobals = new(StringComparer.Ordinal)
        {
            "DateTime", "DateTimeOffset", "TimeSpan", "Guid", "Random", "_", "moment",
        };

        private static Elsa3ExpressionRewriteIssue UnavailableGlobal(string name) => new(
            Elsa3ImportDiagnosticCodes.ForbiddenCapability,
            $"JavaScript uses '{name}', which the Elsa 4 JavaScript engine does not provide.",
            name switch
            {
                "DateTime" or "DateTimeOffset" => TimeGuidance,
                "Guid" or "Random" => UniqueValueGuidance,
                _ => "Supply the value as a pinned input or compute it in an activity before import.",
            });

        private const string TimeGuidance =
            "Elsa 4 expression values are JSON and expressions have no .NET types. Use JavaScript's Date or Temporal (for example Date.now() or new Date().toISOString()) in the Elsa 3 workflow before import.";

        private const string UniqueValueGuidance =
            "Elsa 4 expressions have no .NET types. Use newGuid() for a GUID, or Math.random() for a random number, in the Elsa 3 workflow before import.";

        private const string DataGuidance =
            "Elsa 4 values are JSON and expressions have no byte or stream helpers. Move the conversion into an activity before import.";

        private const string InstanceGuidance =
            "Elsa 4 expressions can read only the correlation ID and the instance ID of a workflow instance. Pass the value in as a workflow input, or move the operation into an activity, before import.";

        private const string OutcomeGuidance =
            "Only a RunJavaScript script selects outcomes in Elsa 4. In the Elsa 3 workflow, move the call into a RunJavaScript, or route with a Decision or Switch activity, before import.";

        private const string Elsa4CompleteName = "complete";

        /// <summary>
        /// Elsa 3 host functions Elsa 4 cannot run the same, each with what to change in the Elsa 3 workflow
        /// (spec 247 contracts/javascript-conversion.md, "Refused"). The code stays VF-IMP-005. The GUID family,
        /// <c>parseGuid</c> and the Base64 pair are Elsa 4 built-ins and convert as written; <c>toJson</c> becomes
        /// <c>JSON.stringify</c> with a warning (spec 1223 FR-009, FR-014; supersedes spec 247 R-6 for them).
        /// </summary>
        private static readonly Dictionary<string, (string Message, string Guidance)> RefusedHostFunctions = new(StringComparer.Ordinal)
        {
            ["setOutcome"] = ("JavaScript calls 'setOutcome', which sets an Elsa 3 script outcome.", OutcomeGuidance),
            ["setOutcomes"] = ("JavaScript calls 'setOutcomes', which sets Elsa 3 script outcomes.", OutcomeGuidance),
            ["bytesToString"] = ("JavaScript calls 'bytesToString'.", DataGuidance),
            ["bytesFromString"] = ("JavaScript calls 'bytesFromString'.", DataGuidance),
            ["bytesToBase64"] = ("JavaScript calls 'bytesToBase64'.", DataGuidance),
            ["bytesFromBase64"] = ("JavaScript calls 'bytesFromBase64'.", DataGuidance),
            ["streamToBytes"] = ("JavaScript calls 'streamToBytes'.", DataGuidance),
            ["streamToBase64"] = ("JavaScript calls 'streamToBase64'.", DataGuidance),
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
        /// own <c>this</c> and <c>arguments</c>; arrow functions share their enclosing one's (spec 262 R-6).
        /// </summary>
        private readonly List<FunctionContext> _functions = [];

        public void Visit(Node node)
        {
            var scope = CachedScope(node);
            if (scope is not null)
            {
                _scopes.Add(scope);
                if (scope.Contains(Elsa4CompleteName))
                    DeclaresComplete = true;
            }
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
                // A bare native helper reference can be aliased without being a direct host-function call.
                // The global object also allows computed access to that helper. Record this separately so
                // newly enabled Switch bodies cannot claim proven outcome safety from setter names alone.
                if (node is Identifier { Name: "complete" or "globalThis" } identifier && !IsDeclared(identifier.Name))
                    MayAccessNativeCompletion = true;
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
        /// <c>arguments</c> (spec 262 R-6): either refused here (outside every function) or noted on the enclosing
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
                // A class static block is its own var scope.
                case StaticBlock staticBlock:
                    CollectVarDeclarations(staticBlock, names);
                    CollectLexicalDeclarations(staticBlock.Body, names);
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
                if (child is IFunction or StaticBlock)
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
                // Spec 1231 FR-032: in a Run JavaScript body, a change to a member of a variable's value
                // (getVariable('order').status = 'Paid') is a member change through variables, written with the step.
                var target = node is AssignmentExpression changed ? changed.Left : ((UpdateExpression)node).Argument;
                if (!ConvertsVariableWrites || !IsVariableMemberTarget(target))
                {
                    Issues.Add(new Elsa3ExpressionRewriteIssue(
                        Elsa3ImportDiagnosticCodes.ForbiddenCapability,
                        "JavaScript mutates an Elsa 3 ambient value.",
                        Mode != Elsa3JavaScriptMode.Script ? VariableWriteGuidance
                        : !WritesVariables ? ReusableVariableWriteGuidance
                        : "Change a variable with setVariable('name', value), or a member of its value through getVariable('name'), in the Elsa 3 script before import; " +
                          "a loop's current value and workflow inputs are read-only in Elsa 4."));
                    return;
                }
            }

            if (node is ExpressionStatement { Expression: CallExpression statementCall } statement && !ReferenceEquals(statement, TrailingStatement))
                _statementCalls.Add(statementCall);

            // Elsa 3's sloppy mode turned a write to an undeclared name into a global; Elsa 4's strict mode throws
            // (spec 262 R-4). The parse cannot see it, so the scopes decide.
            foreach (var written in WrittenNames(node).Where(name => !IsDeclared(name)))
                Issues.Add(UndeclaredWrite(written));
            CheckThisAndArguments(node);

            // A function the script declares itself is a local call, not an Elsa 3 host function.
            if (node is CallExpression call && call.Callee is Identifier callee && !IsDeclared(callee.Name))
            {
                if (callee.Name == "setVariable")
                {
                    ConvertSetVariable(call);
                    return;
                }

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
                        // Spec 1222 FR-027: an Elsa 3 computed variable read keeps its form, getVariable(expr), and is
                        // served by the Elsa 4 run-time lookup, which resolves the name nearest scope first.
                        if (kind == Elsa3AmbientReferenceKind.Variable && call.Arguments.Count == 1 &&
                            call.Arguments[0] is not SpreadElement)
                        {
                            if (!EnsureRootFree("getVariable", call))
                                return;
                            HasComputedVariableRead = true;
                            Visit(call.Arguments[0]);
                            return;
                        }

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
                    if (!ConvertsVariableWrites)
                    {
                        Issues.Add(new Elsa3ExpressionRewriteIssue(
                            Elsa3ImportDiagnosticCodes.ForbiddenCapability,
                            $"JavaScript calls generated setter '{callee.Name}', which mutates workflow state.",
                            Mode == Elsa3JavaScriptMode.Script ? ReusableVariableWriteGuidance : VariableWriteGuidance));
                        return;
                    }

                    if (call.Arguments.Count != 1 || call.Arguments[0] is SpreadElement)
                    {
                        Issues.Add(new Elsa3ExpressionRewriteIssue(
                            Elsa3ImportDiagnosticCodes.UnsupportedShape,
                            $"JavaScript calls generated setter '{callee.Name}' with other than one value.",
                            "Call the setter with exactly the value to set before import."));
                        return;
                    }

                    ConvertSetter(call, Elsa3AmbientReferenceKind.GeneratedAccessor, setter, callee.Name, call.Arguments[0]);
                    return;
                }

                // Elsa 3 script outcomes convert to the returned completion form (spec 1208 FR-027).
                if (AllowsOutcomeSetters && callee.Name is "setOutcome" or "setOutcomes")
                {
                    RecordOutcomeSetter(call, callee.Name);
                    Edits.Add(new SourceEdit(callee.Start, callee.End, callee.Name == "setOutcome" ? "__elsa3SetOutcome" : "__elsa3SetOutcomes"));
                    foreach (var argument in call.Arguments)
                        Visit(argument);
                    return;
                }

                if (callee.Name == "toJson")
                {
                    ConvertToJson(call, callee);
                    return;
                }

                // Elsa 3's GUID and Base64 functions are Elsa 4 built-ins that return the same text (FR-006, FR-007, FR-026).
                if (JavaScriptBuiltInFunctions.Names.Contains(callee.Name))
                {
                    // Elsa 3 registered built-ins and generated accessors in an order its source does not fix (R-3).
                    if (callee.Name.StartsWith("get", StringComparison.Ordinal) &&
                        resolve(new Elsa3AmbientReference(Elsa3AmbientReferenceKind.GeneratedAccessor, callee.Name["get".Length..])).Succeeded)
                    {
                        Issues.Add(new Elsa3ExpressionRewriteIssue(
                            Elsa3ImportDiagnosticCodes.AmbiguousReference,
                            $"JavaScript calls '{callee.Name}', which is both an Elsa 3 built-in and the generated accessor of a variable or input.",
                            $"Rename the variable or input so that '{callee.Name}' only means the built-in, before import."));
                        return;
                    }

                    foreach (var argument in call.Arguments)
                        Visit(argument);
                    return;
                }

                // String(d) of a date is its local text (FR-022).
                if (callee.Name == "String" && call.Arguments.Count == 1 && !IsDeclared("String"))
                {
                    Visit(call.Arguments[0]);
                    if (IsKnownDate(call.Arguments[0]))
                        ZoneDependent(call, DateUse.LocalFormat, null);
                    return;
                }

                // Date(...) without new returns the current time as local text (FR-022).
                if (callee.Name == "Date")
                {
                    foreach (var argument in call.Arguments)
                        Visit(argument);
                    ZoneDependent(call, DateUse.LocalFormat, null);
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

            if (node is NewExpression { Callee: Identifier { Name: "Date" } } construction && !IsDeclared("Date"))
            {
                VisitDateConstruction(construction);
                return;
            }

            if (node is NewExpression { Callee: MemberExpression { Computed: false, Object: Identifier { Name: "Intl" }, Property: Identifier intlConstructor } } intlConstruction &&
                !IsDeclared("Intl") && IntlConstructors.Contains(intlConstructor.Name))
            {
                foreach (var argument in intlConstruction.Arguments)
                    Visit(argument);
                CheckIntlLocale(intlConstruction, intlConstructor.Name, intlConstruction.Arguments);
                return;
            }

            if (node is CallExpression { Callee: MemberExpression dateOrLocaleCallee } dateOrLocaleCall &&
                VisitDateOrLocaleCall(dateOrLocaleCall, dateOrLocaleCallee))
                return;

            // A date turned into text implicitly ('at ' + d, `${d}`, String(d)) is local text in Elsa 3 (FR-022).
            if (node is BinaryExpression { Operator: Operator.Addition } addition)
            {
                Visit(addition.Left);
                Visit(addition.Right);
                if (IsKnownDate(addition.Left) || IsKnownDate(addition.Right))
                    ZoneDependent(addition, DateUse.LocalFormat, null);
                return;
            }

            if (node is AssignmentExpression { Left: Identifier assigned } assignmentToName)
            {
                Visit(assignmentToName.Right);
                Visit(assignmentToName.Left);
                // s += d joins the date's local text (FR-022). A reassigned name is never a date alias (see _dateAliases).
                if (assignmentToName.Operator == Operator.AdditionAssignment && IsKnownDate(assignmentToName.Right))
                    ZoneDependent(assignmentToName, DateUse.LocalFormat, null);
                return;
            }
            if (node is TemplateLiteral template)
            {
                foreach (var expression in template.Expressions)
                    Visit(expression);
                if (template.Expressions.Any(IsKnownDate))
                    ZoneDependent(template, DateUse.LocalFormat, null);
                return;
            }

            if (node is VariableDeclaration declaration)
            {
                foreach (var declarator in declaration.Declarations)
                {
                    Visit(declarator);
                    // An alias of a known date that is never assigned again is a known date (FR-022). Elsa 3 scripts mostly
                    // declare with var, so var counts as well as const and let.
                    if (declarator.Id is Identifier alias && declarator.Init is { } init && IsKnownDate(init) &&
                        DeclaringScope(alias.Name) is { } scope &&
                        !(_reassigned.TryGetValue(scope, out var reassigned) && reassigned.Contains(alias.Name)))
                    {
                        if (!_dateAliases.TryGetValue(scope, out var aliases))
                            _dateAliases[scope] = aliases = new HashSet<string>(StringComparer.Ordinal);
                        aliases.Add(alias.Name);
                    }
                }

                return;
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
                    $"JavaScript calls '{helperRoot.Name}.{helper.Name}', which Elsa 3 did not provide.",
                    UniqueValueGuidance));
                return;
            }

            if (node is MemberExpression member && member.Object is Identifier root && !IsDeclared(root.Name) &&
                TryGetAmbientKind(root.Name, out var memberKind))
            {
                if (member.Computed && member.Property is Literal { Value: string literalMember })
                {
                    AddResolvedEdit(member, memberKind, literalMember);
                    return;
                }

                if (member.Computed || member.Property is not Identifier property)
                {
                    // Spec 1222 FR-027: variables[expr] keeps its form and is served by the run-time lookup.
                    if (member.Computed)
                    {
                        HasComputedVariableRead = true;
                        Visit(member.Property);
                        return;
                    }

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
                // Spec 1222 FR-008, FR-027: the variables root used as a value (enumerated, passed, aliased) is an opaque
                // read the run-time lookup serves.
                HasComputedVariableRead = true;
                return;
            }

            foreach (var child in node.ChildNodes)
                Visit(child);
        }

        /// <summary>
        /// Elsa 3's <c>setVariable(name, value)</c>. In a Run JavaScript body a literal name of a declared variable becomes a
        /// whole assignment, <c>variables.name = value</c> (spec 1231 FR-032, Q6), and a computed name becomes
        /// <c>variables[name] = value</c> (FR-033 as amended). Refused with guidance (FR-033): an undeclared variable, an
        /// input expression (which only reads) and a reusable workflow's script.
        /// </summary>
        private void ConvertSetVariable(CallExpression call)
        {
            if (!ConvertsVariableWrites)
            {
                Issues.Add(new Elsa3ExpressionRewriteIssue(
                    Elsa3ImportDiagnosticCodes.ForbiddenCapability,
                    Mode == Elsa3JavaScriptMode.Script
                        ? "JavaScript calls 'setVariable' in a reusable workflow's script, whose variables Elsa 4 cannot set from a script yet."
                        : "JavaScript calls 'setVariable', which writes a workflow variable; an Elsa 4 input expression only reads.",
                    Mode == Elsa3JavaScriptMode.Script ? ReusableVariableWriteGuidance : VariableWriteGuidance));
                return;
            }

            if (call.Arguments.Count != 2 || call.Arguments.Any(argument => argument is SpreadElement))
            {
                Issues.Add(new Elsa3ExpressionRewriteIssue(
                    Elsa3ImportDiagnosticCodes.UnsupportedShape,
                    "JavaScript calls 'setVariable' with other than a name and a value.",
                    "Call setVariable('name', value) with exactly a variable name and a value before import."));
                return;
            }

            if (call.Arguments[0] is not Literal { Value: string name })
            {
                ConvertComputedSetVariable(call);
                return;
            }

            ConvertSetter(call, Elsa3AmbientReferenceKind.Variable, name, "setVariable", call.Arguments[1]);
        }

        /// <summary>
        /// <c>setVariable(k, v)</c> with a computed name becomes <c>variables[k] = v</c> (spec 1231 FR-033 as amended on
        /// 2026-11-20, #4899): the name resolves when the script runs, nearest scope first; a declared variable is written,
        /// and an undeclared name faults the step (FR-020a) where Elsa 3 created the variable. It is marked as a computed
        /// read too, so the conversion reports it as a script that reaches variables by computed names.
        /// </summary>
        private void ConvertComputedSetVariable(CallExpression call)
        {
            if (!EnsureRootFree(ExpressionParameterSurfaces.Variables, call))
                return;

            HasComputedVariableRead = true;
            var name = call.Arguments[0];
            var value = call.Arguments[1];
            var standsAlone = _statementCalls.Contains(call);
            var grouped = value is SequenceExpression;
            Edits.Add(new SourceEdit(call.Start, name.Start, (standsAlone ? "" : "void (") + "variables["));
            Edits.Add(new SourceEdit(name.End, value.Start, "] = " + (grouped ? "(" : "")));
            Edits.Add(new SourceEdit(value.End, call.End, (grouped ? ")" : "") + (standsAlone ? "" : ")")));
            Visit(name);
            Visit(value);
        }

        /// <summary>
        /// Rewrites a setter call to a whole assignment through <c>variables</c>. A call that stands alone as a statement
        /// becomes <c>variables.x = v</c>; anywhere its value could be read (the trailing statement, which becomes the
        /// script's return, or inside an expression) it becomes <c>void (variables.x = v)</c>, since Elsa 3's setters
        /// returned nothing.
        /// </summary>
        private void ConvertSetter(CallExpression call, Elsa3AmbientReferenceKind kind, string name, string calleeName, Node value)
        {
            var resolution = resolve(new Elsa3AmbientReference(kind, name));
            if (!resolution.Succeeded)
            {
                Issues.Add(resolution.Issue!.Code == Elsa3ImportDiagnosticCodes.DanglingReference
                    ? new Elsa3ExpressionRewriteIssue(
                        Elsa3ImportDiagnosticCodes.SetterOfUndeclaredVariable,
                        $"JavaScript calls '{calleeName}' for variable '{name}', which the workflow does not declare. Elsa 3 created the variable on its first write; Elsa 4 never creates a variable from a script.",
                        $"Declare variable '{name}' in the Elsa 3 workflow before import.")
                    : resolution.Issue);
                return;
            }

            if (resolution.VariableName is not { } variableName || resolution.Binding is not VariableExpressionParameterBinding variable)
            {
                Issues.Add(new Elsa3ExpressionRewriteIssue(
                    Elsa3ImportDiagnosticCodes.ForbiddenCapability,
                    $"JavaScript calls '{calleeName}', which writes '{name}', a value an Elsa 4 script cannot write (a workflow input or an activity result).",
                    "Copy the value into a declared variable and set that variable in the Elsa 3 workflow before import."));
                return;
            }

            if (IsReadAsCopy(variable))
            {
                Issues.Add(new Elsa3ExpressionRewriteIssue(
                    Elsa3ImportDiagnosticCodes.ForbiddenCapability,
                    $"JavaScript calls '{calleeName}' for '{name}', a loop's current value, which is read-only in Elsa 4.",
                    "Copy the loop's current value into another variable and set that variable in the Elsa 3 workflow before import."));
                return;
            }

            if (!EnsureRootFree(ExpressionParameterSurfaces.Variables, call))
                return;

            var pinned = new VariableExpressionParameterBinding(variable.DeclaringScopeNodeId, variable.VariableKey, ExpressionParameterSurfaces.Variables);
            if (!TryAddParameter(Parameters, ExpressionParameterSurfaces.VariablesKey(variableName), pinned, out var collision))
            {
                Issues.Add(collision!);
                return;
            }

            var standsAlone = _statementCalls.Contains(call);
            // A comma expression is parenthesized in the source; it keeps its parentheses as the assigned value.
            var grouped = value is SequenceExpression;
            var prefix = (standsAlone ? "" : "void (") + FormatVariableAccess(variableName) + " = " + (grouped ? "(" : "");
            var suffix = (grouped ? ")" : "") + (standsAlone ? "" : ")");
            Edits.Add(new SourceEdit(call.Start, value.Start, prefix));
            Edits.Add(new SourceEdit(value.End, call.End, suffix));
            Visit(value);
        }

        /// <summary>
        /// Whether an assignment target changes a member of a variable's value, read through <c>getVariable('name')</c>, a
        /// generated <c>get{Name}()</c> or <c>variables.name</c>, where that read becomes <c>variables.name</c> over the
        /// step's session (spec 1231 FR-032). A whole assignment of <c>variables.name</c>, a computed name, a loop's current
        /// value (read as a copy) and a workflow input are not.
        /// </summary>
        private bool IsVariableMemberTarget(Node target)
        {
            if (target is not MemberExpression)
                return false;

            Node current = target;
            while (current is MemberExpression member)
            {
                if (member.Object is Identifier { Name: "variables" } && !IsDeclared("variables"))
                {
                    if (ReferenceEquals(member, target))
                        return false;
                    var name = !member.Computed && member.Property is Identifier property ? property.Name
                        : member.Computed && member.Property is Literal { Value: string literal } ? literal
                        : null;
                    return name is not null && IsSessionVariable(Elsa3AmbientReferenceKind.Variable, name);
                }

                current = member.Object;
            }

            if (current is not CallExpression { Callee: Identifier reader } read || IsDeclared(reader.Name))
                return false;
            if (reader.Name == "getVariable")
                return read.Arguments.Count == 1 && read.Arguments[0] is Literal { Value: string variable } &&
                       IsSessionVariable(Elsa3AmbientReferenceKind.Variable, variable);
            return read.Arguments.Count == 0 && TryGetAccessorName(reader.Name, "get", out var accessor) &&
                   IsSessionVariable(Elsa3AmbientReferenceKind.GeneratedAccessor, accessor);
        }

        private bool IsSessionVariable(Elsa3AmbientReferenceKind kind, string name) =>
            resolve(new Elsa3AmbientReference(kind, name)) is { Succeeded: true, VariableName: not null, Binding: VariableExpressionParameterBinding variable } &&
            !IsReadAsCopy(variable);

        public bool ReusableScriptMayWrite(Script script) =>
            new Elsa3ReusableVariableWriteAnalyzer(CachedScope, IsSessionVariable,
                name => TryGetAccessorName(name, "get", out var accessor) ? accessor : null).MayWrite(script);

        /// <summary>
        /// A Run JavaScript body reads a loop's item or index through args, as a copy: Elsa 4 freezes a loop value in a body
        /// (spec 1231 FR-006), and an Elsa 3 body that changed its item would fault where Elsa 3 ran.
        /// </summary>
        private bool IsReadAsCopy(VariableExpressionParameterBinding variable) =>
            Mode == Elsa3JavaScriptMode.Script &&
            !StringComparer.Ordinal.Equals(variable.DeclaringScopeNodeId, Elsa.Expressions.Core.Models.VariableReference.WorkflowScopeId) &&
            Elsa3.Mapping.ActivityMap.Rewrites.Elsa3LoopVariableReferencesRewrite.LoopVariableKeys.Contains(variable.VariableKey);

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

            // Spec 1222 FR-026: a variable read becomes variables.<name>, spelled as the variable is declared, and is
            // pinned at publish to its nearest declaring scope. The parameter added here is what the converter resolved;
            // publish derives the pins again.
            // A RunJavaScript body reads a loop's item or index through args, as a copy (IsReadAsCopy).
            if (resolution.VariableName is { } variableName &&
                resolution.Binding is VariableExpressionParameterBinding variable &&
                !IsReadAsCopy(variable))
            {
                if (!EnsureRootFree(ExpressionParameterSurfaces.Variables, node))
                    return;

                var pinned = new VariableExpressionParameterBinding(variable.DeclaringScopeNodeId, variable.VariableKey, ExpressionParameterSurfaces.Variables);
                if (!TryAddParameter(Parameters, ExpressionParameterSurfaces.VariablesKey(variableName), pinned, out var variableCollision))
                {
                    Issues.Add(variableCollision!);
                    return;
                }

                Edits.Add(new SourceEdit(node.Start, node.End, ScriptValue(node, resolution.ScriptValueKind, "variables", variableName, FormatVariableAccess(variableName))));
                return;
            }

            // #3296: a lowered args.X inside a scope where the script declares its own args would read the script's
            // value, not the converted one.
            if (!EnsureRootFree("args", node))
                return;

            if (!TryAddParameter(Parameters, name, resolution.Binding!, out var collision))
            {
                Issues.Add(collision!);
                return;
            }

            Edits.Add(new SourceEdit(node.Start, node.End, ScriptValue(node, resolution.ScriptValueKind, "args", name, FormatArgumentAccess(name))));
        }

        /// <summary>
        /// A converted read of a typed Elsa 3 value. A date-typed value, which Elsa 3 handed scripts as a <c>Date</c>,
        /// becomes a <c>Date</c> of the same instant, null kept (FR-032); a value that was a live .NET object is recorded
        /// for the .NET-usage analysis (FR-016).
        /// </summary>
        private string ScriptValue(Node node, Elsa3ScriptValueKind kind, string root, string name, string access)
        {
            switch (kind)
            {
                case Elsa3ScriptValueKind.Date:
                    _dateReads.Add(node);
                    return $"({access} == null ? {access} : new Date({access}))";
                case Elsa3ScriptValueKind.Guid:
                    KnownDotNetValues[$"{root}.{name}"] = JavaScriptDotNetValueKind.Guid;
                    break;
                case Elsa3ScriptValueKind.Object:
                    KnownDotNetValues[$"{root}.{name}"] = JavaScriptDotNetValueKind.Object;
                    break;
                case Elsa3ScriptValueKind.Collection:
                    KnownDotNetValues[$"{root}.{name}"] = JavaScriptDotNetValueKind.Collection;
                    break;
            }

            return access;
        }

        /// <summary>
        /// Refuses a converted read whose Elsa 4 root the script declares itself in an enclosing scope (#3296,
        /// faithful-or-refuse, spec 1222 FR-028): the read would bind to the script's own value.
        /// </summary>
        private bool EnsureRootFree(string root, Node read)
        {
            if (!IsDeclared(root))
                return true;

            Issues.Add(new Elsa3ExpressionRewriteIssue(
                Elsa3ImportDiagnosticCodes.AmbiguousReference,
                $"JavaScript declares its own '{root}', and a converted read in its scope would become '{root}' too, reading the script's value instead of the workflow's.",
                $"Rename the script's '{root}' in the Elsa 3 workflow before import."));
            return false;
        }

        private static string FormatVariableAccess(string name) =>
            IsJavaScriptIdentifier(name)
                ? $"variables.{name}"
                : $"variables[{JsonSerializer.Serialize(name)}]";

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

        /// <summary>
        /// Elsa 3's <c>toJson(x)</c> becomes <c>JSON.stringify(x)</c>, with a behaviour warning naming the text
        /// differences (spec 1223 FR-009). Elsa 4 has no <c>toJson</c>.
        /// </summary>
        private void ConvertToJson(CallExpression call, Identifier callee)
        {
            if (call.Arguments.Count != 1 || call.Arguments[0] is SpreadElement)
            {
                Issues.Add(new Elsa3ExpressionRewriteIssue(
                    Elsa3ImportDiagnosticCodes.ForbiddenCapability,
                    $"JavaScript calls 'toJson' with other than one argument in '{Quote(call)}'.",
                    "Call it with exactly one argument before import."));
                return;
            }

            if (IsDeclared("JSON"))
            {
                Issues.Add(new Elsa3ExpressionRewriteIssue(
                    Elsa3ImportDiagnosticCodes.AmbiguousReference,
                    "JavaScript declares its own 'JSON', and the converter turns Elsa 3's 'toJson' into 'JSON.stringify'.",
                    "Rename the script's 'JSON' in the Elsa 3 workflow before import."));
                return;
            }

            Visit(call.Arguments[0]);
            Edits.Add(new SourceEdit(callee.Start, callee.End, "JSON.stringify"));
            Warnings.Add(new Elsa3ExpressionRewriteIssue(
                Elsa3ImportDiagnosticCodes.ToJsonRewritten,
                $"JavaScript calls 'toJson' in '{Quote(call)}', converted to JSON.stringify. The text can differ from Elsa 3's: Elsa 3 wrote the HTML-sensitive characters <, >, &, ', + and ` and characters outside the Basic Multilingual Plane as \\uXXXX escapes, wrote a Date with trimmed fractional seconds, wrote undefined as null and failed on NaN; JSON.stringify omits an undefined property and writes NaN as null. The parsed value is the same apart from the undefined and NaN cases.",
                "Check that nothing depends on the exact text, before import."));
        }

        private enum DateUse
        {
            /// <summary>A local getter or setter (<c>getHours()</c>, <c>setDate()</c>).</summary>
            LocalField,

            /// <summary>The local multi-argument constructor (<c>new Date(2026, 9, 28)</c>).</summary>
            LocalConstructor,

            /// <summary>A parse of text without an offset, or of a value that is not a literal.</summary>
            Parse,

            /// <summary>Local text: <c>toString()</c>, <c>toDateString()</c>, <c>toTimeString()</c>, <c>Date()</c>.</summary>
            LocalFormat,

            /// <summary>Locale text formatted in the server's zone: a locale but no <c>timeZone</c>.</summary>
            ZoneFormat,
        }

        /// <summary>Methods that exist only on <c>Date</c> and read or write local time (FR-022), recognised by name.</summary>
        private static readonly HashSet<string> LocalDateMethods = new(StringComparer.Ordinal)
        {
            "getDate", "getDay", "getFullYear", "getHours", "getMilliseconds", "getMinutes", "getMonth", "getSeconds",
            "getTimezoneOffset", "getYear", "setDate", "setFullYear", "setHours", "setMilliseconds", "setMinutes",
            "setMonth", "setSeconds", "setYear", "toDateString", "toTimeString",
        };

        private static readonly HashSet<string> LocaleMethods = new(StringComparer.Ordinal)
        {
            "toLocaleString", "toLocaleDateString", "toLocaleTimeString",
        };

        private static readonly HashSet<string> IntlConstructors = new(StringComparer.Ordinal)
        {
            "DateTimeFormat", "NumberFormat", "Collator", "PluralRules", "RelativeTimeFormat", "ListFormat", "DisplayNames",
            "Segmenter",
        };

        /// <summary>
        /// Date statics, local date methods and locale-dependent calls (FR-021 to FR-024, FR-033). Returns false for any
        /// other member call, which the caller visits as usual.
        /// </summary>
        private bool VisitDateOrLocaleCall(CallExpression call, MemberExpression callee)
        {
            if (callee.Computed || callee.Property is not Identifier property)
                return false;
            var name = property.Name;
            var receiver = callee.Object;

            if (receiver is Identifier { Name: "Date" } && !IsDeclared("Date"))
            {
                if (name != "parse")
                    return false;
                foreach (var argument in call.Arguments)
                    Visit(argument);
                if (call.Arguments.Count > 0 && !IsZoneIndependentText(call.Arguments[0]))
                    ZoneDependent(call, DateUse.Parse, () => Edits.Add(new SourceEdit(callee.Start, callee.End, $"{Helper()}.parse")));
                return true;
            }

            if (receiver is Identifier { Name: "Intl" } && !IsDeclared("Intl") && IntlConstructors.Contains(name))
            {
                foreach (var argument in call.Arguments)
                    Visit(argument);
                CheckIntlLocale(call, name, call.Arguments);
                return true;
            }

            if (LocaleMethods.Contains(name))
            {
                Visit(receiver);
                foreach (var argument in call.Arguments)
                    Visit(argument);
                if (call.Arguments.Count == 0 || IsUndefined(call.Arguments[0]))
                    Issues.Add(LocaleRefusal(call, name));
                else if (IsKnownDate(receiver) && !HasTimeZoneOption(call.Arguments, 1))
                    ZoneDependent(call, DateUse.ZoneFormat, ZoneOption(call.Arguments));
                return true;
            }

            if (name == "localeCompare")
            {
                Visit(receiver);
                foreach (var argument in call.Arguments)
                    Visit(argument);
                if (call.Arguments.Count < 2 || IsUndefined(call.Arguments[1]))
                    Issues.Add(LocaleRefusal(call, name));
                return true;
            }

            if (LocalDateMethods.Contains(name))
            {
                Visit(receiver);
                foreach (var argument in call.Arguments)
                    Visit(argument);
                if (name is "toDateString" or "toTimeString")
                    ZoneDependent(call, DateUse.LocalFormat, null);
                // Behind a deeper optional chain (a?.b.getHours()), Elsa 3 short-circuited only when a was null: the
                // helper cannot tell, so the call is not rewritten.
                else if (!callee.Optional && IsOptionalChain(callee.Object))
                    ZoneDependent(call, DateUse.LocalField, null);
                else
                    ZoneDependent(call, DateUse.LocalField, () => WrapLocalCall(call, callee, name));
                return true;
            }

            if (name == "toString" && call.Arguments.Count == 0)
            {
                // The receiver is visited first: a converted date-typed read is known as a date once it is rewritten.
                Visit(receiver);
                if (IsKnownDate(receiver))
                    ZoneDependent(call, DateUse.LocalFormat, null);
                return true;
            }

            return false;
        }

        /// <summary><c>new Date(...)</c>: no argument, a number, a date or text with an offset is an instant (FR-021).</summary>
        private void VisitDateConstruction(NewExpression construction)
        {
            foreach (var argument in construction.Arguments)
                Visit(argument);

            var arguments = construction.Arguments;
            if (arguments.Count == 0)
                return;
            if (arguments.Count == 1 && arguments[0] is not SpreadElement)
            {
                var argument = arguments[0];
                if (IsZoneIndependentDateArgument(argument))
                    return;
                ZoneDependent(construction, DateUse.Parse, () =>
                {
                    Edits.Add(new SourceEdit(argument.Start, argument.Start, $"{Helper()}.from("));
                    Edits.Add(new SourceEdit(argument.End, argument.End, ")"));
                });
                return;
            }

            // A spread (new Date(...parts)) may be one value or the fields: it cannot be rewritten faithfully.
            if (arguments.Any(argument => argument is SpreadElement))
            {
                ZoneDependent(construction, DateUse.LocalConstructor, null);
                return;
            }

            ZoneDependent(construction, DateUse.LocalConstructor, () =>
            {
                Edits.Add(new SourceEdit(arguments[0].Start, arguments[0].Start, $"{Helper()}.local("));
                Edits.Add(new SourceEdit(arguments[^1].End, arguments[^1].End, ")"));
            });
        }

        /// <summary>FR-033: an <c>Intl</c> constructor needs an explicit locale; a date format without a zone is zone-dependent.</summary>
        private void CheckIntlLocale(Node node, string constructor, IReadOnlyList<Node> arguments)
        {
            if (arguments.Count == 0 || IsUndefined(arguments[0]))
                Issues.Add(LocaleRefusal(node, $"Intl.{constructor}"));
            else if (constructor == "DateTimeFormat" && !HasTimeZoneOption(arguments, 1))
                ZoneDependent(node, DateUse.ZoneFormat, ZoneOption(arguments));
        }

        /// <summary>
        /// Under <c>SourceTimeZone</c>, locale text on a date names the Elsa 3 server's zone explicitly: a
        /// <c>{ timeZone }</c> options argument is added, or <c>timeZone</c> is added to a literal options object. Null
        /// when the options are not a literal, which is refused.
        /// </summary>
        private Action? ZoneOption(IReadOnlyList<Node> arguments)
        {
            var zone = JsonSerializer.Serialize(SourceTimeZone);
            if (arguments.Count == 1 && arguments[0] is not SpreadElement)
                return () => Edits.Add(new SourceEdit(arguments[0].End, arguments[0].End, $", {{ timeZone: {zone} }}"));
            if (arguments.Count == 2 && IsUndefined(arguments[1]))
                return () => Edits.Add(new SourceEdit(arguments[1].Start, arguments[1].End, $"{{ timeZone: {zone} }}"));
            if (arguments.Count == 2 && arguments[1] is ObjectExpression options)
                return () => Edits.Add(new SourceEdit(options.Start + 1, options.Start + 1, $" timeZone: {zone},"));
            return null;
        }

        /// <summary>The zone helper's name; a rewrite that calls it writes the helper into the expression once.</summary>
        private string Helper()
        {
            _usesZonedHelper = true;
            return Elsa3ZonedDateHelper.Name;
        }

        /// <summary>
        /// Applies the date interpretation to zone-dependent usage (FR-022 to FR-024): refused under <c>Refuse</c>;
        /// converted with a warning under <c>AssumeUtc</c>; under <c>SourceTimeZone</c> rewritten with the zone in the
        /// code, except local text, which depends on the Elsa 3 server's operating system and is refused.
        /// <paramref name="rewrite"/> adds its edits after the node's children were visited, so it wraps theirs.
        /// </summary>
        private void ZoneDependent(Node node, DateUse use, Action? rewrite)
        {
            var quote = Quote(node);
            var what = use switch
            {
                DateUse.Parse => $"parses a date from text in '{quote}': text without an offset is a local time, and text that is not a literal may have none",
                DateUse.LocalFormat => $"formats a date as local text in '{quote}'",
                DateUse.ZoneFormat => $"formats a date with a locale but no timeZone in '{quote}', which uses the local time zone",
                DateUse.LocalConstructor => $"builds a date from local date and time fields in '{quote}'",
                _ => $"reads or changes local date and time in '{quote}'",
            };
            var isText = use is DateUse.LocalFormat or DateUse.ZoneFormat;

            switch (DateInterpretation)
            {
                case Elsa3DateInterpretation.AssumeUtc:
                    AppliedDateInterpretation = true;
                    Warnings.Add(new Elsa3ExpressionRewriteIssue(
                        Elsa3ImportDiagnosticCodes.DateAssumedUtc,
                        $"JavaScript {what}. It is converted as if the Elsa 3 server ran on UTC (date interpretation AssumeUtc); the result differs from Elsa 3 if that server was not on UTC.",
                        "If the Elsa 3 server was not on UTC, set the date interpretation to SourceTimeZone with its IANA time zone and convert again."));
                    return;
                case Elsa3DateInterpretation.SourceTimeZone when rewrite is not null:
                    AppliedDateInterpretation = true;
                    if (IsDeclared(Elsa3ZonedDateHelper.Name))
                    {
                        Issues.Add(new Elsa3ExpressionRewriteIssue(
                            Elsa3ImportDiagnosticCodes.AmbiguousReference,
                            $"JavaScript declares '{Elsa3ZonedDateHelper.Name}', the name the converter gives its time zone helper.",
                            "Rename the declaration before import."));
                        return;
                    }

                    rewrite();
                    return;
                case Elsa3DateInterpretation.SourceTimeZone when !isText:
                    Issues.Add(new Elsa3ExpressionRewriteIssue(
                        Elsa3ImportDiagnosticCodes.ForbiddenCapability,
                        $"JavaScript {what}, which the converter cannot rewrite into the Elsa 3 server's zone.",
                        "Pass the date fields as separate arguments (new Date(year, month, day, ...)) instead of spreading them, and read a value reached through ?. into a variable before calling its date method, in the Elsa 3 workflow before import."));
                    return;
                case Elsa3DateInterpretation.SourceTimeZone:
                    Issues.Add(new Elsa3ExpressionRewriteIssue(
                        Elsa3ImportDiagnosticCodes.ForbiddenCapability,
                        $"JavaScript {what}. Elsa 3's text depended on the server's time zone and operating system (for example 'GMT+0100 (Central European Standard Time)'), which Elsa 4 cannot reproduce.",
                        $"Use toISOString(), or Intl.DateTimeFormat or toLocaleString with an explicit locale and timeZone (for example {{ timeZone: '{SourceTimeZone}' }}), in the Elsa 3 workflow before import."));
                    return;
                default:
                    Issues.Add(new Elsa3ExpressionRewriteIssue(
                        Elsa3ImportDiagnosticCodes.ForbiddenCapability,
                        $"JavaScript {what}. Elsa 3 evaluated it in its server's time zone; Elsa 4 evaluates every expression in UTC, and the converter's date interpretation is Refuse.",
                        isText
                            ? "Set the Elsa 3 converter's date interpretation to AssumeUtc to accept UTC, or rewrite it in the Elsa 3 workflow with toISOString() or with an explicit locale and timeZone, before import."
                            : "Set the Elsa 3 converter's date interpretation to SourceTimeZone with the Elsa 3 server's IANA time zone (for example 'Europe/Amsterdam'), or to AssumeUtc to accept UTC; or rewrite it in the Elsa 3 workflow with UTC methods (getUTCHours) or an explicit offset, before import."));
                    return;
            }
        }

        /// <summary><c>R.name(args)</c> becomes <c>__elsa3Tz.call(R, "name", [args])</c> (FR-023).</summary>
        private void WrapLocalCall(CallExpression call, MemberExpression callee, string name)
        {
            // The member's dot (or ?.) and the call's opening parenthesis, found by skipping whitespace and comments from the
            // parser's node ranges: a comment may itself hold a '.' or '('. From the dot, so a parenthesized receiver keeps
            // its closing parenthesis.
            var dot = SkipTrivia(callee.Object.End, skipClosingParentheses: true);
            var open = SkipTrivia(callee.Property.End, skipClosingParentheses: false);
            if (Source.AsSpan(open).StartsWith("?."))
                open = SkipTrivia(open + 2, skipClosingParentheses: false);
            // An optional chain (d?.getHours(), a?.b.getDate()) gives undefined for a null receiver, as it did in Elsa 3.
            var optional = callee.Optional;
            Edits.Add(new SourceEdit(call.Start, call.Start, $"{Helper()}.call("));
            Edits.Add(new SourceEdit(dot, open + 1, $", {JsonSerializer.Serialize(name)}, ["));
            Edits.Add(new SourceEdit(call.End - 1, call.End, optional ? "], true)" : "])"));
        }

        /// <summary>
        /// The first position at or after <paramref name="position"/> that is not whitespace or a comment (and, when asked,
        /// not the closing parenthesis of a parenthesized receiver).
        /// </summary>
        private int SkipTrivia(int position, bool skipClosingParentheses)
        {
            // Called right after a token, so a line has started only once a line terminator is passed: an HTML-like
            // closing comment (-->) counts only at the start of a line (ECMAScript Annex B, which a script parse allows).
            var lineStart = false;
            while (position < Source.Length)
            {
                var character = Source[position];
                var span = Source.AsSpan(position);
                if (IsLineTerminator(character))
                {
                    lineStart = true;
                    position++;
                }
                // U+FEFF is JavaScript whitespace, which char.IsWhiteSpace does not count.
                else if (char.IsWhiteSpace(character) || character == '\uFEFF' || skipClosingParentheses && character == ')')
                    position++;
                else if (span.StartsWith("//") || span.StartsWith("<!--") || lineStart && span.StartsWith("-->"))
                {
                    var end = Source.IndexOfAny(['\n', '\r', '\u2028', '\u2029'], position);
                    position = end < 0 ? Source.Length : end;
                }
                else if (span.StartsWith("/*"))
                {
                    var end = Source.IndexOf("*/", position + 2, StringComparison.Ordinal);
                    if (end < 0)
                        return Source.Length;
                    if (Source.AsSpan(position, end - position).IndexOfAny(['\n', '\r', '\u2028', '\u2029']) >= 0)
                        lineStart = true;
                    position = end + 2;
                }
                else
                    return position;
            }

            return position;
        }

        private static bool IsLineTerminator(char character) => character is '\n' or '\r' or '\u2028' or '\u2029';

        /// <summary>
        /// Whether the receiver contains an optional link (<c>a?.b</c>) of the same chain. A parenthesized chain ends there,
        /// and an optional call (<c>d.getHours?.()</c>) does not guard its receiver.
        /// </summary>
        private static bool IsOptionalChain(Node receiver) => receiver switch
        {
            MemberExpression member => member.Optional || IsOptionalChain(member.Object),
            CallExpression call => call.Optional || IsOptionalChain(call.Callee),
            _ => false,
        };

        /// <summary>Whether <paramref name="node"/> is known to be a <c>Date</c>: a construction, a rewritten date value or an alias.</summary>
        private bool IsKnownDate(Node node) => node switch
        {
            NewExpression { Callee: Identifier { Name: "Date" } } => !IsDeclared("Date"),
            Identifier alias => IsDateAlias(alias.Name),
            _ => _dateReads.Contains(node),
        };

        /// <summary>A one-argument construction that is an instant whatever the zone: a number, a date or offset text.</summary>
        private bool IsZoneIndependentDateArgument(Node argument) => argument switch
        {
            Literal { Value: string } => IsZoneIndependentText(argument),
            // ISO 8601 text of an instant always carries Z.
            CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "toISOString" or "toJSON" } } } => true,
            _ => IsNumber(argument) || IsKnownDate(argument),
        };

        /// <summary>
        /// An expression known to give a number, as epoch milliseconds do: a number literal, arithmetic on numbers,
        /// <c>Date.now()</c>, <c>Date.UTC(...)</c>, <c>Date.parse(...)</c> (classified on its own), <c>getTime()</c> or
        /// <c>valueOf()</c>. A <c>+</c> is a number only when both operands are; otherwise it may join text.
        /// </summary>
        private bool IsNumber(Node node) => node switch
        {
            Literal { Value: string } => false,
            Literal => true,
            UnaryExpression { Operator: Operator.UnaryNegation or Operator.UnaryPlus } => true,
            BinaryExpression { Operator: Operator.Subtraction or Operator.Multiplication or Operator.Division or Operator.Remainder } => true,
            // A + is a number when both operands are, or when one is and the other cannot be text or a date
            // (Date.now() + ms, d.getTime() + offset).
            BinaryExpression { Operator: Operator.Addition } addition =>
                IsNumber(addition.Left) && IsNumber(addition.Right) ||
                IsEpoch(addition.Left) && MayBeNumber(addition.Right) ||
                MayBeNumber(addition.Left) && IsEpoch(addition.Right),
            CallExpression { Callee: MemberExpression { Computed: false, Object: Identifier { Name: "Date" }, Property: Identifier { Name: "now" or "UTC" or "parse" } } } => !IsDeclared("Date"),
            CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "getTime" } } } => true,
            CallExpression { Callee: MemberExpression { Computed: false, Object: var receiver, Property: Identifier { Name: "valueOf" } } } => IsKnownDate(receiver),
            _ => false,
        };

        /// <summary>Epoch milliseconds: <c>Date.now()</c>, <c>Date.UTC(...)</c>, <c>getTime()</c>, or <c>valueOf()</c> of a known date.</summary>
        private bool IsEpoch(Node node) => node switch
        {
            CallExpression { Callee: MemberExpression { Computed: false, Object: Identifier { Name: "Date" }, Property: Identifier { Name: "now" or "UTC" } } } => !IsDeclared("Date"),
            CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "getTime" } } } => true,
            CallExpression { Callee: MemberExpression { Computed: false, Object: var receiver, Property: Identifier { Name: "valueOf" } } } => IsKnownDate(receiver),
            _ => false,
        };

        /// <summary>An operand that is not visibly text or a date: a literal string, a template or a known date.</summary>
        private bool MayBeNumber(Node node) => node is not (Literal { Value: string } or TemplateLiteral) && !IsKnownDate(node);

        /// <summary>A literal date text that carries <c>Z</c> or an offset, or is date-only, which ECMAScript reads as UTC.</summary>
        private static bool IsZoneIndependentText(Node argument)
        {
            var text = argument switch
            {
                Literal { Value: string literal } => literal,
                TemplateLiteral { Expressions.Count: 0 } template => template.Quasis[0].Value.Cooked,
                _ => null,
            };
            return text is not null && (DateOnlyText.IsMatch(text) || OffsetText.IsMatch(text));
        }

        private static readonly System.Text.RegularExpressions.Regex DateOnlyText = new(@"^\s*[+-]?\d{4,6}(?:-\d{2}(?:-\d{2})?)?\s*$");

        // An offset follows a time (09:15Z, 09:15:00+01:00), or is named (GMT+0100, UTC). The trailing "-2026" of a
        // day-month-year text is not one.
        private static readonly System.Text.RegularExpressions.Regex OffsetText =
            new(@"(?:\d:\d{2}(?::\d{2}(?:\.\d+)?)?\s*(?:[zZ]|[+-]\d{2}(?::?\d{2})?)|\b(?:UTC|GMT|UT)\b[^()]*)\s*(?:\([^)]*\))?\s*$");

        private static bool IsUndefined(Node node) => node is Identifier { Name: "undefined" };

        private static bool HasTimeZoneOption(IReadOnlyList<Node> arguments, int index) =>
            arguments.Count > index && arguments[index] is ObjectExpression options &&
            options.Properties.OfType<Property>().Any(property => property.Key is Identifier { Name: "timeZone" } or Literal { Value: "timeZone" });

        private Elsa3ExpressionRewriteIssue LocaleRefusal(Node node, string name) => new(
            Elsa3ImportDiagnosticCodes.ForbiddenCapability,
            $"JavaScript calls '{name}' without an explicit locale in '{Quote(node)}'. Elsa 3 used the server's culture; Elsa 4 uses the invariant culture.",
            "Pass an explicit locale (for example 'nl-NL') in the Elsa 3 workflow before import.");

        private string Quote(Node node)
        {
            var text = Source[node.Start..node.End].ReplaceLineEndings(" ");
            return text.Length <= 160 ? text : text[..157] + "...";
        }

        /// <summary>
        /// <c>crypto.randomUUID</c>, which Elsa 3 did not provide. The clock and <c>Math.random</c> convert (spec 1223
        /// FR-001, FR-014).
        /// </summary>
        private bool IsNondeterministicCall(MemberExpression member) =>
            member.Object is Identifier root && !IsDeclared(root.Name) &&
            member.Property is Identifier property &&
            root.Name.Equals("crypto", StringComparison.Ordinal) && property.Name.Equals("randomUUID", StringComparison.Ordinal);
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
    private sealed record LiquidRootEdit(string Root, string Member, string? Replacement = null);
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
    private Elsa3ExpressionParameterResolution(ExpressionParameterBinding? binding, Elsa3ExpressionRewriteIssue? issue, string? variableName = null)
    {
        Binding = binding;
        Issue = issue;
        VariableName = variableName;
    }

    public bool Succeeded => Binding is not null;
    public ExpressionParameterBinding? Binding { get; }
    public Elsa3ExpressionRewriteIssue? Issue { get; }

    /// <summary>
    /// The name an Elsa 4 script reads the resolved variable by, spelled as declared (spec 1222 FR-026): a workflow
    /// variable's name, or a loop's iteration value (<c>currentItem</c>, <c>index</c>). Null for any other read.
    /// </summary>
    public string? VariableName { get; }

    /// <summary>
    /// What the value was in an Elsa 3 script, from its Elsa 3 type (spec 1223 FR-016, FR-032): a date is rewritten to a
    /// <c>Date</c>; a live .NET object is checked by the .NET-usage analysis.
    /// </summary>
    public Elsa3ScriptValueKind ScriptValueKind { get; private init; }

    public static Elsa3ExpressionParameterResolution Success(
        ExpressionParameterBinding binding,
        Elsa3ScriptValueKind scriptValueKind = Elsa3ScriptValueKind.Json) =>
        new(binding, null) { ScriptValueKind = scriptValueKind };

    /// <summary>A resolved variable read, which an Elsa 4 script reads as <c>variables.&lt;variableName&gt;</c>.</summary>
    public static Elsa3ExpressionParameterResolution SuccessVariable(
        VariableExpressionParameterBinding binding,
        string variableName,
        Elsa3ScriptValueKind scriptValueKind = Elsa3ScriptValueKind.Json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variableName);
        return new(binding, null, variableName) { ScriptValueKind = scriptValueKind };
    }
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

    /// <summary>Script mode: how Elsa 3 <c>setOutcome</c> / <c>setOutcomes</c> were converted; null when the script calls neither.</summary>
    public Elsa3ScriptOutcomeConversion? ScriptOutcomes { get; private init; }

    /// <summary>Whether the converted source reads variables by a computed name (spec 1222 FR-027), served by the run-time lookup.</summary>
    public bool HasComputedVariableRead { get; private init; }

    /// <summary>Behaviour warnings of a successful conversion (spec 1223 FR-009, FR-022): it converts, but differs.</summary>
    public IReadOnlyList<Elsa3ExpressionRewriteIssue> Warnings { get; private init; } = [];

    /// <summary>Whether the converter's date interpretation decided how the expression converted (spec 1223 FR-034).</summary>
    public bool AppliedDateInterpretation { get; private init; }

    internal bool MayAccessNativeCompletion { get; private init; }

    internal Elsa3ExpressionRewriteResult WithConversionNotes(IReadOnlyList<Elsa3ExpressionRewriteIssue> warnings, bool appliedDateInterpretation, bool mayAccessNativeCompletion) =>
        new(Definition, Issues)
        {
            CompletionValueLost = CompletionValueLost,
            ScriptOutcomes = ScriptOutcomes,
            HasComputedVariableRead = HasComputedVariableRead,
            Warnings = warnings.ToArray(),
            AppliedDateInterpretation = appliedDateInterpretation,
            MayAccessNativeCompletion = mayAccessNativeCompletion,
        };

    public static Elsa3ExpressionRewriteResult SuccessReading(
        bool computedVariableRead,
        ExpressionDefinition definition,
        bool completionValueLost = false,
        Elsa3ScriptOutcomeConversion? scriptOutcomes = null) =>
        new(definition, []) { CompletionValueLost = completionValueLost, ScriptOutcomes = scriptOutcomes, HasComputedVariableRead = computedVariableRead };

    public static Elsa3ExpressionRewriteResult Success(
        ExpressionDefinition definition,
        bool completionValueLost = false,
        Elsa3ScriptOutcomeConversion? scriptOutcomes = null) =>
        new(definition, []) { CompletionValueLost = completionValueLost, ScriptOutcomes = scriptOutcomes };
    public static Elsa3ExpressionRewriteResult Failure(params Elsa3ExpressionRewriteIssue[] issues) => new(null, issues);
    public static Elsa3ExpressionRewriteResult Failure(IEnumerable<Elsa3ExpressionRewriteIssue> issues) => new(null, issues.ToArray());
}

/// <summary>
/// The RunJavaScript node's Elsa 3 possible outcomes, for converting <c>setOutcome</c> / <c>setOutcomes</c>.
/// <paramref name="PossibleOutcomes"/> is null when an expression decides them.
/// </summary>
public sealed record Elsa3ScriptOutcomeContext(IReadOnlyList<string>? PossibleOutcomes);

/// <summary>
/// The outcomes a converted script selects that the node must declare: the literal names its setters pass, and the
/// catch-all it selects for a computed name outside the known set, when it has one (spec 1208 FR-027).
/// </summary>
public sealed record Elsa3ScriptOutcomeConversion(IReadOnlyList<string> LiteralOutcomes, string? CatchAll)
{
    // Scope-specific fidelity analysis uses groups, not the union of alternative singleton selections.
    internal bool HasInvalidLiteralSelection { get; init; }
    internal bool HasComputedSelectionSet { get; init; }
    /// <summary>The catch-all outcome's name, unless the node already uses it.</summary>
    public const string CatchAllOutcome = "Unlisted outcome";
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

    /// <summary>A literal variable read would resolve to another declaration in Elsa 4 than in Elsa 3 (spec 1222 FR-031): refused.</summary>
    public const string ResolutionDiffers = "VF-IMP-010";

    /// <summary>Warning: a computed variable read could reach a name declared in more than one visible scope (spec 1222 FR-031).</summary>
    public const string ShadowedCandidate = "VF-IMP-011";

    /// <summary>
    /// A script uses a member of a value that was a live .NET object in Elsa 3, a .NET type, or a .NET-style member
    /// (spec 1223 FR-016 to FR-018): refused, because Elsa 4 values are JSON.
    /// </summary>
    public const string DotNetMemberUsage = "VF-IMP-012";

    /// <summary>Warning: Elsa 3's <c>toJson</c> was converted to <c>JSON.stringify</c>, whose text can differ (spec 1223 FR-009).</summary>
    public const string ToJsonRewritten = "ELS3-BEHAVIOUR-TOJSON-STRINGIFY";

    /// <summary>Warning: local date and time converted as UTC under the date interpretation AssumeUtc (spec 1223 FR-022).</summary>
    public const string DateAssumedUtc = "ELS3-BEHAVIOUR-DATE-ASSUMED-UTC";

    /// <summary>Info: the date interpretation the converter applied to a workflow's scripts (spec 1223 FR-034).</summary>
    public const string DateInterpretationApplied = "ELS3-DATE-INTERPRETATION";

    /// <summary>Info: a converted script selects a converter-declared catch-all for computed names outside its known set (spec 1208).</summary>
    public const string ScriptOutcomeCatchAll = "ELS3-SCRIPT-OUTCOME-CATCHALL";

    /// <summary>
    /// An untyped activity result is bound to a boolean, string or number input, and the import cannot show that Elsa 4
    /// gives the value Elsa 3 did (spec 1228 FR-023, FR-024): refused, with guidance.
    /// </summary>
    public const string UntypedResultIntoTypedInput = "ELS3-UNTYPED-RESULT";

    /// <summary>
    /// A Run JavaScript script sets a variable the workflow does not declare; Elsa 3 created it on its first write, Elsa 4
    /// never creates a variable (spec 1231 FR-033): refused, with guidance to declare it.
    /// </summary>
    public const string SetterOfUndeclaredVariable = "ELS3-SETTER-UNDECLARED-VARIABLE";
}
