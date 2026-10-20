using Acornima;
using Acornima.Ast;
using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.JavaScript.Services;

/// <summary>
/// Finds the <c>Date</c> forms binding-pure-v2 removes, without evaluating the source, and reports each with the same
/// guidance the sandbox throws (ADR 0088 D4, spec 260 FR-019). Authored JavaScript publishes under binding-pure-v2.
/// </summary>
internal static class ClocklessDateValidator
{
    public const string DiagnosticCode = "JavaScript/ClocklessDate";

    /// <summary>Diagnostics for a source that already parsed; the grammar selection matches the runtime's.</summary>
    public static IReadOnlyList<ExpressionDiagnostic> Validate(string source, string revision)
    {
        Node root = JavaScriptBindingSyntax.IsExpression(source)
            ? new Parser().ParseExpression(source, strict: true)
            : new Parser(new ParserOptions { AllowReturnOutsideFunction = true }).ParseScript(source, strict: true);
        var diagnostics = new List<ExpressionDiagnostic>();
        Walk(root, diagnostics, revision);
        return diagnostics;
    }

    private static void Walk(Node node, List<ExpressionDiagnostic> diagnostics, string revision)
    {
        var rule = node switch
        {
            NewExpression { Callee: Identifier { Name: "Date" }, Arguments.Count: 0 } => ClocklessDateRules.NoArgumentConstruction,
            CallExpression { Callee: Identifier { Name: "Date" } } => ClocklessDateRules.FunctionCall,
            CallExpression { Callee: MemberExpression { Object: Identifier { Name: "Date" }, Property: Identifier { Name: "now" }, Computed: false } } => ClocklessDateRules.Now,
            CallExpression { Callee: MemberExpression { Property: Identifier property, Computed: false } }
                when ClocklessDateRules.LocaleMethods.TryGetValue(property.Name, out var locale) => locale,
            _ => null
        };
        if (rule is not null)
            diagnostics.Add(ToDiagnostic(node, rule, revision));

        foreach (var child in node.ChildNodes)
            Walk(child, diagnostics, revision);
    }

    private static ExpressionDiagnostic ToDiagnostic(Node node, ClocklessDateRule rule, string revision) =>
        new(
            DiagnosticCode,
            rule.IsWarning ? ExpressionDiagnosticSeverity.Warning : ExpressionDiagnosticSeverity.Error,
            rule.Message,
            revision,
            new(
                new(Math.Max(0, node.Location.Start.Line - 1), node.Location.Start.Column),
                new(Math.Max(0, node.Location.End.Line - 1), node.Location.End.Column)));
}
