using Elsa.Persistence.Groundwork.Testing;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.Groundwork;
using Elsa.Secrets.Persistence.Groundwork.Stores;
using Groundwork.Core.PhysicalStorage;
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
    public async Task Secret_filters_order_count_and_window_execute_before_materialization(string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients);
        await driver.ResetPhysicalAsync([new SecretsGroundworkStorageManifestSource()]);

        await using var client = await driver.OpenPhysicalClientAsync(
            DocumentStoreAccess.Scoped(new StorageScope("tenant-a")));
        var bounded = new RecordingBoundedDocumentStore(
            client.BoundedDocumentStore
            ?? throw new InvalidOperationException(
                "The physical provider did not expose its admitted bounded-query runtime."));
        ISecretRepository repository = new GroundworkSecretRepository(client.DocumentStore, bounded);

        await repository.SaveAsync(Secret("payments.alpha", "Payments API Alpha"));
        await repository.SaveAsync(Secret(
            "payments.beta",
            "Payments API Beta",
            expiresAt: Now.AddHours(1)));
        await repository.SaveAsync(Secret(
            "payments.expired",
            "Payments API Expired",
            expiresAt: Now.AddMinutes(-1)));
        await repository.SaveAsync(Secret(
            "payments.revoked",
            "Payments API Revoked",
            status: SecretStatus.Revoked));
        await repository.SaveAsync(Secret(
            "payments.configuration",
            "Payments API Configuration",
            storeName: SecretStoreNames.Configuration));
        await repository.SaveAsync(Secret("orders.alpha", "Orders API", scope: "orders"));
        await repository.SaveAsync(Secret("portable.unicode", "Search Å😀 value", scope: "portable"));
        await repository.SaveAsync(Secret("portable.literal", @"Search %_[].*\ value", scope: "portable"));

        var first = await repository.ListPageAsync(Request(skip: 0));
        var second = await repository.ListPageAsync(Request(skip: 1));
        var searched = await repository.ListPageAsync(Request(skip: 0, search: "Payments API Alpha"));
        var unicode = await repository.ListPageAsync(Request(skip: 0, search: "å😀", scope: "PORTABLE"));
        var literal = await repository.ListPageAsync(Request(skip: 0, search: @"%_[].*\", scope: "portable"));

        Assert.Equal(2, first.TotalCount);
        Assert.Equal(2, second.TotalCount);
        Assert.Equal("payments.alpha", Assert.Single(first.Items).Name);
        Assert.Equal("payments.beta", Assert.Single(second.Items).Name);
        Assert.Equal(1, searched.TotalCount);
        Assert.Equal("payments.alpha", Assert.Single(searched.Items).Name);
        Assert.Equal("portable.unicode", Assert.Single(unicode.Items).Name);
        Assert.Equal("portable.literal", Assert.Single(literal.Items).Name);
        Assert.All(
            bounded.Observations.Take(2),
            observation =>
            {
                Assert.Equal(SecretsStorageManifest.ListFilteredQuery, observation.Query.QueryIdentity);
                Assert.Equal(1, observation.Query.Take);
                Assert.InRange(observation.MaterializedDocuments, 0, 1);
                Assert.Collection(
                    observation.Query.Order,
                    order =>
                    {
                        Assert.Equal(SecretsStorageManifest.NormalizedNameField, order.Path);
                        Assert.Equal(PhysicalSortDirection.Ascending, order.Direction);
                    });
                Assert.Contains(
                    observation.Query.Clauses.SelectMany(clause => clause.Comparisons),
                    comparison =>
                        comparison.Path == SecretsStorageManifest.StatusField &&
                        comparison.Values.SequenceEqual(["active"]));
            });
        Assert.All(
            bounded.Observations.Skip(2),
            searchObservation =>
            {
                Assert.Equal(SecretsStorageManifest.SearchFilteredQuery, searchObservation.Query.QueryIdentity);
                Assert.Equal(1, searchObservation.Query.Take);
                Assert.InRange(searchObservation.MaterializedDocuments, 0, 1);
            });

        await using var otherTenantClient = await driver.OpenPhysicalClientAsync(
            DocumentStoreAccess.Scoped(new StorageScope("tenant-b")));
        ISecretRepository otherTenant = new GroundworkSecretRepository(
            otherTenantClient.DocumentStore,
            otherTenantClient.BoundedDocumentStore);
        await otherTenant.SaveAsync(Secret("payments.alpha", "Tenant B Payments API"));

        Assert.Single((await otherTenant.ListPageAsync(Request(skip: 0))).Items);
        Assert.Equal(2, (await repository.ListPageAsync(Request(skip: 0))).TotalCount);
    }

    private static SecretRepositoryListRequest Request(
        int skip,
        string? search = null,
        string scope = "FINANCE") => new(
        search: search,
        typeName: SecretTypeNames.Text,
        typeNames: [],
        storeName: SecretStoreNames.Encrypted,
        storeNames: [],
        scope: scope,
        status: null,
        excludedStatus: SecretStatus.Deleted,
        activeOnly: true,
        now: Now,
        skip: skip,
        take: 1);

    private static Secret Secret(
        string name,
        string displayName,
        string storeName = SecretStoreNames.Encrypted,
        SecretStatus status = SecretStatus.Active,
        DateTimeOffset? expiresAt = null,
        string scope = "finance") => new()
    {
        Name = name,
        DisplayName = displayName,
        TypeName = SecretTypeNames.Text,
        StoreName = storeName,
        Scope = scope,
        Status = status,
        Versions =
        [
            new SecretVersion
            {
                Version = 1,
                Status = status,
                ExpiresAt = expiresAt,
                Payload = SecretPayload.FromValue("value")
            }
        ]
    };

    private sealed class RecordingBoundedDocumentStore(IBoundedDocumentStore inner) : IBoundedDocumentStore
    {
        public List<QueryObservation> Observations { get; } = [];

        public async Task<DocumentQueryResult> QueryAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.QueryAsync(query, cancellationToken);
            Observations.Add(new QueryObservation(query, result.Documents.Count));
            return result;
        }

        public Task<long> CountAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            inner.CountAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            inner.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            inner.AnyAsync(query, cancellationToken);
    }

    private sealed record QueryObservation(DocumentQuery Query, int MaterializedDocuments);
}
