using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Abstractions.Ownership;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Stores;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

public sealed class FoundationBoundedQueryContractTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> Providers => new()
    {
        "sqlite",
        "sqlserver",
        "postgresql",
        "mongodb"
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Iam_normalized_lookup_executes_in_scope_and_inside_the_declared_bound(
        string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients |
            GroundworkTopologyCapabilities.MultiDocumentTransactions);
        await driver.ResetPhysicalAsync([new IdentityGroundworkStorageManifestSource()]);

        await using var tenantAClient = await driver.OpenPhysicalClientAsync(Access("tenant-a"));
        var tenantABounded = Record(tenantAClient);
        var tenantAUsers = new GroundworkUserStore(
            tenantAClient.DocumentStore,
            Accessor("tenant-a"),
            tenantABounded);
        await tenantAUsers.SaveAsync(User("user-a", "alice", "alice@example.test"));
        await tenantAUsers.SaveAsync(User("user-b", "bob", "shared@example.test"));
        await tenantAUsers.SaveAsync(User("user-c", "carol", "shared@example.test"));
        await tenantAUsers.SaveAsync(User("user-d", "dave", "shared@example.test"));

        var found = await tenantAUsers.FindByEmailAsync("tenant-a", "ALICE@EXAMPLE.TEST");
        var ambiguous = await tenantAUsers.FindByEmailAsync("tenant-a", "SHARED@EXAMPLE.TEST");

        Assert.Equal("user-a", Assert.IsType<UserRecord>(found).Id);
        Assert.Null(ambiguous);
        Assert.Collection(
            tenantABounded.Observations,
            observation => AssertIamEmailLookup(observation, expectedMaterialized: 1),
            observation => AssertIamEmailLookup(observation, expectedMaterialized: 2));

        await using var tenantBClient = await driver.OpenPhysicalClientAsync(Access("tenant-b"));
        var tenantBUsers = new GroundworkUserStore(
            tenantBClient.DocumentStore,
            Accessor("tenant-b"),
            tenantBClient.BoundedDocumentStore);
        await tenantBUsers.SaveAsync(User("user-a", "alice", "alice@example.test", "tenant-b"));

        Assert.Equal(
            "tenant-b",
            Assert.IsType<UserRecord>(
                await tenantBUsers.FindByEmailAsync("tenant-b", "alice@example.test")).TenantId);
        Assert.Equal(
            "tenant-a",
            Assert.IsType<UserRecord>(
                await tenantAUsers.FindByEmailAsync("tenant-a", "alice@example.test")).TenantId);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Iam_claim_mapping_pages_are_complete_deterministic_and_bounded(
        string providerKey)
    {
        const string scope = "tenant-a";
        const string provider = "oidc";
        const int pageSize = ElsaGroundworkQueryRoutes.MaximumResultCount;
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients |
            GroundworkTopologyCapabilities.MultiDocumentTransactions);
        await driver.ResetPhysicalAsync([new IdentityGroundworkStorageManifestSource()]);

        await using var client = await driver.OpenPhysicalClientAsync(Access(scope));
        var bounded = Record(client);
        IClaimMappingStore mappings = new GroundworkClaimMappingStore(
            client.DocumentStore,
            Accessor(scope),
            bounded);
        var expected = Enumerable.Range(0, pageSize + 1)
            .Select(index => ClaimMapping(
                index,
                scope,
                provider,
                order: (index * 37) % 101))
            .OrderBy(rule => rule.Order)
            .ThenBy(rule => rule.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var rule in expected.Reverse())
            await mappings.SaveAsync(rule);

        var actual = await mappings.ListForProviderAsync(scope, provider);

        Assert.Equal(expected.Select(rule => rule.Id), actual.Select(rule => rule.Id));
        Assert.Collection(
            bounded.Observations,
            observation => AssertIamClaimMappingPage(
                observation,
                expectsContinuation: false,
                expectedMaterialized: pageSize),
            observation => AssertIamClaimMappingPage(
                observation,
                expectsContinuation: true,
                expectedMaterialized: 1));
    }

    private static void AssertIamEmailLookup(
        QueryObservation observation,
        int expectedMaterialized)
    {
        Assert.Equal(
            IdentityStorageManifest.FindUserByNormalizedEmailQuery,
            observation.Query.QueryIdentity);
        Assert.Equal(2, observation.Query.Take);
        Assert.Equal(expectedMaterialized, observation.MaterializedDocuments);
        Assert.Collection(
            observation.Query.Clauses,
            clause => AssertComparison(
                clause,
                IdentityStorageManifest.NormalizedEmailKeyField,
                QueryComparisonOperator.Equal));
    }

    private static void AssertIamClaimMappingPage(
        QueryObservation observation,
        bool expectsContinuation,
        int expectedMaterialized)
    {
        Assert.Equal(
            IdentityStorageManifest.ListClaimMappingsByProviderQuery,
            observation.Query.QueryIdentity);
        Assert.Null(observation.Query.Skip);
        if (expectsContinuation)
            Assert.False(string.IsNullOrWhiteSpace(observation.Query.Continuation));
        else
            Assert.Null(observation.Query.Continuation);
        Assert.Equal(ElsaGroundworkQueryRoutes.MaximumResultCount, observation.Query.Take);
        Assert.Equal(expectedMaterialized, observation.MaterializedDocuments);
        Assert.Collection(
            observation.Query.Clauses,
            clause => AssertComparison(
                clause,
                IdentityStorageManifest.ProviderLookupKeyField,
                QueryComparisonOperator.Equal));
    }

    private static void AssertComparison(
        DocumentQueryClause clause,
        string path,
        QueryComparisonOperator comparisonOperator,
        string? value = null)
    {
        var comparison = Assert.Single(clause.Comparisons);
        Assert.Equal(path, comparison.Path);
        Assert.Equal(comparisonOperator, comparison.Operator);
        if (value is not null)
            Assert.Equal(value, Assert.Single(comparison.Values));
    }

    private static void AssertOrder(
        DocumentQueryOrder order,
        string path,
        PhysicalSortDirection direction = PhysicalSortDirection.Ascending)
    {
        Assert.Equal(path, order.Path);
        Assert.Equal(direction, order.Direction);
    }

    private static DocumentStoreAccess Access(string scope) =>
        DocumentStoreAccess.Scoped(new StorageScope(scope));

    private static FixedAccessContextAccessor Accessor(string scope) =>
        new(PersistenceAccessContext.Scoped(new PersistenceScope(scope)));

    private static RecordingBoundedDocumentStore Record(GroundworkProviderClient client) =>
        new(
            client.BoundedDocumentStore
            ?? throw new InvalidOperationException(
                "The physical provider did not expose its admitted bounded-query runtime."));

    private static UserRecord User(
        string id,
        string userName,
        string email,
        string tenantId = "tenant-a") => new(
        Id: id,
        TenantId: tenantId,
        UserName: userName,
        Email: email,
        DisplayName: userName,
        Status: UserStatus.Active,
        Ownership: ResourceOwnership.Foundation,
        RoleIds: new HashSet<string>(),
        DirectPermissions: new HashSet<string>());

    private static ClaimMappingRule ClaimMapping(
        int index,
        string tenantId,
        string provider,
        int order) => new(
        Id: $"mapping-{index:D4}",
        TenantId: tenantId,
        Provider: provider,
        MatchClaimType: "groups",
        MatchValue: $"group-{index:D4}",
        GrantRoles: new HashSet<string> { $"role-{index % 7}" },
        GrantPermissions: new HashSet<string> { $"permission-{index % 11}" },
        Order: order,
        StopOnMatch: index % 2 == 0);

    private static ExecutionPlacementClaim Claim(
        string workflowExecutionId,
        string ownerId,
        TimeSpan expiresIn,
        TimeSpan? requestedIn = null) =>
        new(workflowExecutionId, ownerId, Now.Add(requestedIn ?? TimeSpan.Zero), Now.Add(expiresIn));

    private static WorkflowExecutionCommandEnvelope Envelope(
        string workflowExecutionId,
        string envelopeId,
        string partition)
    {
        var command = new WorkflowExecutionCommand(
            CommandId: $"command-{envelopeId}",
            WorkflowExecutionId: workflowExecutionId,
            Kind: WorkflowExecutionCommandKind.RunSchedulerWork,
            EnqueuedAt: Now,
            Payload: null,
            Metadata: new Dictionary<string, string>());
        return new WorkflowExecutionCommandEnvelope(
            envelopeId,
            workflowExecutionId,
            command,
            $"idempotency-{envelopeId}",
            WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            Now,
            partition: new WorkflowExecutionPartition(partition));
    }

    private sealed class RecordingBoundedDocumentStore(IBoundedDocumentStore inner) : IBoundedDocumentStore
    {
        public List<QueryObservation> Observations { get; } = [];
        public List<DocumentQuery> CountQueries { get; } = [];

        public async Task<DocumentQueryResult> QueryAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.QueryAsync(query, cancellationToken);
            Observations.Add(new QueryObservation(
                query,
                result.Documents.Count,
                result.TotalCount));
            return result;
        }

        public Task<long> CountAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default)
        {
            CountQueries.Add(query);
            return inner.CountAsync(query, cancellationToken);
        }

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            inner.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            inner.AnyAsync(query, cancellationToken);
    }

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current)
        : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed record QueryObservation(
        DocumentQuery Query,
        int MaterializedDocuments,
        long TotalCount);
}
