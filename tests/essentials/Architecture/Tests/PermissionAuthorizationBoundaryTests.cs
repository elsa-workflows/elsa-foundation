using System.Runtime.Loader;
using Elsa.Foundation.Identity.Core.Authorization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Guards Foundation Identity's permission authorization from being bypassed: first-party code decides permissions
/// through <c>IPermissionAuthorizationService</c> and the policy metadata it evaluates, never through a FastEndpoints
/// permission helper or by reading permission claims itself. <see cref="PermissionBypassScanner"/> inspects the IL of
/// every packable runtime library, so the rule follows the compiled code wherever an endpoint or authorization
/// context lives rather than a folder convention (framework constitution §2.23.2).
/// </summary>
public sealed class PermissionAuthorizationBoundaryTests
{
    private const string FixtureAssembly = "Elsa.PermissionBypass.Fixture";

    /// <summary>
    /// The only code allowed to read permission claims: the shared evaluator and the projections that copy the
    /// normalized grants into a session or a token. Each entry must still exempt a read, so the list cannot outlive
    /// the code it excuses.
    /// </summary>
    internal static readonly ReviewedPermissionClaimReader[] ReviewedReaders =
    [
        new("Elsa.Foundation.Identity", "Elsa.Foundation.Identity.Authorization.ClaimsPermissionEvaluator", null,
            "The shared evaluator behind IPermissionAuthorizationService: matching granted permission claims is its job."),
        new("Elsa.Foundation.Identity.Api", "Elsa.Foundation.Identity.Api.FoundationIdentityApi", "HandleTokenAsync",
            "Token issuance copies the normalized principal's grants into the issued token."),
        new("Elsa.Foundation.Identity.Api", "Elsa.Foundation.Identity.Api.Services.ClaimsAuthSessionService", null,
            "Projects the normalized principal's grants into the session the client displays."),
        new("Elsa.Foundation.Identity.AspNetCoreIdentity", "Elsa.Foundation.Identity.AspNetCoreIdentity.Services.DefaultAuthSessionService", null,
            "Projects the normalized principal's grants into the session the client displays.")
    ];

    /// <summary>The HTTP authorization contexts, which must ask the authorization service rather than read grants.</summary>
    private static readonly (string Assembly, string Type)[] AuthorizationContexts =
    [
        ("Elsa.Activities.Design.Api", "Elsa.Activities.Design.Api.Services.HttpContextActivityDesignAuthorizationContext"),
        ("Elsa.Workflows.Runtime.Api", "Elsa.Workflows.Runtime.Api.Services.HttpContextActivityExecutionInspectionAuthorizationContext")
    ];

    private static readonly Lazy<PermissionBypassScan> FirstPartyScan =
        new(() => PermissionBypassScanner.Scan(RuntimeLibraries.Assemblies, ReviewedReaders));

    [Fact]
    public void First_party_runtime_libraries_have_no_permission_bypass()
    {
        var findings = FirstPartyScan.Value.Findings;

        Assert.True(findings.Count == 0, string.Join(Environment.NewLine, findings));
    }

    [Fact]
    public void The_scan_inspects_every_runtime_library_endpoint_and_authorization_context()
    {
        var scan = FirstPartyScan.Value;

        // Every packable runtime library loaded and scanned; one that fails to load would otherwise drop out silently.
        Assert.Equal(RuntimeLibraries.Packable.Count, scan.Assemblies);
        Assert.True(scan.Assemblies >= 100, $"Only {scan.Assemblies} runtime libraries were scanned.");
        Assert.True(scan.MethodBodies >= 50_000, $"Only {scan.MethodBodies} method bodies were scanned.");
        Assert.True(scan.EndpointTypes >= 150, $"Only {scan.EndpointTypes} endpoint types were scanned.");
        Assert.True(scan.ClaimLookupsByType.Values.Sum() >= 20, $"Only {scan.ClaimLookupsByType.Values.Sum()} claim lookups were seen.");
        foreach (var (assembly, type) in AuthorizationContexts)
        {
            Assert.True(
                scan.ClaimLookupsByType.GetValueOrDefault(PermissionBypassScan.TypeKey(assembly, type)) > 0,
                $"The scan saw no claim lookup in {type}, so it is not judging that context's claim reads.");
            Assert.DoesNotContain(ReviewedReaders, reader => reader.Type == type);
        }
    }

    [Fact]
    public void Every_reviewed_permission_claim_reader_still_reads_permission_claims()
    {
        var exempted = FirstPartyScan.Value.Exempted.Select(entry => entry.Reader).ToHashSet();

        Assert.Empty(ReviewedReaders.Where(reader => !exempted.Contains(reader)));
    }

    [Theory]
    [MemberData(nameof(BypassCaseNames))]
    public void A_planted_permission_bypass_is_reported(string caseName)
    {
        var (source, code, method) = BypassCases[caseName];

        var scan = ScanFixture(FixtureAssembly, source);

        var finding = Assert.Single(scan.Findings);
        Assert.Equal(code, finding.Code);
        Assert.Equal(FixtureAssembly, finding.Assembly);
        Assert.Equal("Injected.Endpoint", finding.Type);
        Assert.Equal(method, finding.Method);
    }

    [Fact]
    public void Claim_reads_that_do_not_decide_a_permission_are_not_reported()
    {
        var scan = ScanFixture(FixtureAssembly, """
            using System.Security.Claims;
            using System.Threading.Tasks;
            using Elsa.Foundation.Identity.Core.Authorization;

            namespace Injected;

            public sealed class Endpoint(IPermissionAuthorizationService authorization)
            {
                public const string ReadPermission = "workflow.read";

                public string? Tenant(ClaimsPrincipal user) =>
                    user.FindFirst(IdentityClaimTypes.TenantId)?.Value ?? user.FindFirstValue("sub");

                public Claim Grant(string permission) => new(IdentityClaimTypes.Permission, permission);

                public async Task<bool> AuthorizeAsync(ClaimsPrincipal user) =>
                    (await authorization.AuthorizeAsync(new PermissionEvaluationContext(user, ReadPermission, Tenant(user)))).Succeeded;
            }
            """);

        Assert.Empty(scan.Findings);
        Assert.Equal(2, scan.ClaimLookupsByType[PermissionBypassScan.TypeKey(FixtureAssembly, "Injected.Endpoint")]);
    }

    [Fact]
    public void Transport_authorization_contexts_are_not_allowed_to_read_permission_claims_directly()
    {
        // The token projection is reviewed by method, so the same read is reported anywhere else in its type and in
        // either HTTP authorization context.
        var token = ScanFixture("Elsa.Foundation.Identity.Api",
            PermissionClaimProjection("Elsa.Foundation.Identity.Api", "FoundationIdentityApi", "HandleTokenAsync", "HandleSessionAsync"));
        var contexts = AuthorizationContexts
            .Select(context => ScanFixture(context.Assembly, PermissionClaimProjection(
                context.Type[..context.Type.LastIndexOf('.')],
                context.Type[(context.Type.LastIndexOf('.') + 1)..],
                "Project")))
            .ToArray();

        var exempted = Assert.Single(token.Exempted);
        Assert.Equal("HandleTokenAsync", exempted.Finding.Method);
        var reported = Assert.Single(token.Findings);
        Assert.Equal("HandleSessionAsync", reported.Method);
        Assert.All(contexts, scan =>
        {
            Assert.Empty(scan.Exempted);
            Assert.Equal(PermissionBypassScanner.DirectPermissionClaim, Assert.Single(scan.Findings).Code);
        });
    }

    [Fact]
    public void A_reviewed_permission_claim_reader_may_not_use_a_fastendpoints_permission_helper()
    {
        var scan = ScanFixture("Elsa.Foundation.Identity.Api", """
            using FastEndpoints;

            namespace Elsa.Foundation.Identity.Api.Services;

            public sealed class ClaimsAuthSessionService : Endpoint<EmptyRequest, EmptyResponse>
            {
                public override void Configure() => Permissions("identity.session.read");
            }
            """);

        Assert.Empty(scan.Exempted);
        Assert.Equal(PermissionBypassScanner.FastEndpointsPermission, Assert.Single(scan.Findings).Code);
    }

    public static TheoryData<string> BypassCaseNames => [.. BypassCases.Keys];

    private static readonly Dictionary<string, (string Source, string Code, string Method)> BypassCases = new()
    {
        ["FastEndpoints Permissions"] = FastEndpointsHelper("""
            namespace FastEndpoints
            {
                public abstract class Endpoint
                {
                    protected void Permissions(params string[] values) { }
                }
            }

            namespace Injected
            {
                public sealed class Endpoint : FastEndpoints.Endpoint
                {
                    public void Configure() => Permissions("read");
                }
            }
            """),
        ["FastEndpoints PermissionsAll"] = FastEndpointsHelper("""
            namespace FastEndpoints
            {
                public abstract class Endpoint
                {
                    protected void PermissionsAll(params string[] values) { }
                }
            }

            namespace Injected
            {
                public sealed class Endpoint : FastEndpoints.Endpoint
                {
                    public void Configure() => PermissionsAll("read");
                }
            }
            """),
        ["FastEndpoints package Permissions"] = FastEndpointsHelper("""
            using FastEndpoints;

            namespace Injected;

            public sealed class Endpoint : Endpoint<EmptyRequest, EmptyResponse>
            {
                public override void Configure()
                {
                    Get("/injected");
                    Permissions("read");
                }
            }
            """),
        ["FastEndpoints-derived Permissions wrapper"] = FastEndpointsHelper("""
            using FastEndpoints;

            namespace Injected;

            public abstract class ElsaEndpoint : Endpoint<EmptyRequest, EmptyResponse>
            {
                protected new void Permissions(params string[] values) { }
            }

            public sealed class Endpoint : ElsaEndpoint
            {
                public override void Configure() => Permissions("read");
            }
            """),
        ["FindFirst"] = DirectRead("return user.FindFirst(IdentityClaimTypes.Permission) is not null;"),
        ["FindFirstValue"] = DirectRead("return user.FindFirstValue(IdentityClaimTypes.Permission) is not null;"),
        ["FindFirstValue extension"] = DirectRead(
            "return ClaimExtensions.FindFirstValue(user, IdentityClaimTypes.Permission) is not null;",
            declarations: "public static class ClaimExtensions { public static string? FindFirstValue(this ClaimsPrincipal principal, string type) => null; }"),
        ["FindAll"] = DirectRead("return user.FindAll(IdentityClaimTypes.Permission).Any();"),
        ["FindAll predicate"] = DirectRead("return user.FindAll(claim => claim.Type == IdentityClaimTypes.Permission).Any();"),
        ["HasClaim"] = DirectRead("return user.HasClaim(IdentityClaimTypes.Permission, \"read\");"),
        ["ClaimsIdentity HasClaim"] = DirectRead("return ((ClaimsIdentity)user.Identity!).HasClaim(IdentityClaimTypes.Permission, \"read\");"),
        ["Claims Any"] = DirectRead("return user.Claims.Any(claim => claim.Type == IdentityClaimTypes.Permission && claim.Value == \"read\");"),
        ["Claims Where"] = DirectRead("return user.Claims.Where(claim => claim.Type == IdentityClaimTypes.Permission).Select(claim => claim.Value).Contains(\"read\");"),
        ["Local alias"] = DirectRead("""
            var permissionType = IdentityClaimTypes.Permission;
            return user.FindAll(permissionType).Any();
            """),
        ["Captured alias"] = DirectRead("""
            var type = IdentityClaimTypes.Permission;
            return user.Claims.Any(claim => claim.Type == type);
            """),
        ["Static field alias"] = DirectRead("return user.FindAll(ClaimType).Any();",
            members: "private static readonly string ClaimType = IdentityClaimTypes.Permission;"),
        ["Auto-property alias"] = DirectRead("return user.FindFirst(ClaimType) is not null;",
            members: "private string ClaimType { get; } = IdentityClaimTypes.Permission;"),
        ["Provider permission constant"] = DirectRead("return user.HasClaim(IdentityClaimTypes.Permission, PermissionNames.WorkflowRead);",
            declarations: "public static class PermissionNames { public const string WorkflowRead = \"workflow.read\"; }"),
        ["Provider permission constant alone"] = DirectRead("return user.HasClaim(\"scope\", ProviderGrants.WorkflowReadPermission);",
            declarations: "public static class ProviderGrants { public const string WorkflowReadPermission = \"workflow.read\"; }"),
        ["Async method"] = ("""
            using System.Security.Claims;
            using System.Threading.Tasks;
            using Elsa.Foundation.Identity.Core.Authorization;

            namespace Injected;

            public sealed class Endpoint
            {
                public async Task<bool> AuthorizeAsync(ClaimsPrincipal user)
                {
                    await Task.Yield();
                    return user.HasClaim(IdentityClaimTypes.Permission, "read");
                }
            }
            """, PermissionBypassScanner.DirectPermissionClaim, "AuthorizeAsync")
    };

    /// <summary>A case whose <c>Injected.Endpoint.Configure</c> calls a FastEndpoints permission helper.</summary>
    private static (string Source, string Code, string Method) FastEndpointsHelper(string source) =>
        (source, PermissionBypassScanner.FastEndpointsPermission, "Configure");

    /// <summary>A case whose <c>Injected.Endpoint.Authorize</c> reads permission claims in <paramref name="body"/>.</summary>
    private static (string Source, string Code, string Method) DirectRead(string body, string declarations = "", string members = "") =>
        (Authorize(body, declarations, members), PermissionBypassScanner.DirectPermissionClaim, "Authorize");

    /// <summary>A fixture whose <c>Injected.Endpoint.Authorize</c> runs <paramref name="body"/> against <c>user</c>.</summary>
    private static string Authorize(string body, string declarations = "", string members = "") => $$"""
        using System.Linq;
        using System.Security.Claims;
        using Elsa.Foundation.Identity.Core.Authorization;

        namespace Injected;

        {{declarations}}

        public sealed class Endpoint
        {
            {{members}}

            public bool Authorize(ClaimsPrincipal user)
            {
                {{body}}
            }
        }
        """;

    /// <summary>A type that projects a principal's permission claims in each of <paramref name="methods"/>.</summary>
    private static string PermissionClaimProjection(string @namespace, string type, params string[] methods) => $$"""
        using System.Linq;
        using System.Security.Claims;
        using Elsa.Foundation.Identity.Core.Authorization;

        namespace {{@namespace}};

        public sealed class {{type}}
        {
            {{string.Join(Environment.NewLine, methods.Select(method =>
                $"public string[] {method}(ClaimsPrincipal principal) => principal.FindAll(IdentityClaimTypes.Permission).Select(x => x.Value).ToArray();"))}}
        }
        """;

    /// <summary>Compiles <paramref name="source"/> to IL in memory, loads it into a collectible context and scans it.</summary>
    private static PermissionBypassScan ScanFixture(string assemblyName, string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
            FixtureReferences.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));

        image.Position = 0;
        var context = new AssemblyLoadContext($"permission-bypass-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            return PermissionBypassScanner.Scan([context.LoadFromStream(image)], ReviewedReaders);
        }
        finally
        {
            context.Unload();
        }
    }

    private static readonly Lazy<MetadataReference[]> FixtureReferences = new(() =>
    {
        var platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var packages = new[] { typeof(FastEndpoints.EmptyRequest), typeof(IdentityClaimTypes) }.Select(type => type.Assembly.Location);
        return [.. platform.Concat(packages)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path))];
    });
}
