using System.Security.Claims;
using Elsa.Foundation.Identity.Abstractions.Authorization;

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

    private static PermissionEvaluationContext Context(string granted, string requested) =>
        new(new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim(IdentityClaimTypes.Permission, granted)], "test")), requested);

    private sealed class TestPermissionCatalog(IReadOnlyCollection<Permission> permissions) : IPermissionCatalog
    {
        public IReadOnlyCollection<Permission> List() => permissions;

        public Permission? Find(string key) => permissions.FirstOrDefault(permission => permission.Key == key);
    }
}
