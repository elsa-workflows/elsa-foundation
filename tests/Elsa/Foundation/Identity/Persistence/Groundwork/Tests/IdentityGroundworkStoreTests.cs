using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Core.Scoping;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Tests;

/// <summary>
/// Behavioral tests for the Groundwork-backed identity stores, including the restart-survival guarantee:
/// records written through one store instance are readable through a fresh store instance over the same
/// underlying document store, proving identity is durable rather than tied to a process-lifetime object.
/// </summary>
public sealed class IdentityGroundworkStoreTests
{
    [Fact]
    public async Task User_RoundTrips_By_Id_And_Email()
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        var store = IdentityGroundworkFixtures.UserStore(docStore);
        var user = IdentityGroundworkFixtures.User();

        await store.SaveAsync(user);

        var byId = await store.FindAsync(user.TenantId, user.Id);
        var byEmail = await store.FindByEmailAsync(user.TenantId, "ALICE@example.com");

        Assert.NotNull(byId);
        Assert.Equal("alice", byId!.UserName);
        Assert.NotNull(byEmail);
        Assert.Equal("user-1", byEmail!.Id);
    }

    [Fact]
    public async Task User_Survives_A_Store_Restart()
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        var user = IdentityGroundworkFixtures.User();

        await IdentityGroundworkFixtures.UserStore(docStore).SaveAsync(user);

        // Fresh store instance over the same durable document store == process restart.
        var reloaded = await IdentityGroundworkFixtures.UserStore(docStore).FindAsync(user.TenantId, user.Id);

        Assert.NotNull(reloaded);
        Assert.Equal(user.Email, reloaded!.Email);
        Assert.Equal(user.RoleIds, reloaded.RoleIds);
    }

    [Fact]
    public async Task Role_Lists_By_Tenant_And_Survives_Restart()
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        var role = IdentityGroundworkFixtures.Role();

        await IdentityGroundworkFixtures.RoleStore(docStore).SaveAsync(role);

        var list = await IdentityGroundworkFixtures.RoleStore(docStore).ListAsync("tenant-1");

        Assert.Equal("role-1", Assert.Single(list).Id);
    }

    [Fact]
    public async Task ExternalIdentity_RoundTrips_By_Subject_And_Lists_For_User()
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        var external = IdentityGroundworkFixtures.ExternalIdentity();
        await IdentityGroundworkFixtures.UserStore(docStore).SaveAsync(IdentityGroundworkFixtures.User());

        await IdentityGroundworkFixtures.ExternalIdentityStore(docStore).SaveAsync(external);

        var store = IdentityGroundworkFixtures.ExternalIdentityStore(docStore);
        var bySubject = await store.FindBySubjectAsync("tenant-1", "google", "sub-123");
        var forUser = await store.ListForUserAsync("tenant-1", "user-1");

        Assert.NotNull(bySubject);
        Assert.Equal("user-1", bySubject!.UserId);
        Assert.Single(forUser);
    }

    [Fact]
    public async Task TenantMembership_RoundTrips_And_Survives_Restart()
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        var membership = IdentityGroundworkFixtures.TenantMembership();
        await IdentityGroundworkFixtures.UserStore(docStore).SaveAsync(IdentityGroundworkFixtures.User());

        await IdentityGroundworkFixtures.TenantMembershipStore(docStore).SaveAsync(membership);

        var reloaded = await IdentityGroundworkFixtures.TenantMembershipStore(docStore).FindAsync("tenant-1", "user-1");

        Assert.NotNull(reloaded);
        Assert.Equal(TenantMembershipStatus.Active, reloaded!.Status);
        Assert.Equal(membership.RoleIds, reloaded.RoleIds);
    }

    [Fact]
    public async Task Save_Is_An_Upsert()
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        var store = IdentityGroundworkFixtures.RoleStore(docStore);

        await store.SaveAsync(IdentityGroundworkFixtures.Role());
        await store.SaveAsync(IdentityGroundworkFixtures.Role() with { Name = "Renamed" });

        var list = await store.ListAsync("tenant-1");
        Assert.Equal("Renamed", Assert.Single(list).Name);
    }

    [Fact]
    public async Task Explicit_tenant_mismatch_fails_before_provider_io()
    {
        var documentStore = new ThrowingDocumentStore();
        var store = IdentityGroundworkFixtures.UserStore(documentStore, "tenant-a");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.FindAsync("tenant-b", "user-1"));

        Assert.DoesNotContain("tenant-a", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant-b", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, documentStore.CallCount);
    }

    private sealed class ThrowingDocumentStore : IDocumentStore
    {
        public DocumentStoreAccess Access =>
            DocumentStoreAccess.Scoped(new StorageScope("tenant-a"));

        public TransactionBoundary TransactionBoundary => TransactionBoundary.CrossUnitAtomic;

        public int CallCount { get; private set; }

        public Task<DocumentStoreWriteResult> SaveAsync(
            SaveDocumentRequest request,
            CancellationToken cancellationToken = default) => Fail<DocumentStoreWriteResult>();

        public Task<DocumentEnvelope?> LoadAsync(
            string documentKind,
            string id,
            CancellationToken cancellationToken = default) => Fail<DocumentEnvelope?>();

        public Task<DocumentStoreWriteResult> DeleteAsync(
            DeleteDocumentRequest request,
            CancellationToken cancellationToken = default) => Fail<DocumentStoreWriteResult>();

#pragma warning disable GW0004
        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(
            DocumentStoreQuery query,
            CancellationToken cancellationToken = default) => Fail<IReadOnlyList<DocumentEnvelope>>();

        public Task<DocumentQueryResult> QueryAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) => Fail<DocumentQueryResult>();

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) => Fail<DocumentEnvelope?>();

        public Task<bool> AnyAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) => Fail<bool>();
#pragma warning restore GW0004

        public Task<IDocumentUnitOfWork> BeginAsync(
            DocumentCommitScope scope,
            CancellationToken cancellationToken = default) => Fail<IDocumentUnitOfWork>();

        private Task<T> Fail<T>()
        {
            CallCount++;
            throw new InvalidOperationException("Provider I/O must not be reached.");
        }
    }
}
