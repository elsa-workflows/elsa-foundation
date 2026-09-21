using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Services;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class SecretRepositoryContractTests
{
    private const string TenantId = "tenant-a";
    private static readonly DateTimeOffset Now = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<string, Func<ISecretRepository>> RepositoryFactories { get; } = new()
    {
        { "In-memory", () => new InMemorySecretRepository() }
    };

    [Theory]
    [MemberData(nameof(RepositoryFactories))]
    public async Task Find_Returns_Deleted_Tombstone(string _, Func<ISecretRepository> repositoryFactory)
    {
        var repository = repositoryFactory();
        var secret = DeletedSecret();

        await repository.SaveAsync(secret);

        Assert.Equal(SecretStatus.Deleted, (await repository.FindAsync(TenantId, secret.Name))!.Status);
    }

    [Theory]
    [MemberData(nameof(RepositoryFactories))]
    public async Task List_Includes_Deleted_Tombstones(string _, Func<ISecretRepository> repositoryFactory)
    {
        var repository = repositoryFactory();
        var secret = DeletedSecret();

        await repository.SaveAsync(secret);

        Assert.Equal(
            SecretStatus.Deleted,
            Assert.Single((await repository.ListPageAsync(TenantId, new SecretRepositoryListRequest())).Items).Status);
    }

    [Theory]
    [MemberData(nameof(RepositoryFactories))]
    public async Task TryAdd_Reserves_Deleted_Name(string _, Func<ISecretRepository> repositoryFactory)
    {
        var repository = repositoryFactory();
        var secret = DeletedSecret();

        await repository.SaveAsync(secret);

        Assert.False(await repository.TryAddAsync(ActiveSecret(secret.Name)));
    }

    [Theory]
    [MemberData(nameof(RepositoryFactories))]
    public async Task ListPage_CombinesSearchFacetsScopeStatusAndExclusion(
        string _,
        Func<ISecretRepository> repositoryFactory)
    {
        var repository = repositoryFactory();
        await repository.SaveAsync(ActiveSecret("payments.alpha", "Payments Alpha", scope: "Finance"));
        await repository.SaveAsync(ActiveSecret("payments.configuration", "Payments Config", SecretStoreNames.Configuration, "Finance"));
        await repository.SaveAsync(ActiveSecret("payments.other-scope", "Payments Other", scope: "Operations"));
        await repository.SaveAsync(ActiveSecret("orders.alpha", "Orders Alpha", scope: "Finance"));
        var deleted = ActiveSecret("payments.deleted", "Payments Deleted", scope: "Finance");
        deleted.Status = SecretStatus.Deleted;
        await repository.SaveAsync(deleted);

        var page = await repository.ListPageAsync(TenantId, new SecretRepositoryListRequest(
            search: "PAYMENTS",
            typeName: SecretTypeNames.Text,
            typeNames: [SecretTypeNames.Text, SecretTypeNames.RsaKey],
            storeName: SecretStoreNames.Encrypted,
            storeNames: [SecretStoreNames.Encrypted],
            scope: "FINANCE",
            status: SecretStatus.Active,
            excludedStatus: SecretStatus.Deleted));

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("payments.alpha", Assert.Single(page.Items).Name);
    }

    [Theory]
    [MemberData(nameof(RepositoryFactories))]
    public async Task ListPage_ActiveOnlyUsesAnExplicitBoundaryAndEveryActiveVersion(
        string _,
        Func<ISecretRepository> repositoryFactory)
    {
        var repository = repositoryFactory();
        await repository.SaveAsync(ActiveSecret("a.non-expiring"));
        await repository.SaveAsync(ActiveSecret("b.future", expiresAt: Now.AddMinutes(1)));
        await repository.SaveAsync(ActiveSecret("c.at-boundary", expiresAt: Now));
        await repository.SaveAsync(ActiveSecret("d.expired", expiresAt: Now.AddMinutes(-1)));
        await repository.SaveAsync(ActiveSecret("e.no-versions", versions: []));
        await repository.SaveAsync(ActiveSecret(
            "f.retired-version",
            versions: [Version(SecretStatus.Retired)]));
        await repository.SaveAsync(ActiveSecret(
            "g.mixed",
            versions:
            [
                Version(expiresAt: Now.AddMinutes(-1)),
                Version(expiresAt: Now.AddMinutes(1), version: 2)
            ]));
        var inactiveRoot = ActiveSecret("h.inactive-root");
        inactiveRoot.Status = SecretStatus.Revoked;
        await repository.SaveAsync(inactiveRoot);

        var page = await repository.ListPageAsync(TenantId, new SecretRepositoryListRequest(
            activeOnly: true,
            now: Now,
            take: 20));

        Assert.Equal(
            ["a.non-expiring", "b.future", "g.mixed"],
            page.Items.Select(secret => secret.Name));
        Assert.Equal(3, page.TotalCount);
    }

    [Theory]
    [MemberData(nameof(RepositoryFactories))]
    public async Task ListPage_AppliesCountAndWindowAfterDeterministicOrdering(
        string _,
        Func<ISecretRepository> repositoryFactory)
    {
        var repository = repositoryFactory();
        await repository.SaveAsync(ActiveSecret("c"));
        await repository.SaveAsync(ActiveSecret("a"));
        await repository.SaveAsync(ActiveSecret("b"));

        var page = await repository.ListPageAsync(TenantId, new SecretRepositoryListRequest(skip: 1, take: 1));

        Assert.Equal(3, page.TotalCount);
        Assert.Equal("b", Assert.Single(page.Items).Name);
    }

    [Theory]
    [MemberData(nameof(RepositoryFactories))]
    public async Task Save_RejectsNamesOutsideThePortableNormalizedIdentityDomain(
        string _,
        Func<ISecretRepository> repositoryFactory)
    {
        var repository = repositoryFactory();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await repository.SaveAsync(ActiveSecret("Payments.Api")));
    }

    [Fact]
    public void ListRequest_RejectsUnboundedOrNondeterministicInputsAndDefensivelyCopiesFacets()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecretRepositoryListRequest(skip: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecretRepositoryListRequest(take: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecretRepositoryListRequest(take: SecretRepositoryListRequest.MaximumTake + 1));
        Assert.Throws<ArgumentException>(() => new SecretRepositoryListRequest(typeNames: [""]));
        Assert.Throws<ArgumentException>(() => new SecretRepositoryListRequest(
            typeNames: Enumerable.Range(0, SecretRepositoryListRequest.MaximumFacetValues + 1).Select(index => index.ToString()).ToArray()));
        Assert.Throws<ArgumentException>(() => new SecretRepositoryListRequest(activeOnly: true));
        Assert.Throws<ArgumentException>(() => new SecretRepositoryListRequest(
            search: new string('x', SecretRepositoryListRequest.MaximumSearchLength + 1)));

        var mutable = new List<string> { SecretTypeNames.Text, SecretTypeNames.Text.ToUpperInvariant() };
        var request = new SecretRepositoryListRequest(typeNames: mutable);
        mutable.Clear();

        Assert.Equal([SecretTypeNames.Text], request.TypeNames, StringComparer.OrdinalIgnoreCase);
    }

    private static Secret DeletedSecret()
    {
        var secret = ActiveSecret("payments.api");
        secret.Status = SecretStatus.Deleted;
        return secret;
    }

    private static Secret ActiveSecret(
        string name,
        string? displayName = null,
        string storeName = SecretStoreNames.Encrypted,
        string? scope = null,
        DateTimeOffset? expiresAt = null,
        IList<SecretVersion>? versions = null) => new()
    {
        TenantId = TenantId,
        Name = name,
        DisplayName = displayName ?? name,
        TypeName = SecretTypeNames.Text,
        StoreName = storeName,
        Scope = scope,
        Versions = versions ?? [Version(expiresAt: expiresAt)]
    };

    private static SecretVersion Version(
        SecretStatus status = SecretStatus.Active,
        DateTimeOffset? expiresAt = null,
        int version = 1) => new()
    {
        Version = version,
        Status = status,
        ExpiresAt = expiresAt,
        Payload = SecretPayload.FromValue("v1")
    };
}
