using Elsa.Api.Compatibility.Testing.Manifests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Api.Compatibility.Testing.Transitions;

/// <summary>Discovers exact FastEndpoints registrations from source for transition reconciliation.</summary>
public sealed class FastEndpointsRegistrationScanner
{
    private static readonly IReadOnlyDictionary<string, string> HttpMethods = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Get"] = "GET", ["Post"] = "POST", ["Put"] = "PUT", ["Patch"] = "PATCH",
        ["Delete"] = "DELETE", ["Head"] = "HEAD", ["Options"] = "OPTIONS", ["Connect"] = "CONNECT",
        ["Send"] = "*"
    };

    public IReadOnlyList<FastEndpointsRegistration> Scan(IEnumerable<FastEndpointsSourceDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var sourceDocuments = documents.Where(document => !IsBuildArtifact(document.SourcePath ?? document.Identity)).ToArray();
        var constants = RouteConstantIndex.Create(sourceDocuments);
        var parsedDocuments = sourceDocuments.Select(document => new ParsedSourceDocument(
                document,
                CSharpSyntaxTree.ParseText(document.Content, path: document.SourcePath ?? document.Identity)))
            .ToArray();
        var types = new FastEndpointsTypeIndex(parsedDocuments.SelectMany(document => document.Types));
        var ownerFingerprints = sourceDocuments
            .GroupBy(document => document.Owner ?? "<unknown>", StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => Fingerprint(group.OrderBy(document => document.Identity, StringComparer.Ordinal)
                    .Select(document => document.Identity + "\n" + Normalize(document.Content))),
                StringComparer.Ordinal);
        return parsedDocuments.SelectMany(document => Scan(
                document,
                constants,
                ownerFingerprints[document.Source.Owner ?? "<unknown>"],
                types))
            .OrderBy(registration => registration.Identity, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<FastEndpointsRegistration> Scan(FastEndpointsSourceDocument document) =>
        Scan([document]).ToArray();

    private static IReadOnlyList<FastEndpointsRegistration> Scan(
        ParsedSourceDocument document,
        RouteConstantIndex constants,
        string ownerFingerprint,
        FastEndpointsTypeIndex types)
    {
        ArgumentNullException.ThrowIfNull(document);
        var sourceHash = ownerFingerprint;
        var registrations = new List<FastEndpointsRegistration>();
        foreach (var type in document.Types.Where(types.IsFastEndpointsType).Where(type => !type.IsAbstract))
        {
            var routes = new List<EndpointIdentity>();
            var dynamicRoute = false;
            foreach (var invocation in types.GetEffectiveConfigureMethods(type)
                         .SelectMany(method => method.DescendantNodes().OfType<InvocationExpressionSyntax>())
                         .Where(invocation => TryGetHttpMethod(invocation.Expression, out _)))
            {
                TryGetHttpMethod(invocation.Expression, out var method);

                var routeExpression = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                if (constants.TryResolve(routeExpression, document.Source.Owner, out var route))
                {
                    routes.Add(new EndpointIdentity(route, method));
                }
                else
                {
                    dynamicRoute = true;
                }
            }

            var identity = type.Identity;
            registrations.Add(new FastEndpointsRegistration(
                identity,
                document.Source.Owner ?? "<unknown>",
                routes.Distinct().OrderBy(route => route.Route.Value, StringComparer.Ordinal).ThenBy(route => route.Method.Value, StringComparer.Ordinal).ToArray(),
                document.Source.DynamicallyUnloadable,
                dynamicRoute,
                document.Source.SourcePath ?? document.Source.Identity,
                sourceHash));
        }

        return registrations;
    }

    private static string Normalize(string source) => source.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Fingerprint(IEnumerable<string> values) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Join("\n\u001e\n", values)))).ToLowerInvariant();

    private static bool TryGetHttpMethod(ExpressionSyntax expression, out string method)
    {
        var name = expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            _ => string.Empty
        };
        return HttpMethods.TryGetValue(name, out method!);
    }

    public IReadOnlyList<FastEndpointsRegistration> Scan(string source, string owner = "<unknown>", string sourceIdentity = "<memory>", bool dynamicallyUnloadable = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Scan(new FastEndpointsSourceDocument(sourceIdentity, source, owner, dynamicallyUnloadable));
    }

    private static string GetTypeIdentity(ClassDeclarationSyntax declaration)
    {
        var names = new Stack<string>();
        SyntaxNode? node = declaration;
        while (node is not null)
        {
            switch (node)
            {
                case ClassDeclarationSyntax type:
                    names.Push(type.Identifier.ValueText);
                    break;
                case NamespaceDeclarationSyntax @namespace:
                    names.Push(@namespace.Name.ToString());
                    break;
                case FileScopedNamespaceDeclarationSyntax fileNamespace:
                    names.Push(fileNamespace.Name.ToString());
                    break;
            }
            node = node.Parent;
        }

        return string.Join('.', names);
    }

    private static bool IsBuildArtifact(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "bin" or "obj");
    }

    private sealed class ParsedSourceDocument
    {
        public ParsedSourceDocument(FastEndpointsSourceDocument source, SyntaxTree tree)
        {
            Source = source;
            Types = tree.GetCompilationUnitRoot().DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Select(declaration => new FastEndpointsType(
                    GetTypeIdentity(declaration),
                    source.Owner ?? "<unknown>",
                    declaration,
                    declaration.BaseList?.Types.Select(baseType => new FastEndpointsTypeReference(
                        NormalizeTypeName(baseType.Type), GetGenericArity(baseType.Type))).ToArray() ?? []))
                .ToArray();
        }

        public FastEndpointsSourceDocument Source { get; }
        public IReadOnlyList<FastEndpointsType> Types { get; }
    }

    private sealed class FastEndpointsTypeIndex
    {
        private static readonly string[] KnownEndpointBaseNames = ["Endpoint", "EndpointWithoutRequest", "EndpointWithMapper"];
        private readonly IReadOnlyDictionary<(string Owner, string Identity, int Arity), FastEndpointsType> _byIdentity;
        private readonly IReadOnlyDictionary<(string Owner, string Name, int Arity), FastEndpointsType[]> _bySimpleName;
        private readonly IReadOnlyDictionary<(string Identity, int Arity), FastEndpointsType> _byGlobalIdentity;
        private readonly IReadOnlyDictionary<(string Name, int Arity), FastEndpointsType[]> _byGlobalSimpleName;

        public FastEndpointsTypeIndex(IEnumerable<FastEndpointsType> types)
        {
            var allTypes = types.ToArray();
            _byIdentity = allTypes
                .GroupBy(type => (type.Owner, type.Identity, type.GenericArity))
                .ToDictionary(group => group.Key, group => group.First());
            _bySimpleName = allTypes
                .GroupBy(type => (type.Owner, SimpleTypeName(type.Identity), type.GenericArity))
                .ToDictionary(group => group.Key, group => group.ToArray());
            _byGlobalIdentity = allTypes
                .GroupBy(type => (type.Identity, type.GenericArity))
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single());
            _byGlobalSimpleName = allTypes
                .GroupBy(type => (SimpleTypeName(type.Identity), type.GenericArity))
                .ToDictionary(group => group.Key, group => group.ToArray());
        }

        public bool IsFastEndpointsType(FastEndpointsType type) => IsFastEndpointsType(type, []);

        public IReadOnlyList<MethodDeclarationSyntax> GetEffectiveConfigureMethods(FastEndpointsType type)
        {
            var methods = new List<MethodDeclarationSyntax>();
            CollectEffectiveConfigureMethods(type, [], methods);
            return methods;
        }

        private void CollectEffectiveConfigureMethods(
            FastEndpointsType type,
            HashSet<string> visited,
            List<MethodDeclarationSyntax> methods)
        {
            if (!visited.Add($"{type.Identity}`{type.GenericArity}"))
                return;

            var configureMethods = type.Declaration.Members.OfType<MethodDeclarationSyntax>()
                .Where(method => method.Identifier.ValueText == "Configure" && method.ParameterList.Parameters.Count == 0)
                .ToArray();
            if (configureMethods.Length == 0)
            {
                if (ResolveBaseType(type) is { } inherited)
                    CollectEffectiveConfigureMethods(inherited, visited, methods);
                return;
            }

            methods.AddRange(configureMethods);
            if (configureMethods.Any(CallsBaseConfigure) && ResolveBaseType(type) is { } baseType)
                CollectEffectiveConfigureMethods(baseType, visited, methods);
        }

        private bool IsFastEndpointsType(FastEndpointsType type, HashSet<string> visited)
        {
            if (!visited.Add($"{type.Identity}`{type.GenericArity}"))
                return false;

            foreach (var baseType in type.BaseTypes)
            {
                if (IsKnownEndpointBase(baseType))
                    return true;

                if (ResolveBaseType(type, baseType) is { } inherited && IsFastEndpointsType(inherited, visited))
                    return true;
            }

            return false;
        }

        private FastEndpointsType? ResolveBaseType(FastEndpointsType type) =>
            type.BaseTypes.Select(baseType => ResolveBaseType(type, baseType)).FirstOrDefault(candidate => candidate is not null);

        private FastEndpointsType? ResolveBaseType(FastEndpointsType type, FastEndpointsTypeReference baseType)
        {
            var owner = type.Owner;
            var identity = baseType.Identity.TrimStart('.');
            if (_byIdentity.TryGetValue((owner, identity, baseType.GenericArity), out var exact))
                return exact;

            var candidates = _bySimpleName.GetValueOrDefault((owner, SimpleTypeName(identity), baseType.GenericArity)) ?? [];
            if (candidates.Length == 1)
                return candidates[0];

            if (_byGlobalIdentity.TryGetValue((identity, baseType.GenericArity), out var globalExact))
                return globalExact;

            var globalCandidates = _byGlobalSimpleName.GetValueOrDefault((SimpleTypeName(identity), baseType.GenericArity)) ?? [];
            return globalCandidates.Length == 1 ? globalCandidates[0] : null;
        }

        private static bool IsKnownEndpointBase(FastEndpointsTypeReference baseType)
        {
            var simpleName = SimpleTypeName(baseType.Identity);
            return KnownEndpointBaseNames.Contains(simpleName, StringComparer.Ordinal) ||
                   simpleName.StartsWith("ElsaEndpoint", StringComparison.Ordinal);
        }

        private static bool CallsBaseConfigure(MethodDeclarationSyntax method) => method.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation => invocation.Expression is MemberAccessExpressionSyntax member &&
                               member.Expression is BaseExpressionSyntax &&
                               member.Name.Identifier.ValueText == "Configure");

        private static string SimpleTypeName(string name) => name.Split('.').Last();
    }

    private sealed record FastEndpointsType(
        string Identity,
        string Owner,
        ClassDeclarationSyntax Declaration,
        IReadOnlyList<FastEndpointsTypeReference> BaseTypes)
    {
        public int GenericArity => Declaration.TypeParameterList?.Parameters.Count ?? 0;
        public bool IsAbstract => Declaration.Modifiers.Any(SyntaxKind.AbstractKeyword);
    }

    private sealed record FastEndpointsTypeReference(string Identity, int GenericArity);

    private static int GetGenericArity(TypeSyntax type) => type switch
    {
        GenericNameSyntax generic => generic.TypeArgumentList.Arguments.Count,
        QualifiedNameSyntax qualified => GetGenericArity(qualified.Right),
        AliasQualifiedNameSyntax aliasQualified => GetGenericArity(aliasQualified.Name),
        _ => 0
    };

    private static string NormalizeTypeName(TypeSyntax type)
    {
        var name = type.ToString().Replace("global::", string.Empty, StringComparison.Ordinal);
        var genericStart = name.IndexOf('<');
        return (genericStart < 0 ? name : name[..genericStart]).Trim();
    }

    private sealed class RouteConstantIndex
    {
        private readonly Dictionary<string, SymbolDefinition> _definitions = new(StringComparer.Ordinal);
        private readonly HashSet<string> _ambiguous = new(StringComparer.Ordinal);

        public static RouteConstantIndex Create(IEnumerable<FastEndpointsSourceDocument> documents)
        {
            var index = new RouteConstantIndex();
            foreach (var document in documents)
            {
                var root = CSharpSyntaxTree.ParseText(document.Content).GetCompilationUnitRoot();
                foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
                {
                    var containingType = field.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
                    foreach (var variable in field.Declaration.Variables.Where(variable => variable.Initializer?.Value is not null))
                    {
                        var expression = variable.Initializer!.Value;
                        index.Add(document.Owner, containingType, variable.Identifier.ValueText, expression);
                    }
                }

                foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
                {
                    var expression = property.ExpressionBody?.Expression ?? property.AccessorList?.Accessors
                        .SelectMany(accessor => accessor.Body?.Statements ?? [])
                        .OfType<ReturnStatementSyntax>()
                        .Select(statement => statement.Expression)
                        .FirstOrDefault(value => value is not null);
                    if (expression is not null)
                        index.Add(document.Owner, property.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault(),
                            property.Identifier.ValueText, expression);
                }
            }
            return index;
        }

        public bool TryResolve(ExpressionSyntax? expression, string? owner, out string value) =>
            TryEvaluate(expression, Owner(owner), null, new HashSet<string>(StringComparer.Ordinal), out value);

        private bool TryEvaluate(
            ExpressionSyntax? expression,
            string owner,
            string? scopeType,
            HashSet<string> resolving,
            out string value)
        {
            switch (expression)
            {
                case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
                    value = literal.Token.ValueText;
                    return true;
                case IdentifierNameSyntax identifier:
                    return TryGet(owner, identifier.Identifier.ValueText, scopeType, resolving, out value);
                case MemberAccessExpressionSyntax member:
                    return TryGet(owner, member.ToString(), null, resolving, out value);
                case ParenthesizedExpressionSyntax parenthesized:
                    return TryEvaluate(parenthesized.Expression, owner, scopeType, resolving, out value);
                case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression) &&
                                                        TryEvaluate(binary.Left, owner, scopeType, resolving, out var left) &&
                                                        TryEvaluate(binary.Right, owner, scopeType, resolving, out var right):
                    value = left + right;
                    return true;
                case InterpolatedStringExpressionSyntax interpolated:
                    var parts = new List<string>();
                    foreach (var content in interpolated.Contents)
                    {
                        if (content is InterpolatedStringTextSyntax text)
                        {
                            parts.Add(text.TextToken.ValueText);
                            continue;
                        }
                        if (content is InterpolationSyntax interpolation &&
                            TryEvaluate(interpolation.Expression, owner, scopeType, resolving, out var part))
                        {
                            parts.Add(part);
                            continue;
                        }
                        value = string.Empty;
                        return false;
                    }
                    value = string.Concat(parts);
                    return true;
                case InvocationExpressionSyntax invocation when TryEvaluateRouteHelper(
                    invocation, owner, scopeType, resolving, out value):
                    return true;
                default:
                    value = string.Empty;
                    return false;
            }
        }

        private bool TryEvaluateRouteHelper(
            InvocationExpressionSyntax invocation,
            string owner,
            string? scopeType,
            HashSet<string> resolving,
            out string value)
        {
            var (helperType, helperName) = invocation.Expression switch
            {
                MemberAccessExpressionSyntax member => (member.Expression.ToString(), member.Name.Identifier.ValueText),
                IdentifierNameSyntax identifier => (scopeType, identifier.Identifier.ValueText),
                _ => (null, string.Empty)
            };
            if (helperName != "GetRoute" || helperType is null ||
                invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is not { } argument ||
                !TryEvaluate(argument, owner, scopeType, resolving, out var suffix))
            {
                value = string.Empty;
                return false;
            }

            var prefix = new[] { "DomainPrefix", "Prefix", "BasePath" }
                .Select(prefixName => TryGet(owner, $"{helperType}.{prefixName}", null, resolving, out var candidate) ? candidate : null)
                .FirstOrDefault(candidate => candidate is not null);
            if (prefix is not null)
            {
                value = $"{prefix.TrimEnd('/')}/{suffix.TrimStart('/')}";
                return true;
            }

            value = string.Empty;
            return false;
        }

        private bool TryGet(
            string owner,
            string key,
            string? scopeType,
            HashSet<string> resolving,
            out string value)
        {
            var candidates = scopeType is null ? new[] { key } : new[] { $"{scopeType}.{key}", key };
            foreach (var scopedKey in candidates
                         .Select(candidate => Key(owner, candidate))
                         .Where(scopedKey => !_ambiguous.Contains(scopedKey) &&
                                             _definitions.ContainsKey(scopedKey) &&
                                             resolving.Add(scopedKey)))
            {
                var definition = _definitions[scopedKey];
                try
                {
                    if (TryEvaluate(definition.Expression, owner, definition.ScopeType, resolving, out value))
                        return true;
                }
                finally
                {
                    resolving.Remove(scopedKey);
                }
            }

            value = string.Empty;
            return false;
        }

        private void Add(string? owner, ClassDeclarationSyntax? containingType, string name, ExpressionSyntax expression)
        {
            var scopeType = containingType?.Identifier.ValueText;
            Add(Owner(owner), name, new SymbolDefinition(expression, scopeType));
            if (containingType is null)
                return;
            Add(Owner(owner), $"{containingType.Identifier.ValueText}.{name}", new SymbolDefinition(expression, scopeType));
            Add(Owner(owner), $"{GetTypeIdentity(containingType)}.{name}", new SymbolDefinition(expression, scopeType));
        }

        private void Add(string owner, string key, SymbolDefinition definition)
        {
            var scopedKey = Key(owner, key);
            if (_ambiguous.Contains(scopedKey))
                return;
            if (_definitions.TryGetValue(scopedKey, out var existing) && existing.Expression.ToString() != definition.Expression.ToString())
            {
                _definitions.Remove(scopedKey);
                _ambiguous.Add(scopedKey);
                return;
            }
            _definitions[scopedKey] = definition;
        }

        private static string Owner(string? owner) => owner ?? "<unknown>";
        private static string Key(string owner, string symbol) => owner + "\0" + symbol;
        private sealed record SymbolDefinition(ExpressionSyntax Expression, string? ScopeType);
    }
}

public sealed record FastEndpointsSourceDocument(
    string Identity,
    string Content,
    string? Owner = null,
    bool DynamicallyUnloadable = false,
    string? SourcePath = null);

public sealed record FastEndpointsRegistration(
    string Identity,
    string Owner,
    IReadOnlyList<EndpointIdentity> Endpoints,
    bool DynamicallyUnloadable = false,
    bool DynamicRoute = false,
    string? SourcePath = null,
    string SourceHash = "")
{
    public IReadOnlyList<string> Routes => Endpoints.Select(endpoint => endpoint.Route.Value).Distinct(StringComparer.Ordinal).ToArray();
    public IReadOnlyList<string> Methods => Endpoints.Select(endpoint => endpoint.Method.Value).Distinct(StringComparer.Ordinal).OrderBy(method => method, StringComparer.Ordinal).ToArray();
}

public sealed record FastEndpointsTransitionException(
    string RegistrationIdentity,
    string Owner,
    IReadOnlyList<EndpointIdentity> Endpoints,
    string RemovalOwner,
    string FollowUp,
    bool DynamicallyUnloadable = false,
    string SourceHash = "");
