using System.Security.Claims;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;

namespace Elsa.Foundation.Identity.Tests;

public sealed class ClaimsPermissionEvaluatorTests
{
    [Fact]
    public async Task GrantsAnExactPermissionUsingCanonicalOrdinalKeys()
    {
        var evaluator = new ClaimsPermissionEvaluator(new TestPermissionCatalog(
        [new("re\u0301ad", "Read", "Test", "Read permission.")]));

        var result = await evaluator.EvaluateAsync(Context("r\u0065\u0301ad", "RÉAD"));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ExpandsImplicationsFromGrantedPermissionOnlyAndTerminatesCycles()
    {
        var evaluator = new ClaimsPermissionEvaluator(new TestPermissionCatalog(
        [
            new("admin", "Admin", "Test", "Admin.", new HashSet<string> { "MANAGE" }),
            new("manage", "Manage", "Test", "Manage.", new HashSet<string> { "READ" }),
            new("read", "Read", "Test", "Read.", new HashSet<string> { "MANAGE" }),
            new("write", "Write", "Test", "Write.")
        ]));

        var implied = await evaluator.EvaluateAsync(Context("ADMIN", "read"));
        var reverse = await evaluator.EvaluateAsync(Context("READ", "admin"));
        var requestedExpansion = await evaluator.EvaluateAsync(Context("MANAGE", "admin"));

        Assert.True(implied.Succeeded);
        Assert.False(reverse.Succeeded);
        Assert.False(requestedExpansion.Succeeded);
    }

    [Fact]
    public async Task WildcardGrantSatisfiesOrdinaryPermissionButNotWildcardRequest()
    {
        var evaluator = new ClaimsPermissionEvaluator(new TestPermissionCatalog(
        [new("read", "Read", "Test", "Read permission.")]));

        var ordinary = await evaluator.EvaluateAsync(Context("*", "READ"));
        var wildcard = await evaluator.EvaluateAsync(Context("read", "*"));
        var explicitWildcard = await evaluator.EvaluateAsync(Context("*", "*"));

        Assert.True(ordinary.Succeeded);
        Assert.False(wildcard.Succeeded);
        Assert.True(explicitWildcard.Succeeded);
    }

    [Fact]
    public async Task RejectsPaddedDeclarationsAndDoesNotAuthorizeFromPaddedClaims()
    {
        var paddedCatalog = new ClaimsPermissionEvaluator(new TestPermissionCatalog(
        [new(" read ", "Read", "Test", "Invalid definition.")]));
        var evaluator = new ClaimsPermissionEvaluator(new TestPermissionCatalog(
        [new("read", "Read", "Test", "Read permission.")]));

        await Assert.ThrowsAsync<InvalidOperationException>(() => paddedCatalog.EvaluateAsync(Context("read", "read")).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => evaluator.EvaluateAsync(Context("read", " read ")).AsTask());
        var paddedClaim = await evaluator.EvaluateAsync(Context(" read ", "read"));
        Assert.False(paddedClaim.Succeeded);
    }

    [Fact]
    public async Task RejectsWildcardCatalogDefinitionsAndImplicationTargets()
    {
        var definitionEvaluator = new ClaimsPermissionEvaluator(new TestPermissionCatalog(
        [new("*", "Wildcard", "Test", "Invalid definition.")]));
        var targetEvaluator = new ClaimsPermissionEvaluator(new TestPermissionCatalog(
        [new("admin", "Admin", "Test", "Invalid target.", new HashSet<string> { "*" })]));

        await Assert.ThrowsAsync<InvalidOperationException>(() => definitionEvaluator.EvaluateAsync(Context("admin", "read")).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => targetEvaluator.EvaluateAsync(Context("admin", "read")).AsTask());
    }

    [Fact]
    public async Task BuildsTheCatalogIndexOnceAcrossEvaluations()
    {
        // An endpoint that authorizes each row of a page evaluates twice per row against one scoped evaluator, so a
        // per-evaluation rebuild put a full catalog walk on the per-row path.
        var catalog = new CountingPermissionCatalog(
        [
            new("admin", "Admin", "Test", "Admin.", new HashSet<string> { "READ" }),
            new("read", "Read", "Test", "Read.")
        ]);
        var evaluator = new ClaimsPermissionEvaluator(catalog);

        var first = await evaluator.EvaluateAsync(Context("ADMIN", "read"));
        var second = await evaluator.EvaluateAsync(Context("ADMIN", "read"));
        var third = await evaluator.EvaluateAsync(Context("read", "admin"));

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.False(third.Succeeded);
        Assert.Equal(1, catalog.ListCalls);
    }

    [Fact]
    public async Task KeepsThrowingForAMalformedCatalogOnEveryEvaluation()
    {
        // A failed build must not be cached as a success, and must not be swallowed on the second call.
        var evaluator = new ClaimsPermissionEvaluator(new TestPermissionCatalog(
        [new("*", "Wildcard", "Test", "Invalid definition.")]));

        await Assert.ThrowsAsync<InvalidOperationException>(() => evaluator.EvaluateAsync(Context("admin", "read")).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => evaluator.EvaluateAsync(Context("admin", "read")).AsTask());
    }

    private static PermissionEvaluationContext Context(string granted, string requested) =>
        new(new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim(IdentityClaimTypes.Permission, granted)], "test")), requested);

    private sealed class CountingPermissionCatalog(IReadOnlyCollection<Permission> permissions) : IPermissionCatalog
    {
        public int ListCalls { get; private set; }

        public IReadOnlyCollection<Permission> List()
        {
            ListCalls++;
            return permissions;
        }

        public Permission? Find(string key) => permissions.FirstOrDefault(permission => permission.Key == key);
    }

    private sealed class TestPermissionCatalog(IReadOnlyCollection<Permission> permissions) : IPermissionCatalog
    {
        public IReadOnlyCollection<Permission> List() => permissions;

        public Permission? Find(string key) => permissions.FirstOrDefault(permission => permission.Key == key);
    }
}
