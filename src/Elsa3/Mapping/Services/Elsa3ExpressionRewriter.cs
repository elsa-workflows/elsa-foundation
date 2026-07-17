using System.Collections;
using System.Reflection;
using System.Text.Json;
using Acornima;
using Acornima.Ast;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Fluid;

namespace Elsa3.Mapping.Services;

/// <summary>
/// Rewrites the narrowly supported Elsa 3 ambient expression surface into a portable expression.
/// All semantic decisions are made from a language syntax tree. Source edits use parser-provided
/// JavaScript ranges or a Liquid-aware token scanner, never regular expressions.
/// </summary>
public sealed class Elsa3ExpressionRewriter
{
    public Elsa3ExpressionRewriteResult Rewrite(
        string language,
        string source,
        TypeReference resultType,
        Func<Elsa3AmbientReference, Elsa3ExpressionParameterResolution> resolve)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(resultType);
        ArgumentNullException.ThrowIfNull(resolve);

        return language switch
        {
            var value when value.Equals("JavaScript", StringComparison.OrdinalIgnoreCase) =>
                RewriteJavaScript(source, resultType, resolve),
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
        Func<Elsa3AmbientReference, Elsa3ExpressionParameterResolution> resolve)
    {
        Script script;
        try
        {
            script = new Parser().ParseScript(source);
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

        var rewritten = ApplyEdits(source, analyzer.Edits);
        return Elsa3ExpressionRewriteResult.Success(CreateDefinition("JavaScript", rewritten, resultType, analyzer.Parameters));
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
                "Move mutation into a graph-visible activity or Set intrinsic before import."));
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
        private static readonly HashSet<string> PureGlobalCalls = new(StringComparer.Ordinal)
        {
            "Boolean", "Number", "String", "parseFloat", "parseInt",
        };

        public Dictionary<string, ExpressionParameterBinding> Parameters { get; } = new(StringComparer.Ordinal);
        public List<SourceEdit> Edits { get; } = [];
        public List<Elsa3ExpressionRewriteIssue> Issues { get; } = [];

        public void Visit(Node node)
        {
            if (node is AssignmentExpression assignment && ContainsAmbientRoot(assignment.Left) ||
                node is UpdateExpression update && ContainsAmbientRoot(update.Argument))
            {
                Issues.Add(new Elsa3ExpressionRewriteIssue(
                    Elsa3ImportDiagnosticCodes.ForbiddenCapability,
                    "JavaScript mutates an Elsa 3 ambient value.",
                    "Move mutation into a graph-visible activity or Set intrinsic before import."));
                return;
            }

            if (node is CallExpression call && call.Callee is Identifier callee)
            {
                if (SetterCalls.Contains(callee.Name))
                {
                    Issues.Add(new Elsa3ExpressionRewriteIssue(
                        Elsa3ImportDiagnosticCodes.ForbiddenCapability,
                        $"JavaScript calls forbidden mutation helper '{callee.Name}'.",
                        "Move mutation into a graph-visible activity or Set intrinsic before import."));
                    return;
                }

                var kind = callee.Name switch
                {
                    "getVariable" => Elsa3AmbientReferenceKind.Variable,
                    "getInput" => Elsa3AmbientReferenceKind.WorkflowRequest,
                    "getOutput" => Elsa3AmbientReferenceKind.ActivityResult,
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

                if (!PureGlobalCalls.Contains(callee.Name))
                {
                    Issues.Add(new Elsa3ExpressionRewriteIssue(
                        Elsa3ImportDiagnosticCodes.ForbiddenCapability,
                        $"JavaScript calls unknown or unapproved host function '{callee.Name}'.",
                        "Replace the call with deterministic language syntax or move it into a graph-visible activity."));
                    return;
                }
            }

            if (node is CallExpression { Callee: MemberExpression calledMember } && IsNondeterministicCall(calledMember))
            {
                Issues.Add(new Elsa3ExpressionRewriteIssue(
                    Elsa3ImportDiagnosticCodes.ForbiddenCapability,
                    "JavaScript calls a nondeterministic time or random helper.",
                    "Supply time/random data as an explicit pinned input or move the operation into an activity."));
                return;
            }

            if (node is MemberExpression member && member.Object is Identifier root && TryGetAmbientKind(root.Name, out var memberKind))
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

            if (node is Identifier bareIdentifier && TryGetAmbientKind(bareIdentifier.Name, out _))
            {
                Issues.Add(DynamicIssue($"JavaScript ambient root '{bareIdentifier.Name}' is not followed by a static member name."));
                return;
            }

            foreach (var child in node.ChildNodes)
                Visit(child);
        }

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

        private static bool TryGetAmbientKind(string root, out Elsa3AmbientReferenceKind kind)
        {
            switch (root)
            {
                case "variables":
                    kind = Elsa3AmbientReferenceKind.Variable;
                    return true;
                case "input":
                case "inputs":
                    kind = Elsa3AmbientReferenceKind.WorkflowRequest;
                    return true;
                case "output":
                case "outputs":
                    kind = Elsa3AmbientReferenceKind.ActivityResult;
                    return true;
                default:
                    kind = default;
                    return false;
            }
        }

        private static bool ContainsAmbientRoot(Node node)
        {
            if (node is Identifier identifier &&
                (TryGetAmbientKind(identifier.Name, out _) || identifier.Name.Equals("args", StringComparison.Ordinal)))
                return true;
            foreach (var child in node.ChildNodes)
            {
                if (ContainsAmbientRoot(child))
                    return true;
            }
            return false;
        }

        private static bool IsNondeterministicCall(MemberExpression member) =>
            member.Object is Identifier root &&
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

public enum Elsa3AmbientReferenceKind
{
    WorkflowRequest,
    Variable,
    ActivityResult,
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

    public static Elsa3ExpressionRewriteResult Success(ExpressionDefinition definition) => new(definition, []);
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
