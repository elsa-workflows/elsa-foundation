using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests;

public sealed class BoundedIdentityContinuationTests
{
    [Fact]
    public async Task Exact_page_completes_without_guessing_from_page_size()
    {
        var store = new SequenceStore(
            Page(Items(2), next: null));

        var result = await ReadAsync(store, pageSize: 2);

        Assert.Equal(["0", "1"], result.Select(item => item.Id));
        Assert.Single(store.Queries);
    }

    [Fact]
    public async Task Page_plus_one_follows_the_returned_continuation()
    {
        var store = new SequenceStore(
            Page(Items(2), next: "next-1"),
            Page(Items(1, offset: 2), next: null));

        var result = await ReadAsync(store, pageSize: 2);

        Assert.Equal(["0", "1", "2"], result.Select(item => item.Id));
        Assert.Collection(
            store.Queries,
            first => Assert.Null(first.Continuation),
            second => Assert.Equal("next-1", second.Continuation));
    }

    [Fact]
    public async Task Multi_page_result_is_complete_and_has_no_duplicate_identity()
    {
        var store = new SequenceStore(
            Page(Items(2), next: "next-1"),
            Page(Items(2, offset: 2), next: "next-2"),
            Page(Items(1, offset: 4), next: null));

        var result = await ReadAsync(store, pageSize: 2);

        Assert.Equal(["0", "1", "2", "3", "4"], result.Select(item => item.Id));
        Assert.All(store.Queries, query =>
        {
            Assert.Null(query.Skip);
            Assert.Equal(IdentityStorageManifest.ListUserClaimsQuery, query.QueryIdentity);
            Assert.Equal(IdentityStorageManifest.GetDeclaredOrder(query.QueryIdentity), query.Order);
        });
    }

    [Fact]
    public async Task Underfilled_pages_with_forward_progress_remain_valid()
    {
        var store = new SequenceStore(
            Page(Items(1), next: "next-1"),
            Page(Items(1, offset: 1), next: "next-2"),
            Page(Items(1, offset: 2), next: null));

        var result = await IdentityBoundedDocumentQueryPager.ReadAllPagesAsync(
            store,
            IdentityStorageManifest.UserClaimDocumentKind,
            IdentityStorageManifest.ListUserClaimsQuery,
            [DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                IdentityStorageManifest.UserLookupKeyField,
                "tenant:user"))],
            pageSize: 2,
            maximumMaterialization: 3,
            CancellationToken.None);

        Assert.Equal(["0", "1", "2"], result.Select(item => item.Id));
        Assert.Equal(3, store.Queries.Count);
    }

    [Theory]
    [MemberData(nameof(ExhaustiveRoutes))]
    public async Task Every_exhaustive_identity_uses_the_same_finite_cursor_protocol(
        string documentKind,
        string queryIdentity,
        string field)
    {
        const int pageSize = 512;
        var store = new SequenceStore(
            Page(Items(pageSize), next: "next-1"),
            Page(Items(1, offset: pageSize), next: null));

        var result = await IdentityBoundedDocumentQueryPager.ReadAllPagesAsync(
            store,
            documentKind,
            queryIdentity,
            [DocumentQueryClause.Of(DocumentQueryComparison.Equal(field, "scope:key"))],
            pageSize,
            maximumMaterialization: 100_000,
            CancellationToken.None);

        Assert.Equal(pageSize + 1, result.Count);
        Assert.Collection(
            store.Queries,
            first =>
            {
                Assert.Equal(queryIdentity, first.QueryIdentity);
                Assert.Equal(IdentityStorageManifest.GetDeclaredOrder(queryIdentity), first.Order);
                Assert.Null(first.Skip);
                Assert.Null(first.Continuation);
            },
            second => Assert.Equal("next-1", second.Continuation));
    }

    public static TheoryData<string, string, string> ExhaustiveRoutes => new()
    {
        { IdentityStorageManifest.IdentityRoleDocumentKind, IdentityStorageManifest.ListRolesByTenantQuery, IdentityStorageManifest.TenantIdField },
        { IdentityStorageManifest.IdentityClaimMappingDocumentKind, IdentityStorageManifest.ListClaimMappingsByProviderQuery, IdentityStorageManifest.ProviderLookupKeyField },
        { IdentityStorageManifest.UserClaimDocumentKind, IdentityStorageManifest.ListUserClaimsQuery, IdentityStorageManifest.UserLookupKeyField },
        { IdentityStorageManifest.UserClaimDocumentKind, IdentityStorageManifest.FindUsersByClaimQuery, IdentityStorageManifest.ClaimKeyField },
        { IdentityStorageManifest.RoleClaimDocumentKind, IdentityStorageManifest.ListRoleClaimsQuery, IdentityStorageManifest.RoleLookupKeyField },
        { IdentityStorageManifest.ExternalLoginDocumentKind, IdentityStorageManifest.ListUserLoginsQuery, IdentityStorageManifest.UserLookupKeyField },
        { IdentityStorageManifest.UserRoleDocumentKind, IdentityStorageManifest.ListUserRolesQuery, IdentityStorageManifest.UserLookupKeyField },
        { IdentityStorageManifest.UserRoleDocumentKind, IdentityStorageManifest.ListRoleUsersQuery, IdentityStorageManifest.RoleLookupKeyField }
    };

    [Fact]
    public async Task Cancellation_is_preserved_before_the_first_provider_page()
    {
        var store = new SequenceStore(Page(Items(1), next: null));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await ReadAsync(store, pageSize: 2, cancellation.Token));

        Assert.Empty(store.Queries);
    }

    [Fact]
    public async Task Repeated_continuation_fails_closed()
    {
        var store = new SequenceStore(
            Page(Items(1), next: "same"),
            Page(Items(1, offset: 1), next: "same"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ReadAsync(store, pageSize: 1).AsTask());

        Assert.Contains("repeated a previously seen continuation", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Empty_continuation_fails_closed()
    {
        var store = new SequenceStore(Page(Items(1), next: " "));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ReadAsync(store, pageSize: 1).AsTask());

        Assert.Contains("empty continuation", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nonadvancing_page_fails_closed_even_when_the_provider_changes_the_token()
    {
        var store = new SequenceStore(
            Page(Items(1), next: "one"),
            Page(Items(1), next: "two"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ReadAsync(store, pageSize: 1).AsTask());

        Assert.Contains("repeated a storage identity", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Continuation_cannot_be_reused_under_a_different_route_scope_or_order()
    {
        var store = new InMemoryDocumentStore(IdentityStorageManifest.Create());
        for (var index = 0; index < 2; index++)
        {
            await store.SaveAsync(
                new SaveDocumentRequest(
                    IdentityStorageManifest.UserClaimDocumentKind,
                    index.ToString(),
                    IdentityStorageManifest.SchemaVersion,
                    """{"userLookupKey":"tenant:user","claimKey":"tenant:claim"}""",
                    ExpectedVersion: 0));
        }

        var first = await store.QueryAsync(IdentityBoundedDocumentQueryPager.CreatePageQuery(
            IdentityStorageManifest.UserClaimDocumentKind,
            IdentityStorageManifest.ListUserClaimsQuery,
            [DocumentQueryClause.Of(DocumentQueryComparison.Equal(IdentityStorageManifest.UserLookupKeyField, "tenant:user"))],
            take: 1));
        Assert.NotNull(first.NextContinuation);

        var second = IdentityBoundedDocumentQueryPager.CreatePageQuery(
            IdentityStorageManifest.UserClaimDocumentKind,
            IdentityStorageManifest.FindUsersByClaimQuery,
            [DocumentQueryClause.Of(DocumentQueryComparison.Equal(IdentityStorageManifest.ClaimKeyField, "tenant:claim"))],
            take: 2,
            continuation: first.NextContinuation);

        await Assert.ThrowsAsync<InvalidDocumentQueryContinuationException>(() =>
            store.QueryAsync(second));
    }

    private static ValueTask<IReadOnlyList<DocumentEnvelope>> ReadAsync(
        SequenceStore store,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        IdentityBoundedDocumentQueryPager.ReadAllPagesAsync(
            store,
            IdentityStorageManifest.UserClaimDocumentKind,
            IdentityStorageManifest.ListUserClaimsQuery,
            [DocumentQueryClause.Of(DocumentQueryComparison.Equal(IdentityStorageManifest.UserLookupKeyField, "tenant:user"))],
            pageSize,
            maximumMaterialization: 100,
            cancellationToken);

    private static DocumentQueryResult Page(IReadOnlyList<DocumentEnvelope> documents, string? next) =>
        new(documents, documents.Count, next);

    private static DocumentEnvelope[] Items(int count, int offset = 0) =>
        Enumerable.Range(offset, count)
            .Select(index => new DocumentEnvelope("identityUserClaim", index.ToString(), "1", 1, "{}", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch))
            .ToArray();

    private sealed class SequenceStore(params DocumentQueryResult[] results) : IBoundedDocumentStore
    {
        private readonly Queue<DocumentQueryResult> _results = new(results);

        public List<DocumentQuery> Queries { get; } = [];

        public Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Queries.Add(query);
            return Task.FromResult(_results.Count == 0
                ? throw new InvalidOperationException("The test provider received an unexpected page request.")
                : _results.Dequeue());
        }

        public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(0L);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<DocumentEnvelope?>(null);

        public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
