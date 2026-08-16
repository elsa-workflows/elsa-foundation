using System.Collections.Immutable;
using System.Security.Claims;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Guards first-party endpoint authorization from bypassing Foundation Identity with the old
/// FastEndpoints permission helpers or direct permission-claim matching. The scan deliberately uses
/// Roslyn symbols and a small constant/alias data-flow pass; text matching would both miss aliases and
/// reject unrelated identity/session projection code.
/// </summary>
public sealed class PermissionAuthorizationBoundaryTests
{
    [Fact]
    public void First_party_endpoint_and_base_authorization_paths_have_no_permission_bypass()
    {
        var diagnostics = PermissionAuthorizationBoundaryScanner.Scan(RepoRoot);

        Assert.True(
            diagnostics.Count == 0,
            string.Join(Environment.NewLine, diagnostics.Select(Format)));
    }

    [Theory]
    [MemberData(nameof(MutationCases))]
    public void Intentional_permission_bypass_mutation_reports_the_expected_boundary(
        string relativePath,
        string source,
        string expectedCode)
    {
        using var fixture = new TemporaryDirectory();
        fixture.Write(relativePath, source);

        var diagnostics = PermissionAuthorizationBoundaryScanner.Scan(fixture.Path);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(expectedCode, diagnostic.Code);
        Assert.Equal(relativePath, diagnostic.Path);
    }

    [Fact]
    public void Transport_authorization_contexts_are_not_allowed_to_read_permission_claims_directly()
    {
        using var fixture = new TemporaryDirectory();
        fixture.Write(
            "src/Elsa/Foundation/Identity/Api/Endpoints/Token.cs",
            PermissionClaimProjectionFixture);
        fixture.Write(
            "src/Elsa/Activities/Design/Api/Services/HttpContextActivityDesignAuthorizationContext.cs",
            PermissionClaimProjectionFixture);
        fixture.Write(
            "src/Elsa/Workflows/Runtime/Api/Services/HttpContextActivityExecutionInspectionAuthorizationContext.cs",
            PermissionClaimProjectionFixture);

        var diagnostics = PermissionAuthorizationBoundaryScanner.Scan(fixture.Path);

        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, diagnostic => Assert.Equal("AUTHZ-DIRECT-PERMISSION-CLAIM", diagnostic.Code));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Path.EndsWith("HttpContextActivityDesignAuthorizationContext.cs", StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Path.EndsWith("HttpContextActivityExecutionInspectionAuthorizationContext.cs", StringComparison.Ordinal));
    }

    public static TheoryData<string, string, string> MutationCases => new()
    {
        {
            "src/Elsa/Api/FastEndpoints/Abstractions/InjectedPermissions.cs",
            """
            namespace FastEndpoints;

            public abstract class Endpoint
            {
                protected void Permissions(params string[] values) { }
            }

            namespace Injected;

            public sealed class Endpoint : FastEndpoints.Endpoint
            {
                public void Configure() => Permissions("read");
            }
            """,
            "AUTHZ-FASTENDPOINTS-PERMISSION"
        },
        {
            "src/Elsa/Api/FastEndpoints/Abstractions/InjectedPermissionsAll.cs",
            """
            namespace FastEndpoints;

            public abstract class Endpoint
            {
                protected void PermissionsAll(params string[] values) { }
            }

            namespace Injected;

            public sealed class Endpoint : FastEndpoints.Endpoint
            {
                public void Configure() => PermissionsAll("read");
            }
            """,
            "AUTHZ-FASTENDPOINTS-PERMISSION"
        },
        {
            "src/Elsa/Api/Capabilities/Endpoints/InjectedFindFirst.cs",
            """
            using System.Security.Claims;

            namespace Injected;

            public static class IdentityClaimTypes
            {
                public const string Permission = "elsa.identity.permission";
            }

            public sealed class Endpoint
            {
                public bool Authorize(ClaimsPrincipal user) =>
                    user.FindFirst(IdentityClaimTypes.Permission) is not null;
            }
            """,
            "AUTHZ-DIRECT-PERMISSION-CLAIM"
        },
        {
            "src/Elsa/Api/Capabilities/Endpoints/InjectedFindFirstValue.cs",
            """
            using System.Security.Claims;

            namespace Injected;

            public static class IdentityClaimTypes
            {
                public const string Permission = "elsa.identity.permission";
            }

            public static class ClaimExtensions
            {
                public static string? FindFirstValue(this ClaimsPrincipal principal, string type) => null;
            }

            public sealed class Endpoint
            {
                public bool Authorize(ClaimsPrincipal user) =>
                    user.FindFirstValue(IdentityClaimTypes.Permission) is not null;
            }
            """,
            "AUTHZ-DIRECT-PERMISSION-CLAIM"
        },
        {
            "src/Elsa/Api/Capabilities/Endpoints/InjectedFindAll.cs",
            """
            using System.Security.Claims;
            using System.Linq;

            namespace Injected;

            public static class IdentityClaimTypes
            {
                public const string Permission = "elsa.identity.permission";
            }

            public sealed class Endpoint
            {
                public bool Authorize(ClaimsPrincipal user) =>
                    user.FindAll(IdentityClaimTypes.Permission).Any();
            }
            """,
            "AUTHZ-DIRECT-PERMISSION-CLAIM"
        },
        {
            "src/Elsa/Api/Capabilities/Endpoints/InjectedHasClaim.cs",
            """
            using System.Security.Claims;

            namespace Injected;

            public static class IdentityClaimTypes
            {
                public const string Permission = "elsa.identity.permission";
            }

            public sealed class Endpoint
            {
                public bool Authorize(ClaimsPrincipal user) =>
                    user.HasClaim(IdentityClaimTypes.Permission, "read");
            }
            """,
            "AUTHZ-DIRECT-PERMISSION-CLAIM"
        },
        {
            "src/Elsa/Api/Capabilities/Endpoints/InjectedClaimsAny.cs",
            """
            using System.Linq;
            using System.Security.Claims;

            namespace Injected;

            public static class IdentityClaimTypes
            {
                public const string Permission = "elsa.identity.permission";
            }

            public sealed class Endpoint
            {
                public bool Authorize(ClaimsPrincipal user) =>
                    user.Claims.Any(claim =>
                        claim.Type == IdentityClaimTypes.Permission && claim.Value == "read");
            }
            """,
            "AUTHZ-DIRECT-PERMISSION-CLAIM"
        },
        {
            "src/Elsa/Api/Capabilities/Endpoints/InjectedAlias.cs",
            """
            using System.Linq;
            using System.Security.Claims;

            namespace Injected;

            public static class IdentityClaimTypes
            {
                public const string Permission = "elsa.identity.permission";
            }

            public sealed class Endpoint
            {
                public bool Authorize(ClaimsPrincipal user)
                {
                    var permissionType = IdentityClaimTypes.Permission;
                    return user.FindAll(permissionType).Any();
                }
            }
            """,
            "AUTHZ-DIRECT-PERMISSION-CLAIM"
        },
        {
            "src/Elsa/Api/Capabilities/Endpoints/InjectedProviderPermissionConstant.cs",
            """
            using System.Security.Claims;

            namespace Injected;

            public static class IdentityClaimTypes
            {
                public const string Permission = "elsa.identity.permission";
            }

            public static class PermissionNames
            {
                public const string WorkflowRead = "workflow.read";
            }

            public sealed class Endpoint
            {
                public bool Authorize(ClaimsPrincipal user) =>
                    user.HasClaim(IdentityClaimTypes.Permission, PermissionNames.WorkflowRead);
            }
            """,
            "AUTHZ-DIRECT-PERMISSION-CLAIM"
        }
    };

    private const string PermissionClaimProjectionFixture = """
        using System.Linq;
        using System.Security.Claims;

        namespace Injected;

        public static class IdentityClaimTypes
        {
            public const string Permission = "elsa.identity.permission";
        }

        public sealed class Projection
        {
            public string[] Project(ClaimsPrincipal principal) =>
                principal.FindAll(IdentityClaimTypes.Permission).Select(x => x.Value).ToArray();
        }
        """;

    private static string Format(PermissionAuthorizationDiagnostic diagnostic) =>
        $"{diagnostic.Code}: {diagnostic.Path}:{diagnostic.Line}: {diagnostic.Message}";

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
                directory = directory.Parent;

            return directory?.FullName
                   ?? throw new InvalidOperationException("Could not locate the Elsa Foundation repository root.");
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = System.IO.Path.Join(
            System.IO.Path.GetTempPath(),
            $"elsa-permission-boundary-{Guid.NewGuid():N}");

        public string Path { get; }

        public void Write(string relativePath, string content)
        {
            var normalizedRelativePath = relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar);
            if (System.IO.Path.IsPathRooted(normalizedRelativePath))
                throw new ArgumentException("The fixture path must be relative.", nameof(relativePath));

            var rootPath = System.IO.Path.GetFullPath(Path) + System.IO.Path.DirectorySeparatorChar;
            var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Join(rootPath, normalizedRelativePath));
            if (!fullPath.StartsWith(rootPath, StringComparison.Ordinal))
                throw new ArgumentException("The fixture path must remain inside the temporary directory.", nameof(relativePath));

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}

internal sealed record PermissionAuthorizationDiagnostic(
    string Code,
    string Path,
    int Line,
    string Message);

internal static class PermissionAuthorizationBoundaryScanner
{
    private const string PermissionClaimType = "elsa.identity.permission";

    private static readonly ImmutableHashSet<string> ClaimReaderNames =
        ImmutableHashSet.Create(StringComparer.Ordinal, "FindFirst", "FindFirstValue", "FindAll", "HasClaim");

    private static readonly ImmutableHashSet<string> ReviewedAllowlist =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "src/Elsa/Foundation/Identity/Abstractions/Authorization/AuthorizationContracts.cs",
            "src/Elsa/Foundation/Identity/Api/Endpoints/Token.cs",
            "src/Elsa/Foundation/Identity/Api/Services/ClaimsAuthSessionService.cs",
            "src/Elsa/Foundation/Identity/AspNetCoreIdentity/Services/DefaultAuthSessionService.cs",
            "src/Elsa/Foundation/Identity/AspNetCoreIdentity/Services/IdentityClaimsProjector.cs",
            "src/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Authentication/GroundworkIdentityCookieEvents.cs",
            "src/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Authentication/GroundworkIdentitySessionInvalidator.cs",
            "src/Elsa/Foundation/Identity/OpenIddict/Behavior/OpenIddictTokenService.cs");

    private static readonly ImmutableHashSet<string> AuthorizationContextPaths =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "src/Elsa/Activities/Design/Api/Services/HttpContextActivityDesignAuthorizationContext.cs",
            "src/Elsa/Workflows/Runtime/Api/Services/HttpContextActivityExecutionInspectionAuthorizationContext.cs");

    public static IReadOnlyList<PermissionAuthorizationDiagnostic> Scan(string repositoryRoot)
    {
        var diagnostics = new List<PermissionAuthorizationDiagnostic>();
        foreach (var file in EnumerateBoundaryFiles(repositoryRoot))
        {
            var relativePath = Path.GetRelativePath(repositoryRoot, file)
                .Replace(Path.DirectorySeparatorChar, '/');

            ScanFile(
                relativePath,
                File.ReadAllText(file),
                ReviewedAllowlist.Contains(relativePath),
                diagnostics);
        }

        return diagnostics
            .OrderBy(diagnostic => diagnostic.Path, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Line)
            .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateBoundaryFiles(string repositoryRoot)
    {
        var sourceRoot = Path.Join(repositoryRoot, "src", "Elsa");
        if (!Directory.Exists(sourceRoot))
            yield break;

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(repositoryRoot, file)
                .Replace(Path.DirectorySeparatorChar, '/');

            if (IsBoundaryPath(relativePath))
                yield return file;
        }
    }

    private static bool IsBoundaryPath(string relativePath) =>
        relativePath.StartsWith("src/Elsa/Api/FastEndpoints/Abstractions/", StringComparison.Ordinal) ||
        relativePath.Contains("/Endpoints/", StringComparison.Ordinal) ||
        ReviewedAllowlist.Contains(relativePath) ||
        AuthorizationContextPaths.Contains(relativePath);

    private static void ScanFile(
        string relativePath,
        string source,
        bool directClaimAllowlisted,
        ICollection<PermissionAuthorizationDiagnostic> diagnostics)
    {
        var tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            relativePath);
        var compilation = CSharpCompilation.Create(
            $"PermissionBoundary_{Guid.NewGuid():N}",
            [tree],
            MetadataReferences,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
        var aliases = PermissionAliasMap.Create(model, tree.GetRoot());

        foreach (var invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var method = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (IsFastEndpointsPermissionMethod(method))
            {
                Add(
                    diagnostics,
                    "AUTHZ-FASTENDPOINTS-PERMISSION",
                    relativePath,
                    tree,
                    invocation,
                    "First-party endpoint authorization must use Foundation Identity policy metadata instead of a FastEndpoints permission helper.");
                continue;
            }

            if (!directClaimAllowlisted && IsDirectPermissionClaimReader(invocation, method, model, aliases))
            {
                Add(
                    diagnostics,
                    "AUTHZ-DIRECT-PERMISSION-CLAIM",
                    relativePath,
                    tree,
                    invocation,
                    "First-party endpoint authorization must not read permission claims directly; use the shared Foundation Identity policy evaluator.");
            }
        }

        if (!directClaimAllowlisted)
        {
            foreach (var invocation in tree.GetRoot().DescendantNodes()
                         .OfType<InvocationExpressionSyntax>()
                         .Where(invocation => IsPermissionClaimsAny(invocation, model, aliases)))
            {
                Add(
                    diagnostics,
                    "AUTHZ-DIRECT-PERMISSION-CLAIM",
                    relativePath,
                    tree,
                    invocation,
                    "First-party endpoint authorization must not inspect Claims.Any for permission grants.");
            }
        }
    }

    private static bool IsFastEndpointsPermissionMethod(IMethodSymbol? method)
    {
        if (method is null || method.Name is not ("Permissions" or "PermissionsAll"))
            return false;

        var containingType = method.ContainingType;
        return containingType is not null &&
               (containingType.ContainingNamespace.ToDisplayString().StartsWith("FastEndpoints", StringComparison.Ordinal) ||
                method.ContainingAssembly?.Name.Contains("FastEndpoints", StringComparison.OrdinalIgnoreCase) == true ||
                HasFastEndpointsBase(containingType));
    }

    private static bool HasFastEndpointsBase(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ContainingNamespace.ToDisplayString().StartsWith("FastEndpoints", StringComparison.Ordinal) ||
                current.ContainingAssembly?.Name.Contains("FastEndpoints", StringComparison.OrdinalIgnoreCase) == true)
                return true;
        }

        return false;
    }

    private static bool IsDirectPermissionClaimReader(
        InvocationExpressionSyntax invocation,
        IMethodSymbol? method,
        SemanticModel model,
        PermissionAliasMap aliases)
    {
        if (method is null || !ClaimReaderNames.Contains(method.Name) || !IsClaimsApi(invocation, method, model))
            return false;

        return invocation.ArgumentList.Arguments.Any(argument => aliases.IsPermissionExpression(argument.Expression));
    }

    private static bool IsClaimsApi(InvocationExpressionSyntax invocation, IMethodSymbol method, SemanticModel model)
    {
        if (IsClaimsType(method.ContainingType))
            return true;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return false;

        return IsClaimsType(model.GetTypeInfo(memberAccess.Expression).Type);
    }

    private static bool IsPermissionClaimsAny(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        PermissionAliasMap aliases)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax anyAccess ||
            !StringComparer.Ordinal.Equals(anyAccess.Name.Identifier.ValueText, "Any") ||
            anyAccess.Expression is not MemberAccessExpressionSyntax claimsAccess ||
            !StringComparer.Ordinal.Equals(claimsAccess.Name.Identifier.ValueText, "Claims"))
        {
            return false;
        }

        var claimsSymbol = model.GetSymbolInfo(claimsAccess).Symbol;
        if (claimsSymbol is IPropertySymbol property &&
            !StringComparer.Ordinal.Equals(property.Name, "Claims"))
        {
            return false;
        }

        var propertySymbol = claimsSymbol as IPropertySymbol;
        if (!IsClaimsType(model.GetTypeInfo(claimsAccess.Expression).Type) &&
            (propertySymbol is null || !IsClaimsType(propertySymbol.ContainingType)))
        {
            return false;
        }

        return invocation.ArgumentList.Arguments
            .SelectMany(argument => argument.Expression.DescendantNodesAndSelf().OfType<ExpressionSyntax>())
            .Any(aliases.IsPermissionExpression);
    }

    private static bool IsClaimsType(ITypeSymbol? type)
    {
        if (type is null)
            return false;

        var displayName = type.ToDisplayString();
        return type.Name is "ClaimsPrincipal" or "ClaimsIdentity" ||
               displayName.StartsWith("System.Security.Claims.Claims", StringComparison.Ordinal);
    }

    private static void Add(
        ICollection<PermissionAuthorizationDiagnostic> diagnostics,
        string code,
        string relativePath,
        SyntaxTree tree,
        SyntaxNode node,
        string message)
    {
        var line = tree.GetLineSpan(node.Span).StartLinePosition.Line + 1;
        diagnostics.Add(new PermissionAuthorizationDiagnostic(code, relativePath, line, message));
    }

    private static ImmutableArray<MetadataReference> MetadataReferences { get; } = CreateMetadataReferences();

    private static ImmutableArray<MetadataReference> CreateMetadataReferences()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var references = ImmutableArray.CreateBuilder<MetadataReference>();

        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedPlatformAssemblies)
        {
            foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator))
                AddReference(path);
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()
                     .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location)))
            AddReference(assembly.Location);

        AddReference(typeof(ClaimsPrincipal).Assembly.Location);
        AddReference(typeof(FastEndpoints.Factory).Assembly.Location);

        return references.ToImmutable();

        void AddReference(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !paths.Add(path))
                return;

            references.Add(MetadataReference.CreateFromFile(path));
        }
    }

    private sealed class PermissionAliasMap
    {
        private readonly SemanticModel _model;
        private readonly HashSet<ISymbol> _aliases = new(SymbolEqualityComparer.Default);

        private PermissionAliasMap(SemanticModel model) => _model = model;

        public static PermissionAliasMap Create(SemanticModel model, SyntaxNode root)
        {
            var aliases = new PermissionAliasMap(model);
            var declarations = root.DescendantNodes().OfType<VariableDeclaratorSyntax>().ToArray();
            var assignments = root.DescendantNodes().OfType<AssignmentExpressionSyntax>().ToArray();

            var changed = true;
            while (changed)
            {
                changed = false;

                foreach (var declaration in declarations)
                {
                    if (declaration.Initializer is null ||
                        !aliases.IsPermissionExpression(declaration.Initializer.Value))
                    {
                        continue;
                    }

                    if (model.GetDeclaredSymbol(declaration) is { } symbol)
                        changed |= aliases._aliases.Add(symbol);
                }

                foreach (var assignment in assignments)
                {
                    if (!aliases.IsPermissionExpression(assignment.Right) ||
                        model.GetSymbolInfo(assignment.Left).Symbol is not { } symbol)
                    {
                        continue;
                    }

                    changed |= aliases._aliases.Add(symbol);
                }
            }

            return aliases;
        }

        public bool IsPermissionExpression(ExpressionSyntax expression)
        {
            var constant = _model.GetConstantValue(expression);
            if (constant.HasValue && constant.Value is string value &&
                StringComparer.Ordinal.Equals(value, PermissionClaimType))
            {
                return true;
            }

            var symbol = _model.GetSymbolInfo(expression).Symbol;
            if (symbol is null)
                return false;

            if (_aliases.Contains(symbol))
                return true;

            if (symbol is not IFieldSymbol field)
                return false;

            if (field.ConstantValue is string fieldValue &&
                StringComparer.Ordinal.Equals(fieldValue, PermissionClaimType))
            {
                return true;
            }

            return field.Name.Contains("Permission", StringComparison.OrdinalIgnoreCase) ||
                   field.ContainingType?.Name.Contains("Permission", StringComparison.OrdinalIgnoreCase) == true;
        }
    }
}
