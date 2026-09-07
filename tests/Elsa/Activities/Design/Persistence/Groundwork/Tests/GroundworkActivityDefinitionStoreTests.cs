using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Primitives.Exceptions;
using Groundwork.Kernel;
using Groundwork.Store;
using Xunit;

namespace Elsa.Activities.Design.Persistence.Groundwork.Tests;

public sealed class GroundworkActivityDefinitionStoreTests
{
    [Fact]
    public async Task Provider_and_corrupt_payload_reads_surface_domain_scoped_failures()
    {
        using var harness = ActivityDesignV2TestHarness.Create();
        var exception = await Assert.ThrowsAsync<DesignPersistenceException>(() => harness.Store.SaveAsync(new ActivityDesignSaveRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            "corrupt-activity",
            ActivitiesDesignStorageManifest.SchemaVersion,
            "null")));

        Assert.Equal(DesignPersistenceFailureKind.Serialization, exception.FailureKind);

        var unit = ActivitiesDesignStorageManifest.Require(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind);
        harness.Connection.OpenSession(unit, StorageAccess.Scoped(new StorageScope("tenant-a"))).Upsert(
            new StorageValues(new Dictionary<string, object?>
            {
                [ActivitiesDesignStorageManifest.IdField] = "corrupt-read",
                [ActivitiesDesignStorageManifest.SchemaVersionField] = ActivitiesDesignStorageManifest.SchemaVersion,
                [ActivitiesDesignStorageManifest.ContentField] = "{not-json",
                [ActivitiesDesignStorageManifest.RevisionField] = 1L,
                [ActivitiesDesignStorageManifest.UpdatedAtField] = DateTimeOffset.UtcNow
            }),
            WriteOptions.Unconditional);
        var corrupt = await Assert.ThrowsAsync<DesignPersistenceException>(() =>
            new GroundworkActivityDefinitionStore(harness.Store).GetAsync("corrupt-read"));
        Assert.Equal(DesignPersistenceDomain.Activity, corrupt.Domain);
        Assert.Equal(DesignPersistenceFailureKind.Serialization, corrupt.FailureKind);
    }

    [Fact]
    public async Task Get_returns_definition_by_id()
    {
        using var harness = await SeededAsync(Definition("a1", "Acme.Send"), Definition("a2", "Acme.Wait"));
        var result = await new GroundworkActivityDefinitionStore(harness.Store).GetAsync("a1");
        Assert.Equal("Acme.Send", result.ActivityTypeKey);
    }

    [Fact]
    public async Task Get_throws_when_absent()
    {
        using var harness = await SeededAsync(Definition("a1", "Acme.Send"));
        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            new GroundworkActivityDefinitionStore(harness.Store).GetAsync("missing"));
    }

    [Fact]
    public async Task FindByIdOrActivityTypeKey_matches_either_field()
    {
        using var harness = await SeededAsync(Definition("a1", "Acme.Send"), Definition("a2", "Acme.Wait"));
        var store = new GroundworkActivityDefinitionStore(harness.Store);

        Assert.Equal("a1", (await store.FindByIdOrActivityTypeKeyAsync("a1", "none"))!.Id);
        Assert.Equal("a2", (await store.FindByIdOrActivityTypeKeyAsync("none", "Acme.Wait"))!.Id);
        Assert.Null(await store.FindByIdOrActivityTypeKeyAsync("none", "none"));
    }

    [Fact]
    public async Task FindByIdOrActivityTypeKey_prefers_the_id_match_over_the_natural_key_match()
    {
        using var harness = await SeededAsync(
            Definition("requested-id", "Acme.ById"),
            Definition("other-id", "Acme.ByTypeKey"));

        var result = await new GroundworkActivityDefinitionStore(harness.Store)
            .FindByIdOrActivityTypeKeyAsync("requested-id", "Acme.ByTypeKey");

        Assert.NotNull(result);
        Assert.Equal("requested-id", result!.Id);
    }

    [Fact]
    public async Task ExistsByActivityTypeKey_reflects_presence()
    {
        using var harness = await SeededAsync(Definition("a1", "Acme.Send"));
        var store = new GroundworkActivityDefinitionStore(harness.Store);
        Assert.True(await store.ExistsByActivityTypeKeyAsync("Acme.Send"));
        Assert.False(await store.ExistsByActivityTypeKeyAsync("Acme.Missing"));
    }

    [Fact]
    public async Task Find_by_filter_search_term_matches_substring()
    {
        using var harness = await SeededAsync(
            Definition("a1", "Acme.Send", search: "Sends an email"),
            Definition("a2", "Acme.Wait", search: "Waits a while"));

        var result = await new GroundworkActivityDefinitionStore(harness.Store)
            .FindAsync(new ActivityDefinitionFilter { SearchTerm = "email" });

        Assert.NotNull(result);
        Assert.Equal("a1", result!.Id);
    }

    [Fact]
    public async Task Find_by_filter_category_matches_exact()
    {
        using var harness = await SeededAsync(
            Definition("a1", "Acme.Send", category: "Mail"),
            Definition("a2", "Acme.Wait", category: "Timing"));

        var result = await new GroundworkActivityDefinitionStore(harness.Store)
            .FindAsync(new ActivityDefinitionFilter { Category = "Timing" });

        Assert.NotNull(result);
        Assert.Equal("a2", result!.Id);
    }

    [Fact]
    public async Task Find_by_filter_display_name_matches_exact()
    {
        using var harness = await SeededAsync(
            Definition("a1", "Acme.Send", search: "Send Email"),
            Definition("a2", "Acme.Wait", search: "Wait"));

        var result = await new GroundworkActivityDefinitionStore(harness.Store)
            .FindAsync(new ActivityDefinitionFilter { DisplayName = "Send Email" });

        Assert.NotNull(result);
        Assert.Equal("a1", result!.Id);
    }

    [Fact]
    public async Task Definition_reads_use_the_selected_named_route_and_result_operation()
    {
        using var harness = await SeededAsync(
            Definition("a2", "Acme.Send", category: "Mail", search: "Send Email"),
            Definition("a1", "Acme.Send", category: "Mail", search: "Send Email"));
        var store = new GroundworkActivityDefinitionStore(harness.Store);

        var listed = await store.ListAsync(new ActivityDefinitionFilter { Category = "Mail" });
        Assert.Equal(["a1", "a2"], listed.Select(x => x.Id));
        Assert.Equal("a1", (await store.FindAsync(new ActivityDefinitionFilter { Category = "Mail" }))!.Id);
    }

    [Fact]
    public async Task List_by_filter_returns_all_matching_definitions_in_deterministic_order()
    {
        using var harness = await SeededAsync(
            Definition("a3", "Acme.Send", category: "Mail", search: "Third"),
            Definition("a1", "Acme.Send", category: "Mail", search: "First"),
            Definition("a2", "Acme.Send", category: "Mail", search: "Second"),
            Definition("outside", "Acme.Wait", category: "Timing", search: "Outside"));
        var store = new GroundworkActivityDefinitionStore(harness.Store);

        var byCategory = await store.ListAsync(new ActivityDefinitionFilter { Category = "Mail" });
        Assert.Equal(3, byCategory.Count);
        Assert.Equal(["a1", "a2", "a3"], byCategory.Select(definition => definition.Id));
        Assert.All(byCategory, definition => Assert.Equal("Mail", definition.Category));

        var byTypeKeys = await store.ListAsync(new ActivityDefinitionFilter { ActivityTypeKeys = ["Acme.Send"] });
        Assert.Equal(byCategory.Select(definition => definition.Id), byTypeKeys.Select(definition => definition.Id));
    }

    [Fact]
    public async Task Tenant_agnostic_find_requires_explicit_privileged_across_scope_context()
    {
        using var harness = await SeededAsync(Definition("tenant-a-definition", "Acme.Send"));
        var store = new GroundworkActivityDefinitionStore(harness.Store);
        harness.Access.Current = PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.FindAsync(new ActivityDefinitionFilter
        {
            Id = "tenant-a-definition",
            TenantAgnostic = true
        }));
        Assert.Empty(harness.QueryRequests);
        Assert.Empty(harness.AuditSink.Snapshot());

        harness.Access.Current = PersistenceAccessContext.PrivilegedGlobal(
            new PersistenceAccessPurpose("activity-definition-global-only-test"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.FindAsync(new ActivityDefinitionFilter
        {
            Id = "tenant-a-definition",
            TenantAgnostic = true
        }));
        Assert.Empty(harness.QueryRequests);
        Assert.Empty(harness.AuditSink.Snapshot());

        harness.Access.Current = PersistenceAccessContext.PrivilegedAcrossScopes(
            new PersistenceAccessPurpose("activity-definition-tenant-agnostic-test"));
        var result = await store.FindAsync(new ActivityDefinitionFilter
        {
            Id = "tenant-a-definition",
            TenantAgnostic = true
        });
        Assert.NotNull(result);
        Assert.Equal("tenant-a-definition", result!.Id);

        var records = harness.AuditSink.Snapshot();
        Assert.Equal(2, records.Count);
        var acquisition = Assert.Single(records, record =>
            record.EventKind == GroundworkPrivilegedQueryAuditEventKind.Acquisition);
        var outcome = Assert.Single(records, record =>
            record.EventKind == GroundworkPrivilegedQueryAuditEventKind.Outcome);
        Assert.NotEqual(Guid.Empty, acquisition.AcquisitionId);
        Assert.Equal(acquisition.AcquisitionId, outcome.AcquisitionId);
        Assert.Equal(StorageAccessKind.PrivilegedAcrossScopes, acquisition.AccessKind);
        Assert.Equal(StorageAccessKind.PrivilegedAcrossScopes, outcome.AccessKind);
        Assert.Equal("elsa-activities-design", acquisition.AuditIdentity);
        Assert.Equal("activity-definition-tenant-agnostic-test", acquisition.Purpose);
        Assert.Equal(GroundworkPrivilegedQueryOutcome.Succeeded, outcome.Outcome);
        Assert.Null(outcome.FailureType);
    }

    private static async Task<ActivityDesignV2TestHarness> SeededAsync(params ActivityDefinition[] definitions)
    {
        var harness = ActivityDesignV2TestHarness.Create();
        foreach (var definition in definitions)
            await harness.SaveAsync(
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionCollection,
                definition,
                GroundworkActivitiesDesignJson.Options);
        return harness;
    }

    private static ActivityDefinition Definition(
        string id,
        string typeKey,
        string category = "General",
        string? search = null) =>
        new()
        {
            Id = id,
            ActivityTypeKey = typeKey,
            Category = category,
            DisplayName = search ?? typeKey,
            Description = search,
            TenantId = "tenant-a"
        };
}
