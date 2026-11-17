using Elsa.Expressions.Core.Contracts;
using Fluid;
using Fluid.Ast;
using Fluid.Values;

namespace Elsa.Expressions.Liquid.Services;

/// <summary>
/// Parses a Liquid template with Fluid and reads its variable reads (spec 1222 FR-005, FR-014): <c>variables.X</c> and
/// <c>variables['X']</c> are literal reads; a non-literal index, or <c>variables</c> used on its own (assigned,
/// iterated), makes the template read variables it does not name.
/// </summary>
internal sealed class LiquidVariableReadAnalyzer(FluidParser parser) : IScriptVariableReadAnalyzer
{
    private const string VariablesRoot = "variables";

    public string Language => "Liquid";

    public ScriptVariableReadAnalysis? Analyze(string source, bool scriptBody = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!parser.TryParse(source, out var template, out _))
            return null;

        var visitor = new ReadVisitor();
        visitor.VisitTemplate(template);
        return new ScriptVariableReadAnalysis(visitor.LiteralNames, visitor.HasComputedRead);
    }

    private sealed class ReadVisitor : AstVisitor
    {
        public List<string> LiteralNames { get; } = [];
        public bool HasComputedRead { get; private set; }

        protected override Expression VisitMemberExpression(MemberExpression memberExpression)
        {
            var segments = memberExpression.Segments;
            if (segments.Count > 0 && segments[0] is IdentifierSegment { Identifier: VariablesRoot })
            {
                if (segments.Count == 1)
                    HasComputedRead = true;
                else if (segments[1] is IdentifierSegment member)
                    LiteralNames.Add(member.Identifier);
                else if (segments[1] is IndexerSegment { Expression: LiteralExpression { Value: StringValue literal } })
                    LiteralNames.Add(literal.ToStringValue());
                else
                    HasComputedRead = true;
            }

            return base.VisitMemberExpression(memberExpression);
        }
    }
}
