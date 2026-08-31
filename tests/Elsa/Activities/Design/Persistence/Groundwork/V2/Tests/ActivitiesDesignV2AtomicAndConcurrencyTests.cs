using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Sqlite;
using Groundwork.Store;
using System.Text.Json;
using Xunit;

namespace Elsa.Activities.Design.Persistence.Groundwork.Tests;

public sealed class ActivitiesDesignV2AtomicAndConcurrencyTests
{
    [Fact]
    public void Public_query_rejects_pages_larger_than_the_provider_safe_bound()
    {
        using var fixture = ActivityDesignV2Fixture.Create();
        Assert.Throws<ArgumentOutOfRangeException>(() => fixture.Store.Query(new ActivityDesignQuery(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            ActivitiesDesignStorageManifest.ListActivityDefinitionsByIdQuery,
            [],
            [],
            Take: ActivityDesignQueryPager.PageSize + 1)));
    }

    [Fact]
    public void Public_query_rejects_an_undeclared_route_before_provider_execution()
    {
        using var fixture = ActivityDesignV2Fixture.Create();
        Assert.Throws<ArgumentException>(() => fixture.Store.Query(new ActivityDesignQuery(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            "route-that-is-not-declared",
            [],
            [],
            Take: 1)));
    }

    [Fact]
    public void Privileged_global_context_cannot_be_promoted_to_cross_scope_access()
    {
        using var fixture = ActivityDesignV2Fixture.Create();
        fixture.Access.Current = PersistenceAccessContext.PrivilegedGlobal(
            new PersistenceAccessPurpose("activity-design-cross-scope-promotion-test"));

        Assert.Throws<InvalidOperationException>(() => fixture.Store.Load(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            "not-used",
            acrossScopes: true));
        Assert.Equal(0, fixture.Sessions.OpenCount);
    }

    [Fact]
    public void Cross_scope_query_without_the_audit_executor_fails_before_provider_session_io()
    {
        using var fixture = ActivityDesignV2Fixture.Create();
        fixture.Access.Current = PersistenceAccessContext.PrivilegedAcrossScopes(
            new PersistenceAccessPurpose("activity-design-missing-audit-executor-test"));
        var storeWithoutExecutor = new GroundworkV2ActivityDesignStore(fixture.Sessions, fixture.Access);

        var exception = Assert.Throws<InvalidOperationException>(() => storeWithoutExecutor.Query(
            new ActivityDesignQuery(
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                ActivitiesDesignStorageManifest.ListAllDocumentsQuery,
                [],
                [new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.IdField)],
                Take: 1),
            acrossScopes: true));

        Assert.Contains("audit executor", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Sessions.OpenCount);
        Assert.Empty(fixture.AuditSink.Snapshot());
    }

    [Fact]
    public void Named_routes_require_their_declared_predicate_and_operation_before_provider_execution()
    {
        using var fixture = ActivityDesignV2Fixture.Create();
        Assert.Throws<ArgumentException>(() => fixture.Store.Query(new ActivityDesignQuery(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            ActivitiesDesignStorageManifest.ListActivityDefinitionsByTypeKeyQuery,
            [], [], Take: 1)));
        Assert.Throws<ArgumentException>(() => fixture.Store.Query(new ActivityDesignQuery(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            ActivitiesDesignStorageManifest.ListActivityDefinitionsByTypeKeyQuery,
            [ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.Contains(
                ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyField, "Acme"))],
            [], Take: 1)));
        Assert.Throws<ArgumentException>(() => fixture.Store.Query(new ActivityDesignQuery(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind,
            ActivitiesDesignStorageManifest.FindActivityDefinitionVersionByDefinitionAndSortKeyQuery,
            [ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.Equal(
                ActivitiesDesignStorageManifest.ActivityDefinitionVersionDefinitionIdField, "definition-1"))],
            [], Take: 1)));
    }

    [Fact]
    public void Search_route_requires_the_reviewed_cross_field_substring_shape()
    {
        using var fixture = ActivityDesignV2Fixture.Create();
        Assert.Throws<ArgumentException>(() => fixture.Store.Query(new ActivityDesignQuery(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            ActivitiesDesignStorageManifest.SearchActivityDefinitionsQuery,
            [ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.Equal(
                ActivitiesDesignStorageManifest.ActivityDefinitionDisplayNameField, "Acme"))],
            [], Take: 1)));
        Assert.Throws<ArgumentException>(() => fixture.Store.Query(new ActivityDesignQuery(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            ActivitiesDesignStorageManifest.SearchActivityDefinitionsQuery,
            [], [], Take: 1)));
    }

    [Fact]
    public async Task Public_query_uses_declared_field_types_and_bounds_before_provider_execution()
    {
        using var fixture = ActivityDesignV2Fixture.Create();
        var timestamp = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        Assert.Throws<ArgumentException>(() => fixture.Store.Query(new ActivityDesignQuery(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            ActivitiesDesignStorageManifest.ListActivityDefinitionsByIdQuery,
            [ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.Equal("not-a-column", "value"))],
            [],
            Take: 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => fixture.Store.Query(new ActivityDesignQuery(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            ActivitiesDesignStorageManifest.ListActivityDefinitionsByIdQuery,
            [ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.Equal(
                ActivitiesDesignStorageManifest.IdField,
                new string('x', ActivitiesDesignStorageManifest.MaximumIdLength + 1)))],
            [],
            Take: 1)));

        await fixture.Store.SaveAsync(Save("tenant-a", "typed-query", "Acme.Typed") with { UpdatedAt = timestamp });
        var result = fixture.Store.Query(new ActivityDesignQuery(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            "point-read",
            [
                ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.Equal(
                    ActivitiesDesignStorageManifest.IdField, "typed-query")),
                ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.Equal(
                    ActivitiesDesignStorageManifest.UpdatedAtField, timestamp))
            ],
            [new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.UpdatedAtField)],
            Take: 1));
        Assert.Single(result.Documents);
        Assert.Equal(timestamp, result.Documents[0].UpdatedAt);
    }

    [Fact]
    public async Task Sqlite_query_materializes_declared_revision_and_supports_cas_mutation()
    {
        using var fixture = ActivityDesignV2Fixture.Create();
        var first = Save("tenant-a", "activity-1", "Acme.Send");

        await fixture.Store.SaveAsync(first);
        var queried = await fixture.Store.QueryAsync(new ActivityDesignQuery(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            ActivitiesDesignStorageManifest.ListActivityDefinitionsByTypeKeyQuery,
            [ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.Equal(
                ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyField,
                "Acme.Send"))],
            [new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyField), new(ActivitiesDesignStorageManifest.IdField)],
            Take: 10));

        var current = Assert.Single(queried.Documents);
        Assert.Equal(1, current.Version);

        await fixture.Store.SaveAsync(first with
        {
            ContentJson = Content("tenant-a", "Acme.Send.Updated"),
            ExpectedVersion = current.Version
        });

        var updated = await fixture.Store.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            "activity-1");
        Assert.NotNull(updated);
        Assert.Equal(2, updated.Version);
        Assert.Contains("Acme.Send.Updated", updated.ContentJson);

        await fixture.Store.SaveAsync(first with
        {
            ContentJson = Content("tenant-a", "Acme.Send.Unconditional")
        });
        var unconditionallyUpdated = await fixture.Store.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            "activity-1");
        Assert.NotNull(unconditionallyUpdated);
        Assert.Equal(3, unconditionallyUpdated.Version);

        await Assert.ThrowsAsync<ActivityDesignWriteConflictException>(() => fixture.Store.SaveAsync(first with
        {
            ContentJson = Content("tenant-a", "Acme.Stale"),
            ExpectedVersion = current.Version
        }));
    }

    [Fact]
    public async Task Public_query_keyset_pages_large_ordered_results_without_duplicates()
    {
        using var fixture = ActivityDesignV2Fixture.Create();
        var expected = Enumerable.Range(0, 205)
            .Select(index => $"activity-{index:D4}")
            .ToArray();
        var requests = expected
            .Select(id => new ActivityDesignSaveRequest(
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                id,
                ActivitiesDesignStorageManifest.SchemaVersion,
                Content("tenant-a", "Acme.Paged")))
            .ToArray();

        await fixture.Store.SaveAllAsync(
            ActivityDesignCommitScope.Of(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind),
            requests);

        var actual = new List<string>();
        string? continuation = null;
        do
        {
            var page = await fixture.Store.QueryAsync(new ActivityDesignQuery(
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                ActivitiesDesignStorageManifest.ListActivityDefinitionsByTypeKeyQuery,
                [ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.Equal(
                    ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyField,
                    "Acme.Paged"))],
                ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyOrder,
                Take: 37,
                ContinuationToken: continuation));

            actual.AddRange(page.Documents.Select(document => document.Id));
            Assert.Equal(expected.Length, page.TotalCount);
            continuation = page.NextContinuationToken;
            if (continuation is not null)
                Assert.NotEmpty(page.Documents);
        }
        while (continuation is not null);

        Assert.Equal(expected, actual);
        Assert.Equal(expected.Length, actual.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Public_sqlite_search_round_trip_preserves_the_declared_near_limit_projection()
    {
        using var fixture = ActivityDesignV2Fixture.Create();
        var searchText = new string('x', ActivitiesDesignStorageManifest.ManagementSearchMaximumLength);
        await fixture.Store.SaveAsync(new ActivityDesignSaveRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            "activity-long-search",
            ActivitiesDesignStorageManifest.SchemaVersion,
            JsonSerializer.Serialize(new
            {
                tenantId = "tenant-a",
                activityTypeKey = "Acme.LongSearch",
                searchText
            })));

        var loaded = await fixture.Store.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            "activity-long-search");
        Assert.NotNull(loaded);
        Assert.Contains(searchText, loaded!.ContentJson, StringComparison.Ordinal);

        var queried = await fixture.Store.QueryAsync(new ActivityDesignQuery(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            ActivitiesDesignStorageManifest.SearchActivityDefinitionsQuery,
            [ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.Contains(
                ActivitiesDesignStorageManifest.ManagementSearchField,
                new string('x', 200)))],
            [new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.IdField)],
            Take: 10));
        Assert.Equal("activity-long-search", Assert.Single(queried.Documents).Id);
    }

    [Fact]
    public async Task Public_search_refuses_a_scope_over_the_enforced_catalog_scan_budget()
    {
        using var fixture = ActivityDesignV2Fixture.Create();
        var requests = Enumerable.Range(0, ActivitiesDesignStorageManifest.MaximumActivityDefinitionSearchCatalogRows + 1)
            .Select(index => new ActivityDesignSaveRequest(
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                $"activity-search-bound-{index:D5}",
                ActivitiesDesignStorageManifest.SchemaVersion,
                Content("tenant-a", "Acme.BoundedSearch")))
            .ToArray();
        await fixture.Store.SaveAllAsync(
            ActivityDesignCommitScope.Of(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind),
            requests);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.QueryAsync(new ActivityDesignQuery(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            ActivitiesDesignStorageManifest.SearchActivityDefinitionsQuery,
            [ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.Contains(
                ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyField, "Bounded"))],
            [], Take: 1)));
    }

    [Fact]
    public async Task Public_search_enumeration_proves_the_catalog_once_then_uses_bounded_keyset_pages()
    {
        var requests = new List<QueryRequest>();
        using var fixture = ActivityDesignV2Fixture.Create(requests);
        await fixture.Store.SaveAllAsync(
            ActivityDesignCommitScope.Of(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind),
            Enumerable.Range(0, 205)
                .Select(index => new ActivityDesignSaveRequest(
                    ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                    $"activity-search-page-{index:D3}",
                    ActivitiesDesignStorageManifest.SchemaVersion,
                    JsonSerializer.Serialize(new
                    {
                        collection = ActivitiesDesignStorageManifest.ActivityDefinitionCollection,
                        entity = new
                        {
                            id = $"activity-search-page-{index:D3}",
                            tenantId = "tenant-a",
                            activityTypeKey = "Acme.SearchPaged",
                            category = "General",
                            displayName = "SearchPaged",
                            description = "SearchPaged"
                        }
                    })))
                .ToArray());

        var store = new Elsa.Activities.Design.Persistence.Groundwork.Services.GroundworkActivityDefinitionStore(fixture.Store);
        var definitions = await store.ListAsync(new Elsa.Activities.Design.Persistence.Core.Filters.ActivityDefinitionFilter
        {
            SearchTerm = "SearchPaged"
        });

        Assert.Equal(205, definitions.Count);
        var accepted = requests.Where(request => request.AcceptedScan?.Allowed == true).ToArray();
        // One 10,001-row cardinality proof plus three result pages; no page
        // repeats the proof, and every search operation remains provider-bounded.
        Assert.Equal(4, accepted.Length);
        Assert.Equal(ActivitiesDesignStorageManifest.MaximumActivityDefinitionSearchCatalogRows + 1, accepted[0].Paging.Limit);
        Assert.All(accepted.Skip(1), request => Assert.InRange(request.Paging.Limit!.Value, 1, ActivityDesignQueryPager.PageSize));
        Assert.All(accepted.Skip(1), request => Assert.Null(request.Paging.Offset));
    }

    [Fact]
    public async Task Public_reads_round_trip_non_epoch_last_modified_timestamps_across_updates()
    {
        using var fixture = ActivityDesignV2Fixture.Create();
        var firstTimestamp = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        var secondTimestamp = firstTimestamp.AddMinutes(1);
        var first = new ActivityDesignSaveRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            "activity-timestamped",
            ActivitiesDesignStorageManifest.SchemaVersion,
            Content("tenant-a", "Acme.Timestamped"),
            UpdatedAt: firstTimestamp);
        await fixture.Store.SaveAsync(first);

        var created = await fixture.Store.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            first.Id);
        Assert.NotNull(created);
        Assert.Equal(firstTimestamp, created!.UpdatedAt);

        await fixture.Store.SaveAsync(first with
        {
            ContentJson = Content("tenant-a", "Acme.Timestamped.Updated"),
            ExpectedVersion = created.Version,
            UpdatedAt = secondTimestamp
        });
        var updated = await fixture.Store.QueryAsync(new ActivityDesignQuery(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            ActivitiesDesignStorageManifest.ListActivityDefinitionsByTypeKeyQuery,
            [ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.Equal(
                ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyField,
                "Acme.Timestamped.Updated"))],
            [new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.IdField)],
            Take: 10));

        Assert.Equal(secondTimestamp, Assert.Single(updated.Documents).UpdatedAt);
    }

    [Fact]
    public async Task Scoped_rows_are_isolated_and_cross_scope_reads_require_privileged_context()
    {
        using var fixture = ActivityDesignV2Fixture.Create();
        await fixture.Store.SaveAsync(Save("tenant-a", "activity-1", "Acme.Send"));

        fixture.Access.Current = PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b"));
        Assert.Null(fixture.Store.Load(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind, "activity-1"));
        Assert.Throws<InvalidOperationException>(() => fixture.Store.Load(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            "activity-1",
            acrossScopes: true));

        fixture.Access.Current = PersistenceAccessContext.PrivilegedAcrossScopes(
            new PersistenceAccessPurpose("activity-design-test"));
        Assert.NotNull(fixture.Store.Load(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            "activity-1",
            acrossScopes: true));
    }

    [Fact]
    public async Task Cross_scope_point_reads_preserve_provider_scope_and_refuse_same_id_ambiguity()
    {
        using var fixture = ActivityDesignV2Fixture.Create();
        await fixture.Store.SaveAsync(Save("tenant-a", "same-id", "Acme.ScopeA"));

        var unit = ActivitiesDesignStorageManifest.Require(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind);
        var forgedContent = Content("tenant-a", "Acme.ForgedTenantContent");
        var otherScope = fixture.Connection.OpenSession(
            unit,
            StorageAccess.Scoped(new StorageScope("tenant-b")));
        otherScope.Upsert(new StorageValues(new Dictionary<string, object?>
        {
            [ActivitiesDesignStorageManifest.IdField] = "same-id",
            [ActivitiesDesignStorageManifest.SchemaVersionField] = ActivitiesDesignStorageManifest.SchemaVersion,
            [ActivitiesDesignStorageManifest.ContentField] = forgedContent,
            [ActivitiesDesignStorageManifest.RevisionField] = 1L,
            [ActivitiesDesignStorageManifest.UpdatedAtField] = DateTimeOffset.UtcNow,
            [ActivitiesDesignStorageManifest.ScopeField] = "tenant-a",
            [ActivitiesDesignStorageManifest.TenantIdField] = "tenant-a"
        }), WriteOptions.Unconditional);

        fixture.Access.Current = PersistenceAccessContext.PrivilegedAcrossScopes(
            new PersistenceAccessPurpose("activity-design-cross-scope-test"));
        Assert.Throws<InvalidOperationException>(() => fixture.Store.Load(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            "same-id",
            acrossScopes: true));

        var first = fixture.Store.Query(new ActivityDesignQuery(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            ActivitiesDesignStorageManifest.ListAllDocumentsQuery,
            [],
            [new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.IdField)],
            Take: 1), acrossScopes: true);
        Assert.Single(first.Documents);
        Assert.NotNull(first.NextContinuationToken);
        var second = fixture.Store.Query(new ActivityDesignQuery(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            ActivitiesDesignStorageManifest.ListAllDocumentsQuery,
            [],
            [new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.IdField)],
            Take: 1,
            ContinuationToken: first.NextContinuationToken), acrossScopes: true);
        Assert.Single(second.Documents);
        Assert.NotEqual(first.Documents[0].ContentJson, second.Documents[0].ContentJson);
    }

    [Fact]
    public async Task Cross_scope_first_or_default_refuses_same_id_ambiguity()
    {
        using var fixture = ActivityDesignV2Fixture.Create();
        await fixture.Store.SaveAsync(Save("tenant-a", "first-or-default-id", "Acme.ScopeA"));

        var unit = ActivitiesDesignStorageManifest.Require(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind);
        fixture.Connection.OpenSession(
                unit,
                StorageAccess.Scoped(new StorageScope("tenant-b")))
            .Upsert(new StorageValues(new Dictionary<string, object?>
            {
                [ActivitiesDesignStorageManifest.IdField] = "first-or-default-id",
                [ActivitiesDesignStorageManifest.SchemaVersionField] = ActivitiesDesignStorageManifest.SchemaVersion,
                [ActivitiesDesignStorageManifest.ContentField] = Content("tenant-a", "Acme.ForgedTenantContent"),
                [ActivitiesDesignStorageManifest.RevisionField] = 1L,
                [ActivitiesDesignStorageManifest.UpdatedAtField] = DateTimeOffset.UtcNow,
                [ActivitiesDesignStorageManifest.ScopeField] = "tenant-a",
                [ActivitiesDesignStorageManifest.TenantIdField] = "tenant-a"
            }), WriteOptions.Unconditional);

        fixture.Access.Current = PersistenceAccessContext.PrivilegedAcrossScopes(
            new PersistenceAccessPurpose("activity-design-cross-scope-first-or-default-test"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.FirstOrDefaultAsync(
            new ActivityDesignQuery(
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                "point-read",
                [ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.Equal(
                    ActivitiesDesignStorageManifest.IdField, "first-or-default-id"))],
                [new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.IdField)],
                Take: 1),
            acrossScopes: true));
    }

    [Fact]
    public async Task Cross_scope_identity_keeps_control_character_scope_separate_from_forged_payload_tenant()
    {
        using var fixture = ActivityDesignV2Fixture.Create();
        var id = "control-identity-id";
        await fixture.Store.SaveAsync(Save("tenant-a", id, "Acme.ScopeA"));

        var unit = ActivitiesDesignStorageManifest.Require(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind);
        var controlCharacterScope = "tenant-\u001F-b";
        fixture.Connection.OpenSession(
                unit,
                StorageAccess.Scoped(new StorageScope(controlCharacterScope)))
            .Upsert(new StorageValues(new Dictionary<string, object?>
            {
                [ActivitiesDesignStorageManifest.IdField] = id,
                [ActivitiesDesignStorageManifest.SchemaVersionField] = ActivitiesDesignStorageManifest.SchemaVersion,
                [ActivitiesDesignStorageManifest.ContentField] = Content("tenant-a", "Acme.ForgedControlCharacterTenant"),
                [ActivitiesDesignStorageManifest.RevisionField] = 1L,
                [ActivitiesDesignStorageManifest.UpdatedAtField] = DateTimeOffset.UtcNow,
                [ActivitiesDesignStorageManifest.ScopeField] = "tenant-a",
                [ActivitiesDesignStorageManifest.TenantIdField] = "tenant-a"
            }), WriteOptions.Unconditional);

        fixture.Access.Current = PersistenceAccessContext.PrivilegedAcrossScopes(
            new PersistenceAccessPurpose("activity-design-control-character-scope-test"));
        var result = fixture.Store.Query(new ActivityDesignQuery(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            ActivitiesDesignStorageManifest.ListAllDocumentsQuery,
            [],
            [new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.IdField)],
            Take: 100), acrossScopes: true);

        Assert.Equal(2, result.Documents.Count(document => document.Id == id));
        Assert.Contains(result.Documents, document => document.ContentJson.Contains("Acme.ScopeA", StringComparison.Ordinal));
        Assert.Contains(result.Documents, document => document.ContentJson.Contains("Acme.ForgedControlCharacterTenant", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Exact_unit_of_work_reads_its_own_staged_save_and_delete()
    {
        using var fixture = ActivityDesignV2Fixture.Create();
        var save = Save("tenant-a", "activity-stage", "Acme.Stage");
        using var unitOfWork = fixture.Store.Begin(ActivityDesignCommitScope.Of(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind));

        unitOfWork.StageSave(save);
        Assert.Equal(1, unitOfWork.Load(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            save.Id)!.Version);

        unitOfWork.StageDelete(new ActivityDesignDeleteRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            save.Id,
            ExpectedVersion: 1));
        Assert.Null(unitOfWork.Load(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            save.Id));
        unitOfWork.Rollback();
    }

    [Fact]
    public async Task Atomic_operation_replay_does_not_restage_and_preserves_authoritative_result()
    {
        using var fixture = ActivityDesignV2Fixture.Create();
        var writer = new GroundworkDesignAtomicWrite(fixture.Store);
        var request = new GroundworkDesignAtomicWriteRequest(
            new GroundworkDesignOperationIdentity("activity-create", "operation-1"),
            "request-fingerprint",
            [ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind]);
        var calls = 0;

        async Task<GroundworkDesignAtomicWriteStageResult> Stage(
            GroundworkDesignAtomicWriteContext context,
            CancellationToken token)
        {
            calls++;
            await context.SaveAsync(Save("tenant-a", "activity-atomic", "Acme.Atomic"), token);
            var staged = await context.LoadAsync(
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                "activity-atomic",
                token);
            Assert.Equal(1, staged!.Version);
            return GroundworkDesignAtomicWriteStageResult.Accepted("result-fingerprint", "{\"ok\":true}");
        }

        var committed = await writer.ExecuteAsync(request, Stage);
        var replayed = await writer.ExecuteAsync(request, Stage);

        Assert.Equal(GroundworkDesignAtomicWriteStatus.Committed, committed.Status);
        Assert.Equal(GroundworkDesignAtomicWriteStatus.Replayed, replayed.Status);
        Assert.Equal(1, calls);
        Assert.Equal("{\"ok\":true}", replayed.AuthoritativeResultJson);
    }

    [Fact]
    public async Task Concurrent_first_writers_converge_on_one_authoritative_marker()
    {
        using var fixture = ActivityDesignV2Fixture.Create();
        var writer = new GroundworkDesignAtomicWrite(fixture.Store);
        var request = new GroundworkDesignAtomicWriteRequest(
            new GroundworkDesignOperationIdentity("activity-create", "operation-concurrent"),
            "request-fingerprint",
            [ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind]);
        var entered = 0;
        var bothEntered = new TaskCompletionSource<object?>
            (TaskCreationOptions.RunContinuationsAsynchronously);

        async Task BeforeAttempt(CancellationToken token)
        {
            if (Interlocked.Increment(ref entered) == 2)
                bothEntered.TrySetResult(null);
            await bothEntered.Task.WaitAsync(token);
        }

        async Task<GroundworkDesignAtomicWriteStageResult> Stage(
            GroundworkDesignAtomicWriteContext context,
            CancellationToken token)
        {
            await context.SaveAsync(Save("tenant-a", "activity-concurrent", "Acme.Concurrent"), token);
            return GroundworkDesignAtomicWriteStageResult.Accepted("result-fingerprint", "{\"ok\":true}");
        }

        var results = await Task.WhenAll(
            writer.ExecuteAsync(request, BeforeAttempt, Stage),
            writer.ExecuteAsync(request, BeforeAttempt, Stage));

        Assert.Equal(2, results.Length);
        Assert.Contains(results, result => result.Status == GroundworkDesignAtomicWriteStatus.Committed);
        Assert.Contains(results, result => result.Status is GroundworkDesignAtomicWriteStatus.Replayed or GroundworkDesignAtomicWriteStatus.Reconciled);
        var markerRows = (await fixture.Store.QueryAsync(new ActivityDesignQuery(
            ActivitiesDesignStorageManifest.DesignOperationDocumentKind,
            "list-design-operations",
            [],
            [new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.IdField)],
            Take: 10))).Documents;
        Assert.Single(markerRows);
    }

    private static ActivityDesignSaveRequest Save(string tenantId, string id, string activityTypeKey) =>
        new(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            id,
            ActivitiesDesignStorageManifest.SchemaVersion,
            Content(tenantId, activityTypeKey));

    private static string Content(string tenantId, string activityTypeKey) =>
        JsonSerializer.Serialize(new { tenantId, activityTypeKey, category = "General" });
}

internal sealed class ActivityDesignV2Fixture : IDisposable
{
    private readonly string databasePath;
    private readonly IStorageProviderConnection connection;
    private readonly IReadOnlyDictionary<string, StorageUnit> units;

    private ActivityDesignV2Fixture(
        string databasePath,
        IStorageProviderConnection connection,
        IReadOnlyDictionary<string, StorageUnit> units,
        MutableActivityDesignAccess access,
        DirectActivityDesignSessionSource sessions,
        GroundworkV2ActivityDesignStore store,
        GroundworkPrivilegedQueryAuditSink auditSink)
    {
        this.databasePath = databasePath;
        this.connection = connection;
        this.units = units;
        Access = access;
        Sessions = sessions;
        Store = store;
        AuditSink = auditSink;
    }

    public MutableActivityDesignAccess Access { get; }
    public DirectActivityDesignSessionSource Sessions { get; }
    public GroundworkV2ActivityDesignStore Store { get; }
    public GroundworkPrivilegedQueryAuditSink AuditSink { get; }
    public IStorageProviderConnection Connection => connection;

    public static ActivityDesignV2Fixture Create(ICollection<QueryRequest>? queryRequests = null)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"elsa-activity-design-v2-{Guid.NewGuid():N}.db");
        var connection = new SqliteProviderFactory().Create($"Data Source={databasePath}");
        var units = ActivitiesDesignStorageManifest.CreateUnits()
            .ToDictionary(unit => unit.Id.Value, StringComparer.Ordinal);
        foreach (var unit in units.Values)
            connection.Schema.Apply(unit);

        var access = new MutableActivityDesignAccess(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var sessions = new DirectActivityDesignSessionSource(connection, units, queryRequests);
        var auditSink = new GroundworkPrivilegedQueryAuditSink();
        var auditExecutor = new GroundworkPrivilegedQueryAuditExecutor(sessions, access, auditSink);
        var store = new GroundworkV2ActivityDesignStore(
            sessions,
            access,
            privilegedQueryAuditExecutor: auditExecutor);
        return new(databasePath, connection, units, access, sessions, store, auditSink);
    }

    public void Dispose()
    {
        connection.Dispose();
        if (File.Exists(databasePath))
            File.Delete(databasePath);
    }
}

internal sealed class MutableActivityDesignAccess(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
{
    public PersistenceAccessContext Current { get; set; } = current;
}

internal sealed class DirectActivityDesignSessionSource(
    IStorageProviderConnection connection,
    IReadOnlyDictionary<string, StorageUnit> units,
    ICollection<QueryRequest>? queryRequests = null) : IGroundworkStorageSessionSource
{
    public int OpenCount { get; private set; }

    public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
    {
        OpenCount++;
        var session = connection.OpenSession(Unit(unitId), access);
        return queryRequests is null ? session : new RecordingActivityDesignSession(session, queryRequests);
    }

    public IUnitOfWork BeginUnitOfWork(
        StorageAccess access,
        BatchWriteOptions options,
        IReadOnlyList<string> unitIds,
        string? targetName = null) =>
        connection.BeginUnitOfWork(access, options, unitIds.Select(id => Unit(id)).ToArray());

    public StorageUnit Unit(string unitId, string? targetName = null) => units[unitId];
}

internal sealed class RecordingActivityDesignSession(
    IStorageSession inner,
    ICollection<QueryRequest> requests) : SynchronousStorageSessionTestDouble, IStorageSession, IPrivilegedCrossScopeQuerySession
{
    public StorageUnit Unit => inner.Unit;
    public StorageAccess Access => inner.Access;
    public StoredEntry? Read(StorageKey key) => inner.Read(key);
    public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null)
    {
        requests.Add(request);
        return inner.Query(request, options);
    }

    public CrossScopeQueryResult QueryAcrossScopes(QueryRequest request, QueryRenderOptions? options = null)
    {
        requests.Add(request);
        return ((IPrivilegedCrossScopeQuerySession)inner).QueryAcrossScopes(request, options);
    }

    public AggregationResult Aggregate(AggregationQuery query) => inner.Aggregate(query);
    public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => inner.Insert(values, options);
    public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => inner.Update(values, options);
    public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => inner.Upsert(values, options);
    public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => inner.Delete(key, options);
    public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => inner.Append(operationId, values);
}
