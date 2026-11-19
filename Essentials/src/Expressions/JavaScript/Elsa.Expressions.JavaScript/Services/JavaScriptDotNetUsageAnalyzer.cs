using Acornima;
using Acornima.Ast;
using Elsa.Expressions.JavaScript.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Services;

/// <summary>
/// The default strategy's .NET-usage analysis (spec 1223 FR-016 to FR-019, FR-030; data-model §3), shared by publish
/// validation and the Elsa 3 converter. See <see cref="IJavaScriptDotNetUsageAnalyzer"/> for the rules.
/// </summary>
public sealed class JavaScriptDotNetUsageAnalyzer(IEnumerable<JavaScriptHostFunctionDeclaration>? hostFunctions = null)
    : IJavaScriptDotNetUsageAnalyzer
{
    private const string JsonGuidance =
        "Elsa 4 expression values are JSON, and live .NET objects and types are not available to them. Rewrite it with JavaScript (Date, Math, Intl, Temporal, JSON, the string and array methods) or a built-in function.";

    /// <summary>.NET types scripts reached for in Elsa 3 (data-model §3.2); a JavaScript global is never listed.</summary>
    private static readonly HashSet<string> DotNetTypes = new(StringComparer.Ordinal)
    {
        "Guid", "DateTime", "DateTimeOffset", "DateOnly", "TimeOnly", "TimeSpan", "Convert", "Encoding", "Enumerable",
        "Queryable", "Regex", "StringBuilder", "CultureInfo", "TimeZoneInfo", "BitConverter", "File", "Directory", "Path",
        "Stream", "Uri",
    };

    /// <summary>JavaScript globals: an invocation on one is native, whatever the member's casing.</summary>
    private static readonly HashSet<string> JavaScriptGlobals = new(StringComparer.Ordinal)
    {
        "String", "Number", "Boolean", "Array", "Object", "Date", "Math", "JSON", "Intl", "Temporal", "RegExp", "Map", "Set",
        "WeakMap", "WeakSet", "Symbol", "BigInt", "Promise", "Error", "Function", "Proxy", "Reflect",
    };

    /// <summary>The <c>Array</c> and <c>Object</c> members a .NET list or dictionary answered like a JavaScript value.</summary>
    private static readonly HashSet<string> NativeCollectionMembers = new(StringComparer.Ordinal)
    {
        "at", "concat", "copyWithin", "entries", "every", "fill", "filter", "find", "findIndex", "findLast", "findLastIndex",
        "flat", "flatMap", "forEach", "includes", "indexOf", "join", "keys", "lastIndexOf", "map", "pop", "push", "reduce",
        "reduceRight", "reverse", "shift", "slice", "some", "sort", "splice", "toReversed", "toSorted", "toSpliced",
        "unshift", "values", "with", "toString", "toLocaleString", "valueOf", "hasOwnProperty", "isPrototypeOf",
        "propertyIsEnumerable",
    };

    private readonly HashSet<string> _namespaceRoots = (hostFunctions ?? [])
        .Select(function => function.Name)
        .Where(name => name.Contains('.', StringComparison.Ordinal))
        .Select(name => name[..name.IndexOf('.', StringComparison.Ordinal)])
        .ToHashSet(StringComparer.Ordinal);

    public IReadOnlyList<JavaScriptDotNetUsage>? Analyze(string source, bool scriptBody = false, JavaScriptDotNetUsageContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        Node parsed;
        try
        {
            parsed = scriptBody
                ? new Parser(new ParserOptions { AllowReturnOutsideFunction = true }).ParseScript(source)
                : JavaScriptBindingSyntax.IsExpression(source)
                    ? new Parser().ParseExpression(source)
                    : new Parser(new ParserOptions { AllowReturnOutsideFunction = true }).ParseScript(source);
        }
        catch (ParseErrorException)
        {
            return null;
        }

        var walker = new Walker(source, context, _namespaceRoots, parsed);
        walker.Visit(parsed);
        return walker.Usages;
    }

    private sealed class Walker
    {
        private readonly string _source;
        private readonly JavaScriptDotNetUsageContext? _context;
        private readonly IReadOnlySet<string> _namespaceRoots;
        private readonly HashSet<string> _declared;
        private readonly HashSet<string> _definedMembers;
        private readonly Dictionary<string, JavaScriptDotNetValueKind> _aliases = new(StringComparer.Ordinal);

        public Walker(string source, JavaScriptDotNetUsageContext? context, IReadOnlySet<string> namespaceRoots, Node root)
        {
            _source = source;
            _context = context;
            _namespaceRoots = namespaceRoots;
            _declared = JavaScriptReservedNames.CollectDeclarations(root, _ => true).Select(identifier => identifier.Name).ToHashSet(StringComparer.Ordinal);
            _definedMembers = new HashSet<string>(StringComparer.Ordinal);
            CollectDefinedMembers(root, _definedMembers);
        }

        public List<JavaScriptDotNetUsage> Usages { get; } = [];

        public void Visit(Node? node)
        {
            switch (node)
            {
                case null:
                    return;
                case VariableDeclaration declaration:
                    foreach (var declarator in declaration.Declarations)
                    {
                        Visit(declarator.Init);
                        // A const/let alias of a known value is followed (FR-016); a var, or a later reassignment, is not.
                        if (_context is not null && declaration.Kind is VariableDeclarationKind.Const or VariableDeclarationKind.Let &&
                            declarator.Id is Identifier alias)
                        {
                            if (declarator.Init is { } init && KnownKind(init) is { } kind)
                                _aliases[alias.Name] = kind;
                            else
                                _aliases.Remove(alias.Name);
                        }
                    }

                    return;
                case CallExpression { Callee: MemberExpression callee } call:
                    VisitMemberCall(call, callee);
                    return;
                case MemberExpression member:
                    VisitMemberRead(member);
                    return;
                case NewExpression { Callee: Identifier type } construction when IsDotNetType(type.Name):
                    AddType(construction, type.Name);
                    foreach (var argument in construction.Arguments)
                        Visit(argument);
                    return;
            }

            foreach (var child in node.ChildNodes)
                Visit(child);
        }

        private void VisitMemberCall(CallExpression call, MemberExpression callee)
        {
            // Member use on a value that was a live .NET object in Elsa 3 (conversion only).
            if (KnownKind(callee.Object) is { } kind)
            {
                var member = MemberName(callee);
                var allowed = kind switch
                {
                    JavaScriptDotNetValueKind.Guid => member == "toString" && call.Arguments.Count == 0,
                    JavaScriptDotNetValueKind.Collection => member is not null && NativeCollectionMembers.Contains(member),
                    _ => false
                };
                if (!allowed)
                    AddKnown(call, kind);
                VisitArguments(call);
                return;
            }

            if (callee.Object is Identifier type && IsDotNetType(type.Name))
            {
                AddType(call, type.Name);
                VisitArguments(call);
                return;
            }

            // JSON.stringify (and a converted toJson) of a known .NET object other than a GUID: Elsa 3 serialized its .NET
            // properties, which the Elsa 4 value does not reproduce.
            if (_context is not null && callee is { Computed: false, Object: Identifier { Name: "JSON" }, Property: Identifier { Name: "stringify" } } &&
                !_declared.Contains("JSON") && call.Arguments.Count > 0 && KnownKind(call.Arguments[0]) is { } serialized &&
                serialized != JavaScriptDotNetValueKind.Guid)
            {
                Usages.Add(Usage(call,
                    $"JavaScript serializes '{Quote(call.Arguments[0])}', which was a live .NET object in Elsa 3: Elsa 3 wrote its .NET properties, which the Elsa 4 JSON value does not have. {JsonGuidance}"));
                VisitArguments(call);
                return;
            }

            if (MemberName(callee) is { } invoked && IsPascalCase(invoked) && !_definedMembers.Contains(invoked) && !IsNativeReceiver(callee.Object))
            {
                Usages.Add(Usage(call,
                    $"JavaScript invokes '{invoked}' in '{Quote(call)}', a .NET-style member on a value the script did not define. {JsonGuidance}"));
                Visit(callee.Object);
                VisitArguments(call);
                return;
            }

            Visit(callee);
            VisitArguments(call);
        }

        private void VisitMemberRead(MemberExpression member)
        {
            if (KnownKind(member.Object) is { } kind)
            {
                // A list or dictionary answered property reads like a JavaScript value; anything else had no JSON member.
                if (kind != JavaScriptDotNetValueKind.Collection)
                    AddKnown(member, kind);
                else if (member.Computed)
                    Visit(member.Property);
                return;
            }

            if (member.Object is Identifier type && IsDotNetType(type.Name))
            {
                AddType(member, type.Name);
                return;
            }

            Visit(member.Object);
            if (member.Computed)
                Visit(member.Property);
        }

        private void VisitArguments(CallExpression call)
        {
            foreach (var argument in call.Arguments)
                Visit(argument);
        }

        /// <summary>What <paramref name="node"/> was in Elsa 3, when it is a known .NET-object value (conversion only).</summary>
        private JavaScriptDotNetValueKind? KnownKind(Node node)
        {
            if (_context is null)
                return null;
            switch (node)
            {
                case CallExpression { Callee: Identifier { Name: JavaScriptBuiltInFunctions.NewGuid or JavaScriptBuiltInFunctions.ParseGuid } callee }
                    when !_declared.Contains(callee.Name):
                    return JavaScriptDotNetValueKind.Guid;
                case Identifier alias when _aliases.TryGetValue(alias.Name, out var aliased):
                    return aliased;
                case MemberExpression { Object: Identifier root } read when ReadName(read) is { } name &&
                                                                            _context.KnownValues.TryGetValue($"{root.Name}.{name}", out var known):
                    return known;
                default:
                    return null;
            }
        }

        private static string? ReadName(MemberExpression read) => read switch
        {
            { Computed: false, Property: Identifier identifier } => identifier.Name,
            { Computed: true, Property: Literal { Value: string literal } } => literal,
            _ => null
        };

        private static string? MemberName(MemberExpression member) =>
            member is { Computed: false, Property: Identifier identifier } ? identifier.Name : null;

        private bool IsDotNetType(string name) =>
            DotNetTypes.Contains(name) && !_declared.Contains(name) && !_namespaceRoots.Contains(name);

        /// <summary>Whether the receiver is rooted at a JavaScript global or a registered function namespace.</summary>
        private bool IsNativeReceiver(Node receiver) =>
            RootIdentifier(receiver) is { } root &&
            (JavaScriptGlobals.Contains(root) && !_declared.Contains(root) || _namespaceRoots.Contains(root));

        private static string? RootIdentifier(Node node) => node switch
        {
            Identifier identifier => identifier.Name,
            MemberExpression member => RootIdentifier(member.Object),
            CallExpression { Callee: var callee } => RootIdentifier(callee),
            NewExpression { Callee: var callee } => RootIdentifier(callee),
            _ => null
        };

        private static bool IsPascalCase(string name) => name.Length > 0 && char.IsUpper(name[0]);

        private void AddType(Node node, string type) =>
            Usages.Add(Usage(node,
                $"JavaScript uses the .NET type '{type}' in '{Quote(node)}'. {JsonGuidance}"));

        private void AddKnown(Node node, JavaScriptDotNetValueKind kind) =>
            Usages.Add(Usage(node, kind switch
            {
                JavaScriptDotNetValueKind.Guid =>
                    $"JavaScript uses a member of a GUID in '{Quote(node)}'. Elsa 3 handed scripts a .NET Guid; Elsa 4 gives its canonical text. Only toString() with no arguments returns the same text. {JsonGuidance}",
                JavaScriptDotNetValueKind.Collection =>
                    $"JavaScript invokes a .NET member of a list or dictionary in '{Quote(node)}'. In Elsa 4 the value is a JSON array or object, with the JavaScript members only. {JsonGuidance}",
                _ =>
                    $"JavaScript uses a member of a value that was a live .NET object in Elsa 3, in '{Quote(node)}'. {JsonGuidance}",
            }));

        private JavaScriptDotNetUsage Usage(Node node, string message) => new(Quote(node), message, RangeOf(node));

        private static Elsa.Expressions.Core.Models.ExpressionToolingRange RangeOf(Node node) => new(
            new(Math.Max(0, node.Location.Start.Line - 1), Math.Max(0, node.Location.Start.Column)),
            new(Math.Max(0, node.Location.End.Line - 1), Math.Max(0, node.Location.End.Column)));

        private string Quote(Node node)
        {
            var text = _source[node.Start..node.End].ReplaceLineEndings(" ");
            return text.Length <= 160 ? text : text[..157] + "...";
        }

        /// <summary>Members the script defines: object-literal keys, class members and member assignments <c>x.M = ...</c>.</summary>
        private static void CollectDefinedMembers(Node node, HashSet<string> members)
        {
            switch (node)
            {
                case Property { Computed: false, Key: var key }:
                    AddKey(key, members);
                    break;
                case MethodDefinition { Computed: false, Key: var methodKey }:
                    AddKey(methodKey, members);
                    break;
                case PropertyDefinition { Computed: false, Key: var fieldKey }:
                    AddKey(fieldKey, members);
                    break;
                case AssignmentExpression { Left: MemberExpression { Computed: false, Property: Identifier assigned } }:
                    members.Add(assigned.Name);
                    break;
            }

            foreach (var child in node.ChildNodes)
                CollectDefinedMembers(child, members);
        }

        private static void AddKey(Node key, HashSet<string> members)
        {
            switch (key)
            {
                case Identifier identifier:
                    members.Add(identifier.Name);
                    break;
                case Literal { Value: string literal }:
                    members.Add(literal);
                    break;
            }
        }
    }
}
