using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Events.Core.Contracts;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Atomic;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Commands;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Publishing;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Serialization.Core;
using Elsa.Locking.Core;
using Elsa.Tasks.Core;
using Microsoft.Data.Sqlite;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static Elsa.Persistence.EntityFramework.Tests.ProviderFailures;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Tests;

public sealed class EfWorkflowDesignPersistenceTests
{
    [Fact]
    public void Definition_identity_rejects_malformed_utf16()
    {
        Assert.Throws<ArgumentException>(() => WorkflowDefinitionIdentity.Fold("\uD800"));
        Assert.Throws<ArgumentException>(() => WorkflowDefinitionIdentity.Fold("\uDC00"));
        Assert.Throws<ArgumentException>(() => WorkflowDefinitionIdentity.Fold("\uD800x"));
    }

    [Fact]
    public void Definition_identity_accepts_and_folds_a_valid_surrogate_pair()
    {
        Assert.Equal("|010400", WorkflowDefinitionIdentity.Fold("\uD801\uDC00"));
    }

    [Fact]
    public async Task Definition_ids_follow_portable_identity_folding_and_tenants_remain_ordinal()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        db.Definitions.AddRange(
            new WorkflowDefinition { Id = "Alpha", TenantId = "TenantA", Name = "first" },
            new WorkflowDefinition { Id = "Alpha", TenantId = "tenanta", Name = "second" });
        await db.SaveChangesAsync();

        var tenantA = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("TenantA")));
        var tenanta = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenanta")));
        Assert.Equal("first", (await new EfWorkflowDefinitionStore(db, tenantA).FindByIdAsync("aLPHA"))!.Name);
        Assert.Equal("second", (await new EfWorkflowDefinitionStore(db, tenanta).FindByIdAsync("ALPHA"))!.Name);
        Assert.Equal(2, await db.Definitions.CountAsync());

        db.ChangeTracker.Clear();
        db.Definitions.Add(new WorkflowDefinition { Id = "alpha", TenantId = "TenantA", Name = "duplicate" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Definition_relational_identity_and_relationships_preserve_trailing_spaces()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        db.Definitions.AddRange(
            new WorkflowDefinition { Id = "alpha", TenantId = "tenant-a", Name = "Plain" },
            new WorkflowDefinition { Id = "alpha ", TenantId = "tenant-a", Name = "Trailing" });
        db.Versions.AddRange(
            new WorkflowDefinitionVersion("alpha", "1.0.0", "{}") { Id = "version", TenantId = "tenant-a" },
            new WorkflowDefinitionVersion("alpha ", "1.0.0", "{}") { Id = "version ", TenantId = "tenant-a" });
        db.Drafts.AddRange(
            new WorkflowDefinitionDraft { Id = "draft", TenantId = "tenant-a", WorkflowDefinitionId = "alpha", StateSource = "{}" },
            new WorkflowDefinitionDraft { Id = "draft ", TenantId = "tenant-a", WorkflowDefinitionId = "alpha ", StateSource = "{}" });
        db.DraftLayouts.AddRange(
            new WorkflowDefinitionDraftLayout { Id = "draft-layout", TenantId = "tenant-a", WorkflowDefinitionDraftId = "draft" },
            new WorkflowDefinitionDraftLayout { Id = "draft-layout ", TenantId = "tenant-a", WorkflowDefinitionDraftId = "draft " });
        db.VersionLayouts.AddRange(
            new WorkflowDefinitionVersionLayout { Id = "version-layout", TenantId = "tenant-a", WorkflowDefinitionVersionId = "version" },
            new WorkflowDefinitionVersionLayout { Id = "version-layout ", TenantId = "tenant-a", WorkflowDefinitionVersionId = "version " });
        await db.SaveChangesAsync();

        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var definitions = new EfWorkflowDefinitionStore(db, access);
        var versions = new EfWorkflowDefinitionVersionStore(db, new TestSerializer(), definitions, access);
        var drafts = new EfWorkflowDefinitionDraftStore(db, new TestSerializer(), access);

        Assert.Equal("Plain", (await definitions.FindByIdAsync("ALPHA"))!.Name);
        Assert.Equal("Trailing", (await definitions.FindByIdAsync("ALPHA "))!.Name);
        Assert.Equal("version", (await versions.FindByIdAsync("version"))!.Id);
        Assert.Equal("version ", (await versions.FindByIdAsync("version "))!.Id);
        Assert.Equal("draft", (await drafts.FindByIdAsync("draft"))!.Id);
        Assert.Equal("draft ", (await drafts.FindByIdAsync("draft "))!.Id);
        Assert.Equal("draft", (await drafts.FindWithLayoutByIdAsync("draft"))!.Draft.Id);
        Assert.Equal("draft ", (await drafts.FindWithLayoutByIdAsync("draft "))!.Draft.Id);
        Assert.NotNull(await new EfWorkflowDefinitionVersionLayoutStore(db, access).FindByVersionIdAsync("version"));
        Assert.NotNull(await new EfWorkflowDefinitionVersionLayoutStore(db, access).FindByVersionIdAsync("version "));
    }

    [Fact]
    public async Task Draft_with_layout_reads_keep_tenant_relationships_and_reject_ambiguous_cross_scope_ids()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();

        db.Definitions.AddRange(
            new WorkflowDefinition { Id = "definition", TenantId = "tenant-a", Name = "Tenant A" },
            new WorkflowDefinition { Id = "definition", TenantId = "tenant-b", Name = "Tenant B" },
            new WorkflowDefinition { Id = "definition", TenantId = null, Name = "Global duplicate" },
            new WorkflowDefinition { Id = "global-definition", TenantId = null, Name = "Global" });
        db.Versions.Add(new WorkflowDefinitionVersion("global-definition", "1.0.0", "{}"){ Id = "global-version", TenantId = null });
        db.Drafts.AddRange(
            new WorkflowDefinitionDraft { Id = "shared-draft", TenantId = "tenant-a", WorkflowDefinitionId = "definition", StateSource = "{}" },
            new WorkflowDefinitionDraft { Id = "shared-draft", TenantId = "tenant-b", WorkflowDefinitionId = "definition", StateSource = "{}" },
            new WorkflowDefinitionDraft { Id = "shared-draft", TenantId = null, WorkflowDefinitionId = "definition", StateSource = "{}" },
            new WorkflowDefinitionDraft { Id = "global-draft", TenantId = null, WorkflowDefinitionId = "global-definition", StateSource = "{}" });
        db.DraftLayouts.AddRange(
            new WorkflowDefinitionDraftLayout
            {
                Id = "layout-a",
                TenantId = "tenant-a",
                WorkflowDefinitionDraftId = "shared-draft",
                RecordsJson = JsonSerializer.Serialize(new[] { new DesignMetadataRecord("tenant-a-node", 1, 2) }, JsonSerializerOptions.Web)
            },
            new WorkflowDefinitionDraftLayout
            {
                Id = "layout-b",
                TenantId = "tenant-b",
                WorkflowDefinitionDraftId = "shared-draft",
                RecordsJson = JsonSerializer.Serialize(new[] { new DesignMetadataRecord("tenant-b-node", 3, 4) }, JsonSerializerOptions.Web)
            },
            new WorkflowDefinitionDraftLayout
            {
                Id = "global-shared-layout",
                TenantId = null,
                WorkflowDefinitionDraftId = "shared-draft",
                RecordsJson = JsonSerializer.Serialize(new[] { new DesignMetadataRecord("global-shared-node", 9, 10) }, JsonSerializerOptions.Web)
            },
            new WorkflowDefinitionDraftLayout
            {
                Id = "global-layout",
                TenantId = null,
                WorkflowDefinitionDraftId = "global-draft",
                RecordsJson = JsonSerializer.Serialize(new[] { new DesignMetadataRecord("global-node", 5, 6) }, JsonSerializerOptions.Web)
            });
        db.VersionLayouts.Add(new WorkflowDefinitionVersionLayout
        {
            Id = "global-version-layout", TenantId = null, WorkflowDefinitionVersionId = "global-version",
            RecordsJson = JsonSerializer.Serialize(new[] { new DesignMetadataRecord("global-version-node", 7, 8) }, JsonSerializerOptions.Web)
        });
        await db.SaveChangesAsync();

        var serializer = new TestSerializer();
        var tenantA = await new EfWorkflowDefinitionDraftStore(
                db,
                serializer,
                new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))))
            .FindWithLayoutByIdAsync("shared-draft");
        var tenantB = await new EfWorkflowDefinitionDraftStore(
                db,
                serializer,
                new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b"))))
            .FindWithLayoutByIdAsync("shared-draft");

        Assert.Equal("tenant-a-node", Assert.Single(tenantA!.Layout).NodeId);
        Assert.Equal("tenant-b-node", Assert.Single(tenantB!.Layout).NodeId);
        Assert.Null(await new EfWorkflowDefinitionStore(db, new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")))).FindByIdAsync("global-definition"));
        Assert.Null(await new EfWorkflowDefinitionDraftStore(db, serializer, new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")))).FindWithLayoutByIdAsync("global-draft"));

        var globalOnly = new TestAccessor(PersistenceAccessContext.Global);
        var globalDefinitions = new EfWorkflowDefinitionStore(db, globalOnly);
        var globalVersionsOnly = new EfWorkflowDefinitionVersionStore(db, serializer, globalDefinitions, globalOnly);
        var globalDraftsOnly = new EfWorkflowDefinitionDraftStore(db, serializer, globalOnly);
        Assert.Equal("global-definition", (await globalDefinitions.FindByIdAsync("global-definition"))!.Id);
        Assert.Equal("global-version", (await globalVersionsOnly.FindByIdAsync("global-version"))!.Id);
        Assert.Equal("global-node", Assert.Single((await globalDraftsOnly.FindWithLayoutByIdAsync("global-draft"))!.Layout).NodeId);
        Assert.Equal("global-version-node", Assert.Single((await new EfWorkflowDefinitionVersionLayoutStore(db, globalOnly).FindByVersionIdAsync("global-version"))!.Records).NodeId);

        var privilegedGlobal = new TestAccessor(PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("global-reader")));
        var privilegedDefinitions = new EfWorkflowDefinitionStore(db, privilegedGlobal);
        var privilegedVersions = new EfWorkflowDefinitionVersionStore(db, serializer, privilegedDefinitions, privilegedGlobal);
        var privilegedDrafts = new EfWorkflowDefinitionDraftStore(db, serializer, privilegedGlobal);
        Assert.Equal("global-definition", (await privilegedDefinitions.FindByIdAsync("global-definition"))!.Id);
        Assert.Equal("global-version", (await privilegedVersions.FindByIdAsync("global-version"))!.Id);
        Assert.Equal("global-node", Assert.Single((await privilegedDrafts.FindWithLayoutByIdAsync("global-draft"))!.Layout).NodeId);
        Assert.Equal("global-version-node", Assert.Single((await new EfWorkflowDefinitionVersionLayoutStore(db, privilegedGlobal).FindByVersionIdAsync("global-version"))!.Records).NodeId);

        var globalAccess = new TestAccessor(PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("draft-lookup")));
        var acrossScopes = new EfWorkflowDefinitionDraftStore(
            db,
            serializer,
            globalAccess);
        var global = await acrossScopes.FindWithLayoutByIdAsync("global-draft");
        Assert.Equal("global-node", Assert.Single(global!.Layout).NodeId);
        Assert.Equal("global-definition", (await new EfWorkflowDefinitionStore(db, globalAccess).FindByIdAsync("global-definition"))!.Id);
        var globalVersions = new EfWorkflowDefinitionVersionStore(
            db,
            serializer,
            new EfWorkflowDefinitionStore(db, globalAccess),
            globalAccess);
        Assert.Equal("global-version", (await globalVersions.FindByIdAsync("global-version"))!.Id);
        var globalVersionLayout = await new EfWorkflowDefinitionVersionLayoutStore(db, globalAccess).FindByVersionIdAsync("global-version");
        Assert.Equal("global-version-node", Assert.Single(globalVersionLayout!.Records).NodeId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => acrossScopes.FindWithLayoutByIdAsync("shared-draft"));
    }

    [Fact]
    public void Definition_relational_key_and_child_foreign_keys_use_folded_hashes()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = Create(connection);

        var definition = db.Model.FindEntityType(typeof(WorkflowDefinition))!;
        Assert.Equal(["ScopeKey", "IdLookupHash"], definition.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(
            ["ScopeKey", "DefinitionIdLookupHash"],
            db.Model.FindEntityType(typeof(WorkflowDefinitionVersion))!.GetForeignKeys().Single().Properties.Select(property => property.Name));
        Assert.Equal(
            ["ScopeKey", "WorkflowDefinitionIdLookupHash"],
            db.Model.FindEntityType(typeof(WorkflowDefinitionDraft))!.GetForeignKeys().Single().Properties.Select(property => property.Name));
        Assert.Equal(
            ["ScopeKey", "IdLookupHash"],
            db.Model.FindEntityType(typeof(WorkflowDefinitionVersion))!.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(
            ["ScopeKey", "IdLookupHash"],
            db.Model.FindEntityType(typeof(WorkflowDefinitionDraft))!.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(
            ["ScopeKey", "WorkflowDefinitionDraftIdLookupHash"],
            db.Model.FindEntityType(typeof(WorkflowDefinitionDraftLayout))!.GetForeignKeys().Single().Properties.Select(property => property.Name));
        Assert.Equal(
            ["ScopeKey", "WorkflowDefinitionVersionIdLookupHash"],
            db.Model.FindEntityType(typeof(WorkflowDefinitionVersionLayout))!.GetForeignKeys().Single().Properties.Select(property => property.Name));
    }

    [Fact]
    public async Task Definition_id_hash_candidates_are_residual_validated_before_returning_rows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        db.Definitions.Add(new WorkflowDefinition { Id = "actual", TenantId = "tenant-a", Name = "Definition" });
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlRawAsync(
            $"UPDATE {WorkflowsDesignEfModule.DefinitionTable} SET IdLookupHash = {{0}} WHERE TenantId = {{1}} AND Id = {{2}}",
            LookupHash("requested"), "tenant-a", "actual");

        var store = new EfWorkflowDefinitionStore(db, new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.FindByIdAsync("requested"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ListAsync(new WorkflowDefinitionFilter { Id = "requested" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ListAsync(new WorkflowDefinitionFilter { Ids = ["requested"] }));
    }

    [Fact]
    public async Task Draft_and_version_hash_candidates_are_residual_validated_in_all_relationship_routes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        db.Definitions.AddRange(
            new WorkflowDefinition { Id = "actual", TenantId = "tenant-a", Name = "Definition" },
            new WorkflowDefinition { Id = "requested", TenantId = "tenant-a", Name = "Collision target" });
        var version = new WorkflowDefinitionVersion("actual", "1.0.0", "{}") { Id = "version", TenantId = "tenant-a" };
        var draft = new WorkflowDefinitionDraft { Id = "draft", TenantId = "tenant-a", WorkflowDefinitionId = "actual", StateSource = "{}" };
        db.Versions.Add(version);
        db.Drafts.Add(draft);
        await db.SaveChangesAsync();

        var requestedDefinitionHash = LookupHash("requested");
        var requestedIdHash = ExactLookupHash("requested");
        await db.Database.ExecuteSqlRawAsync(
            $"UPDATE {WorkflowsDesignEfModule.VersionTable} SET DefinitionIdLookupHash = {{0}} WHERE TenantId = {{1}} AND Id = {{2}}",
            requestedDefinitionHash, "tenant-a", version.Id);
        await db.Database.ExecuteSqlRawAsync(
            $"UPDATE {WorkflowsDesignEfModule.DraftTable} SET WorkflowDefinitionIdLookupHash = {{0}} WHERE TenantId = {{1}} AND Id = {{2}}",
            requestedDefinitionHash, "tenant-a", draft.Id);
        await db.Database.ExecuteSqlRawAsync(
            $"UPDATE {WorkflowsDesignEfModule.VersionTable} SET IdLookupHash = {{0}} WHERE TenantId = {{1}} AND Id = {{2}}",
            requestedIdHash, "tenant-a", version.Id);
        await db.Database.ExecuteSqlRawAsync(
            $"UPDATE {WorkflowsDesignEfModule.DraftTable} SET IdLookupHash = {{0}} WHERE TenantId = {{1}} AND Id = {{2}}",
            requestedIdHash, "tenant-a", draft.Id);
        db.ChangeTracker.Clear();
        Assert.Equal(requestedIdHash, await db.Drafts.AsNoTracking().Where(x => x.Id == draft.Id).Select(x => x.IdLookupHash).SingleAsync());

        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var serializer = new TestSerializer();
        var definitions = new EfWorkflowDefinitionStore(db, access);
        var versions = new EfWorkflowDefinitionVersionStore(db, serializer, definitions, access);
        var drafts = new EfWorkflowDefinitionDraftStore(db, serializer, access);

        await Assert.ThrowsAsync<InvalidOperationException>(() => drafts.FindByWorkflowDefinitionIdAsync("requested"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => drafts.ListByWorkflowDefinitionIdAsync("requested"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => drafts.FindByIdAsync("requested"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => versions.FindByIdAsync("requested"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => versions.FindLatestVersionAsync("requested"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => versions.ListByDefinitionAsync("requested"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => versions.ExistsAsync("requested", version.SemVerSortKey));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new EfWorkflowDefinitionListProjectionStore(db, access)
            .ListByDefinitionIdsAsync(["requested"]));
    }

    [Fact]
    public async Task Privileged_reads_fail_closed_when_physical_scope_drifted_from_authoritative_tenant()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        db.Definitions.Add(new WorkflowDefinition { Id = "drift-definition", TenantId = null, Name = "Global" });
        db.Versions.Add(new WorkflowDefinitionVersion("drift-definition", "1.0.0", "{}") { Id = "drift-version", TenantId = null });
        db.Drafts.Add(new WorkflowDefinitionDraft { Id = "drift-draft", TenantId = null, WorkflowDefinitionId = "drift-definition", StateSource = "{}" });
        db.DraftLayouts.Add(new WorkflowDefinitionDraftLayout { Id = "drift-draft-layout", TenantId = null, WorkflowDefinitionDraftId = "drift-draft" });
        db.VersionLayouts.Add(new WorkflowDefinitionVersionLayout { Id = "drift-version-layout", TenantId = null, WorkflowDefinitionVersionId = "drift-version" });
        await db.SaveChangesAsync();

        // Mutate only the authoritative residual column so the physical FK envelope remains
        // valid while readers prove they reject a hash/key and tenant mismatch.
        await db.Database.ExecuteSqlRawAsync($"UPDATE {WorkflowsDesignEfModule.DefinitionTable} SET TenantId = {{0}} WHERE Id = {{1}}", "tenant-a", "drift-definition");
        await db.Database.ExecuteSqlRawAsync($"UPDATE {WorkflowsDesignEfModule.VersionTable} SET TenantId = {{0}} WHERE Id = {{1}}", "tenant-a", "drift-version");
        await db.Database.ExecuteSqlRawAsync($"UPDATE {WorkflowsDesignEfModule.DraftTable} SET TenantId = {{0}} WHERE Id = {{1}}", "tenant-a", "drift-draft");
        await db.Database.ExecuteSqlRawAsync($"UPDATE {WorkflowsDesignEfModule.DraftLayoutTable} SET TenantId = {{0}} WHERE Id = {{1}}", "tenant-a", "drift-draft-layout");
        await db.Database.ExecuteSqlRawAsync($"UPDATE {WorkflowsDesignEfModule.VersionLayoutTable} SET TenantId = {{0}} WHERE Id = {{1}}", "tenant-a", "drift-version-layout");
        db.ChangeTracker.Clear();

        var access = new TestAccessor(PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("scope-integrity")));
        var serializer = new TestSerializer();
        var definitions = new EfWorkflowDefinitionStore(db, access);
        var versions = new EfWorkflowDefinitionVersionStore(db, serializer, definitions, access);
        var drafts = new EfWorkflowDefinitionDraftStore(db, serializer, access);
        await Assert.ThrowsAsync<InvalidOperationException>(() => definitions.FindByIdAsync("drift-definition"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => versions.FindByIdAsync("drift-version"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => drafts.FindByIdAsync("drift-draft"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => drafts.FindLayoutByDraftIdAsync("drift-draft"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => drafts.FindWithLayoutByIdAsync("drift-draft"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new EfWorkflowDefinitionVersionLayoutStore(db, access).FindByVersionIdAsync("drift-version"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new EfWorkflowDefinitionListProjectionStore(db, access).ListByDefinitionIdsAsync(["drift-definition"]));
    }

    [Fact]
    public async Task Exact_name_query_is_not_subject_to_the_free_text_candidate_cap()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        db.Definitions.AddRange(Enumerable.Range(0, 1_001).Select(index => new WorkflowDefinition
        {
            Id = $"exact-{index:D4}", TenantId = "tenant-a", Name = "same-name"
        }));
        await db.SaveChangesAsync();

        var store = new EfWorkflowDefinitionStore(db, new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        Assert.Equal(1_001, (await store.ListAsync(new WorkflowDefinitionFilter { Name = "same-name" })).Count);
    }

    [Fact]
    public async Task Exact_text_filters_residual_validate_trailing_spaces_and_return_ordinal_order()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        db.Definitions.AddRange(
            new WorkflowDefinition { Id = "b", TenantId = "tenant-a", Name = "Order", Description = "Exact" },
            new WorkflowDefinition { Id = "a ", TenantId = "tenant-a", Name = "Order ", Description = "Exact " },
            new WorkflowDefinition { Id = "a", TenantId = "tenant-a", Name = "Order", Description = "Exact" });
        await db.SaveChangesAsync();

        var store = new EfWorkflowDefinitionStore(db, new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));

        Assert.Equal(["a", "b"], (await store.ListAsync(new WorkflowDefinitionFilter { Name = "Order" })).Select(x => x.Id));
        Assert.Equal("a ", Assert.Single(await store.ListAsync(new WorkflowDefinitionFilter { Names = ["Order "] })).Id);
        Assert.Equal(["a", "b"], (await store.ListAsync(new WorkflowDefinitionFilter { Description = "Exact" })).Select(x => x.Id));
    }

    [Fact]
    public async Task Search_term_preserves_leading_and_trailing_spaces()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        db.Definitions.AddRange(
            new WorkflowDefinition { Id = "with-spaces", TenantId = "tenant-a", Name = "  Order  " },
            new WorkflowDefinition { Id = "without-spaces", TenantId = "tenant-a", Name = "Order" });
        await db.SaveChangesAsync();

        var store = new EfWorkflowDefinitionStore(db, new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        Assert.Equal(["with-spaces"], (await store.ListAsync(new WorkflowDefinitionFilter { SearchTerm = " Order " })).Select(x => x.Id));
    }

    [Fact]
    public async Task Definition_text_bounds_are_rejected_without_truncation_and_search_keys_fit()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        db.Definitions.Add(new WorkflowDefinition
        {
            Id = new string('i', 128), TenantId = "tenant-a", Name = new string('n', 256), Description = new string('d', 256)
        });
        await db.SaveChangesAsync();
        var keys = await db.Definitions.AsNoTracking().Select(x => new
        {
            Id = x.IdSearchKey,
            Hash = x.IdLookupHash
        }).SingleAsync();
        Assert.Equal(WorkflowDefinitionLimits.IdentitySearchKeyMaximumLength, keys.Id.Length);
        Assert.Equal(64, keys.Hash.Length);

        db.Definitions.Add(new WorkflowDefinition { Id = "too-long", TenantId = "tenant-a", Name = new string('x', 257) });
        await Assert.ThrowsAsync<ArgumentException>(() => db.SaveChangesAsync());
        Assert.Equal(1, await db.Definitions.CountAsync());
    }

    [Fact]
    public async Task Definition_search_uses_provider_neutral_unicode_ordinal_ignore_case_keys()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var writer = new EfDesignAtomicWriter(db, access); var serializer = new TestSerializer(); var identities = new TestIdentity();
        var command = new EfAddWorkflowDefinitionCommand(db, access, writer, serializer, identities);
        await command.Execute(new DesignOperationKey("unicode-cafe"),
            new WorkflowDefinition { Id = "cafe", TenantId = "tenant-a", Name = "Café" },
            new WorkflowDefinitionDraft { Id = "cafe-draft", TenantId = "tenant-a", WorkflowDefinitionId = "cafe", State = State() });
        await command.Execute(new DesignOperationKey("unicode-deseret"),
            new WorkflowDefinition { Id = "deseret", TenantId = "tenant-a", Name = "𐐀" },
            new WorkflowDefinitionDraft { Id = "deseret-draft", TenantId = "tenant-a", WorkflowDefinitionId = "deseret", State = State() });
        await command.Execute(new DesignOperationKey("unicode-sharp-s"),
            new WorkflowDefinition { Id = "sharp-s", TenantId = "tenant-a", Name = "Straße" },
            new WorkflowDefinitionDraft { Id = "sharp-s-draft", TenantId = "tenant-a", WorkflowDefinitionId = "sharp-s", State = State() });

        var store = new EfWorkflowDefinitionStore(db, access);
        var persistedKeys = await db.Definitions.AsNoTracking().Select(x => new { x.Name, Key = x.NameSearchKey }).ToListAsync();
        Assert.Equal("|010400", persistedKeys.Single(x => x.Name == "𐐀").Key);
        Assert.Equal("cafe", Assert.Single(await store.ListAsync(new WorkflowDefinitionFilter { SearchTerm = "CAFÉ" })).Id);
        Assert.Equal("deseret", Assert.Single(await store.ListAsync(new WorkflowDefinitionFilter { SearchTerm = "𐐨" })).Id);
        Assert.Equal("sharp-s", Assert.Single(await store.ListAsync(new WorkflowDefinitionFilter { SearchTerm = "STRAßE" })).Id);
        Assert.Empty(await store.ListAsync(new WorkflowDefinitionFilter { SearchTerm = "STRAẞE" }));
    }

    [Fact]
    public void Sqlite_model_uses_portable_unbounded_text_and_portable_operation_bounds()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        using var db = Create(connection);
        var operation = db.Model.FindEntityType(typeof(DesignOperationEntity))!;
        Assert.Equal(256, operation.FindProperty(nameof(DesignOperationEntity.OperationKind))!.GetMaxLength());
        Assert.Equal(256, operation.FindProperty(nameof(DesignOperationEntity.OperationKey))!.GetMaxLength());
        Assert.Equal("TEXT", operation.FindProperty(nameof(DesignOperationEntity.ResultJson))!.GetColumnType());
        Assert.Equal("TEXT", db.Model.FindEntityType(typeof(WorkflowDefinitionVersion))!.FindProperty(nameof(WorkflowDefinitionVersion.StateSource))!.GetColumnType());
    }

    /// <summary>
    /// The charset stays a model-wide declaration, because a charset decides what a column can store.
    /// The collation does not: #1837 settled that it belongs on the columns this module compares, and
    /// nowhere else, since modules share a database. The schema-level proof is in
    /// <c>OrdinalCollationMigrationTests</c>; this only pins the shape of the declaration.
    /// </summary>
    [Fact]
    public void MySql_model_declares_its_charset_model_wide_and_its_collation_only_per_column()
    {
        // SQLite supplies a relational model builder here; the assertions target the provider
        // metadata emitted by the MySQL context without adding a provider dependency to this test.
        using var db = new WorkflowsDesignMySqlDbContext(new DbContextOptionsBuilder<WorkflowsDesignMySqlDbContext>().UseSqlite("Data Source=:memory:").Options);
        Assert.Equal(WorkflowsDesignMySqlDbContext.CharacterSet, db.Model.FindAnnotation("MySQL:Charset")?.Value);
        var model = db.GetService<IDesignTimeModel>().Model;
        Assert.Null(model.GetCollation());
        Assert.All(model.GetEntityTypes(), entity => Assert.Null(entity.FindAnnotation(RelationalAnnotationNames.Collation)?.Value));
        var definitions = model.FindEntityType(typeof(WorkflowDefinition))!;
        Assert.Equal(EfOrdinalCollation.MySql, definitions.FindProperty(nameof(WorkflowDefinition.IdLookupHash))!.GetCollation());
        Assert.Null(definitions.FindProperty(nameof(WorkflowDefinition.DeletedReason))!.GetCollation());
    }

    [Fact]
    public async Task Operation_identity_over_bound_is_rejected_without_truncation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var writer = new EfDesignAtomicWriter(db, access);
        await Assert.ThrowsAsync<ArgumentException>(() => writer.ExecuteAsync(
            new DesignOperationKey(new string('k', 257)), "test.op", new { Value = 1 }, ["test"],
            _ => Task.FromResult(new { Id = "never-staged" })));
        Assert.Empty(await db.Operations.ToListAsync());
    }

    [Fact]
    public async Task Operation_identity_at_the_shared_bound_is_accepted_for_key_and_kind()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var writer = new EfDesignAtomicWriter(db, new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        var result = await writer.ExecuteAsync(
            new DesignOperationKey(new string('k', DesignOperationKey.MaximumLength)),
            new string('o', DesignOperationKey.MaximumLength), new { Value = 1 }, ["test"],
            _ => Task.FromResult(new { Id = "staged" }));
        Assert.Equal("staged", result.Id);
        Assert.Single(await db.Operations.ToListAsync());
    }

    [Fact]
    public async Task Operation_kind_over_bound_is_rejected_without_truncation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var writer = new EfDesignAtomicWriter(db, new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        await Assert.ThrowsAsync<ArgumentException>(() => writer.ExecuteAsync(
            new DesignOperationKey("bounded-key"), new string('o', DesignOperationKey.MaximumLength + 1), new { Value = 1 }, ["test"],
            _ => Task.FromResult(new { Id = "never-staged" })));
        Assert.Empty(await db.Operations.ToListAsync());
    }

    [Fact]
    public async Task Provider_read_failures_are_normalized_at_the_public_boundary()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        await connection.CloseAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var failure = await Assert.ThrowsAsync<DesignPersistenceException>(() => new EfWorkflowDefinitionStore(db, access).FindByIdAsync("missing"));
        Assert.Equal(DesignPersistenceFailureKind.Provider, failure.FailureKind);
    }

    [Fact]
    public void Ef_registration_resolves_owned_surfaces_and_provides_default_accessor()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPayloadSerializer, TestSerializer>();
        services.AddSingleton<IIdentityGenerator, TestIdentity>();
        services.AddSingleton<IActivityStructureService, EmptyActivityStructureService>();
        services.AddScoped<IDesignAtomicWriter, CustomDesignAtomicWriter>();
        services.AddWorkflowsDesignEntityFrameworkCore(new WorkflowsDesignEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        });

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var serviceProvider = scope.ServiceProvider;

        Assert.NotNull(serviceProvider.GetRequiredService<IPersistenceAccessContextAccessor>());
        Assert.IsType<CustomDesignAtomicWriter>(serviceProvider.GetRequiredService<IDesignAtomicWriter>());
        Assert.IsType<WorkflowDefinitionFactory>(serviceProvider.GetRequiredService<IWorkflowDefinitionFactory>());
        Assert.IsType<WorkflowDefinitionDraftFactory>(serviceProvider.GetRequiredService<IWorkflowDefinitionDraftFactory>());
        Assert.IsType<WorkflowDefinitionVersionFactory>(serviceProvider.GetRequiredService<IWorkflowDefinitionVersionFactory>());
        Assert.IsType<WorkflowDefinitionLookup>(serviceProvider.GetRequiredService<IWorkflowDefinitionLookup>());
        Assert.True(typeof(IDesignAtomicWriter).IsDefined(typeof(DesignPersistenceReplacementContractAttribute), inherit: false));
        _ = serviceProvider.GetRequiredService<WorkflowsDesignDbContext>();
        _ = serviceProvider.GetRequiredService<WorkflowsDesignSqliteDbContext>();
        foreach (var serviceType in new[]
                 {
                     typeof(EfWorkflowDefinitionStore), typeof(EfWorkflowDefinitionVersionStore), typeof(EfWorkflowDefinitionDraftStore),
                     typeof(EfWorkflowDefinitionVersionLayoutStore), typeof(EfWorkflowDefinitionListProjectionStore),
                     typeof(IWorkflowDefinitionLookup), typeof(IWorkflowDefinitionStore), typeof(IWorkflowDefinitionVersionStore), typeof(IWorkflowDefinitionDraftStore),
                     typeof(IWorkflowDefinitionVersionLayoutStore), typeof(IWorkflowDefinitionListProjectionStore),
                     typeof(IAddWorkflowDefinitionCommand), typeof(IAddWorkflowDefinitionVersionCommand), typeof(ICreateDraftCommand),
                     typeof(ICloneDraftFromVersionCommand), typeof(IDeleteWorkflowDefinitionPermanentlyCommand), typeof(IDiscardDraftCommand),
                     typeof(IMaterializeWorkflowDefinitionCommand), typeof(IMaterializeWorkflowDefinitionVersionCommand),
                     typeof(IPromoteDraftToVersionCommand), typeof(ISaveWorkflowDefinitionCommand), typeof(ISubmitWorkflowDefinitionCommand),
                     typeof(IUpdateDraftCommand)
                 })
            Assert.NotNull(serviceProvider.GetRequiredService(serviceType));
    }

    [Fact]
    public void Ef_registration_replaces_the_publishing_layout_fallback()
    {
        var services = new ServiceCollection();
        new WorkflowsPublishingFeature().ConfigureServices(services);

        services.AddWorkflowsDesignEntityFrameworkCore(new WorkflowsDesignEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        });

        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowDefinitionVersionLayoutStore) &&
            descriptor.ImplementationType == typeof(EmptyWorkflowDefinitionVersionLayoutStore));
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowDefinitionVersionLayoutStore) &&
            descriptor.ImplementationFactory is not null);
    }

    [Fact]
    public void Ef_registration_refuses_an_arbitrary_layout_registration()
    {
        var services = new ServiceCollection();
        services.AddScoped<IWorkflowDefinitionVersionLayoutStore, CustomLayoutStore>();

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddWorkflowsDesignEntityFrameworkCore(
            new WorkflowsDesignEntityFrameworkCoreOptions
            {
                Provider = "Sqlite",
                ConnectionString = "Data Source=:memory:"
            }));

        Assert.Contains("already present", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ef_registration_refuses_a_marked_non_layout_store()
    {
        var services = new ServiceCollection();
        services.AddScoped<IWorkflowDefinitionStore, MarkedCustomDefinitionStore>();

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddWorkflowsDesignEntityFrameworkCore(
            new WorkflowsDesignEntityFrameworkCoreOptions
            {
                Provider = "Sqlite",
                ConnectionString = "Data Source=:memory:"
            }));

        Assert.Contains("already present", exception.Message, StringComparison.Ordinal);
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowDefinitionStore) &&
            descriptor.ImplementationType == typeof(MarkedCustomDefinitionStore));
    }

    [Fact]
    public async Task Ef_registration_lookup_reads_a_definition_through_the_provider_store()
    {
        await using var database = new TemporarySqliteDatabase("workflows-design-lookup");
        var services = new ServiceCollection();
        services.AddSingleton<IPayloadSerializer, TestSerializer>();
        services.AddSingleton<IIdentityGenerator, TestIdentity>();
        services.AddSingleton<IActivityStructureService, EmptyActivityStructureService>();
        services.AddWorkflowsDesignEntityFrameworkCore(new WorkflowsDesignEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = database.ConnectionString
        });

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Definitions.Add(new WorkflowDefinition { Id = "lookup-definition", TenantId = "default", Name = "Lookup definition" });
        await db.SaveChangesAsync();

        var lookup = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionLookup>();
        var definition = await lookup.GetDefinition("lookup-definition");

        Assert.Equal("lookup-definition", definition.Id);
        Assert.Equal("Lookup definition", definition.Name);
    }

    [Fact]
    public void Ef_model_table_names_are_the_module_table_names()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = Create(connection);

        Assert.Equal(WorkflowsDesignEfModule.DefinitionTable, db.Model.FindEntityType(typeof(WorkflowDefinition))!.GetTableName());
        Assert.Equal(WorkflowsDesignEfModule.VersionTable, db.Model.FindEntityType(typeof(WorkflowDefinitionVersion))!.GetTableName());
        Assert.Equal(WorkflowsDesignEfModule.DraftTable, db.Model.FindEntityType(typeof(WorkflowDefinitionDraft))!.GetTableName());
    }

    [Fact]
    public async Task Ef_registration_keeps_replacement_validation_when_an_unrelated_startup_task_precedes_it()
    {
        var services = new ServiceCollection();
        services.AddScoped<IStartupTask, UnrelatedStartupTask>();
        var options = new WorkflowsDesignEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        };
        services.AddWorkflowsDesignEntityFrameworkCore(options);
        services.AddWorkflowsDesignEntityFrameworkCore(options);
        services.AddScoped<IWorkflowDefinitionStore>(_ => throw new NotSupportedException());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var validator = Assert.Single(scope.ServiceProvider.GetServices<IStartupTask>(),
            task => task is ValidateDesignPersistenceReplacementContractsStartupTask);
        Assert.Single(scope.ServiceProvider.GetServices<IStartupTask>(), task => task is UnrelatedStartupTask);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ExecuteAsync(CancellationToken.None));
        Assert.Contains(nameof(IWorkflowDefinitionStore), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ef_registration_fails_startup_when_a_replacement_contract_is_removed_late()
    {
        var services = new ServiceCollection();
        services.AddWorkflowsDesignEntityFrameworkCore(new WorkflowsDesignEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        });
        services.RemoveAll<IWorkflowDefinitionStore>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var validator = Assert.Single(scope.ServiceProvider.GetServices<IStartupTask>(),
            task => task is ValidateDesignPersistenceReplacementContractsStartupTask);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ExecuteAsync(CancellationToken.None));
        Assert.Contains($"{nameof(IWorkflowDefinitionStore)} (0 registrations)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ef_repeated_registration_does_not_duplicate_replacement_validator()
    {
        var services = new ServiceCollection();
        var options = new WorkflowsDesignEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        };
        services.AddWorkflowsDesignEntityFrameworkCore(options);
        services.AddWorkflowsDesignEntityFrameworkCore(options);

        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IStartupTask) &&
            descriptor.ImplementationType == typeof(ValidateDesignPersistenceReplacementContractsStartupTask));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Ef_registration_and_persistence_core_are_safe_in_either_order(bool persistenceCoreFirst)
    {
        var services = new ServiceCollection();
        if (persistenceCoreFirst)
            services.AddPersistenceCore();

        services.AddWorkflowsDesignEntityFrameworkCore(new WorkflowsDesignEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        });

        if (!persistenceCoreFirst)
            services.AddPersistenceCore();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var serviceProvider = scope.ServiceProvider;

        Assert.NotNull(serviceProvider.GetRequiredService<IPersistenceAccessContextAccessor>());
        Assert.NotNull(serviceProvider.GetRequiredService<IPersistenceAccessContextBinder>());
        Assert.NotNull(serviceProvider.GetRequiredService<WorkflowsDesignDbContext>());
    }

    [Fact]
    public void Ef_registration_rejects_a_different_selected_backend()
    {
        var services = new ServiceCollection();
        services.AddSingleton<object>(new object());
        var owned = Assert.Single(services, x => x.ServiceType == typeof(object));
        services.AddSingleton(new DesignPersistenceBackend("custom", [owned]));

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddWorkflowsDesignEntityFrameworkCore(new WorkflowsDesignEntityFrameworkCoreOptions()));

        Assert.Contains("already selected", exception.Message, StringComparison.Ordinal);
        Assert.Contains(owned, services);
    }

    [Fact]
    public void Design_backend_contract_accepts_a_custom_backend_name()
    {
        var services = new ServiceCollection();
        services.AddSingleton<object>(new object());
        var owned = Assert.Single(services, x => x.ServiceType == typeof(object));
        var backend = new DesignPersistenceBackend("custom", [owned]);

        DesignPersistenceBackend.Register(services, backend);

        Assert.Same(backend, DesignPersistenceBackend.Find(services));
        Assert.Equal("custom", backend.Name);
    }

    [Fact]
    public void Ef_registration_rejects_a_custom_selected_backend()
    {
        var services = new ServiceCollection();
        services.AddSingleton<object>(new object());
        var owned = Assert.Single(services, x => x.ServiceType == typeof(object));
        DesignPersistenceBackend.Register(services, new DesignPersistenceBackend("custom", [owned]));

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddWorkflowsDesignEntityFrameworkCore(new WorkflowsDesignEntityFrameworkCoreOptions()));

        Assert.Contains("already selected", exception.Message, StringComparison.Ordinal);
        Assert.Contains(owned, services);
    }

    [Fact]
    public void Ef_registration_refuses_a_blank_named_connection_instead_of_handing_it_to_the_provider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:workflow-design"] = " " })
            .Build());
        services.AddWorkflowsDesignEntityFrameworkCore(new WorkflowsDesignEntityFrameworkCoreOptions { Provider = "Sqlite", ConnectionName = "workflow-design" });
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var exception = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>());

        Assert.Contains("'workflow-design' was not found or was empty", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ef_feature_registration_flows_settings_through_ConfigureServices()
    {
        var services = new ServiceCollection();
        var feature = new WorkflowsDesignEntityFrameworkCoreFeature
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=feature.db",
            ConnectionName = "workflow-design"
        };

        feature.ConfigureServices(services);

        var options = Assert.Single(services, x => x.ServiceType == typeof(WorkflowsDesignEntityFrameworkCoreOptions)).ImplementationInstance as WorkflowsDesignEntityFrameworkCoreOptions;
        Assert.NotNull(options);
        Assert.Equal(feature.Provider, options!.Provider);
        Assert.Equal(feature.ConnectionString, options.ConnectionString);
        Assert.Equal(feature.ConnectionName, options.ConnectionName);
        Assert.Contains(services, x => x.ServiceType == typeof(IDesignAtomicWriter));
    }

    [Fact]
    public async Task Marker_race_replay_does_not_publish_lifecycle_events_for_any_draft_command()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var serializer = new TestSerializer();
        var events = new CapturingDeferredEventPublisher();
        var atomic = new ReplayedAtomicWriter();

        await new EfCreateDraftCommand(db, access, atomic, new TestIdentity(), serializer, new TestLockProvider(), deferredEvents: events)
            .Execute(new DesignOperationKey("create-race"), "definition");
        await new EfCloneDraftFromVersionCommand(db, access, atomic, new TestIdentity(), serializer, new TestLockProvider(), deferredEvents: events)
            .Execute(new DesignOperationKey("clone-race"), "version");
        await new EfUpdateDraftCommand(db, access, atomic, serializer, new EmptyActivityStructureService(), new TestLockProvider(), deferredEvents: events)
            .Execute(new DesignOperationKey("update-race"), new UpdateDraftRequest("draft", State(), []));
        await new EfDiscardDraftCommand(db, access, atomic, new TestLockProvider(), events)
            .Execute(new DesignOperationKey("discard-race"), "draft");

        Assert.Empty(events.Events);
    }

    [Fact]
    public async Task Concurrent_unique_marker_race_reports_winner_and_replayed_loser_and_publishes_once()
    {
        await using var database = new TemporarySqliteDatabase("workflow-design-race");
        var path = database.Path;
        await using (var setupConnection = new SqliteConnection($"Data Source={path};Default Timeout=30"))
        {
            await setupConnection.OpenAsync();
            await using var setup = Create(setupConnection);
            await setup.Database.EnsureCreatedAsync();
            setup.Definitions.Add(new WorkflowDefinition { Id = "definition", TenantId = "tenant-a", Name = "Definition" });
            await setup.SaveChangesAsync();
        }

        await using var firstConnection = new SqliteConnection($"Data Source={path};Default Timeout=30");
        await using var secondConnection = new SqliteConnection($"Data Source={path};Default Timeout=30");
        await firstConnection.OpenAsync();
        await secondConnection.OpenAsync();
        await using var firstDb = Create(firstConnection);
        await using var secondDb = Create(secondConnection);
        var firstEvents = new CapturingDeferredEventPublisher();
        var secondEvents = new CapturingDeferredEventPublisher();
        using var barrier = new Barrier(2);
        var firstWriter = new PreTransactionBarrierAtomicWriter(new EfDesignAtomicWriter(firstDb, new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")))), barrier);
        var secondWriter = new PreTransactionBarrierAtomicWriter(new EfDesignAtomicWriter(secondDb, new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")))), barrier);
        var first = new EfCreateDraftCommand(firstDb, new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))), firstWriter, new TestIdentity("first"), new TestSerializer(), new TestLockProvider(), deferredEvents: firstEvents);
        var second = new EfCreateDraftCommand(secondDb, new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))), secondWriter, new TestIdentity("second"), new TestSerializer(), new TestLockProvider(), deferredEvents: secondEvents);

        await Task.WhenAll(
            Task.Run(() => first.Execute(new DesignOperationKey("same-marker-race"), "definition")),
            Task.Run(() => second.Execute(new DesignOperationKey("same-marker-race"), "definition")));

        Assert.Equal(
            [DesignAtomicWriteStatus.Committed, DesignAtomicWriteStatus.Replayed],
            new[] { firstWriter.LastStatus, secondWriter.LastStatus }.OrderBy(status => status).ToArray());
        Assert.True(firstWriter.BarrierPassed && secondWriter.BarrierPassed);
        Assert.True(firstWriter.StageInvocationCount > 0 && secondWriter.StageInvocationCount > 0);
        Assert.Equal(1, await firstDb.Operations.AsNoTracking().CountAsync());
        var events = firstEvents.Events.Concat(secondEvents.Events).ToArray();
        Assert.Single(events.OfType<DraftCreated>());
        Assert.Single(events.OfType<DraftValidated>());
    }

    [Fact]
    public async Task Concurrent_add_version_unique_identity_is_reported_as_a_version_conflict()
    {
        await using var database = new TemporarySqliteDatabase("workflow-design-version-race");
        var path = database.Path;
        await using (var setupConnection = new SqliteConnection($"Data Source={path};Default Timeout=30"))
        {
            await setupConnection.OpenAsync();
            await using var setup = Create(setupConnection);
            await setup.Database.EnsureCreatedAsync();
            setup.Definitions.Add(new WorkflowDefinition { Id = "definition", TenantId = "tenant-a", Name = "Definition" });
            await setup.SaveChangesAsync();
        }

        await using var firstConnection = new SqliteConnection($"Data Source={path};Default Timeout=30");
        await using var secondConnection = new SqliteConnection($"Data Source={path};Default Timeout=30");
        await firstConnection.OpenAsync();
        await secondConnection.OpenAsync();
        await using var firstDb = Create(firstConnection);
        await using var secondDb = Create(secondConnection);
        using var barrier = new Barrier(2);
        var firstWriter = new PreTransactionBarrierAtomicWriter(new EfDesignAtomicWriter(firstDb, new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")))), barrier);
        var secondWriter = new PreTransactionBarrierAtomicWriter(new EfDesignAtomicWriter(secondDb, new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")))), barrier);
        var first = new EfAddWorkflowDefinitionVersionCommand(firstDb, new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))), firstWriter, new TestSerializer(), new TestIdentity("first"), new TestLockProvider());
        var second = new EfAddWorkflowDefinitionVersionCommand(secondDb, new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))), secondWriter, new TestSerializer(), new TestIdentity("second"), new TestLockProvider());

        var firstTask = Task.Run(() => first.Execute(new DesignOperationKey("version-race-a"), "definition", State()));
        var secondTask = Task.Run(() => second.Execute(new DesignOperationKey("version-race-b"), "definition", State()));
        var results = await Task.WhenAll(
            CaptureAsync(firstTask),
            CaptureAsync(secondTask));

        Assert.Single(results.OfType<WorkflowDefinitionVersionAdded>());
        var conflict = Assert.Single(results.OfType<WorkflowDefinitionVersionConflictException>());
        Assert.Equal("definition", conflict.DefinitionId);
        Assert.Equal("1.0.0", conflict.Version);
        Assert.True(firstWriter.BarrierPassed && secondWriter.BarrierPassed);
        Assert.Single(await firstDb.Versions.AsNoTracking().ToListAsync());

        static async Task<object> CaptureAsync<T>(Task<T> task)
        {
            try
            {
                return (object?)await task ?? throw new InvalidOperationException("The concurrent version operation returned no result.");
            }
            catch (Exception exception)
            {
                return exception;
            }
        }
    }

    [Fact]
    public async Task Legacy_bool_discard_marker_replays_without_duplicate_event()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var writer = new EfDesignAtomicWriter(db, access);
        var key = new DesignOperationKey("legacy-discard");
        await writer.ExecuteAsync(key, "workflow.draft.discard.v1", new { DraftId = "draft" }, [DesignPersistenceUnitNames.Drafts],
            (_, _) => Task.FromResult(DesignAtomicWriteStage<bool>.Accepted(true)));
        var marker = await db.Operations.SingleAsync();
        marker.RequestFingerprint = LegacyFingerprint("workflow.draft.discard.v1", "{\"draftId\":\"draft\"}");
        marker.ResultJson = "true";
        marker.ResultFingerprint = LegacyFingerprint("workflow.draft.discard.v1.result", "true");
        await db.SaveChangesAsync();

        var events = new CapturingDeferredEventPublisher();
        await new EfDiscardDraftCommand(db, access, writer, new TestLockProvider(), events).Execute(key, "draft");

        Assert.Empty(events.Events);
    }

    [Fact]
    public async Task Rollback_and_dispose_failures_do_not_mask_the_primary_domain_failure_or_cancellation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var primary = new InvalidOperationException("primary domain failure");
        var writer = new EfDesignAtomicWriter(db, access, transactionFactory: _ => Task.FromResult<IDbContextTransaction>(new FaultInjectingTransaction(new OperationCanceledException("rollback"), new InvalidOperationException("dispose"))));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.ExecuteAsync(
            new DesignOperationKey("cleanup-domain"), "test.op", new { Value = 1 }, ["test"],
            (_, _) => Task.FromException<DesignAtomicWriteStage<int>>(primary)));
        Assert.Same(primary, exception);
        Assert.NotEmpty(exception.Data);

        using var cancellation = new CancellationTokenSource();
        var cancelled = new OperationCanceledException(cancellation.Token);
        writer = new EfDesignAtomicWriter(db, access, transactionFactory: _ => Task.FromResult<IDbContextTransaction>(new FaultInjectingTransaction(new InvalidOperationException("rollback"), new OperationCanceledException("dispose"))));
        var cancellationException = await Assert.ThrowsAsync<OperationCanceledException>(() => writer.ExecuteAsync(
            new DesignOperationKey("cleanup-cancellation"), "test.op", new { Value = 1 }, ["test"],
            (_, _) => Task.FromException<DesignAtomicWriteStage<int>>(cancelled), cancellationToken: cancellation.Token));
        Assert.Same(cancelled, cancellationException);
        Assert.NotEmpty(cancellationException.Data);
    }

    [Fact]
    public async Task Rejected_stage_survives_rollback_failure_without_persisting_mutation_or_marker()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var writer = new EfDesignAtomicWriter(db, access, transactionFactory: _ => Task.FromResult<IDbContextTransaction>(
            new FaultInjectingTransaction(new InvalidOperationException("rollback failure"), null)));

        var result = await writer.ExecuteAsync(
            new DesignOperationKey("rejected-rollback"),
            "test.op",
            new { Value = 1 },
            ["definitions"],
            (_, _) =>
            {
                db.Definitions.Add(new WorkflowDefinition { Id = "should-not-persist", TenantId = "tenant-a", Name = "Rejected" });
                return Task.FromResult(DesignAtomicWriteStage<int>.Rejected());
            });

        Assert.Equal(DesignAtomicWriteStatus.Rejected, result.Status);
        Assert.Empty(await db.Definitions.AsNoTracking().ToListAsync());
        Assert.Empty(await db.Operations.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Rejected_stage_survives_disposal_failure_without_persisting_mutation_or_marker()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var writer = new EfDesignAtomicWriter(db, access, transactionFactory: _ => Task.FromResult<IDbContextTransaction>(
            new FaultInjectingTransaction(null, new InvalidOperationException("dispose failure"))));

        var result = await writer.ExecuteAsync(
            new DesignOperationKey("rejected-disposal"),
            "test.op",
            new { Value = 1 },
            ["definitions"],
            (_, _) =>
            {
                db.Definitions.Add(new WorkflowDefinition { Id = "should-not-persist", TenantId = "tenant-a", Name = "Rejected" });
                return Task.FromResult(DesignAtomicWriteStage<int>.Rejected());
            });

        Assert.Equal(DesignAtomicWriteStatus.Rejected, result.Status);
        Assert.Empty(await db.Definitions.AsNoTracking().ToListAsync());
        Assert.Empty(await db.Operations.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Post_commit_transaction_disposal_failure_does_not_replace_the_committed_result()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var writer = new EfDesignAtomicWriter(
            db,
            access,
            transactionFactory: _ => Task.FromResult<IDbContextTransaction>(
                new FaultInjectingTransaction(null, new InvalidOperationException("dispose after commit"))));

        var result = await writer.ExecuteAsync(
            new DesignOperationKey("committed-cleanup"),
            "test.op",
            new { Value = 1 },
            ["test"],
            (_, _) => Task.FromResult(DesignAtomicWriteStage<int>.Accepted(42)));

        Assert.Equal(DesignAtomicWriteStatus.Committed, result.Status);
        Assert.Equal(42, result.Value);
        Assert.Equal("committed-cleanup", (await db.Operations.AsNoTracking().SingleAsync()).OperationKey);
    }

    [Fact]
    public async Task Save_workflow_definition_covers_lookup_tenant_validation_and_failure_branches()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var writer = new EfDesignAtomicWriter(db, access);
        db.Definitions.Add(new WorkflowDefinition { Id = "stored", TenantId = "tenant-a", Name = "before" });
        await db.SaveChangesAsync();
        var command = new EfSaveWorkflowDefinitionCommand(db, access, writer);

        await command.Execute(new DesignOperationKey("save-success"), new WorkflowDefinition { Id = "STORED", TenantId = "tenant-a", Name = "after" });
        Assert.Equal("after", (await db.Definitions.SingleAsync()).Name);
        await Assert.ThrowsAsync<EntityNotFoundException>(() => command.Execute(new DesignOperationKey("save-missing"), new WorkflowDefinition { Id = "missing", TenantId = "tenant-a", Name = "missing" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => command.Execute(new DesignOperationKey("save-tenant"), new WorkflowDefinition { Id = "stored", TenantId = "tenant-b", Name = "wrong tenant" }));
        await Assert.ThrowsAsync<ArgumentException>(() => command.Execute(new DesignOperationKey("save-invalid"), new WorkflowDefinition { Id = "stored", TenantId = "tenant-a", Name = new string('x', WorkflowDefinitionLimits.TextMaximumLength + 1) }));
    }

    [Fact]
    public async Task SQLite_save_validates_bounded_version_draft_layout_and_operation_identities()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        db.Definitions.Add(new WorkflowDefinition { Id = "definition", TenantId = "tenant-a", Name = "Definition" });
        db.Versions.Add(new WorkflowDefinitionVersion("definition", $"1.0.0-{new string('a', 123)}") { Id = "version", TenantId = "tenant-a" });
        await Assert.ThrowsAsync<ArgumentException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.Drafts.Add(new WorkflowDefinitionDraft { Id = new string('d', 129), TenantId = "tenant-a", WorkflowDefinitionId = "definition", StateSource = "{}" });
        await Assert.ThrowsAsync<ArgumentException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.Operations.Add(new DesignOperationEntity { TenantId = "tenant-a", OperationKind = new string('o', DesignOperationKey.MaximumLength + 1), OperationKey = "key", RequestFingerprint = "request", ResultFingerprint = "result", ResultJson = "{}" });
        await Assert.ThrowsAsync<ArgumentException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Definitions_drafts_layouts_and_versions_survive_reopen_and_preserve_scope()
    {
        await using var database = new TemporarySqliteDatabase("workflow-design-reopen");
        var path = database.Path;
        var serializer = new TestSerializer(); var accessor = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            await using var db = Create(connection);
            await db.Database.EnsureCreatedAsync(); var atomic = new EfDesignAtomicWriter(db, accessor); var identities = new TestIdentity();
            var definition = new WorkflowDefinition { Id = "definition-1", TenantId = "tenant-a", Name = "Order" }; var draft = new WorkflowDefinitionDraft { Id = "draft-1", TenantId = "tenant-a", WorkflowDefinitionId = definition.Id, State = State() };
            var add = new EfAddWorkflowDefinitionCommand(db, accessor, atomic, serializer, identities); await add.Execute(new DesignOperationKey("create-1"), definition, draft, [new DesignMetadataRecord("root", 1, 2)]);
            var definitions = new EfWorkflowDefinitionStore(db, accessor); Assert.Single(await definitions.ListAsync(new WorkflowDefinitionFilter { SearchTerm = "ord" }));
            var drafts = new EfWorkflowDefinitionDraftStore(db, serializer, accessor); var loadedLayout = await drafts.FindWithLayoutByIdAsync(draft.Id); Assert.NotNull(loadedLayout); Assert.Single(loadedLayout!.Layout);
            var version = new EfAddWorkflowDefinitionVersionCommand(db, accessor, atomic, serializer, identities, new TestLockProvider()); var added = await version.Execute(new DesignOperationKey("version-1"), definition.Id, State()); Assert.Equal("1.0.0", added.Version);
        }
        await using (var reopenedConnection = new SqliteConnection($"Data Source={path}"))
        {
            await reopenedConnection.OpenAsync();
            await using var reopened = Create(reopenedConnection);
            var store = new EfWorkflowDefinitionStore(reopened, accessor); Assert.NotNull(await store.FindByIdAsync("definition-1"));
            var versions = new EfWorkflowDefinitionVersionStore(reopened, serializer, store, accessor); Assert.Equal("1.0.0", (await versions.FindLatestVersionAsync("definition-1"))!.Version);
        }
        await using (var scopeConnection = new SqliteConnection($"Data Source={path}"))
        {
            await scopeConnection.OpenAsync();
            await using var scopedDb = Create(scopeConnection);
            var other = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b"))); Assert.Null(await new EfWorkflowDefinitionStore(scopedDb, other).FindByIdAsync("definition-1"));
        }
    }

    [Fact]
    public async Task Operation_key_replays_and_conflicting_request_is_rejected()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync(); await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))); var writer = new EfDesignAtomicWriter(db, access); var key = new DesignOperationKey("same"); var first = await writer.ExecuteAsync(key, "test.op", new { Value = 1 }, ["test"], _ => Task.FromResult(new { Id = "winner" })); var replay = await writer.ExecuteAsync(key, "test.op", new { Value = 1 }, ["test"], _ => Task.FromResult(new { Id = "loser" })); Assert.Equal(first.Id, replay.Id); await Assert.ThrowsAsync<InvalidOperationException>(() => writer.ExecuteAsync(key, "test.op", new { Value = 2 }, ["test"], _ => Task.FromResult(new { Id = "conflict" })));
    }

    [Fact]
    public async Task Operation_identity_preserves_trailing_spaces_in_kind_and_key()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var writer = new EfDesignAtomicWriter(db, access);

        var kind = "test.trailing-kind";
        var key = new DesignOperationKey("test-trailing-key");
        var kindTrailing = await writer.ExecuteAsync(
            key,
            kind,
            new { Value = "kind" },
            ["test"],
            _ => Task.FromResult("kind-result"));
        var kindWithTrailingSpace = await writer.ExecuteAsync(
            key,
            kind + " ",
            new { Value = "kind-trailing" },
            ["test"],
            _ => Task.FromResult("kind-trailing-result"));
        var keyWithTrailingSpace = await writer.ExecuteAsync(
            new DesignOperationKey(key.Value + " "),
            kind,
            new { Value = "key-trailing" },
            ["test"],
            _ => Task.FromResult("key-trailing-result"));

        Assert.Equal("kind-result", kindTrailing);
        Assert.Equal("kind-trailing-result", kindWithTrailingSpace);
        Assert.Equal("key-trailing-result", keyWithTrailingSpace);
        Assert.Equal(kindTrailing, await writer.ExecuteAsync(key, kind, new { Value = "kind" }, ["test"], _ => Task.FromResult("wrong")));
        Assert.Equal(kindWithTrailingSpace, await writer.ExecuteAsync(key, kind + " ", new { Value = "kind-trailing" }, ["test"], _ => Task.FromResult("wrong")));
        Assert.Equal(keyWithTrailingSpace, await writer.ExecuteAsync(new DesignOperationKey(key.Value + " "), kind, new { Value = "key-trailing" }, ["test"], _ => Task.FromResult("wrong")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.ExecuteAsync(key, kind, new { Value = "different" }, ["test"], _ => Task.FromResult("conflict")));

        var markers = await db.Operations.AsNoTracking().ToListAsync();
        Assert.Equal(3, markers.Count);
        Assert.Contains(markers, marker => marker.OperationKind == kind && marker.OperationKey == key.Value);
        Assert.Contains(markers, marker => marker.OperationKind == kind + " " && marker.OperationKey == key.Value);
        Assert.Contains(markers, marker => marker.OperationKind == kind && marker.OperationKey == key.Value + " ");
        Assert.All(markers, marker =>
        {
            Assert.Equal(ExactLookupHash(marker.OperationKind), marker.OperationKindLookupHash);
            Assert.Equal(ExactLookupHash(marker.OperationKey), marker.OperationKeyLookupHash);
        });

        static string ExactLookupHash(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    [Fact]
    public async Task Operation_fingerprint_is_canonical_across_request_property_order()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync(); await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))); IDesignAtomicWriter writer = new EfDesignAtomicWriter(db, access);
        var key = new DesignOperationKey("canonical-request");
        await writer.ExecuteAsync(key, "test.op", new { A = "é", B = "東京" }, ["test"], (_, _) => Task.FromResult(DesignAtomicWriteStage<string>.Accepted("winner")));
        var replay = await writer.ExecuteAsync<string>(key, "test.op", new { B = "東京", A = "é" }, ["test"], (_, _) => throw new InvalidOperationException("canonical replay must not restage"));
        Assert.Equal(DesignAtomicWriteStatus.Replayed, replay.Status);
        Assert.Equal("winner", replay.Value);
    }

    [Fact]
    public async Task State_request_material_uses_the_configured_payload_serializer_and_portable_framing()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync(); await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))); var state = State();
        var serializerOptions = new JsonSerializerOptions { PropertyNamingPolicy = null }; var serializer = new TestSerializer(serializerOptions); var identity = new TestIdentity(); var writer = new EfDesignAtomicWriter(db, access);
        db.Definitions.Add(new WorkflowDefinition { Id = "definition", TenantId = "tenant-a", Name = "Definition" }); await db.SaveChangesAsync();
        await new EfAddWorkflowDefinitionVersionCommand(db, access, writer, serializer, identity, new TestLockProvider()).Execute(new DesignOperationKey("serializer-request"), "definition", state);
        var marker = await db.Operations.SingleAsync(x => x.OperationKey == "serializer-request");
        var stateJson = serializer.Serialize(state);
        var materialJson = JsonSerializer.Serialize(new { definitionId = "definition", stateJson });
        Assert.Equal(PortableFingerprint("workflow.version.add.v1", materialJson), marker.RequestFingerprint);
    }

    [Fact]
    public async Task Projection_batches_definition_ids_without_truncating_rows_and_preserves_scope_and_order()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync(); await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var tenant = "tenant-a"; var definitions = Enumerable.Range(0, 205).Select(index => new WorkflowDefinition { Id = $"definition-{index:D3}", TenantId = tenant, Name = $"Definition {index:D3}" }).ToArray();
        db.Definitions.AddRange(definitions);
        var oldDraft = new WorkflowDefinitionDraft { Id = "draft-old", TenantId = tenant, WorkflowDefinitionId = definitions[0].Id, CreatedAt = DateTimeOffset.UnixEpoch, LastModifiedAt = DateTimeOffset.UnixEpoch.AddDays(1), StateSource = "{}" };
        var currentDraft = new WorkflowDefinitionDraft { Id = "draft-current", TenantId = tenant, WorkflowDefinitionId = definitions[0].Id, CreatedAt = DateTimeOffset.UnixEpoch.AddDays(2), LastModifiedAt = DateTimeOffset.UnixEpoch.AddDays(1), StateSource = "{}" };
        db.Drafts.AddRange([oldDraft, currentDraft]);
        foreach (var definition in definitions.Skip(1))
            db.Drafts.Add(new WorkflowDefinitionDraft { Id = $"draft-{definition.Id}", TenantId = tenant, WorkflowDefinitionId = definition.Id, StateSource = "{}" });
        db.Versions.AddRange(Enumerable.Range(1, 201).Select(number => new WorkflowDefinitionVersion(definitions[0].Id, $"{number}.0.0") { Id = $"version-{number:D3}", TenantId = tenant }));
        foreach (var definition in definitions.Skip(1))
            db.Versions.Add(new WorkflowDefinitionVersion(definition.Id, "1.0.0") { Id = $"version-{definition.Id}", TenantId = tenant });
        db.Definitions.Add(new WorkflowDefinition { Id = "tenant-b-only", TenantId = "tenant-b", Name = "Foreign" });
        db.Versions.Add(new WorkflowDefinitionVersion("tenant-b-only", "9.0.0") { Id = "foreign-version", TenantId = "tenant-b" });
        await db.SaveChangesAsync();

        var requested = definitions.Select(x => x.Id).Reverse().Append("tenant-b-only").Append(definitions[0].Id).Append(definitions[0].Id.ToUpperInvariant()).ToArray();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));
        var projections = await new EfWorkflowDefinitionListProjectionStore(db, access).ListByDefinitionIdsAsync(requested);
        Assert.Equal(206, projections.Count);
        Assert.Equal(requested.GroupBy(WorkflowDefinitionIdentity.Fold, StringComparer.Ordinal).Select(group => group.First()), projections.Select(x => x.WorkflowDefinitionId));
        var first = Assert.Single(projections, x => x.WorkflowDefinitionId == definitions[0].Id);
        Assert.Equal("draft-current", first.DraftId);
        Assert.Equal("version-201", first.LatestVersionId);
        Assert.Equal("201.0.0", first.LatestVersion);
        Assert.Equal(201, first.VersionCount);
        var foreign = Assert.Single(projections, x => x.WorkflowDefinitionId == "tenant-b-only");
        Assert.Null(foreign.DraftId); Assert.Null(foreign.LatestVersionId); Assert.Null(foreign.LatestVersion); Assert.Equal(0, foreign.VersionCount);
    }

    [Fact]
    public async Task Projection_refuses_same_definition_identity_across_privileged_scopes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        db.Definitions.AddRange(
            new WorkflowDefinition { Id = "shared", TenantId = "tenant-a" },
            new WorkflowDefinition { Id = "shared", TenantId = "tenant-b" });
        db.Drafts.AddRange(
            new WorkflowDefinitionDraft { Id = "draft-a", TenantId = "tenant-a", WorkflowDefinitionId = "shared", StateSource = "{}" },
            new WorkflowDefinitionDraft { Id = "draft-b", TenantId = "tenant-b", WorkflowDefinitionId = "shared", StateSource = "{}" });
        db.Versions.AddRange(
            new WorkflowDefinitionVersion("shared", "1.0.0") { Id = "version-a", TenantId = "tenant-a" },
            new WorkflowDefinitionVersion("shared", "2.0.0") { Id = "version-b", TenantId = "tenant-b" });
        await db.SaveChangesAsync();

        var access = new TestAccessor(
            PersistenceAccessContext.PrivilegedAcrossScopes(
                new PersistenceAccessPurpose("projection-scope-integrity")));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EfWorkflowDefinitionListProjectionStore(db, access)
                .ListByDefinitionIdsAsync(["shared"]));
    }

    [Fact]
    public async Task Projection_preserves_unique_definition_identities_across_privileged_scopes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        db.Definitions.AddRange(
            new WorkflowDefinition { Id = "definition-a", TenantId = "tenant-a" },
            new WorkflowDefinition { Id = "definition-b", TenantId = "tenant-b" });
        db.Drafts.AddRange(
            new WorkflowDefinitionDraft { Id = "draft-a", TenantId = "tenant-a", WorkflowDefinitionId = "definition-a", StateSource = "{}" },
            new WorkflowDefinitionDraft { Id = "draft-b", TenantId = "tenant-b", WorkflowDefinitionId = "definition-b", StateSource = "{}" });
        db.Versions.AddRange(
            new WorkflowDefinitionVersion("definition-a", "1.0.0") { Id = "version-a", TenantId = "tenant-a" },
            new WorkflowDefinitionVersion("definition-b", "2.0.0") { Id = "version-b", TenantId = "tenant-b" });
        await db.SaveChangesAsync();

        var access = new TestAccessor(
            PersistenceAccessContext.PrivilegedAcrossScopes(
                new PersistenceAccessPurpose("projection-scope-integrity")));
        var projections = await new EfWorkflowDefinitionListProjectionStore(db, access)
            .ListByDefinitionIdsAsync(["definition-a", "definition-b"]);

        Assert.Equal(["definition-a", "definition-b"], projections.Select(x => x.WorkflowDefinitionId));
        Assert.Equal(["version-a", "version-b"], projections.Select(x => x.LatestVersionId));
    }

    [Fact]
    public async Task Draft_current_and_list_use_last_modified_created_and_id_order()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        var timestamp = DateTimeOffset.UnixEpoch.AddDays(10);
        var serializer = new TestSerializer();
        db.Definitions.Add(new WorkflowDefinition { Id = "definition", TenantId = "tenant-a", Name = "Definition" });
        db.Drafts.AddRange(
            new WorkflowDefinitionDraft
            {
                Id = "draft-created-old",
                TenantId = "tenant-a",
                WorkflowDefinitionId = "definition",
                CreatedAt = timestamp.AddDays(-1),
                LastModifiedAt = timestamp,
                StateSource = serializer.Serialize(State())
            },
            new WorkflowDefinitionDraft
            {
                Id = "draft-created-new",
                TenantId = "tenant-a",
                WorkflowDefinitionId = "definition",
                CreatedAt = timestamp.AddDays(1),
                LastModifiedAt = timestamp,
                StateSource = serializer.Serialize(State())
            },
            new WorkflowDefinitionDraft
            {
                Id = "draft-created-new-z",
                TenantId = "tenant-a",
                WorkflowDefinitionId = "definition",
                CreatedAt = timestamp.AddDays(1),
                LastModifiedAt = timestamp,
                StateSource = serializer.Serialize(State())
            });
        await db.SaveChangesAsync();

        var store = new EfWorkflowDefinitionDraftStore(
            db,
            serializer,
            new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));

        var current = await store.FindByWorkflowDefinitionIdAsync("definition");
        var listed = await store.ListByWorkflowDefinitionIdAsync("definition");

        Assert.Equal("draft-created-new-z", current?.Id);
        Assert.Equal(["draft-created-new-z", "draft-created-new", "draft-created-old"], listed.Select(x => x.Id));
    }

    [Fact]
    public async Task Same_ids_and_operation_keys_are_isolated_by_tenant()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var a = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var b = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b")));
        var serializer = new TestSerializer(); var identities = new TestIdentity();
        var first = new EfDesignAtomicWriter(db, access: a);
        var second = new EfDesignAtomicWriter(db, access: b);
        await first.ExecuteAsync(new DesignOperationKey("same"), "test.op", new { Value = 1 }, ["test"], _ => Task.FromResult(new { Id = "a" }));
        var replay = await second.ExecuteAsync(new DesignOperationKey("same"), "test.op", new { Value = 1 }, ["test"], _ => Task.FromResult(new { Id = "b" }));
        Assert.Equal("b", replay.Id);
        var addA = new EfAddWorkflowDefinitionCommand(db, a, first, serializer, identities);
        var addB = new EfAddWorkflowDefinitionCommand(db, b, second, serializer, identities);
        await addA.Execute(new DesignOperationKey("add-a"), new WorkflowDefinition { Id = "shared", TenantId = "tenant-a", Name = "A" }, new WorkflowDefinitionDraft { Id = "draft-a", TenantId = "tenant-a", WorkflowDefinitionId = "shared", State = State() });
        await addB.Execute(new DesignOperationKey("add-b"), new WorkflowDefinition { Id = "shared", TenantId = "tenant-b", Name = "B" }, new WorkflowDefinitionDraft { Id = "draft-b", TenantId = "tenant-b", WorkflowDefinitionId = "shared", State = State() });
        Assert.Equal("A", (await new EfWorkflowDefinitionStore(db, a).GetAsync("shared")).Name);
        Assert.Equal("B", (await new EfWorkflowDefinitionStore(db, b).GetAsync("shared")).Name);
    }

    [Fact]
    public async Task Scope_less_mutations_are_rejected_and_corrupt_replays_fail_closed()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var global = new TestAccessor(PersistenceAccessContext.Global);
        var writer = new EfDesignAtomicWriter(db, access: global);
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.ExecuteAsync(new DesignOperationKey("global"), "test.op", new { Value = 1 }, ["test"], _ => Task.FromResult(new { Id = "x" })));

        var scoped = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var scopedWriter = new EfDesignAtomicWriter(db, access: scoped);
        await scopedWriter.ExecuteAsync(new DesignOperationKey("corrupt"), "test.op", new { Value = 1 }, ["test"], _ => Task.FromResult(new { Id = "x" }));
        var marker = await db.Operations.SingleAsync(x => x.OperationKey == "corrupt"); marker.ResultJson = "{\"Id\":\"tampered\"}"; await db.SaveChangesAsync();
        var corrupt = await Assert.ThrowsAsync<DesignPersistenceException>(() => scopedWriter.ExecuteAsync(new DesignOperationKey("corrupt"), "test.op", new { Value = 1 }, ["test"], _ => Task.FromResult(new { Id = "unused" })));
        Assert.Equal(DesignPersistenceFailureKind.Serialization, corrupt.FailureKind);
    }

    [Fact]
    public async Task Promotion_copies_layout_and_presentation_into_write_once_version_sibling()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var accessor = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var serializer = new TestSerializer(); var identities = new TestIdentity(); var writer = new EfDesignAtomicWriter(db, accessor);
        var definition = new WorkflowDefinition { Id = "definition", TenantId = "tenant-a", Name = "Definition" };
        var draft = new WorkflowDefinitionDraft { Id = "draft", TenantId = "tenant-a", WorkflowDefinitionId = definition.Id, State = State() };
        await new EfAddWorkflowDefinitionCommand(db, accessor, writer, serializer, identities).Execute(new DesignOperationKey("add"), definition, draft, [new DesignMetadataRecord("root", 3, 4)], [new ActivityPresentationRecord("root", "Root", "Description")]);
        var versionStore = new EfWorkflowDefinitionVersionStore(db, serializer, new EfWorkflowDefinitionStore(db, accessor), accessor);
        var versionId = await new EfPromoteDraftToVersionCommand(db, accessor, writer, serializer, identities, versionStore, new TestLockProvider()).Execute(new DesignOperationKey("promote"), draft.Id, "1.0.0");
        var layout = await new EfWorkflowDefinitionVersionLayoutStore(db, accessor).FindByVersionIdAsync(versionId);
        Assert.NotNull(layout); Assert.Single(layout!.Records);
        Assert.Single(layout.ActivityPresentation);
        Assert.NotEqual(default, layout.CreatedAt);
        Assert.NotEqual(default, layout.LastModifiedAt);
    }

    [Fact]
    public async Task Update_draft_upsert_stamps_and_serializes_a_missing_layout_sibling()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        var accessor = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var serializer = new TestSerializer();
        var definition = new WorkflowDefinition { Id = "definition", TenantId = "tenant-a", Name = "Definition" };
        var draft = new WorkflowDefinitionDraft
        {
            Id = "draft", TenantId = "tenant-a", WorkflowDefinitionId = definition.Id,
            State = State(), StateSource = serializer.Serialize(State()),
            CreatedAt = DateTimeOffset.UnixEpoch, LastModifiedAt = DateTimeOffset.UnixEpoch
        };
        db.Definitions.Add(definition);
        db.Drafts.Add(draft);
        await db.SaveChangesAsync();
        var writer = new EfDesignAtomicWriter(db, accessor);
        var root = new ActivityNode("root", "activity", [], []);

        await new EfUpdateDraftCommand(db, accessor, writer, serializer, new EmptyActivityStructureService(), new TestLockProvider())
            .Execute(new DesignOperationKey("upsert-layout"), new UpdateDraftRequest(
                draft.Id,
                new WorkflowDefinitionState([], root, [], [], null),
                [new DesignMetadataRecord("root", 1, 2)],
                [new ActivityPresentationRecord("root", "Root", "Description")]));

        db.ChangeTracker.Clear();
        var layout = await db.DraftLayouts.SingleAsync();
        Assert.NotEqual(default, layout.CreatedAt);
        Assert.NotEqual(default, layout.LastModifiedAt);
        Assert.Contains("\"nodeId\":\"root\"", layout.RecordsJson, StringComparison.Ordinal);
        Assert.Contains("\"displayName\":\"Root\"", layout.ActivityPresentationJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Promotion_acquires_draft_then_definition_and_rejects_semver_identity_conflicts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var accessor = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var serializer = new TestSerializer(); var identities = new TestIdentity(); var writer = new EfDesignAtomicWriter(db, accessor);
        var definition = new WorkflowDefinition { Id = "definition", TenantId = "tenant-a", Name = "Definition" };
        var draft = new WorkflowDefinitionDraft { Id = "draft", TenantId = "tenant-a", WorkflowDefinitionId = definition.Id, State = State() };
        await new EfAddWorkflowDefinitionCommand(db, accessor, writer, serializer, identities).Execute(new DesignOperationKey("add"), definition, draft);
        db.Versions.Add(new WorkflowDefinitionVersion(definition.Id, "1.0.0")
        {
            Id = "existing-version", TenantId = "tenant-a", State = State(), StateSource = serializer.Serialize(State()), CreatedAt = DateTimeOffset.UtcNow, LastModifiedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var locks = new RecordingLockProvider();
        var versionStore = new EfWorkflowDefinitionVersionStore(db, serializer, new EfWorkflowDefinitionStore(db, accessor), accessor);
        var command = new EfPromoteDraftToVersionCommand(db, accessor, writer, serializer, identities, versionStore, locks);

        var conflict = await Assert.ThrowsAsync<WorkflowDefinitionVersionConflictException>(() =>
            command.Execute(new DesignOperationKey("promote-conflict"), draft.Id, "1.0.0+build.7"));

        Assert.Equal("1.0.0+build.7", conflict.Version);
        Assert.Equal(
            [WorkflowDesignPersistenceLockKeys.DraftKey(draft.Id), WorkflowDesignPersistenceLockKeys.DefinitionKey(definition.Id)],
            locks.Acquired);
    }

    [Fact]
    public async Task Promotion_maps_reused_operation_key_to_promotion_operation_conflict()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var accessor = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var serializer = new TestSerializer(); var identities = new TestIdentity(); var writer = new EfDesignAtomicWriter(db, accessor);
        var definition = new WorkflowDefinition { Id = "definition", TenantId = "tenant-a", Name = "Definition" };
        var draft = new WorkflowDefinitionDraft { Id = "draft", TenantId = "tenant-a", WorkflowDefinitionId = definition.Id, State = State() };
        await new EfAddWorkflowDefinitionCommand(db, accessor, writer, serializer, identities).Execute(new DesignOperationKey("add"), definition, draft);
        var versionStore = new EfWorkflowDefinitionVersionStore(db, serializer, new EfWorkflowDefinitionStore(db, accessor), accessor);
        var command = new EfPromoteDraftToVersionCommand(db, accessor, writer, serializer, identities, versionStore, new TestLockProvider());
        await command.Execute(new DesignOperationKey("promote"), draft.Id, "1.0.0");

        await Assert.ThrowsAsync<WorkflowPromotionOperationConflictException>(() =>
            command.Execute(new DesignOperationKey("promote"), draft.Id, "2.0.0"));
    }

    [Fact]
    public async Task Submit_validates_the_complete_activity_tree_before_persisting()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var accessor = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var serializer = new TestSerializer(); var writer = new EfDesignAtomicWriter(db, accessor);
        var command = new EfSubmitWorkflowDefinitionCommand(db, accessor, writer, serializer, new TestIdentity(), new EmptyActivityStructureService());

        await Assert.ThrowsAsync<ArgumentException>(() => command.Execute(
            new DesignOperationKey("submit-invalid"), "Invalid", null, new WorkflowDefinitionState([], null, [], [], null)));

        Assert.Empty(await db.Definitions.ToListAsync());
    }

    [Fact]
    public async Task Create_draft_resolves_definition_identity_and_write_scope_tenant()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var accessor = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var serializer = new TestSerializer();
        db.Definitions.Add(new WorkflowDefinition { Id = "Stored-Definition", TenantId = "tenant-a", Name = "Definition" });
        await db.SaveChangesAsync();

        var events = new CapturingDeferredEventPublisher();
        var command = new EfCreateDraftCommand(db, accessor, new EfDesignAtomicWriter(db, accessor), new TestIdentity(), serializer, new TestLockProvider(), deferredEvents: events);
        var state = State() with { RootActivity = new ActivityNode("created-root", "activity", [], []) };
        var draftId = await command.Execute(new DesignOperationKey("create-canonical-draft"), "stored-definition", state);
        var draft = await db.Drafts.SingleAsync(x => x.Id == draftId);
        var created = Assert.Single(events.Events.OfType<DraftCreated>());
        var validated = Assert.Single(events.Events.OfType<DraftValidated>());

        Assert.Equal("Stored-Definition", draft.WorkflowDefinitionId);
        Assert.Equal("tenant-a", draft.TenantId);
        Assert.Equal("Stored-Definition", created.WorkflowDefinitionId);
        Assert.Equal("created-root", validated.Draft.State.RootActivity?.NodeId);
    }

    [Fact]
    public async Task Draft_validated_events_hydrate_state_for_create_clone_and_update()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        var accessor = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var serializer = new TestSerializer();
        var writer = new EfDesignAtomicWriter(db, accessor);
        db.Definitions.Add(new WorkflowDefinition { Id = "definition", TenantId = "tenant-a", Name = "Definition" });
        var cloneState = State() with { RootActivity = new ActivityNode("cloned-root", "activity", [], []) };
        db.Versions.Add(new WorkflowDefinitionVersion("definition", "1.0.0")
        {
            Id = "source-version",
            TenantId = "tenant-a",
            State = cloneState,
            StateSource = serializer.Serialize(cloneState)
        });
        await db.SaveChangesAsync();

        var events = new CapturingDeferredEventPublisher();
        var createdState = State() with { RootActivity = new ActivityNode("created-root", "activity", [], []) };
        var createdId = await new EfCreateDraftCommand(
            db, accessor, writer, new TestIdentity("created"), serializer, new TestLockProvider(), deferredEvents: events)
            .Execute(new DesignOperationKey("draft-event-create"), "definition", createdState);
        var clonedId = await new EfCloneDraftFromVersionCommand(
            db, accessor, writer, new TestIdentity("cloned"), serializer, new TestLockProvider(), deferredEvents: events)
            .Execute(new DesignOperationKey("draft-event-clone"), "source-version");
        var updatedState = State() with { RootActivity = new ActivityNode("updated-root", "activity", [], []) };
        await new EfUpdateDraftCommand(
                db, accessor, writer, serializer, new EmptyActivityStructureService(), new TestLockProvider(), deferredEvents: events)
            .Execute(new DesignOperationKey("draft-event-update"), new UpdateDraftRequest(createdId, updatedState, []));

        var validated = events.Events.OfType<DraftValidated>().ToArray();
        Assert.Equal(3, validated.Length);
        Assert.Contains(validated, @event => @event.Draft.Id == createdId && @event.Draft.State.RootActivity?.NodeId == "created-root");
        Assert.Equal("cloned-root", Assert.Single(validated, @event => @event.Draft.Id == clonedId).Draft.State.RootActivity?.NodeId);
        Assert.Contains(validated, @event => @event.Draft.Id == createdId && @event.Draft.State.RootActivity?.NodeId == "updated-root");
    }

    [Fact]
    public async Task Committed_draft_commands_publish_once_when_caller_cancels_after_commit()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        var accessor = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var serializer = new TestSerializer();
        db.Definitions.Add(new WorkflowDefinition { Id = "definition", TenantId = "tenant-a", Name = "Definition" });
        var cloneState = State() with { RootActivity = new ActivityNode("cloned-root", "activity", [], []) };
        db.Versions.Add(new WorkflowDefinitionVersion("definition", "1.0.0")
        {
            Id = "source-version",
            TenantId = "tenant-a",
            State = cloneState,
            StateSource = serializer.Serialize(cloneState)
        });
        await db.SaveChangesAsync();

        var events = new CapturingDeferredEventPublisher();
        var createdState = State() with { RootActivity = new ActivityNode("created-root", "activity", [], []) };
        using var createCancellation = new CancellationTokenSource();
        var createdId = await new EfCreateDraftCommand(
                db, accessor, new CancellingAfterCommitAtomicWriter(new EfDesignAtomicWriter(db, accessor), createCancellation),
                new TestIdentity("created"), serializer, new TestLockProvider(), deferredEvents: events)
            .Execute(new DesignOperationKey("cancel-after-create-commit"), "definition", createdState, ct: createCancellation.Token);

        using var cloneCancellation = new CancellationTokenSource();
        var clonedId = await new EfCloneDraftFromVersionCommand(
                db, accessor, new CancellingAfterCommitAtomicWriter(new EfDesignAtomicWriter(db, accessor), cloneCancellation),
                new TestIdentity("cloned"), serializer, new TestLockProvider(), deferredEvents: events)
            .Execute(new DesignOperationKey("cancel-after-clone-commit"), "source-version", cloneCancellation.Token);

        using var updateCancellation = new CancellationTokenSource();
        var updatedState = State() with { RootActivity = new ActivityNode("updated-root", "activity", [], []) };
        await new EfUpdateDraftCommand(
                db, accessor, new CancellingAfterCommitAtomicWriter(new EfDesignAtomicWriter(db, accessor), updateCancellation),
                serializer, new EmptyActivityStructureService(), new TestLockProvider(), deferredEvents: events)
            .Execute(new DesignOperationKey("cancel-after-update-commit"), new UpdateDraftRequest(createdId, updatedState, []), updateCancellation.Token);

        Assert.True(createCancellation.IsCancellationRequested);
        Assert.True(cloneCancellation.IsCancellationRequested);
        Assert.True(updateCancellation.IsCancellationRequested);
        Assert.Equal(2, events.Events.Count(@event => @event is DraftCreated));
        Assert.Equal(3, events.Events.Count(@event => @event is DraftValidated));
        Assert.Equal(1, events.Events.Count(@event => @event is DraftCreated created && created.DraftId == createdId));
        Assert.Equal(1, events.Events.Count(@event => @event is DraftCreated created && created.DraftId == clonedId));
        Assert.Equal(1, events.Events.Count(@event => @event is DraftValidated validated && validated.Draft.Id == createdId && validated.Draft.State.RootActivity?.NodeId == "created-root"));
        Assert.Equal(1, events.Events.Count(@event => @event is DraftValidated validated && validated.Draft.Id == createdId && validated.Draft.State.RootActivity?.NodeId == "updated-root"));
        Assert.Equal(1, events.Events.Count(@event => @event is DraftValidated validated && validated.Draft.Id == clonedId && validated.Draft.State.RootActivity?.NodeId == "cloned-root"));
    }

    [Fact]
    public async Task Submit_creates_the_normalized_draft_layout_sibling_atomically()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var accessor = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var result = await new EfSubmitWorkflowDefinitionCommand(
            db, accessor, new EfDesignAtomicWriter(db, accessor), new TestSerializer(), new TestIdentity(), new EmptyActivityStructureService())
            .Execute(new DesignOperationKey("submit-layout"), "Definition", null,
                State() with { RootActivity = new ActivityNode("root", "activity", [], []) });

        var layout = await db.DraftLayouts.SingleAsync(x => x.WorkflowDefinitionDraftId == result.DraftId);
        Assert.Equal("tenant-a", layout.TenantId);
        Assert.Empty(layout.Records);
        Assert.Empty(layout.ActivityPresentation);
    }

    [Fact]
    public async Task Version_draft_and_version_layout_immutable_source_fields_reject_after_save_changes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        db.Definitions.Add(new WorkflowDefinition { Id = "definition", TenantId = "tenant-a", Name = "Definition" });
        var sourceCreatedAt = DateTimeOffset.UnixEpoch;
        var version = new WorkflowDefinitionVersion("definition", "1.0.0", "{}", sourceCreatedAt)
        {
            Id = "version", TenantId = "tenant-a", SourceDraftId = "draft"
        };
        var layout = new WorkflowDefinitionVersionLayout
        {
            Id = "layout", TenantId = "tenant-a", WorkflowDefinitionVersionId = version.Id,
            Records = [new DesignMetadataRecord("root", 1, 2)]
        };
        db.Versions.Add(version); layout.RecordsJson = "[{\"nodeId\":\"root\",\"x\":1,\"y\":2,\"width\":null,\"height\":null,\"additionalProperties\":null}]"; layout.ActivityPresentationJson = "[]"; db.VersionLayouts.Add(layout);
        var draft = new WorkflowDefinitionDraft
        {
            Id = "provenance-draft",
            TenantId = "tenant-a",
            WorkflowDefinitionId = "definition",
            SourceVersionId = "source-version",
            StateSource = "{}"
        };
        db.Drafts.Add(draft);
        await db.SaveChangesAsync();

        version.StateSource = "{\"changed\":true}";
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        db.ChangeTracker.Clear();
        var loadedVersion = await db.Versions.SingleAsync();
        db.Entry(loadedVersion).Property(x => x.SemVerSortKey).CurrentValue = "changed";
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        db.ChangeTracker.Clear();
        var loadedDraft = await db.Drafts.SingleAsync();
        loadedDraft.SourceVersionId = "rewritten-source-version";
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        db.ChangeTracker.Clear();
        Assert.Equal("source-version", (await db.Drafts.AsNoTracking().SingleAsync()).SourceVersionId);
        var loadedLayout = await db.VersionLayouts.SingleAsync();
        loadedLayout.RecordsJson = "[]";
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    [Fact]
    public async Task Update_draft_prunes_presentation_for_unreachable_activity_nodes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var accessor = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var serializer = new TestSerializer(); var identities = new TestIdentity(); var writer = new EfDesignAtomicWriter(db, accessor);
        var definition = new WorkflowDefinition { Id = "definition", TenantId = "tenant-a", Name = "Definition" };
        var draft = new WorkflowDefinitionDraft { Id = "draft", TenantId = "tenant-a", WorkflowDefinitionId = definition.Id, State = State() };
        await new EfAddWorkflowDefinitionCommand(db, accessor, writer, serializer, identities).Execute(new DesignOperationKey("add"), definition, draft);
        var root = new ActivityNode("root", "activity", [], []);
        var command = new EfUpdateDraftCommand(db, accessor, writer, serializer, new EmptyActivityStructureService(), new TestLockProvider());

        await command.Execute(new DesignOperationKey("update"), new UpdateDraftRequest(
            draft.Id,
            new WorkflowDefinitionState([], root, [], [], null),
            [],
            [new ActivityPresentationRecord("root", "Root", null), new ActivityPresentationRecord("ghost", "Ghost", null)]));

        var loaded = await new EfWorkflowDefinitionDraftStore(db, serializer, accessor).FindWithLayoutByIdAsync(draft.Id);
        Assert.Equal("root", Assert.Single(loaded!.ActivityPresentation).NodeId);
    }

    [Fact]
    public async Task Operation_fingerprints_cover_workflow_metadata_and_provenance_fields()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var accessor = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var serializer = new TestSerializer(); var identities = new TestIdentity(); var writer = new EfDesignAtomicWriter(db, accessor);

        var add = new EfAddWorkflowDefinitionCommand(db, accessor, writer, serializer, identities);
        async Task AssertAddConflictAsync(string suffix, WorkflowDefinition changedDefinition, WorkflowDefinitionDraft changedDraft)
        {
            var originalDefinition = new WorkflowDefinition { Id = $"definition-{suffix}", TenantId = "tenant-a", Name = "Definition" };
            var originalDraft = new WorkflowDefinitionDraft { Id = $"draft-{suffix}", TenantId = "tenant-a", WorkflowDefinitionId = originalDefinition.Id, State = State() };
            changedDefinition.Id = originalDefinition.Id;
            changedDraft.Id = originalDraft.Id;
            changedDraft.WorkflowDefinitionId = originalDefinition.Id;
            await add.Execute(new DesignOperationKey($"add-{suffix}"), originalDefinition, originalDraft);
            await Assert.ThrowsAsync<InvalidOperationException>(() => add.Execute(new DesignOperationKey($"add-{suffix}"), changedDefinition, changedDraft));
        }

        await AssertAddConflictAsync("deleted-at", new WorkflowDefinition { TenantId = "tenant-a", Name = "Definition", DeletedAt = DateTimeOffset.UnixEpoch }, new WorkflowDefinitionDraft { TenantId = "tenant-a", State = State() });
        await AssertAddConflictAsync("deleted-reason", new WorkflowDefinition { TenantId = "tenant-a", Name = "Definition", DeletedReason = "source changed" }, new WorkflowDefinitionDraft { TenantId = "tenant-a", State = State() });
        await AssertAddConflictAsync("source-owned", new WorkflowDefinition { TenantId = "tenant-a", Name = "Definition", IsSourceOwned = true }, new WorkflowDefinitionDraft { TenantId = "tenant-a", State = State() });
        await AssertAddConflictAsync("source-version", new WorkflowDefinition { TenantId = "tenant-a", Name = "Definition" }, new WorkflowDefinitionDraft { TenantId = "tenant-a", SourceVersionId = "source-version", State = State() });

        var materializeDefinition = new EfMaterializeWorkflowDefinitionCommand(db, accessor, writer);
        var materializedDefinition = new WorkflowDefinition { Id = "materialized-definition", TenantId = "tenant-a", Name = "Definition", DeletedReason = "one" };
        await materializeDefinition.Execute(new DesignOperationKey("materialize-definition"), materializedDefinition);
        await Assert.ThrowsAsync<InvalidOperationException>(() => materializeDefinition.Execute(new DesignOperationKey("materialize-definition"), new WorkflowDefinition { Id = materializedDefinition.Id, TenantId = "tenant-a", Name = "Definition", DeletedReason = "two" }));

        var materializeVersion = new EfMaterializeWorkflowDefinitionVersionCommand(db, accessor, writer, serializer);
        var firstVersion = new WorkflowDefinitionVersion("definition-deleted-at", "2.0.0") { Id = "version-1", TenantId = "tenant-a", State = State(), SourceDraftId = "draft-a", SourceCreatedAt = DateTimeOffset.UnixEpoch };
        await materializeVersion.Execute(new DesignOperationKey("materialize-version-draft"), firstVersion);
        await Assert.ThrowsAsync<InvalidOperationException>(() => materializeVersion.Execute(new DesignOperationKey("materialize-version-draft"), new WorkflowDefinitionVersion("definition-deleted-at", "2.0.0") { Id = firstVersion.Id, TenantId = "tenant-a", State = State(), SourceDraftId = "draft-b", SourceCreatedAt = DateTimeOffset.UnixEpoch }));
        await materializeVersion.Execute(new DesignOperationKey("materialize-version-created"), new WorkflowDefinitionVersion("definition-deleted-at", "3.0.0") { Id = "version-2", TenantId = "tenant-a", State = State(), SourceDraftId = "draft-a", SourceCreatedAt = DateTimeOffset.UnixEpoch });
        await Assert.ThrowsAsync<InvalidOperationException>(() => materializeVersion.Execute(new DesignOperationKey("materialize-version-created"), new WorkflowDefinitionVersion("definition-deleted-at", "3.0.0") { Id = "version-2", TenantId = "tenant-a", State = State(), SourceDraftId = "draft-a", SourceCreatedAt = DateTimeOffset.UnixEpoch.AddDays(1) }));
    }

    [Fact]
    public async Task Operation_request_material_is_portable_for_layout_and_promotion()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var interceptor = new TransientSaveInterceptor();
        var options = new DbContextOptionsBuilder<WorkflowsDesignSqliteDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;
        await using var db = new WorkflowsDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var accessor = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var serializer = new TestSerializer(new JsonSerializerOptions { PropertyNamingPolicy = null });
        var identities = new TestIdentity();
        var writer = new EfDesignAtomicWriter(db, accessor);
        var state = State();
        var definition = new WorkflowDefinition
        {
            Id = "definition-parity",
            TenantId = "tenant-a",
            Name = "Parity",
            Description = "Description",
            DeletedAt = DateTimeOffset.UnixEpoch,
            DeletedReason = "source",
            IsSourceOwned = true
        };
        var draft = new WorkflowDefinitionDraft
        {
            Id = "draft-parity",
            TenantId = "tenant-a",
            WorkflowDefinitionId = definition.Id,
            SourceVersionId = "source-version",
            State = state
        };
        var layout = new DesignMetadataRecord("root", 1, 2, 3, 4);
        var presentation = new ActivityPresentationRecord("root", "Root", "Description");

        await new EfAddWorkflowDefinitionCommand(db, accessor, writer, serializer, identities)
            .Execute(new DesignOperationKey("parity-create"), definition, draft, [layout], [presentation]);

        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var expectedCreateJson = JsonSerializer.Serialize(new
        {
            name = definition.Name,
            description = definition.Description,
            deletedAt = definition.DeletedAt,
            deletedReason = definition.DeletedReason,
            isSourceOwned = definition.IsSourceOwned,
            sourceVersionId = draft.SourceVersionId,
            stateJson = serializer.Serialize(state),
            layout = new[] { new { nodeId = "root", x = 1d, y = 2d, width = 3d, height = 4d, additionalPropertiesJson = (string?)null } },
            activityPresentation = new[] { new { nodeId = "root", displayName = "Root", description = "Description" } }
        }, json);
        var createMarker = await db.Operations.SingleAsync(x => x.OperationKey == "parity-create");
        Assert.Equal(PortableFingerprint("workflow.definition.create.v1", expectedCreateJson), createMarker.RequestFingerprint);

        var versionStore = new EfWorkflowDefinitionVersionStore(db, serializer, new EfWorkflowDefinitionStore(db, accessor), accessor);
        await new EfPromoteDraftToVersionCommand(db, accessor, writer, serializer, identities, versionStore, new TestLockProvider())
            .Execute(new DesignOperationKey("parity-promote"), draft.Id, "1.0.0");
        var expectedPromotionJson = JsonSerializer.Serialize(new
        {
            draftId = draft.Id,
            assignmentMode = "exact",
            requestedVersion = "1.0.0"
        }, json);
        var promotionMarker = await db.Operations.SingleAsync(x => x.OperationKey == "parity-promote");
        Assert.Equal(PortableFingerprint("workflow.draft.promote.v1", expectedPromotionJson), promotionMarker.RequestFingerprint);
    }

    [Fact]
    public async Task Failed_stage_does_not_leak_tracked_rows_into_the_next_operation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var accessor = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var writer = new EfDesignAtomicWriter(db, accessor);
        await Assert.ThrowsAsync<InvalidDataException>(() => writer.ExecuteAsync<object>(new DesignOperationKey("failed"), "test.op", new { Value = 1 }, ["test"], _ =>
        {
            db.Definitions.Add(new WorkflowDefinition { Id = "leaked", TenantId = "tenant-a", Name = "Should not persist" });
            throw new InvalidDataException("stage failed");
        }));
        Assert.Empty(db.ChangeTracker.Entries());
        await writer.ExecuteAsync(new DesignOperationKey("next"), "test.op", new { Value = 2 }, ["test"], _ => Task.FromResult(new { Id = "next" }));
        Assert.Null(await db.Definitions.SingleOrDefaultAsync(x => x.Id == "leaked"));
    }

    [Fact]
    public async Task Direct_atomic_interface_returns_conflict_and_rejects_empty_mutation_units()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IDesignAtomicWriter writer = new EfDesignAtomicWriter(db, access);
        var key = new DesignOperationKey("direct");
        await writer.ExecuteAsync(key, "test.op", new { Value = 1 }, ["test"],
            (_, _) => Task.FromResult(DesignAtomicWriteStage<int>.Accepted(1)));
        var conflict = await writer.ExecuteAsync(key, "test.op", new { Value = 2 }, ["test"],
            (_, _) => Task.FromResult(DesignAtomicWriteStage<int>.Accepted(2)));
        Assert.Equal(DesignAtomicWriteStatus.Conflict, conflict.Status);
        await Assert.ThrowsAsync<ArgumentException>(() => writer.ExecuteAsync(
            new DesignOperationKey("empty"), "test.op", new { Value = 1 }, [],
            (_, _) => Task.FromResult(DesignAtomicWriteStage<int>.Accepted(1))));
    }

    [Fact]
    public async Task Version_add_and_submit_throw_on_reused_operation_key_conflicts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        var accessor = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var serializer = new TestSerializer();
        var writer = new EfDesignAtomicWriter(db, accessor);
        db.Definitions.Add(new WorkflowDefinition { Id = "definition", TenantId = "tenant-a", Name = "Definition" });
        await db.SaveChangesAsync();

        var versionCommand = new EfAddWorkflowDefinitionVersionCommand(
            db, accessor, writer, serializer, new TestIdentity("version"), new TestLockProvider());
        await versionCommand.Execute(new DesignOperationKey("version-conflict"), "definition", State());
        var changedState = State() with { RootActivity = new ActivityNode("changed-root", "activity", [], []) };
        var versionConflict = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            versionCommand.Execute(new DesignOperationKey("version-conflict"), "definition", changedState));
        Assert.Contains("version-conflict", versionConflict.Message, StringComparison.Ordinal);

        var submitState = State() with { RootActivity = new ActivityNode("submit-root", "activity", [], []) };
        var submitCommand = new EfSubmitWorkflowDefinitionCommand(
            db, accessor, writer, serializer, new TestIdentity("submit"), new EmptyActivityStructureService());
        await submitCommand.Execute(new DesignOperationKey("submit-conflict"), "Submitted", null, submitState);
        var submitConflict = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            submitCommand.Execute(new DesignOperationKey("submit-conflict"), "Changed", null, submitState));
        Assert.Contains("submit-conflict", submitConflict.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Replay_and_conflict_are_resolved_before_before_attempt_work()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IDesignAtomicWriter writer = new EfDesignAtomicWriter(db, access);
        var key = new DesignOperationKey("preflight-order");
        await writer.ExecuteAsync(key, "test.op", new { Value = 1 }, ["test"],
            (_, _) => Task.FromResult(DesignAtomicWriteStage<int>.Accepted(1)));
        var beforeAttemptCalls = 0;

        var replay = await writer.ExecuteAsync<int>(key, "test.op", new { Value = 1 }, ["test"],
            (_, _) => throw new InvalidOperationException("stage must not run"),
            _ =>
            {
                beforeAttemptCalls++;
                return Task.CompletedTask;
            });
        var conflict = await writer.ExecuteAsync<int>(key, "test.op", new { Value = 2 }, ["test"],
            (_, _) => throw new InvalidOperationException("stage must not run"),
            _ =>
            {
                beforeAttemptCalls++;
                return Task.CompletedTask;
            });

        Assert.Equal(DesignAtomicWriteStatus.Replayed, replay.Status);
        Assert.Equal(DesignAtomicWriteStatus.Conflict, conflict.Status);
        Assert.Equal(0, beforeAttemptCalls);
    }

    [Fact]
    public async Task Ef_rejects_authoritative_result_that_differs_from_staged_value()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var writer = new EfDesignAtomicWriter(db, access);
        var suppliedJson = JsonSerializer.Serialize(new ResultValue("supplied"));
        var invalid = await Assert.ThrowsAsync<DesignPersistenceException>(() => writer.ExecuteAsync(
            new DesignOperationKey("mismatch"), "test.op", new { Value = 1 }, ["test"],
            (_, _) => Task.FromResult(DesignAtomicWriteStage<ResultValue>.Accepted(
                new ResultValue("staged"), "sha256:invalid", suppliedJson))));
        Assert.Equal(DesignPersistenceFailureKind.Serialization, invalid.FailureKind);
        Assert.Empty(await db.Operations.ToListAsync());
    }

    [Fact]
    public async Task Ef_honors_custom_result_codec_for_authoritative_validation_and_replay()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IDesignAtomicWriter writer = new EfDesignAtomicWriter(db, access);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        var value = new CustomResult(CustomResultStatus.Ready);
        var json = JsonSerializer.Serialize(value, options);
        var fingerprint = PortableFingerprint("test.op.result", json);
        var codec = new CustomResultCodec(options);

        var committed = await writer.ExecuteAsync(
            new DesignOperationKey("custom-codec"), "test.op", new { Value = 1 }, ["test"],
            (_, _) => Task.FromResult(DesignAtomicWriteStage<CustomResult>.Accepted(value, fingerprint, json)),
            resultCodec: codec);
        var replayed = await writer.ExecuteAsync<CustomResult>(
            new DesignOperationKey("custom-codec"), "test.op", new { Value = 1 }, ["test"],
            (_, _) => throw new InvalidOperationException("replay must not restage"),
            resultCodec: codec);

        Assert.Equal(DesignAtomicWriteStatus.Committed, committed.Status);
        Assert.Equal(DesignAtomicWriteStatus.Replayed, replayed.Status);
        Assert.Equal(value, committed.Value);
        Assert.Equal(value, replayed.Value);
    }

    [Fact]
    public async Task Ef_reconciles_a_commit_acknowledgement_failure_without_rerunning_stage()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var interceptor = new AcknowledgementLostInterceptor();
        var options = new DbContextOptionsBuilder<WorkflowsDesignSqliteDbContext>()
            .UseSqlite(connection).AddInterceptors(interceptor).Options;
        await using var db = new WorkflowsDesignSqliteDbContext(options); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IDesignAtomicWriter writer = new EfDesignAtomicWriter(db, access, reconciliationTimeout: TimeSpan.FromMilliseconds(250));
        var stageCalls = 0;
        interceptor.FailNextCommit = true;

        var result = await writer.ExecuteAsync(
            new DesignOperationKey("ack-lost"), "test.op", new { Value = 1 }, ["test"],
            (_, _) =>
            {
                stageCalls++;
                return Task.FromResult(DesignAtomicWriteStage<ResultValue>.Accepted(new ResultValue("durable")));
            });

        Assert.Equal(DesignAtomicWriteStatus.Reconciled, result.Status);
        Assert.Equal(1, stageCalls);
        Assert.Single(await db.Operations.ToListAsync());
    }

    [Fact]
    public async Task Ef_maps_reconciliation_timeout_after_provider_read_failures_to_unknown_outcome()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var state = new FailureState();
        var options = new DbContextOptionsBuilder<WorkflowsDesignSqliteDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new CommitAcknowledgementFailureInterceptor(state), new ReadFailureInterceptor(state))
            .Options;
        await using var db = new WorkflowsDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var writer = new EfDesignAtomicWriter(db, access, reconciliationTimeout: TimeSpan.FromMilliseconds(100));
        state.FailNextCommit = true;

        var exception = await Assert.ThrowsAsync<DesignAtomicWriteUnknownOutcomeException>(() => writer.ExecuteAsync(
            new DesignOperationKey("ack-lost-read-failure"),
            "test.op",
            new { Value = 1 },
            ["test"],
            (_, _) => Task.FromResult(DesignAtomicWriteStage<int>.Accepted(1))));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public async Task Ef_preserves_caller_cancellation_during_commit_acknowledgement()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var callerCancellation = new CancellationTokenSource();
        var interceptor = new CallerCancellationCommitInterceptor(callerCancellation);
        var options = new DbContextOptionsBuilder<WorkflowsDesignSqliteDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;
        await using var db = new WorkflowsDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        interceptor.Arm();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var writer = new EfDesignAtomicWriter(db, access, reconciliationTimeout: TimeSpan.FromMilliseconds(100));

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.ExecuteAsync(
            new DesignOperationKey("caller-cancelled-commit"),
            "test.op",
            new { Value = 1 },
            ["test"],
            (_, _) => Task.FromResult(DesignAtomicWriteStage<int>.Accepted(1)),
            cancellationToken: callerCancellation.Token));

        Assert.NotEqual(typeof(DesignAtomicWriteUnknownOutcomeException), exception.GetType());
        Assert.True(callerCancellation.IsCancellationRequested);
    }

    [Fact]
    public async Task Ef_permanent_delete_requires_publication_guard_invokes_all_guards_and_declares_cascade_units()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var inner = new EfDesignAtomicWriter(db, access);
        var atomic = new CapturingAtomicWriter(inner);
        var publication = new PermittingPublicationGuard();
        var other = new RecordingDeletionGuard();
        db.Definitions.Add(new WorkflowDefinition { Id = "definition-delete", TenantId = "tenant-a", Name = "Delete", DeletedAt = DateTimeOffset.UnixEpoch });
        await db.SaveChangesAsync();

        await new EfDeleteWorkflowDefinitionPermanentlyCommand(db, access, atomic, [publication, other])
            .Execute(new DesignOperationKey("delete-all-units"), "definition-delete");

        Assert.Equal("definition-delete", publication.SeenDefinitionId);
        Assert.Equal("definition-delete", other.SeenDefinitionId);
        Assert.Contains(DesignPersistenceUnitNames.VersionLayouts, atomic.MutatedUnits);
        Assert.Equal(
            [DesignPersistenceUnitNames.Definitions, DesignPersistenceUnitNames.Drafts, DesignPersistenceUnitNames.Versions, DesignPersistenceUnitNames.DraftLayouts, DesignPersistenceUnitNames.VersionLayouts],
            atomic.MutatedUnits);
    }

    [Fact]
    public async Task Ef_permanent_delete_refuses_a_composition_without_publication_guard()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var atomic = new CapturingAtomicWriter(new EfDesignAtomicWriter(db, access));

        await Assert.ThrowsAsync<PermanentDeletionUnavailableException>(() =>
            new EfDeleteWorkflowDefinitionPermanentlyCommand(
                    db,
                    access,
                    atomic,
                    [new RecordingDeletionGuard()])
                .Execute(new DesignOperationKey("delete-without-publication-guard"), "missing"));

        Assert.Equal(
            [DesignPersistenceUnitNames.Definitions, DesignPersistenceUnitNames.Drafts, DesignPersistenceUnitNames.Versions, DesignPersistenceUnitNames.DraftLayouts, DesignPersistenceUnitNames.VersionLayouts],
            atomic.MutatedUnits);
    }

    [Fact]
    public async Task Ef_maps_only_unique_provider_failures_during_promotion_to_version_conflict()
    {
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var unique = new DbUpdateException("unique", new SqliteException(
            "UNIQUE constraint failed: elsa_workflow_definition_versions.TenantId, elsa_workflow_definition_versions.DefinitionIdLookupHash, elsa_workflow_definition_versions.SemVerSortKey",
            19,
            1555));
        var writer = new ThrowingAtomicWriter(new DesignPersistenceException(
            DesignPersistenceDomain.Workflow,
            DesignPersistenceFailureKind.Provider,
            "workflow.draft.promote.v1",
            null,
            unique.InnerException!));
        var command = new EfPromoteDraftToVersionCommand(
            null!, access, writer, new TestSerializer(), new TestIdentity(), null!, new TestLockProvider());

        var exception = await Assert.ThrowsAsync<WorkflowDefinitionVersionConflictException>(() => command.Execute(
            new DesignOperationKey("promotion-unique-race"), "draft-1", "1.0.0"));

        Assert.Equal("draft-1", exception.DefinitionId);
    }

    [Fact]
    public async Task Ef_preserves_unrelated_unique_provider_failures_during_promotion()
    {
        var providerFailure = new DesignPersistenceException(
            DesignPersistenceDomain.Workflow,
            DesignPersistenceFailureKind.Provider,
            "workflow.draft.promote.v1",
            null,
            new SqliteException("UNIQUE constraint failed: elsa_workflow_definition_versions.TenantId, elsa_workflow_definition_versions.IdLookupHash", 19, 1555));
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var command = new EfPromoteDraftToVersionCommand(
            null!, access, new ThrowingAtomicWriter(providerFailure), new TestSerializer(), new TestIdentity(), null!, new TestLockProvider());

        var exception = await Assert.ThrowsAsync<DesignPersistenceException>(() => command.Execute(
            new DesignOperationKey("promotion-generated-id-race"), "draft-1", "1.0.0"));

        Assert.Same(providerFailure, exception);
    }

    [Fact]
    public async Task Ef_preserves_generated_id_unique_provider_failures_during_version_add()
    {
        var providerFailure = new DesignPersistenceException(
            DesignPersistenceDomain.Workflow,
            DesignPersistenceFailureKind.Provider,
            "workflow.version.add.v1",
            null,
            new SqliteException("UNIQUE constraint failed: elsa_workflow_definition_versions.TenantId, elsa_workflow_definition_versions.IdLookupHash", 19, 1555));
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var command = new EfAddWorkflowDefinitionVersionCommand(
            null!, access, new ThrowingAtomicWriter(providerFailure), new TestSerializer(), new TestIdentity(), new TestLockProvider());

        var exception = await Assert.ThrowsAsync<DesignPersistenceException>(() => command.Execute(
            new DesignOperationKey("version-generated-id-race"), "definition-1", State()));

        Assert.Same(providerFailure, exception);
    }

    [Fact]
    public async Task Ef_does_not_map_unrelated_provider_failures_during_promotion_to_version_conflict()
    {
        var providerFailure = new DesignPersistenceException(
            DesignPersistenceDomain.Workflow,
            DesignPersistenceFailureKind.Provider,
            "workflow.draft.promote.v1",
            null,
            new InvalidOperationException("provider unavailable"));
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var command = new EfPromoteDraftToVersionCommand(
            null!, access, new ThrowingAtomicWriter(providerFailure), new TestSerializer(), new TestIdentity(), null!, new TestLockProvider());

        var exception = await Assert.ThrowsAsync<DesignPersistenceException>(() => command.Execute(
            new DesignOperationKey("promotion-provider-failure"), "draft-1", "1.0.0"));

        Assert.Same(providerFailure, exception);
    }

    [Fact]
    public async Task Ef_retries_transient_writes_after_rerunning_attempt_setup()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var interceptor = new TransientSaveInterceptor();
        var options = new DbContextOptionsBuilder<WorkflowsDesignSqliteDbContext>()
            .UseSqlite(connection).AddInterceptors(interceptor).Options;
        await using var db = new WorkflowsDesignSqliteDbContext(options); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IDesignAtomicWriter writer = new EfDesignAtomicWriter(db, access);
        var beforeAttemptCalls = 0;
        var stageCalls = 0;
        interceptor.FailNextSave = true;

        var result = await writer.ExecuteAsync(
            new DesignOperationKey("transient-retry"), "test.op", new { Value = 1 }, ["test"],
            (_, _) =>
            {
                stageCalls++;
                return Task.FromResult(DesignAtomicWriteStage<int>.Accepted(1));
            },
            _ =>
            {
                beforeAttemptCalls++;
                return Task.CompletedTask;
            });

        Assert.Equal(DesignAtomicWriteStatus.Committed, result.Status);
        Assert.Equal(2, beforeAttemptCalls);
        Assert.Equal(2, stageCalls);
    }

    [Fact]
    public async Task Ef_fails_a_transient_write_at_once_inside_a_caller_owned_shared_transaction()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var interceptor = new TransientSaveInterceptor();
        var options = new DbContextOptionsBuilder<WorkflowsDesignSqliteDbContext>()
            .UseSqlite(connection).AddInterceptors(interceptor).Options;
        await using (var schema = new WorkflowsDesignSqliteDbContext(options))
            await schema.Database.EnsureCreatedAsync();
        await using var configured = new WorkflowsDesignSqliteDbContext(options);
        await using var shared = await Elsa.Persistence.EntityFramework.EfSharedTransaction.BeginAsync([configured]);
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IDesignAtomicWriter writer = new EfDesignAtomicWriter(
            shared.Context<WorkflowsDesignSqliteDbContext>(), access, transactionFactory: shared.BeginOperationAsync);
        var stageCalls = 0;
        interceptor.FailNextSave = true;

        var failure = await Assert.ThrowsAsync<DesignPersistenceException>(() => writer.ExecuteAsync(
            new DesignOperationKey("transient-in-shared-transaction"), "test.op", new { Value = 1 }, ["test"],
            (_, _) =>
            {
                stageCalls++;
                return Task.FromResult(DesignAtomicWriteStage<int>.Accepted(1));
            }));

        Assert.Equal(DesignPersistenceFailureKind.Provider, failure.FailureKind);
        Assert.Equal(5, Assert.IsType<SqliteException>(failure.InnerException).SqliteErrorCode);
        Assert.Equal(1, stageCalls);
        Assert.True(shared.IsRollbackOnly);
    }

    [Fact]
    public async Task Ef_retries_a_transient_write_the_provider_execution_strategy_wrapped()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var saves = FailingSaveInterceptor.WrappedDeadlock(failures: 1);
        var options = new DbContextOptionsBuilder<WorkflowsDesignSqliteDbContext>()
            .UseSqlite(connection).AddInterceptors(saves).Options;
        await using var db = new WorkflowsDesignSqliteDbContext(options); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IDesignAtomicWriter writer = new EfDesignAtomicWriter(db, access);
        var stageCalls = 0;

        var result = await writer.ExecuteAsync(
            new DesignOperationKey("wrapped-transient-retry"), "test.op", new { Value = 1 }, ["test"],
            (_, _) =>
            {
                stageCalls++;
                return Task.FromResult(DesignAtomicWriteStage<int>.Accepted(1));
            });

        Assert.Equal(DesignAtomicWriteStatus.Committed, result.Status);
        Assert.Equal(2, stageCalls);
        Assert.Equal(2, saves.Attempts);
    }

    [Fact]
    public async Task Ef_fails_a_wrapped_transient_write_at_once_inside_a_caller_owned_shared_transaction()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var saves = FailingSaveInterceptor.WrappedDeadlock();
        var options = new DbContextOptionsBuilder<WorkflowsDesignSqliteDbContext>()
            .UseSqlite(connection).AddInterceptors(saves).Options;
        await using (var schema = new WorkflowsDesignSqliteDbContext(options))
            await schema.Database.EnsureCreatedAsync();
        await using var configured = new WorkflowsDesignSqliteDbContext(options);
        await using var shared = await Elsa.Persistence.EntityFramework.EfSharedTransaction.BeginAsync([configured]);
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IDesignAtomicWriter writer = new EfDesignAtomicWriter(
            shared.Context<WorkflowsDesignSqliteDbContext>(), access, transactionFactory: shared.BeginOperationAsync);

        var failure = await Assert.ThrowsAsync<DesignPersistenceException>(() => writer.ExecuteAsync(
            new DesignOperationKey("wrapped-transient-in-shared-transaction"), "test.op", new { Value = 1 }, ["test"],
            (_, _) => Task.FromResult(DesignAtomicWriteStage<int>.Accepted(1))));

        Assert.Equal(DesignPersistenceFailureKind.Provider, failure.FailureKind);
        Assert.True(Elsa.Persistence.EntityFramework.EfRelationalExceptionClassifier.IsTransientWriteConflict(failure));
        Assert.Equal(1, saves.Attempts);
        Assert.True(shared.IsRollbackOnly);
    }

    [Fact]
    public async Task Ef_fails_a_wrapped_provider_failure_that_is_not_a_transient_conflict_without_a_retry()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var saves = FailingSaveInterceptor.WrappedProviderFailure();
        var options = new DbContextOptionsBuilder<WorkflowsDesignSqliteDbContext>()
            .UseSqlite(connection).AddInterceptors(saves).Options;
        await using var db = new WorkflowsDesignSqliteDbContext(options); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IDesignAtomicWriter writer = new EfDesignAtomicWriter(db, access);

        // Before #1814 this asserted InvalidOperationException, which was the execution strategy's own wrapper
        // escaping the store untouched. The writer now normalizes it like any other provider failure, so the
        // assertion moves to the store's exception; that the failure is not retried is what this test pins.
        var failure = await Assert.ThrowsAsync<DesignPersistenceException>(() => writer.ExecuteAsync(
            new DesignOperationKey("wrapped-provider-failure"), "test.op", new { Value = 1 }, ["test"],
            (_, _) => Task.FromResult(DesignAtomicWriteStage<int>.Accepted(1))));

        Assert.IsType<DbUpdateException>(failure.InnerException);

        Assert.Equal(1, saves.Attempts);
    }

    [Fact]
    public async Task Ef_version_allocation_recomputes_latest_version_after_a_transient_retry()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        db.Definitions.Add(new WorkflowDefinition { Id = "definition", TenantId = "tenant-a", Name = "Definition" });
        db.Versions.Add(new WorkflowDefinitionVersion("definition", "1.0.0", "{}")
        {
            Id = "existing-version",
            TenantId = "tenant-a"
        });
        await db.SaveChangesAsync();

        var command = new EfAddWorkflowDefinitionVersionCommand(
            db,
            access,
            new RetryingVersionAllocationAtomicWriter(db),
            new TestSerializer(),
            new TestIdentity("allocated"),
            new TestLockProvider());
        var added = await command.Execute(new DesignOperationKey("version-retry-state-change"), "definition", State());
        await db.SaveChangesAsync();

        Assert.Equal("3.0.0", added.Version);
        Assert.Equal(["1.0.0", "2.0.0", "3.0.0"], (await db.Versions.AsNoTracking().OrderBy(x => x.SemVerSortKey).Select(x => x.Version).ToListAsync()));
    }

    [Fact]
    public async Task Ef_clone_releases_the_previous_generated_draft_lock_before_retrying_stage()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var interceptor = new TransientSaveInterceptor();
        var options = new DbContextOptionsBuilder<WorkflowsDesignSqliteDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;
        await using var db = new WorkflowsDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var serializer = new TestSerializer();
        db.Definitions.Add(new WorkflowDefinition { Id = "definition", TenantId = "tenant-a", Name = "Definition" });
        db.Versions.Add(new WorkflowDefinitionVersion("definition", "1.0.0")
        {
            Id = "source-version",
            TenantId = "tenant-a",
            State = State(),
            StateSource = serializer.Serialize(State())
        });
        await db.SaveChangesAsync();
        var locks = new DisposalRecordingLockProvider();
        interceptor.FailNextSave = true;
        var atomic = new EfDesignAtomicWriter(db, access);
        var clone = new EfCloneDraftFromVersionCommand(
            db,
            access,
            atomic,
            new TestIdentity(),
            serializer,
            locks);

        var draftId = await clone.Execute(new DesignOperationKey("clone-retry"), "source-version");

        Assert.Equal("generated-3", draftId);
        Assert.Equal(2, locks.AcquireCount);
        Assert.Equal(2, locks.DisposeCount);
    }

    [Fact]
    public async Task Shared_protocol_rolls_back_when_commit_is_rejected()
    {
        var scope = new ProtocolScope();
        var rollbackCount = 0;
        var lane = new DesignAtomicWriteLane<ProtocolScope, ProtocolMarker, ProtocolStage, ProtocolResult>
        {
            MarkerId = "marker",
            LoadMarker = _ => Task.FromResult<ProtocolMarker?>(null),
            BeginScope = () => scope,
            SaveMarker = (_, _, _) => Task.CompletedTask,
            Commit = (_, _) => Task.FromResult(DesignAtomicCommitDisposition.Rejected),
            Rollback = _ => rollbackCount++,
            ClassifyMarkerRace = _ => false,
            ClassifyUncertainCommit = _ => false,
            OnUncertainCommit = (_, _) => throw new InvalidOperationException(),
            TryReconcileAfterCommit = (_, _) => Task.FromResult<ProtocolResult?>(null),
            Delay = (_, _) => Task.CompletedTask,
            IsAccepted = stage => stage.Accepted,
            OnCommitted = _ => new ProtocolResult("committed"),
            OnReplay = _ => new ProtocolResult("replayed"),
            OnRejected = () => new ProtocolResult("rejected")
        };

        var result = await DesignAtomicWriteProtocol.ExecuteAsync(
            lane,
            (_, _) => Task.FromResult(new ProtocolStage(true)),
            null,
            CancellationToken.None);

        Assert.Equal("rejected", result.Status);
        Assert.Equal(1, rollbackCount);
        Assert.True(scope.Disposed);
    }

    [Fact]
    public async Task Shared_protocol_stage_rejection_survives_rollback_failure()
    {
        var scope = new ProtocolScope();
        var lane = CreateProtocolLane(
            scope,
            (_, _) => Task.FromResult(DesignAtomicCommitDisposition.Committed),
            _ => throw new InvalidOperationException("rollback failure"));

        var result = await DesignAtomicWriteProtocol.ExecuteAsync(
            lane,
            (_, _) => Task.FromResult(new ProtocolStage(false)),
            null,
            CancellationToken.None);

        Assert.Equal("rejected", result.Status);
        Assert.Equal(1, scope.DisposeCount);
    }

    [Fact]
    public async Task Shared_protocol_stage_rejection_survives_disposal_failure()
    {
        var scope = new ProtocolScope(new InvalidOperationException("dispose failure"));
        var lane = CreateProtocolLane(scope, (_, _) => Task.FromResult(DesignAtomicCommitDisposition.Committed));

        var result = await DesignAtomicWriteProtocol.ExecuteAsync(
            lane,
            (_, _) => Task.FromResult(new ProtocolStage(false)),
            null,
            CancellationToken.None);

        Assert.Equal("rejected", result.Status);
        Assert.Equal(1, scope.DisposeCount);
    }

    [Fact]
    public async Task Shared_protocol_scope_disposal_failure_preserves_authoritative_outcome_and_primary_exception()
    {
        var committedScope = new ProtocolScope(new InvalidOperationException("committed cleanup"));
        var committedLane = CreateProtocolLane(
            committedScope,
            (_, _) => Task.FromResult(DesignAtomicCommitDisposition.Committed));

        var committed = await DesignAtomicWriteProtocol.ExecuteAsync(
            committedLane,
            (_, _) => Task.FromResult(new ProtocolStage(true)),
            null,
            CancellationToken.None);

        Assert.Equal("committed", committed.Status);
        Assert.Equal(1, committedScope.DisposeCount);

        var rejectedScope = new ProtocolScope(new InvalidOperationException("rejected cleanup"));
        var rejectedLane = CreateProtocolLane(
            rejectedScope,
            (_, _) => Task.FromResult(DesignAtomicCommitDisposition.Rejected));

        var rejected = await DesignAtomicWriteProtocol.ExecuteAsync(
            rejectedLane,
            (_, _) => Task.FromResult(new ProtocolStage(true)),
            null,
            CancellationToken.None);

        Assert.Equal("rejected", rejected.Status);
        Assert.Equal(1, rejectedScope.DisposeCount);

        var failedScope = new ProtocolScope(new InvalidOperationException("failed cleanup"));
        var primary = new InvalidOperationException("primary stage failure");
        var failedLane = CreateProtocolLane(
            failedScope,
            (_, _) => Task.FromResult(DesignAtomicCommitDisposition.Committed));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => DesignAtomicWriteProtocol.ExecuteAsync(
            failedLane,
            (_, _) => Task.FromException<ProtocolStage>(primary),
            null,
            CancellationToken.None));

        Assert.Same(primary, exception);
        Assert.NotEmpty(primary.Data);
        Assert.Equal(1, failedScope.DisposeCount);
    }

    [Fact]
    public async Task Shared_protocol_reconcile_before_read_disposes_scope_once()
    {
        var scope = new ProtocolScope();
        var primary = new InvalidOperationException("commit acknowledgement lost");
        var lane = new DesignAtomicWriteLane<ProtocolScope, ProtocolMarker, ProtocolStage, ProtocolResult>
        {
            MarkerId = "reconcile-before-read",
            LoadMarker = _ => Task.FromResult<ProtocolMarker?>(null),
            BeginScope = () => scope,
            SaveMarker = (_, _, _) => Task.CompletedTask,
            Commit = (_, _) => Task.FromException<DesignAtomicCommitDisposition>(primary),
            Rollback = _ => throw new InvalidOperationException("rollback should not run after early disposal"),
            ClassifyMarkerRace = _ => false,
            ClassifyUncertainCommit = _ => false,
            OnUncertainCommit = (_, _) => throw new InvalidOperationException(),
            DisposeBeforeReconcile = value =>
            {
                value.Dispose();
                return Task.CompletedTask;
            },
            TryReconcileAfterCommit = (_, _) => Task.FromResult<ProtocolResult?>(new ProtocolResult("reconciled")),
            Delay = (_, _) => Task.CompletedTask,
            IsAccepted = stage => stage.Accepted,
            OnCommitted = _ => new ProtocolResult("committed"),
            OnReplay = _ => new ProtocolResult("replayed"),
            OnRejected = () => new ProtocolResult("rejected")
        };

        var result = await DesignAtomicWriteProtocol.ExecuteAsync(
            lane,
            (_, _) => Task.FromResult(new ProtocolStage(true)),
            null,
            CancellationToken.None);

        Assert.Equal("reconciled", result.Status);
        Assert.Equal(1, scope.DisposeCount);
    }

    [Fact]
    public async Task Shared_protocol_callback_disposal_failure_preserves_commit_reconciliation_and_disposes_once()
    {
        var scope = new ProtocolScope();
        var primary = new InvalidOperationException("commit acknowledgement lost");
        var callbackException = new InvalidOperationException("dispose before reconcile failed");
        var rollbackCount = 0;
        var lane = new DesignAtomicWriteLane<ProtocolScope, ProtocolMarker, ProtocolStage, ProtocolResult>
        {
            MarkerId = "reconcile-before-read-failure",
            LoadMarker = _ => Task.FromResult<ProtocolMarker?>(null),
            BeginScope = () => scope,
            SaveMarker = (_, _, _) => Task.CompletedTask,
            Commit = (_, _) => Task.FromException<DesignAtomicCommitDisposition>(primary),
            Rollback = _ => rollbackCount++,
            ClassifyMarkerRace = _ => false,
            ClassifyUncertainCommit = _ => false,
            OnUncertainCommit = (_, _) => throw new InvalidOperationException(),
            DisposeBeforeReconcile = value =>
            {
                value.Dispose();
                return Task.FromException(callbackException);
            },
            TryReconcileAfterCommit = (_, _) => Task.FromResult<ProtocolResult?>(new ProtocolResult("reconciled")),
            Delay = (_, _) => Task.CompletedTask,
            IsAccepted = stage => stage.Accepted,
            OnCommitted = _ => new ProtocolResult("committed"),
            OnReplay = _ => new ProtocolResult("replayed"),
            OnRejected = () => new ProtocolResult("rejected")
        };

        var result = await DesignAtomicWriteProtocol.ExecuteAsync(
            lane,
            (_, _) => Task.FromResult(new ProtocolStage(true)),
            null,
            CancellationToken.None);

        Assert.Equal("reconciled", result.Status);
        Assert.Contains(primary.Data.Values.Cast<object>(), value => ReferenceEquals(value, callbackException));
        Assert.Equal(1, scope.DisposeCount);
        Assert.Equal(0, rollbackCount);
    }

    [Fact]
    public async Task Shared_protocol_logs_rollback_failure_without_masking_primary_exception()
    {
        var scope = new ProtocolScope();
        var primary = new InvalidOperationException("primary failure");
        var rollback = new InvalidOperationException("rollback failure");
        var listener = new RecordingTraceListener();
        Trace.Listeners.Add(listener);
        try
        {
            var lane = new DesignAtomicWriteLane<ProtocolScope, ProtocolMarker, ProtocolStage, ProtocolResult>
            {
                MarkerId = "rollback-failure",
                LoadMarker = _ => Task.FromResult<ProtocolMarker?>(null),
                BeginScope = () => scope,
                SaveMarker = (_, _, _) => Task.CompletedTask,
                Commit = (_, _) => Task.FromException<DesignAtomicCommitDisposition>(primary),
                Rollback = _ => throw rollback,
                ClassifyMarkerRace = _ => false,
                ClassifyUncertainCommit = _ => false,
                OnUncertainCommit = (_, _) => throw new InvalidOperationException(),
                TryReconcileAfterCommit = (_, _) => Task.FromResult<ProtocolResult?>(null),
                Delay = (_, _) => Task.CompletedTask,
                IsAccepted = stage => stage.Accepted,
                OnCommitted = _ => new ProtocolResult("committed"),
                OnReplay = _ => new ProtocolResult("replayed"),
                OnRejected = () => new ProtocolResult("rejected")
            };

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => DesignAtomicWriteProtocol.ExecuteAsync(
                lane,
                (_, _) => Task.FromResult(new ProtocolStage(true)),
                null,
                CancellationToken.None));

            Assert.Same(primary, exception);
            Trace.Flush();
            Assert.Contains(listener.Messages, message =>
                message.Contains("rollback-failure", StringComparison.Ordinal) &&
                message.Contains("rollback failure", StringComparison.Ordinal));
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }


    private static DesignAtomicWriteLane<ProtocolScope, ProtocolMarker, ProtocolStage, ProtocolResult> CreateProtocolLane(
        ProtocolScope scope,
        Func<ProtocolScope, CancellationToken, Task<DesignAtomicCommitDisposition>> commit,
        Action<ProtocolScope>? rollback = null) => new()
    {
        MarkerId = "scope-cleanup",
        LoadMarker = _ => Task.FromResult<ProtocolMarker?>(null),
        BeginScope = () => scope,
        SaveMarker = (_, _, _) => Task.CompletedTask,
        Commit = commit,
        Rollback = rollback ?? (_ => { }),
        ClassifyMarkerRace = _ => false,
        ClassifyUncertainCommit = _ => false,
        OnUncertainCommit = (_, _) => throw new InvalidOperationException(),
        TryReconcileAfterCommit = (_, _) => Task.FromResult<ProtocolResult?>(null),
        Delay = (_, _) => Task.CompletedTask,
        IsAccepted = stage => stage.Accepted,
        OnCommitted = _ => new ProtocolResult("committed"),
        OnReplay = _ => new ProtocolResult("replayed"),
        OnRejected = () => new ProtocolResult("rejected")
    };

    private static WorkflowsDesignSqliteDbContext Create(SqliteConnection connection) => new(new DbContextOptionsBuilder<WorkflowsDesignSqliteDbContext>().UseSqlite(connection).Options);
    private static string LookupHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(WorkflowDefinitionIdentity.Fold(value)))).ToLowerInvariant();

    private static string ExactLookupHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static WorkflowDefinitionState State() => new([], null, [], [], null);
    private sealed class TestIdentity(string prefix = "generated") : IIdentityGenerator { private int n; public string Generate() => $"{prefix}-{Interlocked.Increment(ref n)}"; }
    private sealed class CustomLayoutStore : IWorkflowDefinitionVersionLayoutStore
    {
        public Task<WorkflowDefinitionVersionLayout?> FindByVersionIdAsync(string workflowDefinitionVersionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class MarkedCustomDefinitionStore : IWorkflowDefinitionStore, IDesignPersistenceFallback
    {
        public Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class CustomDesignAtomicWriter : IDesignAtomicWriter
    {
        public Task<DesignAtomicWriteResult<T>> ExecuteAsync<T>(DesignOperationKey operationKey, string operationKind, object requestMaterial, IReadOnlyCollection<string> mutatedUnits, Func<IDesignAtomicWriteContext, CancellationToken, Task<DesignAtomicWriteStage<T>>> stage, Func<CancellationToken, Task>? beforeAttempt = null, CancellationToken cancellationToken = default, IDesignAtomicWriteResultCodec<T>? resultCodec = null) => throw new NotSupportedException();
    }
    private sealed class ReplayedAtomicWriter : IDesignAtomicWriter
    {
        public Task<DesignAtomicWriteResult<T>> ExecuteAsync<T>(DesignOperationKey operationKey, string operationKind, object requestMaterial, IReadOnlyCollection<string> mutatedUnits, Func<IDesignAtomicWriteContext, CancellationToken, Task<DesignAtomicWriteStage<T>>> stage, Func<CancellationToken, Task>? beforeAttempt = null, CancellationToken cancellationToken = default, IDesignAtomicWriteResultCodec<T>? resultCodec = null) =>
            Task.FromResult(new DesignAtomicWriteResult<T>(DesignAtomicWriteStatus.Replayed, default));
    }
    private sealed class CancellingAfterCommitAtomicWriter(IDesignAtomicWriter inner, CancellationTokenSource cancellation) : IDesignAtomicWriter
    {
        public async Task<DesignAtomicWriteResult<T>> ExecuteAsync<T>(DesignOperationKey operationKey, string operationKind, object requestMaterial, IReadOnlyCollection<string> mutatedUnits, Func<IDesignAtomicWriteContext, CancellationToken, Task<DesignAtomicWriteStage<T>>> stage, Func<CancellationToken, Task>? beforeAttempt = null, CancellationToken cancellationToken = default, IDesignAtomicWriteResultCodec<T>? resultCodec = null)
        {
            var result = await inner.ExecuteAsync(operationKey, operationKind, requestMaterial, mutatedUnits, stage, beforeAttempt, cancellationToken, resultCodec);
            cancellation.Cancel();
            return result;
        }
    }
    private sealed class RetryingVersionAllocationAtomicWriter(WorkflowsDesignSqliteDbContext db) : IDesignAtomicWriter
    {
        public async Task<DesignAtomicWriteResult<T>> ExecuteAsync<T>(DesignOperationKey operationKey, string operationKind, object requestMaterial, IReadOnlyCollection<string> mutatedUnits, Func<IDesignAtomicWriteContext, CancellationToken, Task<DesignAtomicWriteStage<T>>> stage, Func<CancellationToken, Task>? beforeAttempt = null, CancellationToken cancellationToken = default, IDesignAtomicWriteResultCodec<T>? resultCodec = null)
        {
            if (beforeAttempt is null)
                throw new InvalidOperationException("The version allocation test requires attempt setup.");

            await beforeAttempt(cancellationToken);
            var firstStage = await stage(null!, cancellationToken);
            if (!firstStage.IsAccepted)
                return new DesignAtomicWriteResult<T>(DesignAtomicWriteStatus.Rejected, default);

            db.ChangeTracker.Clear();
            db.Versions.Add(new WorkflowDefinitionVersion("definition", "2.0.0", "{}")
            {
                Id = "retry-latest",
                TenantId = "tenant-a"
            });
            await db.SaveChangesAsync(cancellationToken);
            await beforeAttempt(cancellationToken);
            var staged = await stage(null!, cancellationToken);
            return new DesignAtomicWriteResult<T>(
                staged.IsAccepted ? DesignAtomicWriteStatus.Committed : DesignAtomicWriteStatus.Rejected,
                staged.Value,
                staged.ResultFingerprint,
                staged.ResultJson);
        }
    }
    private sealed class FaultInjectingTransaction(Exception? rollbackFailure, Exception? disposeFailure) : IDbContextTransaction
    {
        public Guid TransactionId { get; } = Guid.NewGuid();
        public bool SupportsSavepoints => false;
        public DbTransaction GetDbTransaction() => throw new NotSupportedException();
        public void Commit() { }
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Rollback()
        {
            if (rollbackFailure is not null)
                throw rollbackFailure;
        }
        public Task RollbackAsync(CancellationToken cancellationToken = default) =>
            rollbackFailure is null ? Task.CompletedTask : Task.FromException(rollbackFailure);
        public void CreateSavepoint(string name) => throw new NotSupportedException();
        public Task CreateSavepointAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void RollbackToSavepoint(string name) => throw new NotSupportedException();
        public Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void ReleaseSavepoint(string name) => throw new NotSupportedException();
        public Task ReleaseSavepointAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Dispose()
        {
            if (disposeFailure is not null)
                throw disposeFailure;
        }
        public ValueTask DisposeAsync() =>
            disposeFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(disposeFailure);
    }
    private sealed class TestLockProvider : IDistributedLockProvider
    {
        public IDistributedSynchronizationHandle AcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new Handle();
        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle());
        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new Handle();
        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle?>(new Handle());
        private sealed class Handle : IDistributedSynchronizationHandle { public CancellationToken HandleLostToken => CancellationToken.None; public void Dispose() { } public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }
    private sealed class DisposalRecordingLockProvider : IDistributedLockProvider
    {
        public int AcquireCount { get; private set; }
        public int DisposeCount { get; private set; }

        public IDistributedSynchronizationHandle AcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            new Handle(this);

        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            AcquireCount++;
            return ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle(this));
        }

        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            AcquireLock(name, timeout, cancellationToken);

        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDistributedSynchronizationHandle?>(AcquireLock(name, timeout, cancellationToken));

        private sealed class Handle(DisposalRecordingLockProvider owner) : IDistributedSynchronizationHandle
        {
            private int disposed;
            public CancellationToken HandleLostToken => CancellationToken.None;
            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) == 0)
                    owner.DisposeCount++;
            }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
    private sealed class RecordingLockProvider : IDistributedLockProvider
    {
        public List<string> Acquired { get; } = [];
        public IDistributedSynchronizationHandle AcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) { Acquired.Add(name); return new Handle(); }
        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) { Acquired.Add(name); return ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle()); }
        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => AcquireLock(name, timeout, cancellationToken);
        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle?>(AcquireLock(name, timeout, cancellationToken));
        private sealed class Handle : IDistributedSynchronizationHandle { public CancellationToken HandleLostToken => CancellationToken.None; public void Dispose() { } public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }
    private sealed class EmptyActivityStructureService : IActivityStructureService
    {
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) => [];
        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => activity;
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => null;
        public IReadOnlyCollection<Elsa.Expressions.Core.Models.VariableDefinition> ProjectScopedVariables(ActivityNode activity) => [];
        public bool SupportsScopedVariables(ActivityNode activity) => false;
    }
    private sealed class TestAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current => current; }
    private sealed record ResultValue(string Value);
    private enum CustomResultStatus { Ready }
    private sealed record CustomResult(CustomResultStatus Status);
    private sealed class CustomResultCodec(JsonSerializerOptions options) : IDesignAtomicWriteResultCodec<CustomResult>
    {
        public CustomResult Deserialize(string json) => JsonSerializer.Deserialize<CustomResult>(json, options)!;
        public bool Equivalent(CustomResult left, CustomResult right) => left == right;
    }

    private static string PortableFingerprint(string operationKind, string json)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(writer, document.RootElement);
        var canonical = Encoding.UTF8.GetString(stream.ToArray());
        var identity = "elsa-design-material:v1";
        var material = $"{Encoding.UTF8.GetByteCount(identity)}:{identity}{Encoding.UTF8.GetByteCount(operationKind)}:{operationKind}1:1{Encoding.UTF8.GetByteCount(canonical)}:{canonical}";
        return $"sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)))}";
    }

    private static string LegacyFingerprint(string operationKind, string json) =>
        $"sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"elsa-design-material:v1\n{operationKind}\n{json}")))}";

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in element.EnumerateArray())
                WriteCanonical(writer, item);
            writer.WriteEndArray();
        }
        else
            element.WriteTo(writer);
    }
    private sealed class ProtocolScope(Exception? disposeFailure = null) : IDisposable
    {
        public bool Disposed { get; private set; }
        public int DisposeCount { get; private set; }
        public void Dispose()
        {
            DisposeCount++;
            Disposed = true;
            if (disposeFailure is not null)
                throw disposeFailure;
        }
    }
    private sealed class RecordingTraceListener : TraceListener
    {
        public List<string> Messages { get; } = [];
        public override void Write(string? message) => Messages.Add(message ?? string.Empty);
        public override void WriteLine(string? message) => Write(message);
    }
    private sealed class ProtocolMarker { }
    private sealed record ProtocolStage(bool Accepted);
    private sealed record ProtocolResult(string Status);
    private sealed class AcknowledgementLostInterceptor : DbTransactionInterceptor
    {
        public bool FailNextCommit { get; set; }

        public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
        {
            if (!FailNextCommit)
                return;
            FailNextCommit = false;
            throw new InvalidOperationException("commit acknowledgement lost");
        }

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!FailNextCommit)
                return Task.CompletedTask;
            FailNextCommit = false;
            return Task.FromException(new InvalidOperationException("commit acknowledgement lost"));
        }
    }

    private sealed class TransientSaveInterceptor : SaveChangesInterceptor
    {
        private int failNextSave;
        public bool FailNextSave { set => Interlocked.Exchange(ref failNextSave, value ? 1 : 0); }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref failNextSave, 0) == 1)
                return ValueTask.FromException<InterceptionResult<int>>(
                    new DbUpdateException("simulated transient write conflict", new SqliteException("database is locked", 5, 5)));
            return ValueTask.FromResult(result);
        }
    }
    private sealed class FailureState
    {
        public bool FailNextCommit { get; set; }
        public bool FailReads { get; set; }
    }
    private sealed class CommitAcknowledgementFailureInterceptor(FailureState state) : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!state.FailNextCommit)
                return Task.CompletedTask;
            state.FailNextCommit = false;
            state.FailReads = true;
            return Task.FromException(new InvalidOperationException("commit acknowledgement lost"));
        }
    }
    private sealed class CallerCancellationCommitInterceptor(CancellationTokenSource cancellation) : DbTransactionInterceptor
    {
        private int armed;

        public void Arm() => Interlocked.Exchange(ref armed, 1);

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref armed, 0) == 0)
                return Task.CompletedTask;
            cancellation.Cancel();
            return Task.FromCanceled(cancellation.Token);
        }
    }
    private sealed class ReadFailureInterceptor(FailureState state) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default) => state.FailReads
            ? ValueTask.FromException<InterceptionResult<DbDataReader>>(new InvalidOperationException("provider read unavailable"))
            : ValueTask.FromResult(result);
    }
    private sealed class CapturingAtomicWriter(IDesignAtomicWriter inner) : IDesignAtomicWriter
    {
        public IReadOnlyCollection<string> MutatedUnits { get; private set; } = [];
        public Task<DesignAtomicWriteResult<T>> ExecuteAsync<T>(DesignOperationKey operationKey, string operationKind, object requestMaterial, IReadOnlyCollection<string> mutatedUnits, Func<IDesignAtomicWriteContext, CancellationToken, Task<DesignAtomicWriteStage<T>>> stage, Func<CancellationToken, Task>? beforeAttempt = null, CancellationToken cancellationToken = default, IDesignAtomicWriteResultCodec<T>? resultCodec = null)
        {
            MutatedUnits = mutatedUnits.ToArray();
            return inner.ExecuteAsync(operationKey, operationKind, requestMaterial, mutatedUnits, stage, beforeAttempt, cancellationToken, resultCodec);
        }
    }
    private sealed class PreTransactionBarrierAtomicWriter(IDesignAtomicWriter inner, Barrier barrier) : IDesignAtomicWriter
    {
        private int barrierEntered;
        private int stageInvocationCount;
        public DesignAtomicWriteStatus? LastStatus { get; private set; }
        public bool BarrierPassed { get; private set; }
        public int StageInvocationCount => Volatile.Read(ref stageInvocationCount);

        public async Task<DesignAtomicWriteResult<T>> ExecuteAsync<T>(DesignOperationKey operationKey, string operationKind, object requestMaterial, IReadOnlyCollection<string> mutatedUnits, Func<IDesignAtomicWriteContext, CancellationToken, Task<DesignAtomicWriteStage<T>>> stage, Func<CancellationToken, Task>? beforeAttempt = null, CancellationToken cancellationToken = default, IDesignAtomicWriteResultCodec<T>? resultCodec = null)
        {
            async Task ComposedBeforeAttempt(CancellationToken token)
            {
                if (beforeAttempt is not null)
                    await beforeAttempt(token);
                if (!Synchronize())
                    throw new TimeoutException("The deterministic atomic-write overlap barrier did not complete.");
            }

            async Task<DesignAtomicWriteStage<T>> RecordedStage(IDesignAtomicWriteContext context, CancellationToken token)
            {
                Interlocked.Increment(ref stageInvocationCount);
                return await stage(context, token);
            }

            var result = await inner.ExecuteAsync(operationKey, operationKind, requestMaterial, mutatedUnits, RecordedStage, ComposedBeforeAttempt, cancellationToken, resultCodec);
            LastStatus = result.Status;
            return result;
        }

        private bool Synchronize()
        {
            if (Interlocked.Exchange(ref barrierEntered, 1) != 0)
                return true;
            BarrierPassed = barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            return BarrierPassed;
        }
    }
    private sealed class ThrowingAtomicWriter(Exception exception) : IDesignAtomicWriter
    {
        public Task<DesignAtomicWriteResult<T>> ExecuteAsync<T>(DesignOperationKey operationKey, string operationKind, object requestMaterial, IReadOnlyCollection<string> mutatedUnits, Func<IDesignAtomicWriteContext, CancellationToken, Task<DesignAtomicWriteStage<T>>> stage, Func<CancellationToken, Task>? beforeAttempt = null, CancellationToken cancellationToken = default, IDesignAtomicWriteResultCodec<T>? resultCodec = null) => Task.FromException<DesignAtomicWriteResult<T>>(exception);
    }
    private sealed class PermittingPublicationGuard : IWorkflowDefinitionPublicationDeletionGuard
    {
        public string? SeenDefinitionId { get; private set; }
        public Task EnsureCanDeleteAsync(string definitionId, CancellationToken cancellationToken = default) { SeenDefinitionId = definitionId; return Task.CompletedTask; }
    }
    private sealed class RecordingDeletionGuard : IWorkflowDefinitionPermanentDeletionGuard
    {
        public string? SeenDefinitionId { get; private set; }
        public Task EnsureCanDeleteAsync(string definitionId, CancellationToken cancellationToken = default) { SeenDefinitionId = definitionId; return Task.CompletedTask; }
    }
    private sealed class CapturingDeferredEventPublisher : IDeferredEventPublisher
    {
        public List<IEvent> Events { get; } = [];
        public Task Publish(IEvent @event, CancellationToken cancellationToken = default)
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }
    }
    private sealed class UnrelatedStartupTask : IStartupTask
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class TestSerializer(JsonSerializerOptions? serializerOptions = null) : IPayloadSerializer
    {
        private readonly JsonSerializerOptions options = serializerOptions ?? new();
        public string Serialize(object payload) => JsonSerializer.Serialize(payload, options);
        public JsonElement SerializeToElement(object payload) => JsonSerializer.SerializeToElement(payload, options);
        public object Deserialize(string serializedData) => JsonSerializer.Deserialize<JsonElement>(serializedData);
        public object Deserialize(string serializedData, Type type) => JsonSerializer.Deserialize(serializedData, type)!;
        public object Deserialize(JsonElement serializedData) => serializedData;
        public T Deserialize<T>(string serializedData) => JsonSerializer.Deserialize<T>(serializedData)!;
        public T Deserialize<T>(JsonElement serializedData) => serializedData.Deserialize<T>(options)!;
        public JsonSerializerOptions GetOptions() => options;
    }
}
