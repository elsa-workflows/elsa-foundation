using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Querying;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Activities.Design.Persistence.Groundwork.TemporalProjectionTests;

public sealed class GroundworkActivityDefinitionTemporalProjectionTests
{
    [Fact]
    public void Manifest_declares_scale_bearing_physical_queries_for_every_temporal_projection()
    {
        var managementUnits = ActivitiesDesignStorageManifest.Create().StorageUnits
            .Where(unit => unit.Identity.Value is
                ActivitiesDesignStorageManifest.ActivityDefinitionManagementProjectionDocumentKind or
                ActivitiesDesignStorageManifest.ActivityDraftManagementProjectionDocumentKind or
                ActivitiesDesignStorageManifest.ActivityVersionManagementProjectionDocumentKind)
            .ToArray();

        Assert.Equal(3, managementUnits.Length);
        Assert.All(managementUnits, unit =>
        {
            Assert.NotNull(unit.PhysicalStorage);
            var physicalStorage = unit.PhysicalStorage;
            var policy = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(physicalStorage.Policy);
            Assert.Equal(PhysicalStorageForm.PhysicalEntityTable, policy.Definition.Form);

            var current = physicalStorage.BoundedQueries.Single(query =>
                query.Identity.EndsWith("-current", StringComparison.Ordinal));
            Assert.Equal(BoundedQueryExecutionClass.ScaleBearing, current.ExecutionClass);
            Assert.Equal(QueryPagingSupport.None, current.PagingSupport);
            Assert.Single(current.PredicateFields);
            Assert.Contains(current.ResidualPredicateFields, field =>
                field.Path == ActivitiesDesignStorageManifest.ManagementValidToField);

            var page = physicalStorage.BoundedQueries.Single(query =>
                query.Identity.EndsWith("-identity-asc", StringComparison.Ordinal));
            Assert.Equal(BoundedQueryExecutionClass.ScaleBearing, page.ExecutionClass);
            Assert.Equal(QueryPagingSupport.Offset, page.PagingSupport);
            Assert.True(page.SupportsDisjunction);
            Assert.True(page.SupportsTotalCount);
            Assert.Equal(
                new BoundedQuerySortField(
                    ActivitiesDesignStorageManifest.ManagementSortField,
                    PhysicalSortDirection.Ascending),
                Assert.Single(page.SortFields));
            Assert.Equal(
                ActivitiesDesignStorageManifest.ManagementSortField,
                Assert.Single(page.PredicateFields).Path);
            AssertSuperset(page.ResidualPredicateFields.Select(field => field.Path),
            [
                ActivitiesDesignStorageManifest.ManagementVisibilityField,
                ActivitiesDesignStorageManifest.ManagementValidFromField,
                ActivitiesDesignStorageManifest.ManagementValidToField,
                ActivitiesDesignStorageManifest.ManagementSearchField
            ]);

            var retention = physicalStorage.BoundedQueries.Single(query =>
                query.Identity == ActivitiesDesignStorageManifest.ManagementExpiredQuery);
            Assert.Equal(BoundedQueryExecutionClass.ScaleBearing, retention.ExecutionClass);
            Assert.True(retention.SupportsTotalCount);
            Assert.Contains(retention.PredicateFields, field =>
                field.Path == ActivitiesDesignStorageManifest.ManagementValidToField);

            Assert.All(policy.Definition.ProjectedColumns
                .Where(column => column.Path is
                    ActivitiesDesignStorageManifest.ManagementValidFromField or
                    ActivitiesDesignStorageManifest.ManagementValidToField),
                column => Assert.Equal(
                    ActivitiesDesignStorageManifest.ManagementSequenceKeyLength,
                    column.Length));
            Assert.Collection(
                policy.Definition.Indexes.Single(index => index.LogicalName == "management-by-id").Columns,
                scope => Assert.Equal("storage_scope", scope.ColumnLogicalName),
                id => Assert.NotEqual("valid_to", id.ColumnLogicalName));
            Assert.Collection(
                policy.Definition.Indexes.Single(index => index.LogicalName == "management-by-sort").Columns,
                scope => Assert.Equal("storage_scope", scope.ColumnLogicalName),
                sort => Assert.Equal("sort_key", sort.ColumnLogicalName),
                identity => Assert.Equal(
                    new DocumentEnvelopeDefinition().IdComparisonKeyColumn,
                    identity.ColumnLogicalName));
        });
    }

    [Fact]
    public async Task Bound_snapshot_keeps_the_original_definition_when_a_later_mutation_changes_it()
    {
        var documents = new TemporalProjectionDocumentStore();
        var writer = new GroundworkActivityManagementProjectionWriter(documents, new ImmediateDistributedLockProvider());
        var reader = Reader(documents);
        var firstDefinition = Definition("Original display name", new DateTimeOffset(2026, 7, 17, 8, 0, 0, TimeSpan.Zero));
        var authoring = Authoring(firstDefinition.LastModifiedAt);
        var draft = Draft(firstDefinition.LastModifiedAt);

        await CommitAsync(documents, await writer.PrepareAsync(new(
            firstDefinition.LastModifiedAt,
            [new(firstDefinition, authoring)],
            [draft],
            [])));
        var firstSnapshot = await reader.GetCurrentSnapshotAsync();

        var changedAt = firstDefinition.LastModifiedAt.AddMinutes(1);
        var changedDefinition = Definition("Changed display name", changedAt);
        authoring.LastModifiedAt = changedAt;
        await CommitAsync(documents, await writer.PrepareAsync(new(
            changedAt,
            [new(changedDefinition, authoring)],
            [],
            [])));

        var originalPage = await reader.ReadDefinitionsAsync(new(null, firstSnapshot.Sequence, 0, 20));
        var currentPage = await reader.ReadDefinitionsAsync(new(null, null, 0, 20));

        Assert.Equal("Original display name", Assert.Single(originalPage.Items).DisplayName);
        Assert.Equal("Changed display name", Assert.Single(currentPage.Items).DisplayName);
        Assert.Equal(firstSnapshot, originalPage.Snapshot);
        Assert.True(currentPage.Snapshot.Sequence > firstSnapshot.Sequence);
    }

    [Fact]
    public async Task Pre_watermark_snapshot_remains_a_valid_empty_view_when_the_first_write_races()
    {
        var documents = new TemporalProjectionDocumentStore();
        var writer = new GroundworkActivityManagementProjectionWriter(documents, new ImmediateDistributedLockProvider());
        var reader = Reader(documents);
        var emptySnapshot = await reader.GetCurrentSnapshotAsync();
        var changedAt = new DateTimeOffset(2026, 7, 17, 8, 30, 0, TimeSpan.Zero);

        await CommitAsync(documents, await writer.PrepareAsync(new(
            changedAt,
            [DefinitionChange("definition-first", null, "First", changedAt)],
            [],
            [])));

        var racedFirstPage = await reader.ReadDefinitionsAsync(new(null, emptySnapshot.Sequence, 0, 25));
        var freshFirstPage = await reader.ReadDefinitionsAsync(new(null, null, 0, 25));
        Assert.Equal(0, emptySnapshot.Sequence);
        Assert.Empty(racedFirstPage.Items);
        Assert.Equal(0, racedFirstPage.TotalCount);
        Assert.Equal(0, racedFirstPage.Snapshot.Sequence);
        Assert.Equal("definition-first", Assert.Single(freshFirstPage.Items).DefinitionId);
        Assert.Equal(1, freshFirstPage.Snapshot.Sequence);
    }

    [Fact]
    public async Task Point_lookups_select_the_first_result_operation_declared_by_the_physical_route()
    {
        var documents = new TemporalProjectionDocumentStore();
        var writer = new GroundworkActivityManagementProjectionWriter(documents, new ImmediateDistributedLockProvider());
        var changedAt = new DateTimeOffset(2026, 7, 17, 8, 45, 0, TimeSpan.Zero);
        var definition = Definition("Point lookup", changedAt);
        var authoring = Authoring(changedAt);

        await CommitAsync(documents, await writer.PrepareAsync(new(
            changedAt,
            [new(definition, authoring)],
            [],
            [])));

        documents.ResetQueries();
        var updatedAt = changedAt.AddMinutes(1);
        definition.LastModifiedAt = updatedAt;
        authoring.LastModifiedAt = updatedAt;
        await using var update = await writer.PrepareAsync(new(
            updatedAt,
            [new(definition, authoring)],
            [],
            []));
        var currentQuery = Assert.Single(documents.Queries);
        Assert.Equal(ActivitiesDesignStorageManifest.ManagementDefinitionCurrentQuery, currentQuery.QueryIdentity);
        Assert.Equal(BoundedQueryResultOperation.First, currentQuery.ResultOperation);

        documents.ResetQueries();
        var found = await Reader(documents).FindDefinitionAsync(definition.Id, definition.TenantId);
        Assert.NotNull(found);
        var definitionQuery = Assert.Single(documents.Queries);
        Assert.Equal(ActivitiesDesignStorageManifest.ManagementDefinitionsQuery, definitionQuery.QueryIdentity);
        Assert.Equal(BoundedQueryResultOperation.First, definitionQuery.ResultOperation);
    }

    [Fact]
    public async Task Provider_applies_visibility_search_sort_page_and_exact_count_with_more_than_500_noise_rows()
    {
        var documents = new TemporalProjectionDocumentStore();
        var writer = new GroundworkActivityManagementProjectionWriter(documents, new ImmediateDistributedLockProvider());
        var reader = Reader(documents, "tenant-a");
        var changedAt = new DateTimeOffset(2026, 7, 17, 9, 0, 0, TimeSpan.Zero);
        var changes = Enumerable.Range(0, 520)
            .Select(index => DefinitionChange(
                $"definition-{index:D4}",
                index < 3 ? null : "tenant-b",
                index < 3 ? $"Needle {index}" : $"Noise {index}",
                changedAt))
            .ToArray();
        await CommitAsync(documents, await writer.PrepareAsync(new(changedAt, changes, [], [])));
        documents.ResetQueries();

        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadDefinitionsAsync(new(
            "tenant-b",
            null,
            0,
            2)));

        var first = await reader.ReadDefinitionsAsync(new(
            "tenant-a",
            null,
            0,
            2,
            "needle",
            ActivityContentAuthorityKind.Design));
        var insertedAt = changedAt.AddMinutes(1);
        await CommitAsync(documents, await writer.PrepareAsync(new(
            insertedAt,
            [DefinitionChange("definition-new", null, "Needle new", insertedAt)],
            [],
            [])));
        var second = await reader.ReadDefinitionsAsync(new(
            "tenant-a",
            first.Snapshot.Sequence,
            first.NextOffset!.Value,
            2,
            "needle",
            ActivityContentAuthorityKind.Design));
        var fresh = await reader.ReadDefinitionsAsync(new(
            "tenant-a",
            null,
            0,
            2,
            "needle",
            ActivityContentAuthorityKind.Design));

        Assert.Equal(3, first.TotalCount);
        Assert.Equal(new[] { "definition-0000", "definition-0001" }, first.Items.Select(x => x.DefinitionId));
        Assert.Equal("definition-0002", Assert.Single(second.Items).DefinitionId);
        Assert.Null(second.NextOffset);
        Assert.Equal(first.Snapshot, second.Snapshot);
        Assert.Equal(4, fresh.TotalCount);
        Assert.True(fresh.Snapshot.Sequence > first.Snapshot.Sequence);
        var pageQueries = documents.Queries
            .Where(query => query.QueryIdentity == ActivitiesDesignStorageManifest.ManagementDefinitionsQuery)
            .ToArray();
        Assert.Equal(3, pageQueries.Length);
        Assert.All(pageQueries, query =>
        {
            Assert.Equal(2, query.Take);
            Assert.Contains(query.Clauses.SelectMany(x => x.Comparisons), x =>
                x.Path == ActivitiesDesignStorageManifest.ManagementVisibilityField);
            Assert.Contains(query.Clauses.SelectMany(x => x.Comparisons), x =>
                x.Path == ActivitiesDesignStorageManifest.ManagementValidFromField);
            Assert.Contains(query.Clauses.SelectMany(x => x.Comparisons), x =>
                x.Path == ActivitiesDesignStorageManifest.ManagementValidToField);
            Assert.Contains(query.Clauses.SelectMany(x => x.Comparisons), x =>
                x.Path == ActivitiesDesignStorageManifest.ManagementSearchField);
        });
    }

    [Fact]
    public async Task Late_projection_conflict_rolls_back_authoritative_rows_revisions_and_watermark()
    {
        const string domainKind = "authoritative-test-domain";
        var documents = new TemporalProjectionDocumentStore
        {
            FailOnDocumentKind = ActivitiesDesignStorageManifest.ActivityManagementProjectionSnapshotDocumentKind
        };
        var writer = new GroundworkActivityManagementProjectionWriter(documents, new ImmediateDistributedLockProvider());
        var changedAt = new DateTimeOffset(2026, 7, 17, 10, 0, 0, TimeSpan.Zero);
        await using var update = await writer.PrepareAsync(new(
            changedAt,
            [DefinitionChange("definition-atomic", null, "Atomic", changedAt)],
            [],
            []));

        await Assert.ThrowsAsync<DocumentAtomicWriteException>(() => update.CommitAsync(
            [domainKind],
            [new SaveDocumentRequest(domainKind, "domain-1", "1.0.0", "{}", 0)]));

        Assert.Null(await documents.LoadAsync(domainKind, "domain-1"));
        Assert.Null(await documents.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityManagementProjectionWatermarkDocumentKind,
            ActivityManagementProjectionWatermark.CurrentId));
        Assert.Null(await documents.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityDefinitionManagementProjectionDocumentKind,
            update.Requests.First(x =>
                x.DocumentKind == ActivitiesDesignStorageManifest.ActivityDefinitionManagementProjectionDocumentKind).Id));
    }

    [Fact]
    public async Task Writer_rejects_cross_definition_and_cross_tenant_projection_ownership()
    {
        var documents = new TemporalProjectionDocumentStore();
        var writer = new GroundworkActivityManagementProjectionWriter(documents, new ImmediateDistributedLockProvider());
        var changedAt = new DateTimeOffset(2026, 7, 17, 10, 30, 0, TimeSpan.Zero);
        var first = DefinitionChange("definition-a", null, "A", changedAt);
        var second = DefinitionChange("definition-b", null, "B", changedAt);
        var firstVersion = Version("version-a", "definition-a", "1.0.0", ActivityDefinitionVersionLifecycle.Active, "provider-a", changedAt);
        var secondVersion = Version("version-b", "definition-b", "1.0.0", ActivityDefinitionVersionLifecycle.Active, "provider-b", changedAt);
        first.Authoring.HeadVersionId = firstVersion.DefinitionVersionId;
        second.Authoring.HeadVersionId = secondVersion.DefinitionVersionId;
        await CommitAsync(documents, await writer.PrepareAsync(new(
            changedAt,
            [first, second],
            [],
            [firstVersion, secondVersion])));

        var crossDefinition = DefinitionChange("definition-a", null, "A", changedAt.AddMinutes(1));
        crossDefinition.Authoring.HeadVersionId = secondVersion.DefinitionVersionId;
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.PrepareAsync(new(
            changedAt.AddMinutes(1),
            [crossDefinition],
            [],
            [])));

        var crossTenantDraft = Draft(
            "draft-cross-tenant",
            "definition-a",
            "Cross tenant",
            ActivityDefinitionDraftStatus.Active,
            "provider-a",
            changedAt.AddMinutes(1));
        crossTenantDraft.TenantId = "tenant-b";
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.PrepareAsync(new(
            changedAt.AddMinutes(1),
            [],
            [crossTenantDraft],
            [])));

        Assert.Equal(1, (await Reader(documents).GetCurrentSnapshotAsync()).Sequence);
    }

    [Fact]
    public async Task Draft_version_and_definition_provider_filters_use_only_safe_temporal_summary_fields()
    {
        var documents = new TemporalProjectionDocumentStore();
        var writer = new GroundworkActivityManagementProjectionWriter(documents, new ImmediateDistributedLockProvider());
        var reader = Reader(documents);
        var changedAt = new DateTimeOffset(2026, 7, 17, 11, 0, 0, TimeSpan.Zero);
        var definition = DefinitionChange("definition-filtered", null, "Filtered", changedAt);
        definition.Authoring.HeadVersionId = "version-1";
        definition.Authoring.RecommendedVersionId = "version-2";
        var drafts = new[]
        {
            Draft("draft-alpha", "definition-filtered", "Alpha draft", ActivityDefinitionDraftStatus.Active, "provider-a", changedAt),
            Draft("draft-beta", "definition-filtered", "Beta draft", ActivityDefinitionDraftStatus.Discarded, "provider-a", changedAt),
            Draft("draft-gamma", "definition-filtered", "Gamma draft", ActivityDefinitionDraftStatus.Active, "provider-b", changedAt)
        };
        var versions = new[]
        {
            Version("version-1", "definition-filtered", "1.0.0", ActivityDefinitionVersionLifecycle.Active, "provider-a", changedAt),
            Version("version-2", "definition-filtered", "2.0.0", ActivityDefinitionVersionLifecycle.Retired, "provider-b", changedAt),
            Version("version-3", "definition-filtered", "3.0.0", ActivityDefinitionVersionLifecycle.Active, "provider-b", changedAt)
        };
        await CommitAsync(documents, await writer.PrepareAsync(new(changedAt, [definition], drafts, versions)));

        var definitions = await reader.ReadDefinitionsAsync(new(null, null, 0, 20, ProviderKey: "provider-b"));
        var draftPage = await reader.ReadDraftsAsync("definition-filtered", new(
            null,
            definitions.Snapshot.Sequence,
            0,
            20,
            "alpha",
            ProviderKey: "provider-a",
            DraftStatus: ActivityDefinitionDraftStatus.Active));
        var versionPage = await reader.ReadVersionsAsync("definition-filtered", new(
            null,
            definitions.Snapshot.Sequence,
            0,
            20,
            "2.0",
            ProviderKey: "provider-b",
            VersionLifecycle: ActivityDefinitionVersionLifecycle.Retired));

        var projectedDefinition = Assert.Single(definitions.Items);
        Assert.Equal("version-1", projectedDefinition.Head!.DefinitionVersionId);
        Assert.Equal("version-2", projectedDefinition.Recommendation!.DefinitionVersionId);
        Assert.Equal("draft-alpha", Assert.Single(draftPage.Items).DraftId);
        Assert.Equal("version-2", Assert.Single(versionPage.Items).DefinitionVersionId);
        Assert.Equal(3, projectedDefinition.DraftCount);
        Assert.Equal(3, projectedDefinition.VersionCount);
        Assert.All(
            documents.Documents(ActivitiesDesignStorageManifest.ActivityDraftManagementProjectionDocumentKind)
                .Concat(documents.Documents(ActivitiesDesignStorageManifest.ActivityVersionManagementProjectionDocumentKind)),
            envelope => Assert.DoesNotContain("internalValue", envelope.ContentJson, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Retention_pruning_returns_the_same_safe_expiry_error_for_an_old_snapshot()
    {
        var documents = new TemporalProjectionDocumentStore();
        var locks = new ImmediateDistributedLockProvider();
        var writer = new GroundworkActivityManagementProjectionWriter(documents, locks);
        var reader = Reader(documents);
        var retention = new GroundworkActivityManagementProjectionRetention(documents, documents, locks);
        var firstAt = new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
        var first = DefinitionChange("definition-retained", null, "First", firstAt);
        await CommitAsync(documents, await writer.PrepareAsync(new(firstAt, [first], [], [])));
        var firstSnapshot = await reader.GetCurrentSnapshotAsync();
        var secondAt = firstAt.AddMinutes(1);
        first.Definition.DisplayName = "Second";
        first.Definition.LastModifiedAt = secondAt;
        first.Authoring.LastModifiedAt = secondAt;
        await CommitAsync(documents, await writer.PrepareAsync(new(secondAt, [first], [], [])));

        var tamperedSequence = firstSnapshot.Sequence + 1000;
        var tamperedFailure = await Assert.ThrowsAsync<ActivityManagementSnapshotExpiredException>(() =>
            reader.ReadDefinitionsAsync(new(null, tamperedSequence, 0, 20)));
        Assert.Equal(tamperedSequence, tamperedFailure.Sequence);

        await retention.ExpireBeforeAsync(2, secondAt.AddMinutes(1));

        var firstFailure = await Assert.ThrowsAsync<ActivityManagementSnapshotExpiredException>(() =>
            reader.ReadDefinitionsAsync(new(null, firstSnapshot.Sequence, 0, 20)));
        var replayFailure = await Assert.ThrowsAsync<ActivityManagementSnapshotExpiredException>(() =>
            reader.ReadDefinitionsAsync(new(null, firstSnapshot.Sequence, 0, 20)));
        Assert.Equal(firstSnapshot.Sequence, firstFailure.Sequence);
        Assert.Equal(firstFailure.Message, replayFailure.Message);
        Assert.Equal("Second", Assert.Single((await reader.ReadDefinitionsAsync(new(null, null, 0, 20))).Items).DisplayName);
    }

    private static async Task CommitAsync(
        TemporalProjectionDocumentStore documents,
        GroundworkActivityManagementProjectionUpdate update)
    {
        await using (update)
            await update.CommitAsync([], []);
    }

    private static GroundworkActivityDefinitionManagementProjectionStore Reader(
        TemporalProjectionDocumentStore documents,
        string? tenantId = null) =>
        new(
            documents,
            documents,
            new TestPersistenceAccessContextAccessor(tenantId is null
                ? PersistenceAccessContext.Global
                : PersistenceAccessContext.Scoped(new(tenantId))));

    private static void AssertSuperset(IEnumerable<string> actual, IEnumerable<string> expected)
    {
        var actualSet = actual.ToHashSet(StringComparer.Ordinal);
        Assert.All(expected, path => Assert.Contains(path, actualSet));
    }

    private sealed class TestPersistenceAccessContextAccessor(PersistenceAccessContext current)
        : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private static ActivityDefinition Definition(string displayName, DateTimeOffset updatedAt) => new()
    {
        Id = "definition-1",
        TenantId = null,
        ActivityTypeKey = "tests.activity",
        Category = "Tests",
        DisplayName = displayName,
        Description = "Safe summary",
        LastModifiedAt = updatedAt
    };

    private static ActivityManagementDefinitionChange DefinitionChange(
        string definitionId,
        string? tenantId,
        string displayName,
        DateTimeOffset updatedAt)
    {
        var definition = new ActivityDefinition
        {
            Id = definitionId,
            TenantId = tenantId,
            ActivityTypeKey = $"tests.{definitionId}",
            Category = "Tests",
            DisplayName = displayName,
            LastModifiedAt = updatedAt
        };
        var authoring = new ActivityDefinitionAuthoringState
        {
            Id = $"authoring-{definitionId}",
            DefinitionId = definitionId,
            TenantId = tenantId,
            ContentAuthority = new(ActivityContentAuthorityKind.Design, "elsa.activity-graph", null),
            LastModifiedAt = updatedAt
        };
        return new(definition, authoring);
    }

    private static ActivityDefinitionAuthoringState Authoring(DateTimeOffset updatedAt) => new()
    {
        Id = "authoring-1",
        DefinitionId = "definition-1",
        TenantId = null,
        ContentAuthority = new(ActivityContentAuthorityKind.Design, "elsa.activity-graph", null),
        LastModifiedAt = updatedAt
    };

    private static ActivityDefinitionDraft Draft(DateTimeOffset updatedAt) => new()
    {
        Id = "draft-1",
        DefinitionId = "definition-1",
        TenantId = null,
        Revision = 0,
        Status = ActivityDefinitionDraftStatus.Active,
        State = new(
            new("1", [], [], []),
            new("elsa.activity-graph", "1", JsonSerializer.SerializeToElement(new { label = "draft" })),
            new Dictionary<string, string>()),
        LastModifiedAt = updatedAt
    };

    private static ActivityDefinitionDraft Draft(
        string id,
        string definitionId,
        string label,
        ActivityDefinitionDraftStatus status,
        string providerKey,
        DateTimeOffset updatedAt) => new()
    {
        Id = id,
        DefinitionId = definitionId,
        Revision = 1,
        Status = status,
        PresentationLabel = label,
        State = new(
            new("1", [], [], []),
            new(providerKey, "1", JsonSerializer.SerializeToElement(new { internalValue = "not projected" })),
            new Dictionary<string, string>()),
        CreatedAt = updatedAt,
        LastModifiedAt = updatedAt
    };

    private static ActivityDefinitionVersionPublication Version(
        string id,
        string definitionId,
        string version,
        ActivityDefinitionVersionLifecycle lifecycle,
        string providerKey,
        DateTimeOffset publishedAt) => new()
    {
        Id = $"publication-{id}",
        DefinitionVersionId = id,
        DefinitionId = definitionId,
        Version = version,
        ActivityTypeKey = "tests.filtered",
        Contract = new("1", [], [], []),
        Provider = new(providerKey, "1", JsonSerializer.SerializeToElement(new { internalValue = "not projected" })),
        TemplateId = $"template-{id}",
        TemplateHash = $"hash-{id}",
        SourceReferenceId = $"source-{id}",
        ProviderFingerprint = $"fingerprint-{id}",
        DirectDependencyCount = 0,
        ClosedTemplateCount = 1,
        RuntimeRequirements = [],
        Lifecycle = lifecycle,
        PublishedAt = publishedAt,
        CreatedAt = publishedAt,
        LastModifiedAt = publishedAt
    };
}
