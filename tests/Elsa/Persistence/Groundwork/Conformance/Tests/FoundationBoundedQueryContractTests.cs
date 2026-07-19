using System.Text.Json;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Abstractions.Ownership;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.Groundwork;
using Elsa.Secrets.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Stores;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
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
        const int pageSize = 512;
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
                skip: 0,
                expectedMaterialized: pageSize),
            observation => AssertIamClaimMappingPage(
                observation,
                skip: pageSize,
                expectedMaterialized: 1));
    }

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

        await using var client = await driver.OpenPhysicalClientAsync(Access("tenant-a"));
        var bounded = Record(client);
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
        Assert.Equal(5, bounded.Observations.Count);
        Assert.All(
            bounded.Observations.Take(2),
            observation =>
            {
                Assert.Equal(SecretsStorageManifest.ListFilteredQuery, observation.Query.QueryIdentity);
                Assert.Equal(1, observation.Query.Take);
                Assert.InRange(observation.MaterializedDocuments, 0, 1);
                Assert.Equal(2, observation.TotalCount);
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
                Assert.Equal(1, searchObservation.TotalCount);
            });

        await using var otherTenantClient = await driver.OpenPhysicalClientAsync(Access("tenant-b"));
        ISecretRepository otherTenant = new GroundworkSecretRepository(
            otherTenantClient.DocumentStore,
            otherTenantClient.BoundedDocumentStore);
        await otherTenant.SaveAsync(Secret("payments.alpha", "Tenant B Payments API"));

        Assert.Single((await otherTenant.ListPageAsync(Request(skip: 0))).Items);
        Assert.Equal(2, (await repository.ListPageAsync(Request(skip: 0))).TotalCount);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Workflow_definition_pages_execute_through_the_declared_physical_route_before_materialization(
        string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients);
        await driver.ResetPhysicalAsync([new WorkflowsDesignGroundworkStorageManifestSource()]);

        await using var client = await driver.OpenPhysicalClientAsync(Access("tenant-a"));
        var bounded = Record(client);
        var definitions = new GroundworkWorkflowDefinitionStore(client.DocumentStore, bounded);
        foreach (var index in Enumerable.Range(0, 105).Reverse())
        {
            var definition = new WorkflowDefinition
            {
                Id = $"definition-{index:D3}",
                Name = $"definition-{index:D3}"
            };
            await client.DocumentStore.SaveAsync(new SaveDocumentRequest(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                definition.Id,
                WorkflowsDesignStorageManifest.SchemaVersion,
                JsonSerializer.Serialize(
                    new GroundworkDocument<WorkflowDefinition>(
                        WorkflowsDesignStorageManifest.WorkflowDefinitionCollection,
                        definition),
                    GroundworkDesignJson.Options)));
        }

        var page = await definitions.ListPageAsync(new WorkflowDefinitionListQuery(
            new WorkflowDefinitionFilter(),
            WorkflowDefinitionLifecycleScope.All,
            WorkflowDefinitionSortBy.Name,
            WorkflowDefinitionSortDirection.Asc,
            Page: 2,
            PageSize: 2));

        Assert.Equal(105, page.TotalCount);
        Assert.Equal(["definition-002", "definition-003"], page.Items.Select(definition => definition.Id));
        var observation = Assert.Single(bounded.Observations);
        Assert.Equal(WorkflowsDesignStorageManifest.PageByNameQuery, observation.Query.QueryIdentity);
        Assert.Equal(2, observation.Query.Skip);
        Assert.Equal(2, observation.Query.Take);
        Assert.InRange(observation.MaterializedDocuments, 0, 2);
        Assert.Equal(105, observation.TotalCount);
        Assert.Collection(
            observation.Query.Order,
            order => AssertOrder(order, WorkflowsDesignStorageManifest.WorkflowDefinitionNameField),
            order => AssertOrder(order, WorkflowsDesignStorageManifest.WorkflowDefinitionIdField));

        foreach (var id in new[] { "duplicate-a", "duplicate-b" })
        {
            await client.DocumentStore.SaveAsync(new SaveDocumentRequest(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                id,
                WorkflowsDesignStorageManifest.SchemaVersion,
                JsonSerializer.Serialize(
                    new GroundworkDocument<WorkflowDefinition>(
                        WorkflowsDesignStorageManifest.WorkflowDefinitionCollection,
                        new WorkflowDefinition { Id = id, Name = "zeta" }),
                    GroundworkDesignJson.Options)));
        }

        var descending = await definitions.ListPageAsync(new WorkflowDefinitionListQuery(
            new WorkflowDefinitionFilter(),
            WorkflowDefinitionLifecycleScope.All,
            WorkflowDefinitionSortBy.Name,
            WorkflowDefinitionSortDirection.Desc,
            Page: 1,
            PageSize: 3));

        Assert.Equal(["duplicate-a", "duplicate-b", "definition-104"], descending.Items.Select(definition => definition.Id));
        var descendingObservation = bounded.Observations[^1];
        Assert.Equal(WorkflowsDesignStorageManifest.PageByNameDescendingQuery, descendingObservation.Query.QueryIdentity);
        Assert.Equal(3, descendingObservation.Query.Take);
        Assert.InRange(descendingObservation.MaterializedDocuments, 0, 3);
        Assert.Collection(
            descendingObservation.Query.Order,
            order => AssertOrder(order, WorkflowsDesignStorageManifest.WorkflowDefinitionNameField, PhysicalSortDirection.Descending),
            order => AssertOrder(order, WorkflowsDesignStorageManifest.WorkflowDefinitionIdField));

        await SaveWorkflowDefinitionAsync(
            client.DocumentStore,
            new WorkflowDefinition { Id = "invoice-active", Name = "Invoice Case" });
        await SaveWorkflowDefinitionAsync(
            client.DocumentStore,
            new WorkflowDefinition { Id = "invoice-deleted", Name = "Invoice Deleted", DeletedAt = Now });

        var mixedCaseSearch = await definitions.ListPageAsync(new WorkflowDefinitionListQuery(
            new WorkflowDefinitionFilter { SearchTerm = "iNvOiCe" },
            WorkflowDefinitionLifecycleScope.Active,
            WorkflowDefinitionSortBy.Name,
            WorkflowDefinitionSortDirection.Asc,
            Page: 1,
            PageSize: 10));
        var deleted = await definitions.ListPageAsync(new WorkflowDefinitionListQuery(
            new WorkflowDefinitionFilter(),
            WorkflowDefinitionLifecycleScope.Deleted,
            WorkflowDefinitionSortBy.Name,
            WorkflowDefinitionSortDirection.Asc,
            Page: 1,
            PageSize: 10));
        var legacy = await definitions.ListAsync(new WorkflowDefinitionFilter { Id = "invoice-active" });

        Assert.Equal(["invoice-active"], mixedCaseSearch.Items.Select(definition => definition.Id));
        Assert.Equal(["invoice-deleted"], deleted.Items.Select(definition => definition.Id));
        Assert.Equal(["invoice-active"], legacy.Select(definition => definition.Id));

        await using (var otherTenant = await driver.OpenPhysicalClientAsync(Access("tenant-b")))
        {
            await SaveWorkflowDefinitionAsync(
                otherTenant.DocumentStore,
                new WorkflowDefinition { Id = "tenant-b-only", Name = "Tenant B Invoice" });
            var tenantBDefinitions = new GroundworkWorkflowDefinitionStore(
                otherTenant.DocumentStore,
                otherTenant.BoundedDocumentStore);
            Assert.Equal(
                ["tenant-b-only"],
                (await tenantBDefinitions.ListPageAsync(new WorkflowDefinitionListQuery(
                    new WorkflowDefinitionFilter(),
                    WorkflowDefinitionLifecycleScope.All,
                    WorkflowDefinitionSortBy.Name,
                    WorkflowDefinitionSortDirection.Asc,
                    Page: 1,
                    PageSize: 10))).Items.Select(definition => definition.Id));
        }

        Assert.Equal(
            109,
            (await definitions.ListPageAsync(new WorkflowDefinitionListQuery(
                new WorkflowDefinitionFilter(),
                WorkflowDefinitionLifecycleScope.All,
                WorkflowDefinitionSortBy.Name,
                WorkflowDefinitionSortDirection.Asc,
                Page: 1,
                PageSize: 1))).TotalCount);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Workflow_definition_projection_upgrade_backfills_documents_written_under_the_legacy_manifest(
        string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients);

        await driver.ResetPhysicalAsync([new LegacyWorkflowDesignManifestSource()]);
        await using (var legacyClient = await driver.OpenPhysicalClientAsync(Access("tenant-a")))
        {
            var definition = new WorkflowDefinition { Id = "pre-upgrade", Name = "Pre Upgrade" };
            await legacyClient.DocumentStore.SaveAsync(new SaveDocumentRequest(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                definition.Id,
                "1.0.0",
                JsonSerializer.Serialize(
                    new GroundworkDocument<WorkflowDefinition>(
                        WorkflowsDesignStorageManifest.WorkflowDefinitionCollection,
                        definition),
                    GroundworkDesignJson.Options)));
        }

        await driver.ApplyPhysicalAsync([new WorkflowsDesignGroundworkStorageManifestSource()]);
        await using var upgradedClient = await driver.OpenPhysicalClientAsync(Access("tenant-a"));
        var definitions = new GroundworkWorkflowDefinitionStore(
            upgradedClient.DocumentStore,
            upgradedClient.BoundedDocumentStore);

        var page = await definitions.ListPageAsync(new WorkflowDefinitionListQuery(
            new WorkflowDefinitionFilter(),
            WorkflowDefinitionLifecycleScope.All,
            WorkflowDefinitionSortBy.Name,
            WorkflowDefinitionSortDirection.Asc,
            Page: 1,
            PageSize: 10));

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("pre-upgrade", Assert.Single(page.Items).Id);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Workflow_definition_projection_upgrade_accepts_the_128_character_boundary(string providerKey)
    {
        var id = new string('i', WorkflowDefinitionConstraints.MaximumIdLength);
        var name = new string('n', WorkflowDefinitionConstraints.MaximumNameLength);
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        await driver.ResetPhysicalAsync([new LegacyWorkflowDesignManifestSource()]);
        await using (var legacyClient = await driver.OpenPhysicalClientAsync(Access("tenant-a")))
            await SaveWorkflowDefinitionAsync(legacyClient.DocumentStore, new WorkflowDefinition { Id = id, Name = name });

        await driver.ApplyPhysicalAsync([new WorkflowsDesignGroundworkStorageManifestSource()]);
        await using var upgradedClient = await driver.OpenPhysicalClientAsync(Access("tenant-a"));
        var definitions = new GroundworkWorkflowDefinitionStore(upgradedClient.DocumentStore, upgradedClient.BoundedDocumentStore);
        Assert.Equal(id, Assert.Single((await definitions.ListPageAsync(new WorkflowDefinitionListQuery(
            new WorkflowDefinitionFilter { Id = id }, WorkflowDefinitionLifecycleScope.All,
            WorkflowDefinitionSortBy.Name, WorkflowDefinitionSortDirection.Asc, 1, 10))).Items).Id);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Workflow_definition_projection_upgrade_rejects_129_character_legacy_values_without_data_loss(string providerKey)
    {
        var id = new string('i', WorkflowDefinitionConstraints.MaximumIdLength + 1);
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        await driver.ResetPhysicalAsync([new LegacyWorkflowDesignManifestSource()]);
        await using (var legacyClient = await driver.OpenPhysicalClientAsync(Access("tenant-a")))
            await SaveWorkflowDefinitionAsync(legacyClient.DocumentStore, new WorkflowDefinition { Id = id, Name = "Legacy" });

        var exception = await Assert.ThrowsAsync<PhysicalProjectionValueValidationException>(() =>
            driver.ApplyPhysicalAsync([new WorkflowsDesignGroundworkStorageManifestSource()]).AsTask());
        Assert.Equal("GW-PHYSICAL-037", exception.Diagnostic.Code);
        Assert.Equal("projectedColumns.entity.id", exception.Diagnostic.Target);

        await using var legacyClientAfterRejection = await driver.OpenPhysicalClientAsync(Access("tenant-a"));
        var preserved = await legacyClientAfterRejection.DocumentStore.LoadAsync(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            id);
        Assert.NotNull(preserved);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Workflow_definition_projection_upgrade_rejects_129_character_legacy_names_without_data_loss(string providerKey)
    {
        const string id = "legacy-name-overflow";
        var name = new string('n', WorkflowDefinitionConstraints.MaximumNameLength + 1);
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        await driver.ResetPhysicalAsync([new LegacyWorkflowDesignManifestSource()]);
        await using (var legacyClient = await driver.OpenPhysicalClientAsync(Access("tenant-a")))
            await SaveWorkflowDefinitionAsync(legacyClient.DocumentStore, new WorkflowDefinition { Id = id, Name = name });

        var exception = await Assert.ThrowsAsync<PhysicalProjectionValueValidationException>(() =>
            driver.ApplyPhysicalAsync([new WorkflowsDesignGroundworkStorageManifestSource()]).AsTask());
        Assert.Equal("GW-PHYSICAL-037", exception.Diagnostic.Code);
        Assert.Equal("projectedColumns.entity.name", exception.Diagnostic.Target);

        await using var legacyClientAfterRejection = await driver.OpenPhysicalClientAsync(Access("tenant-a"));
        var preserved = await legacyClientAfterRejection.DocumentStore.LoadAsync(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            id);
        Assert.NotNull(preserved);
    }

    private sealed class LegacyWorkflowDesignManifestSource : IGroundworkStorageManifestSource
    {
        public string FeatureIdentity => "elsa-workflows-design";

        public async ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(
            CancellationToken cancellationToken = default)
        {
            var current = await new WorkflowsDesignGroundworkStorageManifestSource()
                .CreateDeclarationAsync(cancellationToken);
            var legacy = LegacyGroundworkStorageManifestPhysicalizer.Physicalize(current.Manifest with
            {
                Version = new global::Groundwork.Core.Manifests.StorageManifestVersion("1.0.0"),
                StorageUnits = current.Manifest.StorageUnits
                    .Select(unit => unit with { PhysicalStorage = null })
                    .ToArray()
            });
            return new GroundworkStorageManifestDeclaration(
                FeatureIdentity,
                legacy,
                current.RequiredStoreContracts,
                current.RequiredRoutes,
                current.TopologyRequirements,
                current.CoverageRows);
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Distributed_placement_and_command_routes_filter_order_distinct_count_and_limit_before_materialization(
        string providerKey)
    {
        const string scope = "tenant-a";
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients |
            GroundworkTopologyCapabilities.MultiDocumentTransactions);
        await driver.ResetPhysicalAsync([new DistributedGroundworkStorageManifestSource()]);

        await using var client = await driver.OpenPhysicalClientAsync(Access(scope));
        var bounded = Record(client);
        var placements = new GroundworkExecutionPlacementStore(client.DocumentStore, bounded);
        var commands = new GroundworkExecutionCommandTransport(
            client.DocumentStore,
            Accessor(scope),
            bounded);

        await placements.TryClaimAsync(Claim("wf-later", "node-a", TimeSpan.FromMinutes(5)), Now);
        await placements.TryClaimAsync(Claim("wf-first", "node-a", TimeSpan.FromMinutes(1)), Now);
        await placements.TryClaimAsync(
            Claim(
                "wf-expired",
                "node-a",
                TimeSpan.FromMinutes(-1),
                TimeSpan.FromMinutes(-2)),
            Now);
        await placements.TryClaimAsync(Claim("wf-foreign", "node-b", TimeSpan.FromMinutes(1)), Now);

        var owned = await placements.ListOwnedAsync(
            new ExecutionPlacementLeaseListRequest("node-a", Now, take: 1));

        Assert.Equal("wf-first", Assert.Single(owned).WorkflowExecutionId);
        var placementQuery = Assert.Single(bounded.Observations);
        Assert.Equal(
            DistributedGroundworkStorageManifest.ListOwnedPlacementsQuery,
            placementQuery.Query.QueryIdentity);
        Assert.Equal(1, placementQuery.Query.Take);
        Assert.InRange(placementQuery.MaterializedDocuments, 0, 1);
        Assert.Collection(
            placementQuery.Query.Clauses,
            clause => AssertComparison(
                clause,
                DistributedGroundworkStorageManifest.OwnerIdLookupKeyField,
                QueryComparisonOperator.Equal),
            clause => AssertComparison(
                clause,
                DistributedGroundworkStorageManifest.OwnerIdComparisonKeyField,
                QueryComparisonOperator.Equal),
            clause => AssertComparison(
                clause,
                DistributedGroundworkStorageManifest.ExpiresAtField,
                QueryComparisonOperator.GreaterThan,
                Now.ToString("O")));
        Assert.Collection(
            placementQuery.Query.Order,
            order => AssertOrder(
                order,
                DistributedGroundworkStorageManifest.OwnerIdLookupKeyField),
            order => AssertOrder(order, DistributedGroundworkStorageManifest.ExpiresAtField),
            order => AssertOrder(
                order,
                DistributedGroundworkStorageManifest.WorkflowExecutionIdKeyField));

        await commands.SendAsync("wf-a", Envelope("wf-a", "a-1", scope), Now);
        await commands.SendAsync("wf-a", Envelope("wf-a", "a-2", scope), Now.AddSeconds(1));
        await commands.SendAsync("wf-a", Envelope("wf-a", "a-3", scope), Now.AddSeconds(2));
        await commands.SendAsync("wf-b", Envelope("wf-b", "b-1", scope), Now.AddSeconds(3));

        var firstPending = await commands.ListPendingExecutionIdsAsync(Now, maxItems: 1);
        var bothPending = await commands.ListPendingExecutionIdsAsync(Now, maxItems: 2);
        var pendingCount = await commands.CountPendingAsync("wf-a");
        var leased = await commands.LeaseAsync(
            "wf-a",
            "node-a",
            Now,
            TimeSpan.FromMinutes(1),
            maxItems: 2);

        Assert.Equal(["wf-a"], firstPending);
        Assert.Equal(["wf-a", "wf-b"], bothPending);
        Assert.Equal(3, pendingCount);
        Assert.Equal([1L, 2L], leased.Select(item => item.Sequence));

        var commandQueries = bounded.Observations.Skip(1).ToArray();
        Assert.Collection(
            commandQueries,
            observation => AssertPendingExecutions(observation, take: 1),
            observation => AssertPendingExecutions(observation, take: 2),
            observation =>
            {
                Assert.Equal(
                    DistributedGroundworkStorageManifest.LeaseVisibleCommandsQuery,
                    observation.Query.QueryIdentity);
                Assert.Equal(2, observation.Query.Take);
                Assert.InRange(observation.MaterializedDocuments, 0, 2);
                Assert.Collection(
                    observation.Query.Clauses,
                    clause => AssertComparison(
                        clause,
                        DistributedGroundworkStorageManifest.WorkflowExecutionIdKeyField,
                        QueryComparisonOperator.Equal),
                    clause => AssertComparison(
                        clause,
                        DistributedGroundworkStorageManifest.VisibleAtField,
                        QueryComparisonOperator.LessThanOrEqual,
                        Now.ToString("O")));
                Assert.Collection(
                    observation.Query.Order,
                    order => AssertOrder(
                        order,
                        DistributedGroundworkStorageManifest.SequenceField));
            });
        var countQuery = Assert.Single(bounded.CountQueries);
        Assert.Equal(
            DistributedGroundworkStorageManifest.CountPendingCommandsQuery,
            countQuery.QueryIdentity);
        Assert.Equal(BoundedQueryResultOperation.Count, countQuery.ResultOperation);
        Assert.Collection(
            countQuery.Clauses,
            clause => AssertComparison(
                clause,
                DistributedGroundworkStorageManifest.WorkflowExecutionIdKeyField,
                QueryComparisonOperator.Equal));

        await using var otherTenant = await driver.OpenPhysicalClientAsync(Access("tenant-b"));
        var otherTenantCommands = new GroundworkExecutionCommandTransport(
            otherTenant.DocumentStore,
            Accessor("tenant-b"),
            otherTenant.BoundedDocumentStore);
        await otherTenantCommands.SendAsync(
            "wf-a",
            Envelope("wf-a", "tenant-b-a-1", "tenant-b"),
            Now);

        Assert.Equal(1, await otherTenantCommands.CountPendingAsync("wf-a"));
        Assert.Equal(3, await commands.CountPendingAsync("wf-a"));
    }

    private static void AssertPendingExecutions(QueryObservation observation, int take)
    {
        Assert.Equal(
            DistributedGroundworkStorageManifest.ListPendingExecutionIdsQuery,
            observation.Query.QueryIdentity);
        Assert.Equal(take, observation.Query.Take);
        Assert.Equal(
            DistributedGroundworkStorageManifest.WorkflowExecutionIdKeyField,
            observation.Query.LatestPerKeyPath);
        Assert.InRange(observation.MaterializedDocuments, 0, take);
        Assert.Collection(
            observation.Query.Clauses,
            clause => AssertComparison(
                clause,
                DistributedGroundworkStorageManifest.CollectionField,
                QueryComparisonOperator.Equal,
                DistributedRuntimeStorageManifest.ExecutionCommandTransportDocumentKind),
            clause => AssertComparison(
                clause,
                DistributedGroundworkStorageManifest.VisibleAtField,
                QueryComparisonOperator.LessThanOrEqual,
                Now.ToString("O")));
        Assert.Collection(
            observation.Query.Order,
            order => AssertOrder(
                order,
                DistributedGroundworkStorageManifest.WorkflowExecutionIdKeyField),
            order => AssertOrder(
                order,
                DistributedGroundworkStorageManifest.SequenceField,
                PhysicalSortDirection.Descending));
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
        int skip,
        int expectedMaterialized)
    {
        Assert.Equal(
            IdentityStorageManifest.ListClaimMappingsByProviderQuery,
            observation.Query.QueryIdentity);
        Assert.Equal(skip, observation.Query.Skip);
        Assert.Equal(512, observation.Query.Take);
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

    private static async Task SaveWorkflowDefinitionAsync(IDocumentStore store, WorkflowDefinition definition) =>
        _ = await store.SaveAsync(new SaveDocumentRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            definition.Id,
            WorkflowsDesignStorageManifest.SchemaVersion,
            JsonSerializer.Serialize(
                new GroundworkDocument<WorkflowDefinition>(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionCollection,
                    definition),
                GroundworkDesignJson.Options)));

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
