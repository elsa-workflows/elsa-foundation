using Acornima;
using Acornima.Ast;
using Elsa.Expressions.JavaScript.Core.Models;
using Jint.Runtime;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>
/// Attributes a Jint runtime failure to a withheld ambient capability only when its source range identifies the
/// failing syntax and that syntax has an unshadowed, statically known capability path.
/// </summary>
internal static class StrippedAmbientCapabilityExceptionClassifier
{
    public static bool TryClassify(
        string source,
        JavaScriptException exception,
        int evaluationPrefixLength,
        out string capabilityName)
    {
        capabilityName = string.Empty;
        if (!TryMapLocation(exception.Location, evaluationPrefixLength, out var failureLocation))
            return false;

        Expression expression;
        try
        {
            expression = new Parser().ParseExpression(source, strict: true);
        }
        catch (ParseErrorException)
        {
            return false;
        }

        var bindings = new BoundNameCollector();
        bindings.Visit(expression);

        var references = new FailingCapabilityCollector(failureLocation, bindings.Names);
        references.Visit(expression);
        if (references.Matches.Count != 1)
            return false;

        capabilityName = references.Matches.Single();
        return true;
    }

    private static bool TryMapLocation(SourceLocation wrappedLocation, int evaluationPrefixLength, out SourceLocation sourceLocation)
    {
        sourceLocation = default;
        var start = wrappedLocation.Start;
        var end = wrappedLocation.End;
        if (start.Line < 1 || end.Line < 1 || start > end)
            return false;

        var startColumn = start.Column;
        var endColumn = end.Column;
        if (start.Line == 1)
            startColumn -= evaluationPrefixLength;
        if (end.Line == 1)
            endColumn -= evaluationPrefixLength;
        if (startColumn < 0 || endColumn < 0)
            return false;

        var mappedStart = Position.From(start.Line, startColumn);
        var mappedEnd = Position.From(end.Line, endColumn);
        if (mappedStart > mappedEnd)
            return false;

        sourceLocation = SourceLocation.From(mappedStart, mappedEnd);
        return true;
    }

    private sealed class FailingCapabilityCollector(SourceLocation failureLocation, IReadOnlySet<string> boundNames) : AstVisitor
    {
        private readonly HashSet<string> _matches = new(StringComparer.Ordinal);

        public IReadOnlySet<string> Matches => _matches;

        public override object? Visit(Node node)
        {
            switch (node)
            {
                case Identifier identifier when SameRange(identifier.Location, failureLocation):
                    Add(identifier.Name);
                    break;
                case CallExpression call when SameRange(call.Location, failureLocation):
                    Add(call.Callee);
                    break;
                case NewExpression creation when SameRange(creation.Location, failureLocation):
                    Add(creation.Callee);
                    break;
            }

            return base.Visit(node);
        }

        protected override object? VisitMemberExpression(MemberExpression node)
        {
            if (!node.Computed &&
                (SameRange(node.Location, failureLocation) || SameRange(node.Property.Location, failureLocation)))
                Add(node.Object);

            Visit(node.Object);
            if (node.Computed)
                Visit(node.Property);
            return node;
        }

        private void Add(string rootName)
        {
            if (!boundNames.Contains(rootName))
                AddCapability(rootName);
        }

        private void Add(Expression expression)
        {
            if (TryGetStaticPath(expression, out var rootName, out var path) && !boundNames.Contains(rootName))
                AddCapability(path);
        }

        private void AddCapability(string path)
        {
            var capability = FindCapability(path);
            if (capability is not null)
                _matches.Add(capability.DisplayName);
        }
    }

    private sealed class BoundNameCollector : AstVisitor
    {
        private readonly HashSet<string> _names = new(StringComparer.Ordinal);

        public IReadOnlySet<string> Names => _names;

        public override object? Visit(Node node)
        {
            switch (node)
            {
                case VariableDeclarator declaration:
                    AddPattern(declaration.Id);
                    break;
                case FunctionDeclaration function:
                    Add(function.Id);
                    AddParameters(function.Params);
                    break;
                case FunctionExpression function:
                    Add(function.Id);
                    AddParameters(function.Params);
                    break;
                case ArrowFunctionExpression function:
                    AddParameters(function.Params);
                    break;
                case ClassDeclaration declaration:
                    Add(declaration.Id);
                    break;
                case ClassExpression expression:
                    Add(expression.Id);
                    break;
                case CatchClause clause:
                    AddPattern(clause.Param);
                    break;
            }

            return base.Visit(node);
        }

        private void AddParameters(in NodeList<Node> parameters)
        {
            foreach (var parameter in parameters)
                AddPattern(parameter);
        }

        private void Add(Identifier? identifier)
        {
            if (identifier is not null)
                _names.Add(identifier.Name);
        }

        private void AddPattern(Node? pattern)
        {
            switch (pattern)
            {
                case Identifier identifier:
                    _names.Add(identifier.Name);
                    break;
                case RestElement rest:
                    AddPattern(rest.Argument);
                    break;
                case AssignmentPattern assignment:
                    AddPattern(assignment.Left);
                    break;
                case ArrayPattern array:
                    foreach (var element in array.Elements)
                        AddPattern(element);
                    break;
                case ObjectPattern objectPattern:
                    foreach (var property in objectPattern.Properties)
                    {
                        if (property is AssignmentProperty assignmentProperty)
                            AddPattern(assignmentProperty.Value);
                        else
                            AddPattern(property);
                    }
                    break;
            }
        }
    }

    private static bool TryGetStaticPath(Expression expression, out string rootName, out string path)
    {
        switch (expression)
        {
            case Identifier identifier:
                rootName = identifier.Name;
                path = rootName;
                return true;
            case MemberExpression { Computed: false, Property: Identifier property } member
                when TryGetStaticPath(member.Object, out rootName, out var objectPath):
                path = $"{objectPath}.{property.Name}";
                return true;
            default:
                rootName = string.Empty;
                path = string.Empty;
                return false;
        }
    }

    private static JavaScriptAmbientCapability? FindCapability(string path)
    {
        JavaScriptAmbientCapability? match = null;
        foreach (var capability in JavaScriptRuntimeProfile.UnavailableAmbientCapabilities)
        {
            if (!path.Equals(capability.Path, StringComparison.Ordinal) &&
                !path.StartsWith($"{capability.Path}.", StringComparison.Ordinal))
                continue;

            if (match is null || capability.Path.Length > match.Path.Length)
                match = capability;
            else if (capability.Path.Length == match.Path.Length &&
                     !capability.DisplayName.Equals(match.DisplayName, StringComparison.Ordinal))
                return null;
        }

        return match;
    }

    private static bool SameRange(SourceLocation left, SourceLocation right) =>
        left.Start == right.Start && left.End == right.End;
}
