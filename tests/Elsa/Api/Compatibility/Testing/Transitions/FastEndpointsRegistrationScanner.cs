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
        var sourceDocuments = documents.ToArray();
        var constants = RouteConstantIndex.Create(sourceDocuments);
        var ownerFingerprints = sourceDocuments
            .GroupBy(document => document.Owner ?? "<unknown>", StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => Fingerprint(group.OrderBy(document => document.Identity, StringComparer.Ordinal)
                    .Select(document => document.Identity + "\n" + Normalize(document.Content))),
                StringComparer.Ordinal);
        return sourceDocuments.SelectMany(document => Scan(
                document,
                constants,
                ownerFingerprints[document.Owner ?? "<unknown>"]))
            .OrderBy(registration => registration.Identity, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<FastEndpointsRegistration> Scan(FastEndpointsSourceDocument document) =>
        Scan(document, RouteConstantIndex.Create([document]), Fingerprint([Normalize(document.Content)]));

    private static IReadOnlyList<FastEndpointsRegistration> Scan(
        FastEndpointsSourceDocument document,
        RouteConstantIndex constants,
        string ownerFingerprint)
    {
        ArgumentNullException.ThrowIfNull(document);
        var tree = CSharpSyntaxTree.ParseText(document.Content, path: document.SourcePath ?? document.Identity);
        var root = tree.GetCompilationUnitRoot();
        var sourceHash = ownerFingerprint;
        var registrations = new List<FastEndpointsRegistration>();
        foreach (var declaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (!IsFastEndpointsType(declaration))
                continue;

            var routes = new List<EndpointIdentity>();
            var dynamicRoute = false;
            foreach (var invocation in declaration.Members.OfType<MethodDeclarationSyntax>()
                         .Where(method => method.Identifier.ValueText == "Configure")
                         .SelectMany(method => method.DescendantNodes().OfType<InvocationExpressionSyntax>()))
            {
                if (!TryGetHttpMethod(invocation.Expression, out var method))
                    continue;

                var routeExpression = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                if (constants.TryResolve(routeExpression, document.Owner, out var route))
                {
                    routes.Add(new EndpointIdentity(route, method));
                }
                else
                {
                    dynamicRoute = true;
                }
            }

            var identity = GetTypeIdentity(declaration);
            registrations.Add(new FastEndpointsRegistration(
                identity,
                document.Owner ?? "<unknown>",
                routes.Distinct().OrderBy(route => route.Route.Value, StringComparer.Ordinal).ThenBy(route => route.Method.Value, StringComparer.Ordinal).ToArray(),
                document.DynamicallyUnloadable,
                dynamicRoute,
                document.SourcePath ?? document.Identity,
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

    private static bool IsFastEndpointsType(ClassDeclarationSyntax declaration)
    {
        if (declaration.BaseList is null)
            return false;

        return declaration.BaseList.Types
            .Select(type => type.Type.ToString().Split('<')[0].Split('.').Last())
            .Any(type => type is "Endpoint" or "EndpointWithoutRequest" or "EndpointWithMapper" || type.StartsWith("ElsaEndpoint", StringComparison.Ordinal));
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
                    foreach (var variable in field.Declaration.Variables)
                    {
                        if (variable.Initializer?.Value is not { } expression)
                            continue;
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

            foreach (var prefixName in new[] { "DomainPrefix", "Prefix", "BasePath" })
            {
                if (!TryGet(owner, $"{helperType}.{prefixName}", null, resolving, out var prefix))
                    continue;
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
            foreach (var candidate in candidates)
            {
                var scopedKey = Key(owner, candidate);
                if (_ambiguous.Contains(scopedKey) || !_definitions.TryGetValue(scopedKey, out var definition) ||
                    !resolving.Add(scopedKey))
                    continue;
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
