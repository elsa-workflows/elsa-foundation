using Acornima;
using Acornima.Ast;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Models;
using JsExpression = Acornima.Ast.Expression;

namespace Elsa.Expressions.JavaScript.Services;

public sealed partial class JavaScriptExpressionToolingProvider
{
    private static IReadOnlyList<ExpressionDiagnostic> FindAmbientCapabilityDiagnostics(
        JsExpression expression,
        string source,
        string revision)
    {
        var collector = new ScopeCollector();
        collector.Visit(expression);

        var mutations = new IntrinsicRootMutationCollector(collector.NodeScopes);
        mutations.Visit(expression);

        var analyzer = new AmbientCapabilityVisitor(collector.NodeScopes, source, revision, mutations);
        analyzer.Visit(expression);
        return analyzer.Diagnostics;
    }

    private static JsExpression Unwrap(JsExpression expression) =>
        expression switch
        {
            ParenthesizedExpression parenthesized => Unwrap(parenthesized.Expression),
            ChainExpression chain => Unwrap(chain.Expression),
            _ => expression
        };

    private sealed class Scope
    {
        private readonly HashSet<string> _bindings = new(StringComparer.Ordinal);

        public Scope(Scope? parent, bool isFunctionScope)
        {
            Parent = parent;
            IsFunctionScope = isFunctionScope;
        }

        public Scope? Parent { get; }
        public bool IsFunctionScope { get; }
        public Scope FunctionScope => IsFunctionScope ? this : Parent!.FunctionScope;

        public void Declare(string name) => _bindings.Add(name);

        public bool IsBound(string name)
        {
            for (Scope? scope = this; scope is not null; scope = scope.Parent)
                if (scope._bindings.Contains(name))
                    return true;
            return false;
        }
    }

    private sealed class ScopeCollector : AstVisitor
    {
        private Scope _scope = new(null, isFunctionScope: true);

        public Dictionary<Node, Scope> NodeScopes { get; } = new(ReferenceEqualityComparer.Instance);

        public override object? Visit(Node node)
        {
            NodeScopes[node] = _scope;
            return base.Visit(node);
        }

        protected override object? VisitVariableDeclaration(VariableDeclaration node)
        {
            var target = node.Kind == VariableDeclarationKind.Var ? _scope.FunctionScope : _scope;
            foreach (var declaration in node.Declarations)
                DeclarePattern(declaration.Id, target);
            return base.VisitVariableDeclaration(node);
        }

        protected override object? VisitFunctionDeclaration(FunctionDeclaration node)
        {
            if (node.Id is not null)
                _scope.Declare(node.Id.Name);
            return VisitFunction(node.Id, node.Params, node.Body);
        }

        protected override object? VisitFunctionExpression(FunctionExpression node) =>
            VisitFunction(node.Id, node.Params, node.Body);

        protected override object? VisitArrowFunctionExpression(ArrowFunctionExpression node)
        {
            var parent = _scope;
            _scope = new(parent, isFunctionScope: true);
            try
            {
                foreach (var parameter in node.Params)
                {
                    DeclarePattern(parameter, _scope);
                    Visit(parameter);
                }
                if (node.Body is FunctionBody body)
                {
                    var parametersScope = _scope;
                    _scope = new(parametersScope, isFunctionScope: true);
                    new FunctionVarBindingCollector(_scope).Visit(body);
                    Visit(body);
                    _scope = parametersScope;
                }
                else
                    Visit(node.Body);
                return node;
            }
            finally
            {
                _scope = parent;
            }
        }

        protected override object? VisitFunctionBody(FunctionBody node) =>
            VisitBlockContents(node.Body);

        protected override object? VisitBlockStatement(BlockStatement node) =>
            VisitBlockContents(node.Body);

        protected override object? VisitCatchClause(CatchClause node) =>
            VisitWithinScope(() =>
            {
                if (node.Param is not null)
                {
                    DeclarePattern(node.Param, _scope);
                    Visit(node.Param);
                }
                Visit(node.Body);
            });

        protected override object? VisitForStatement(ForStatement node) => VisitWithinScope(() => base.VisitForStatement(node));
        protected override object? VisitForInStatement(ForInStatement node) => VisitWithinScope(() => base.VisitForInStatement(node));
        protected override object? VisitForOfStatement(ForOfStatement node) => VisitWithinScope(() => base.VisitForOfStatement(node));
        protected override object? VisitStaticBlock(StaticBlock node)
        {
            var parent = _scope;
            _scope = new(parent, isFunctionScope: true);
            try
            {
                new FunctionVarBindingCollector(_scope, node).Visit(node);
                return VisitBlockContents(node.Body);
            }
            finally
            {
                _scope = parent;
            }
        }

        protected override object? VisitSwitchStatement(SwitchStatement node)
        {
            Visit(node.Discriminant);
            var parent = _scope;
            _scope = new(parent, isFunctionScope: false);
            try
            {
                foreach (var @case in node.Cases)
                    PredeclareLexicalBindings(@case.Consequent, _scope);
                foreach (var @case in node.Cases)
                    Visit(@case);
                return node;
            }
            finally
            {
                _scope = parent;
            }
        }

        protected override object? VisitClassDeclaration(ClassDeclaration node)
        {
            if (node.Id is not null)
                _scope.Declare(node.Id.Name);
            return VisitClass(node.Id, node.SuperClass, node.Body);
        }

        protected override object? VisitClassExpression(ClassExpression node) =>
            VisitClass(node.Id, node.SuperClass, node.Body);

        private object? VisitFunction(Identifier? id, in NodeList<Node> parameters, FunctionBody body)
        {
            var parent = _scope;
            _scope = new(parent, isFunctionScope: true);
            try
            {
                if (id is not null)
                {
                    _scope.Declare(id.Name);
                    Visit(id);
                }
                for (var index = 0; index < parameters.Count; index++)
                {
                    DeclarePattern(parameters[index], _scope);
                    Visit(parameters[index]);
                }
                var parametersScope = _scope;
                _scope = new(parametersScope, isFunctionScope: true);
                new FunctionVarBindingCollector(_scope).Visit(body);
                Visit(body);
                return body;
            }
            finally
            {
                _scope = parent;
            }
        }

        private object? VisitClass(Identifier? id, JsExpression? superClass, ClassBody body)
        {
            var parent = _scope;
            _scope = new(parent, isFunctionScope: false);
            try
            {
                if (id is not null)
                {
                    _scope.Declare(id.Name);
                    Visit(id);
                }
                if (superClass is not null)
                    Visit(superClass);
                Visit(body);
                return body;
            }
            finally
            {
                _scope = parent;
            }
        }

        private object? VisitWithinScope(Action visit, bool isFunctionScope = false)
        {
            var parent = _scope;
            _scope = new(parent, isFunctionScope);
            try
            {
                visit();
                return null;
            }
            finally
            {
                _scope = parent;
            }
        }

        private object? VisitBlockContents(in NodeList<Statement> statements)
        {
            var parent = _scope;
            _scope = new(parent, isFunctionScope: false);
            try
            {
                PredeclareLexicalBindings(statements, _scope);
                for (var index = 0; index < statements.Count; index++)
                    Visit(statements[index]);
                return null;
            }
            finally
            {
                _scope = parent;
            }
        }

        private static void PredeclareLexicalBindings(in NodeList<Statement> statements, Scope scope)
        {
            for (var index = 0; index < statements.Count; index++)
            {
                switch (statements[index])
                {
                    case VariableDeclaration { Kind: not VariableDeclarationKind.Var } declaration:
                        foreach (var declarator in declaration.Declarations)
                            DeclarePattern(declarator.Id, scope);
                        break;
                    case FunctionDeclaration { Id: not null } declaration:
                        scope.Declare(declaration.Id.Name);
                        break;
                    case ClassDeclaration { Id: not null } declaration:
                        scope.Declare(declaration.Id.Name);
                        break;
                }
            }
        }

        private static void DeclarePattern(Node? pattern, Scope scope)
        {
            switch (pattern)
            {
                case Identifier identifier:
                    scope.Declare(identifier.Name);
                    break;
                case RestElement rest:
                    DeclarePattern(rest.Argument, scope);
                    break;
                case AssignmentPattern assignment:
                    DeclarePattern(assignment.Left, scope);
                    break;
                case ArrayPattern array:
                    foreach (var element in array.Elements)
                        DeclarePattern(element, scope);
                    break;
                case ObjectPattern objectPattern:
                    foreach (var property in objectPattern.Properties)
                    {
                        if (property is AssignmentProperty assignmentProperty)
                            DeclarePattern(assignmentProperty.Value, scope);
                        else
                            DeclarePattern(property, scope);
                    }
                    break;
            }
        }

        private sealed class FunctionVarBindingCollector(Scope functionScope, StaticBlock? rootStaticBlock = null) : AstVisitor
        {
            protected override object? VisitVariableDeclaration(VariableDeclaration node)
            {
                if (node.Kind == VariableDeclarationKind.Var)
                    foreach (var declaration in node.Declarations)
                        DeclarePattern(declaration.Id, functionScope);
                return node;
            }

            protected override object? VisitFunctionDeclaration(FunctionDeclaration node) => node;
            protected override object? VisitFunctionExpression(FunctionExpression node) => node;
            protected override object? VisitArrowFunctionExpression(ArrowFunctionExpression node) => node;
            protected override object? VisitClassDeclaration(ClassDeclaration node) => node;
            protected override object? VisitClassExpression(ClassExpression node) => node;
            protected override object? VisitStaticBlock(StaticBlock node) =>
                ReferenceEquals(node, rootStaticBlock) ? base.VisitStaticBlock(node) : node;
        }
    }

    private sealed class IntrinsicRootMutationCollector(IReadOnlyDictionary<Node, Scope> nodeScopes) : AstVisitor
    {
        // This is a conservative syntax-only identity guard, not control-flow analysis. A root write anywhere in
        // the expression prevents claiming that qualified paths still denote the selected intrinsic roots.
        public bool MathRootMayBeReplaced { get; private set; }
        public bool GlobalThisMayBeReplaced { get; private set; }

        public override object? Visit(Node node)
        {
            if (node is UpdateExpression update)
                RecordPotentialReplacement(update.Argument);
            return base.Visit(node);
        }

        protected override object? VisitAssignmentExpression(AssignmentExpression node)
        {
            RecordPotentialReplacement(node.Left);
            return base.VisitAssignmentExpression(node);
        }

        protected override object? VisitUnaryExpression(UnaryExpression node)
        {
            if (node.Operator == Operator.Delete)
                RecordPotentialReplacement(node.Argument);
            return base.VisitUnaryExpression(node);
        }

        protected override object? VisitForInStatement(ForInStatement node)
        {
            if (node.Left is not VariableDeclaration)
                RecordPotentialReplacement(node.Left);
            return base.VisitForInStatement(node);
        }

        protected override object? VisitForOfStatement(ForOfStatement node)
        {
            if (node.Left is not VariableDeclaration)
                RecordPotentialReplacement(node.Left);
            return base.VisitForOfStatement(node);
        }

        private void RecordPotentialReplacement(Node target)
        {
            switch (target)
            {
                case Identifier identifier:
                    if (!nodeScopes[identifier].IsBound(identifier.Name))
                    {
                        MathRootMayBeReplaced |= identifier.Name == "Math";
                        GlobalThisMayBeReplaced |= identifier.Name == "globalThis";
                    }
                    break;
                case MemberExpression member when TryGetMutationPath(member, out var path):
                    if (!nodeScopes[member].IsBound("globalThis"))
                    {
                        MathRootMayBeReplaced |= path == "globalThis.Math";
                        GlobalThisMayBeReplaced |= path == "globalThis.globalThis";
                    }
                    break;
                case MemberExpression { Computed: true } member
                    when Unwrap(member.Object) is Identifier { Name: "globalThis" } && !nodeScopes[member].IsBound("globalThis"):
                    // An unresolved direct key could replace either writable intrinsic root; do not evaluate it.
                    MathRootMayBeReplaced = true;
                    GlobalThisMayBeReplaced = true;
                    break;
                case RestElement rest:
                    RecordPotentialReplacement(rest.Argument);
                    break;
                case AssignmentPattern assignment:
                    RecordPotentialReplacement(assignment.Left);
                    break;
                case ArrayPattern array:
                    foreach (var element in array.Elements)
                        if (element is not null)
                            RecordPotentialReplacement(element);
                    break;
                case ObjectPattern objectPattern:
                    foreach (var property in objectPattern.Properties)
                        if (property is AssignmentProperty assignmentProperty)
                            RecordPotentialReplacement(assignmentProperty.Value);
                        else
                            RecordPotentialReplacement(property);
                    break;
            }
        }

        private static bool TryGetMutationPath(JsExpression expression, out string path)
        {
            expression = JavaScriptExpressionToolingProvider.Unwrap(expression);
            switch (expression)
            {
                case Identifier identifier:
                    path = identifier.Name;
                    return true;
                case MemberExpression member when TryGetMutationPath(member.Object, out var prefix):
                    var propertyName = member.Property switch
                    {
                        Identifier property when !member.Computed => property.Name,
                        Literal { Value: string literal } when member.Computed => literal,
                        _ => null
                    };
                    if (propertyName is not null)
                    {
                        path = $"{prefix}.{propertyName}";
                        return true;
                    }
                    break;
            }

            path = string.Empty;
            return false;
        }
    }

    private sealed partial class AmbientCapabilityVisitor(
        IReadOnlyDictionary<Node, Scope> nodeScopes,
        string source,
        string revision,
        IntrinsicRootMutationCollector mutations) : AstVisitor
    {
        private readonly HashSet<Node> _safeTypeofOperands = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<Node> _skippedTypeofSubtrees = new(ReferenceEqualityComparer.Instance);

        public List<ExpressionDiagnostic> Diagnostics { get; } = [];

        public override object? Visit(Node node)
        {
            if (_skippedTypeofSubtrees.Contains(node))
                return node;
            return base.Visit(node);
        }

        protected override object? VisitIdentifier(Identifier node)
        {
            if (!_safeTypeofOperands.Contains(node) &&
                nodeScopes[node].IsBound(node.Name) is false &&
                FindCapability(node.Name) is not null)
                AddDiagnostic(node);
            return node;
        }

        protected override object? VisitUnaryExpression(UnaryExpression node)
        {
            if (node.Operator == Operator.TypeOf)
            {
                var facts = AnalyzeTypeofProbe(node.Argument);
                foreach (var safeNode in facts.SafeNodes)
                    _safeTypeofOperands.Add(safeNode);
                foreach (var skippedNode in facts.SkippedNodes)
                    _skippedTypeofSubtrees.Add(skippedNode);
                try
                {
                    Visit(node.Argument);
                }
                finally
                {
                    foreach (var safeNode in facts.SafeNodes)
                        _safeTypeofOperands.Remove(safeNode);
                    foreach (var skippedNode in facts.SkippedNodes)
                        _skippedTypeofSubtrees.Remove(skippedNode);
                }
                return node;
            }
            return base.VisitUnaryExpression(node);
        }

        protected override object? VisitMemberExpression(MemberExpression node)
        {
            if (!_safeTypeofOperands.Contains(node) &&
                !node.Computed &&
                IsUnavailablePath(node, out var capability) &&
                !ShouldWithholdDiagnostic(capability))
                AddDiagnostic(node);

            Visit(node.Object);
            if (node.Computed)
                Visit(node.Property);
            return node;
        }

        protected override object? VisitLabeledStatement(LabeledStatement node)
        {
            Visit(node.Body);
            return node;
        }

        protected override object? VisitBreakStatement(BreakStatement node) => node;

        protected override object? VisitContinueStatement(ContinueStatement node) => node;

        protected override object? VisitForInStatement(ForInStatement node)
        {
            if (node.Left is VariableDeclaration)
                Visit(node.Left);
            else
                VisitAssignmentTarget(node.Left);
            Visit(node.Right);
            Visit(node.Body);
            return node;
        }

        protected override object? VisitForOfStatement(ForOfStatement node)
        {
            if (node.Left is VariableDeclaration)
                Visit(node.Left);
            else
                VisitAssignmentTarget(node.Left);
            Visit(node.Right);
            Visit(node.Body);
            return node;
        }

        protected override object? VisitVariableDeclarator(VariableDeclarator node)
        {
            VisitBindingInitializers(node.Id);
            if (node.Init is not null)
                Visit(node.Init);
            return node;
        }

        protected override object? VisitAssignmentExpression(AssignmentExpression node)
        {
            if (node.Left is ArrayPattern or ObjectPattern)
            {
                VisitAssignmentTarget(node.Left);
                Visit(node.Right);
                return node;
            }
            return base.VisitAssignmentExpression(node);
        }

        private bool ShouldWithholdDiagnostic(JavaScriptAmbientCapability capability) =>
            (capability.Path.StartsWith("globalThis.", StringComparison.Ordinal) && mutations.GlobalThisMayBeReplaced) ||
            (capability.DisplayName == "Math.random" && mutations.MathRootMayBeReplaced);

        protected override object? VisitAssignmentPattern(AssignmentPattern node)
        {
            Visit(node.Right);
            return node;
        }

        protected override object? VisitRestElement(RestElement node) => node;

        protected override object? VisitArrayPattern(ArrayPattern node)
        {
            foreach (var element in node.Elements)
                if (element is AssignmentPattern assignment)
                    Visit(assignment);
                else if (element is ArrayPattern or ObjectPattern)
                    Visit(element);
            return node;
        }

        protected override object? VisitObjectPattern(ObjectPattern node)
        {
            foreach (var property in node.Properties)
                if (property is AssignmentProperty assignment)
                    Visit(assignment);
                else if (property is RestElement rest && rest.Argument is ArrayPattern or ObjectPattern)
                    Visit(rest.Argument);
            return node;
        }

        protected override object? VisitAssignmentProperty(AssignmentProperty node)
        {
            if (node.Computed)
                Visit(node.Key);
            Visit(node.Value);
            return node;
        }

        protected override object? VisitObjectProperty(ObjectProperty node)
        {
            if (node.Computed)
                Visit(node.Key);
            Visit(node.Value);
            return node;
        }

        protected override object? VisitMethodDefinition(MethodDefinition node)
        {
            if (node.Computed)
                Visit(node.Key);
            Visit(node.Value);
            return node;
        }

        protected override object? VisitPropertyDefinition(PropertyDefinition node)
        {
            if (node.Computed)
                Visit(node.Key);
            if (node.Value is not null)
                Visit(node.Value);
            return node;
        }

        protected override object? VisitAccessorProperty(AccessorProperty node)
        {
            if (node.Computed)
                Visit(node.Key);
            if (node.Value is not null)
                Visit(node.Value);
            return node;
        }

        protected override object? VisitFunctionDeclaration(FunctionDeclaration node)
        {
            VisitParameters(node.Params);
            Visit(node.Body);
            return node;
        }

        protected override object? VisitFunctionExpression(FunctionExpression node)
        {
            VisitParameters(node.Params);
            Visit(node.Body);
            return node;
        }

        protected override object? VisitArrowFunctionExpression(ArrowFunctionExpression node)
        {
            VisitParameters(node.Params);
            Visit(node.Body);
            return node;
        }

        protected override object? VisitClassDeclaration(ClassDeclaration node)
        {
            if (node.SuperClass is not null)
                Visit(node.SuperClass);
            Visit(node.Body);
            return node;
        }

        protected override object? VisitClassExpression(ClassExpression node)
        {
            if (node.SuperClass is not null)
                Visit(node.SuperClass);
            Visit(node.Body);
            return node;
        }

        protected override object? VisitCatchClause(CatchClause node)
        {
            if (node.Param is not null)
                VisitBindingInitializers(node.Param);
            Visit(node.Body);
            return node;
        }

        private void VisitParameters(in NodeList<Node> parameters)
        {
            for (var index = 0; index < parameters.Count; index++)
                VisitBindingInitializers(parameters[index]);
        }

        private void VisitBindingInitializers(Node pattern)
        {
            switch (pattern)
            {
                case AssignmentPattern assignment:
                    Visit(assignment.Right);
                    VisitBindingInitializers(assignment.Left);
                    break;
                case RestElement rest:
                    VisitBindingInitializers(rest.Argument);
                    break;
                case ArrayPattern array:
                    foreach (var element in array.Elements)
                        if (element is not null)
                            VisitBindingInitializers(element);
                    break;
                case ObjectPattern objectPattern:
                    foreach (var property in objectPattern.Properties)
                    {
                        switch (property)
                        {
                            case AssignmentProperty assignmentProperty:
                                if (assignmentProperty.Computed)
                                    Visit(assignmentProperty.Key);
                                VisitBindingInitializers(assignmentProperty.Value);
                                break;
                            case RestElement rest:
                                VisitBindingInitializers(rest.Argument);
                                break;
                        }
                    }
                    break;
            }
        }

        private void VisitAssignmentTarget(Node target)
        {
            switch (target)
            {
                case Identifier identifier:
                    Visit(identifier);
                    break;
                case MemberExpression member:
                    Visit(member);
                    break;
                case RestElement rest:
                    VisitAssignmentTarget(rest.Argument);
                    break;
                case AssignmentPattern assignment:
                    Visit(assignment.Right);
                    VisitAssignmentTarget(assignment.Left);
                    break;
                case ArrayPattern array:
                    foreach (var element in array.Elements)
                        if (element is not null)
                            VisitAssignmentTarget(element);
                    break;
                case ObjectPattern objectPattern:
                    foreach (var property in objectPattern.Properties)
                    {
                        switch (property)
                        {
                            case AssignmentProperty assignmentProperty:
                                if (assignmentProperty.Computed)
                                    Visit(assignmentProperty.Key);
                                VisitAssignmentTarget(assignmentProperty.Value);
                                break;
                            case RestElement rest:
                                VisitAssignmentTarget(rest.Argument);
                                break;
                        }
                    }
                    break;
            }
        }

        private bool IsUnavailablePath(JsExpression expression, out JavaScriptAmbientCapability capability)
        {
            if (!TryGetStaticPath(expression, out var path))
            {
                capability = null!;
                return false;
            }

            var separator = path.IndexOf('.');
            var root = separator >= 0 ? path[..separator] : path;
            if (nodeScopes[expression].IsBound(root))
            {
                capability = null!;
                return false;
            }

            capability = FindCapability(path)!;
            return capability is not null;
        }

        private static bool TryGetStaticPath(JsExpression expression, out string path)
        {
            expression = Unwrap(expression);
            switch (expression)
            {
                case Identifier identifier:
                    path = identifier.Name;
                    return true;
                case MemberExpression { Computed: false, Property: Identifier property } member
                    when TryGetStaticPath(member.Object, out var prefix):
                    path = $"{prefix}.{property.Name}";
                    return true;
                default:
                    path = string.Empty;
                    return false;
            }
        }

        private static JavaScriptAmbientCapability? FindCapability(string path) =>
            JavaScriptRuntimeProfile.UnavailableAmbientCapabilities
                .FirstOrDefault(candidate => string.Equals(candidate.Path, path, StringComparison.Ordinal));

        private void AddDiagnostic(Node node)
        {
            var start = ToPosition(source, node.Start);
            var end = ToPosition(source, node.End);
            Diagnostics.Add(new(
                "JavaScript/AmbientCapability",
                ExpressionDiagnosticSeverity.Error,
                "This JavaScript runtime profile does not provide the referenced ambient capability. Supply the value as an input or variable.",
                revision,
                new(start, end)));
        }

        private static ExpressionToolingPosition ToPosition(string text, int offset)
        {
            var line = 0;
            var character = 0;
            var boundedOffset = Math.Clamp(offset, 0, text.Length);
            for (var index = 0; index < boundedOffset; index++)
            {
                if (text[index] is '\r' or '\n')
                {
                    if (text[index] == '\r' && index + 1 < boundedOffset && text[index + 1] == '\n')
                        index++;
                    line++;
                    character = 0;
                }
                else
                    character++;
            }
            return new(line, character);
        }
    }
}
