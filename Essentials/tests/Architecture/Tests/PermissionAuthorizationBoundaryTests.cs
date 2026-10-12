using System.Runtime.Loader;
using Elsa.Foundation.Identity.Core.Authorization;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Guards Foundation Identity's permission authorization from being bypassed: first-party code decides permissions
/// through <c>IPermissionAuthorizationService</c> and the policy metadata it evaluates, never through a FastEndpoints
/// permission helper or by reading permission claims itself. <see cref="PermissionBypassScanner"/> inspects the IL of
/// every packable runtime library, so the rule is checked against the compiled code rather than source text, and
/// follows that code wherever an endpoint or authorization context lives rather than a folder convention.
/// </summary>
public sealed class PermissionAuthorizationBoundaryTests
{
    private const string FixtureAssembly = "Elsa.PermissionBypass.Fixture";

    /// <summary>
    /// The only methods allowed to read permission claims: the shared evaluator and the projections that copy the
    /// normalized grants into a session, a token or an identity. Each entry must still exempt a read, so the list
    /// cannot outlive the code it excuses.
    /// </summary>
    internal static readonly ReviewedPermissionClaimReader[] ReviewedReaders =
    [
        new("Elsa.Foundation.Identity", "Elsa.Foundation.Identity.Authorization.ClaimsPermissionEvaluator", "EvaluateAsync",
            "The shared evaluator behind IPermissionAuthorizationService: matching granted permission claims is its job."),
        new("Elsa.Foundation.Identity.Api", "Elsa.Foundation.Identity.Api.FoundationIdentityApi", "HandleTokenAsync",
            "Token issuance copies the normalized principal's grants into the issued token."),
        new("Elsa.Foundation.Identity.Api", "Elsa.Foundation.Identity.Api.Services.ClaimsAuthSessionService", "GetAsync",
            "Projects the normalized principal's grants into the session the client displays."),
        new("Elsa.Foundation.Identity.AspNetCoreIdentity", "Elsa.Foundation.Identity.AspNetCoreIdentity.Services.DefaultAuthSessionService", "GetAsync",
            "Projects the normalized principal's grants into the session the client displays."),
        new("Elsa.Foundation.Identity.AspNetCoreIdentity", "Elsa.Foundation.Identity.AspNetCoreIdentity.Services.IdentityClaimsProjector", "ApplyUserClaimsAsync",
            "Projects a user's grants onto the identity and checks for an existing permission claim before adding it.")
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

        // Permission keys count as permission values because the code declares them: in an attribute or catalog entry
        // directly, or through a catalog helper's parameter.
        Assert.True(scan.DeclaredPermissionKeys.Count >= 40, $"Only {scan.DeclaredPermissionKeys.Count} declared permission keys were found.");
        Assert.Contains(Elsa.Workflows.Design.Api.Authorization.WorkflowDesignPermissions.Read, scan.DeclaredPermissionKeys);
        Assert.Contains(DefaultIdentityPermissionKeys.IdentityUsersRead, scan.DeclaredPermissionKeys);
        Assert.Contains(Elsa.Secrets.Core.Permissions.SecretsPermissions.Read, scan.DeclaredPermissionKeys);
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

    [Theory]
    [MemberData(nameof(LookalikeCaseNames))]
    public void A_value_that_only_looks_like_a_permission_is_not_reported(string caseName)
    {
        var scan = ScanFixture(FixtureAssembly, LookalikeCases[caseName]);

        Assert.Empty(scan.Findings);
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

                public void GetAsync() => Permissions("identity.session.read");
            }
            """);

        Assert.Empty(scan.Exempted);
        Assert.Equal(2, scan.Findings.Count);
        Assert.All(scan.Findings, finding => Assert.Equal(PermissionBypassScanner.FastEndpointsPermission, finding.Code));
    }

    public static TheoryData<string> BypassCaseNames => [.. BypassCases.Keys];

    public static TheoryData<string> LookalikeCaseNames => [.. LookalikeCases.Keys];

    private static readonly Dictionary<string, (string Source, string Code, string Method)> BypassCases = new()
    {
        ["FastEndpoints Permissions"] = StandInFastEndpointsHelper("Permissions"),
        ["FastEndpoints PermissionsAll"] = StandInFastEndpointsHelper("PermissionsAll"),
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
        ["IPrincipal extension"] = DirectRead(
            "return PrincipalLookups.FindFirstValue(user, IdentityClaimTypes.Permission) is not null;",
            declarations: "public static class PrincipalLookups { public static string? FindFirstValue(this IPrincipal principal, string type) => null; }"),
        ["IIdentity extension"] = DirectRead(
            "return IdentityLookups.HasClaim(user.Identity!, IdentityClaimTypes.Permission, \"read\");",
            declarations: "public static class IdentityLookups { public static bool HasClaim(this IIdentity identity, string type, string value) => false; }"),
        ["FindAll"] = DirectRead("return user.FindAll(IdentityClaimTypes.Permission).Any();"),
        ["FindAll predicate"] = DirectRead("return user.FindAll(claim => claim.Type == IdentityClaimTypes.Permission).Any();"),
        ["HasClaim"] = DirectRead("return user.HasClaim(IdentityClaimTypes.Permission, \"read\");"),
        ["ClaimsIdentity HasClaim"] = DirectRead("return ((ClaimsIdentity)user.Identity!).HasClaim(IdentityClaimTypes.Permission, \"read\");"),
        ["Claims Any"] = DirectRead("return user.Claims.Any(claim => claim.Type == IdentityClaimTypes.Permission && claim.Value == \"read\");"),
        ["Claims Where"] = DirectRead("return user.Claims.Where(claim => claim.Type == IdentityClaimTypes.Permission).Select(claim => claim.Value).Contains(\"read\");"),
        ["Claims ToList"] = DirectRead("""
            var claims = user.Claims.ToList();
            return claims.Any(claim => claim.Type == IdentityClaimTypes.Permission);
            """),
        ["Claims AsEnumerable"] = DirectRead("return user.Claims.AsEnumerable().Any(claim => claim.Type == IdentityClaimTypes.Permission);"),
        ["Identities SelectMany"] = DirectRead("return user.Identities.SelectMany(identity => identity.Claims).Any(claim => claim.Type == IdentityClaimTypes.Permission);"),
        ["Helper returning Claims"] = DirectRead("return All(user).Any(claim => claim.Type == IdentityClaimTypes.Permission);",
            members: "private static IEnumerable<Claim> All(ClaimsPrincipal principal) => principal.Claims;"),
        ["Predicate helper inside a lambda"] = DirectRead("return user.Claims.Any(claim => IsPermission(claim));",
            members: "private static bool IsPermission(Claim claim) => claim.Type == IdentityClaimTypes.Permission;"),
        ["String receiver"] = DirectRead("return user.HasClaim(IdentityClaimTypes.Permission.ToLowerInvariant(), \"read\");"),
        ["Predicate factory"] = DirectRead("return user.Claims.Any(OfType(IdentityClaimTypes.Permission));",
            members: "private static Func<Claim, bool> OfType(string type) => claim => claim.Type == type;"),
        ["Predicate factory with a declared key"] = DirectRead("return user.Claims.Any(HasValue(WorkflowPermissions.Read));",
            declarations: CatalogDeclaredKey,
            members: "private static Func<Claim, bool> HasValue(string value) => claim => claim.Value == value;"),
        ["Ref local"] = DirectRead("""
            var type = IdentityClaimTypes.Permission;
            ref var alias = ref type;
            return user.HasClaim(alias, "read");
            """),
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
        ["Permission returned by a helper"] = DirectRead("return user.FindAll(ClaimType()).Any();",
            members: "private static string ClaimType() => IdentityClaimTypes.Permission;"),
        ["Provider permission constant"] = DirectRead("return user.HasClaim(IdentityClaimTypes.Permission, PermissionNames.WorkflowRead);",
            declarations: "public static class PermissionNames { public const string WorkflowRead = \"workflow.read\"; }"),
        ["Catalog-declared key alone"] = DirectRead("return user.HasClaim(\"scope\", WorkflowPermissions.Read);",
            declarations: CatalogDeclaredKey),
        ["Catalog helper-declared key alone"] = DirectRead("return user.HasClaim(\"scope\", WorkflowPermissions.Read);",
            declarations: """
                public static class WorkflowPermissions
                {
                    public const string Read = "workflow.read";

                    public static readonly Permission Entry = Owned(Read, "Read workflows");

                    private static Permission Owned(string key, string displayName) => new(key, displayName, "Workflows", displayName);
                }
                """),
        ["Attribute-declared key alone"] = DirectRead("return user.HasClaim(\"scope\", WorkflowPermissions.Read);",
            declarations: """
                public sealed class RequirePermissionAttribute(string permission) : Attribute;

                public static class WorkflowPermissions
                {
                    public const string Read = "workflow.read";
                }

                [RequirePermission(WorkflowPermissions.Read)]
                public sealed class ReadEndpoint;
                """),
        ["Provider permission constant alone"] = DirectRead("return user.HasClaim(\"scope\", ProviderGrants.WorkflowReadPermission);",
            declarations: "public static class ProviderGrants { public const string WorkflowReadPermission = \"workflow.read\"; }"),
        ["Helper parameter"] = DirectRead("return Has(user, IdentityClaimTypes.Permission);",
            members: "private static bool Has(ClaimsPrincipal principal, string type) => principal.HasClaim(type, \"read\");"),
        ["Helper parameter through a closure"] = DirectRead("return Has(user, IdentityClaimTypes.Permission);",
            members: "private static bool Has(ClaimsPrincipal principal, string type) => principal.Claims.Any(claim => claim.Type == type);"),
        ["In parameter"] = DirectRead("""
            var type = IdentityClaimTypes.Permission;
            return Has(user, in type);
            """,
            members: "private static bool Has(ClaimsPrincipal principal, in string type) => principal.HasClaim(type, \"read\");"),
        ["Iterator helper parameter"] = DirectRead("return Grants(user, IdentityClaimTypes.Permission).Any();",
            members: "private static IEnumerable<Claim> Grants(ClaimsPrincipal principal, string type) { foreach (var claim in principal.FindAll(type)) yield return claim; }"),
        ["Async helper parameter"] = DirectRead("return HasAsync(user, IdentityClaimTypes.Permission).GetAwaiter().GetResult();",
            members: "private static async Task<bool> HasAsync(ClaimsPrincipal principal, string type) { await Task.Yield(); return principal.HasClaim(type, \"read\"); }"),
        ["Helper chain in another type"] = DirectRead("return ClaimChecks.Has(user, IdentityClaimTypes.Permission);",
            declarations: """
                public static class ClaimChecks
                {
                    public static bool Has(ClaimsPrincipal principal, string type) => Check(principal, type);

                    private static bool Check(ClaimsPrincipal principal, string type) => principal.FindAll(type).Any();
                }
                """),
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

    private static readonly Dictionary<string, string> LookalikeCases = new()
    {
        // The record's tenant is not a permission value just because the record's name contains "Permission".
        ["Tenant of a permission-named record"] = EndpointSource(
            "return TenantMatches(new PermissionEvaluationContext(user, \"workflow.read\", \"tenant-1\"));",
            members: "private static bool TenantMatches(PermissionEvaluationContext context) => context.Principal.HasClaim(IdentityClaimTypes.TenantId, context.TenantId!);"),
        // A permission-named service's settings are not permission values: only a string or delegate receiver passes
        // its value on to a call's result.
        ["Setting of a permission-named service"] = EndpointSource(
            "return user.FindFirst(_permissionOptions.TenantClaimType) is not null;",
            declarations: "public sealed class PermissionOptions { public string TenantClaimType { get; set; } = \"tenant\"; }",
            members: "private readonly PermissionOptions _permissionOptions = new();"),
        // A value computed from a permission value that cannot hold one, a comparison or a length, is not a permission value.
        ["Permission comparison beside Claims"] = EndpointSource(
            """
            var isPermissionKind = string.Equals(kind, IdentityClaimTypes.Permission, StringComparison.Ordinal);
            return Describe(user.Claims, isPermissionKind);
            """,
            members: "private readonly string kind = \"role\"; private static bool Describe(IEnumerable<Claim> claims, bool isPermissionKind) => isPermissionKind;"),
        ["Permission length into a lookup"] = EndpointSource(
            "return user.FindFirst(\"sub\".PadLeft(IdentityClaimTypes.Permission.Length)) is not null;"),
        // A permission-named type's owner id is not a permission key: only keys the code declares as permissions count.
        ["Owner id of a permission-named type"] = EndpointSource(
            "return user.HasClaim(\"iss\", \"elsa.studio.preferences\");",
            declarations: """
                public static class StudioPreferencesPermissions
                {
                    public const string OwnerId = "elsa.studio.preferences";
                    public const string Read = "studio.preferences.read";
                    public static readonly Permission Entry = new(Read, "Read preferences", "Studio", "Reads preferences.");
                }
                """),
        // A bare wildcard constant does not make every "*" literal a permission value.
        ["Wildcard literal"] = EndpointSource(
            "return user.Claims.Any(claim => claim.Value.EndsWith(\"*\"));",
            declarations: "public static class IdentitySeed { public const string AllAccessPermission = \"*\"; }")
    };

    /// <summary>A permission key declared by a catalog entry, the way feature contributors declare theirs.</summary>
    private const string CatalogDeclaredKey = """
        public static class WorkflowPermissions
        {
            public const string Read = "workflow.read";

            public static readonly Permission Entry = new(Read, "Read workflows", "Workflows", "Reads workflows.");
        }
        """;

    /// <summary>A case whose <c>Injected.Endpoint.Configure</c> calls a FastEndpoints permission helper.</summary>
    private static (string Source, string Code, string Method) FastEndpointsHelper(string source) =>
        (source, PermissionBypassScanner.FastEndpointsPermission, "Configure");

    /// <summary>A case whose helper is declared in a stand-in <c>FastEndpoints</c> namespace rather than the package.</summary>
    private static (string Source, string Code, string Method) StandInFastEndpointsHelper(string helper) => FastEndpointsHelper($$"""
        namespace FastEndpoints
        {
            public abstract class Endpoint
            {
                protected void {{helper}}(params string[] values) { }
            }
        }

        namespace Injected
        {
            public sealed class Endpoint : FastEndpoints.Endpoint
            {
                public void Configure() => {{helper}}("read");
            }
        }
        """);

    /// <summary>A case whose <c>Injected.Endpoint.Authorize</c> reads permission claims in <paramref name="body"/>.</summary>
    private static (string Source, string Code, string Method) DirectRead(string body, string declarations = "", string members = "") =>
        (EndpointSource(body, declarations, members), PermissionBypassScanner.DirectPermissionClaim, "Authorize");

    /// <summary>A fixture whose <c>Injected.Endpoint.Authorize</c> runs <paramref name="body"/> against <c>user</c>.</summary>
    private static string EndpointSource(string body, string declarations = "", string members = "") => $$"""
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Security.Claims;
        using System.Security.Principal;
        using System.Threading.Tasks;
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

    /// <summary>
    /// Compiles <paramref name="source"/> with and without optimization, scans each build's IL in a collectible
    /// context, and asserts both scans agree, since the libraries the guard scans are built in either configuration.
    /// </summary>
    private static PermissionBypassScan ScanFixture(string assemblyName, string source)
    {
        var debug = ScanCompiled(assemblyName, source, OptimizationLevel.Debug);
        var release = ScanCompiled(assemblyName, source, OptimizationLevel.Release);

        Assert.Equal(Outcome(debug), Outcome(release));
        return release;
    }

    private static PermissionBypassScan ScanCompiled(string assemblyName, string source, OptimizationLevel optimization)
    {
        var image = FixtureCompiler.Compile(
            assemblyName,
            source,
            [typeof(FastEndpoints.EmptyRequest).Assembly, typeof(IdentityClaimTypes).Assembly],
            optimization);
        var context = new AssemblyLoadContext($"permission-bypass-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            using var stream = new MemoryStream(image, writable: false);
            return PermissionBypassScanner.Scan([context.LoadFromStream(stream)], ReviewedReaders);
        }
        finally
        {
            context.Unload();
        }
    }

    /// <summary>What a scan concluded, without the IL offsets that differ between optimized and unoptimized builds.</summary>
    private static string Outcome(PermissionBypassScan scan) => string.Join(Environment.NewLine,
    [
        .. scan.Findings.Select(finding => $"finding {finding.Code} {finding.Type}.{finding.Method}: {finding.Message}").Order(StringComparer.Ordinal),
        .. scan.Exempted.Select(entry => $"exempted {entry.Finding.Code} {entry.Finding.Type}.{entry.Finding.Method}: {entry.Finding.Message}").Order(StringComparer.Ordinal),
        .. scan.ClaimLookupsByType.Select(entry => $"lookups {entry.Key} {entry.Value}").Order(StringComparer.Ordinal)
    ]);
}
