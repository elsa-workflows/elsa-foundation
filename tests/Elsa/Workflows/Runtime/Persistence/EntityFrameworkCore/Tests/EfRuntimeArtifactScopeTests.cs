using Elsa.Activities.Runtime.Core.Models;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static Elsa.Persistence.EntityFramework.Tests.ProviderFailures;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfRuntimeArtifactScopeTests
{
    [Fact]
    public async Task Malformed_utf16_artifact_identities_round_trip_through_json_and_sqlite_projections()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var malformed = "identity-" + '\uD800';

        await fixture.Executable.SaveAsync(Executable(malformed, "safe-artifact-hash"));
        await fixture.Template.SaveAsync(Template(malformed, "template-hash"));
        await fixture.Store.SaveAsync(new WorkflowExecutableSourceReference(
            malformed, malformed, "WorkflowDefinition", malformed, "1", malformed, malformed, "1",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, WorkflowExecutableReferenceScope.Published));

        Assert.Equal(malformed, (await fixture.Executable.FindAsync(malformed))!.Identity.ArtifactId);
        Assert.Equal(malformed, (await fixture.Template.FindAsync(malformed))!.TemplateId);
        var reference = await fixture.Store.FindAsync(malformed);
        Assert.Equal(malformed, reference!.SourceReferenceId);
        Assert.Equal(malformed, reference.ArtifactId);
        Assert.Equal(malformed, reference.DefinitionId);
        Assert.Equal(malformed, reference.DefinitionVersionId);

        var executableRow = await fixture.Context.WorkflowExecutables.SingleAsync();
        var templateRow = await fixture.Context.ExecutableActivityTemplates.SingleAsync();
        var referenceRow = await fixture.Context.WorkflowExecutableSourceReferences.SingleAsync();
        Assert.Equal(Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode(malformed), executableRow.ArtifactId);
        Assert.Equal(Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode(malformed), templateRow.TemplateId);
        Assert.Equal(Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode(malformed), referenceRow.SourceReferenceId);
        Assert.DoesNotContain('\uD800', executableRow.ContentJson);
        Assert.DoesNotContain('\uD800', templateRow.ContentJson);
        Assert.DoesNotContain('\uD800', referenceRow.ContentJson);
    }

    [Fact]
    public async Task Malformed_utf16_dictionary_keys_round_trip_through_runtime_artifact_json()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var malformedKey = "metadata-" + '\uD800';

        await fixture.Executable.SaveAsync(Executable(
            "malformed-metadata-key",
            compatibilityMetadata: new Dictionary<string, string> { [malformedKey] = "value" }));

        var roundTrip = await fixture.Executable.FindAsync("malformed-metadata-key");

        Assert.NotNull(roundTrip);
        Assert.Equal("value", roundTrip!.CompatibilityMetadata[malformedKey]);
    }

    [Fact]
    public async Task Source_reference_ids_are_isolated_when_tenants_reuse_the_same_id()
    {
        await using var database = await Database.CreateAsync();
        await using var tenantA = database.Open("tenant-a");
        await using var tenantB = database.Open("tenant-b");

        await tenantA.Store.SaveAsync(Reference("same-ref", "artifact-a"));
        await tenantB.Store.SaveAsync(Reference("same-ref", "artifact-b"));

        Assert.Equal("artifact-a", (await tenantA.Store.FindAsync("same-ref"))!.ArtifactId);
        Assert.Equal("artifact-b", (await tenantB.Store.FindAsync("same-ref"))!.ArtifactId);
    }

    [Fact]
    public async Task Executables_and_templates_isolate_same_logical_ids_and_allow_privileged_scoped_access()
    {
        await using var database = await Database.CreateAsync();
        await using var tenantA = database.Open("tenant-a");
        await using var tenantB = database.Open("tenant-b");
        await tenantA.Executable.SaveAsync(Executable("same-artifact"));
        await tenantB.Executable.SaveAsync(Executable("same-artifact"));
        await tenantA.Store.SaveAsync(Reference("same-ref", "artifact-a"));
        await tenantB.Store.SaveAsync(Reference("same-ref", "artifact-b"));
        await tenantA.Template.SaveAsync(Template("same-template", "hash-a"));
        await tenantB.Template.SaveAsync(Template("same-template", "hash-b"));
        Assert.Equal("same-artifact", (await tenantA.Executable.FindAsync("same-artifact"))!.Identity.ArtifactId);
        Assert.Null(await tenantA.Template.FindByHashAsync("hash-b"));
        Assert.Equal("hash-b", (await tenantB.Template.FindByHashAsync("hash-b"))!.TemplateHash);

        await using var privileged = database.Open(PersistenceAccessContext.PrivilegedScoped(new PersistenceScope("tenant-a"), new PersistenceAccessPurpose("maintenance")));
        Assert.NotNull(await privileged.Executable.FindAsync("same-artifact"));
        Assert.NotNull(await privileged.Template.FindAsync("same-template"));
        Assert.NotNull(await privileged.Store.FindAsync("same-ref"));
    }

    [Fact]
    public async Task Global_and_across_scope_access_are_rejected_before_querying()
    {
        await using var database = await Database.CreateAsync();
        await using var global = database.Open(PersistenceAccessContext.Global);
        await Assert.ThrowsAsync<InvalidOperationException>(() => global.Store.FindAsync("ref").AsTask());

        await using var across = database.Open(PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("gc")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => across.Store.ListUnreferencedArtifactIdsAsync(new(["artifact"]), DateTimeOffset.UtcNow).AsTask());
    }

    [Fact]
    public async Task Unreferenced_lookup_requires_exact_artifact_value_after_hash_projection_collision()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Store.SaveAsync(Reference("ref-b", "artifact-b"));

        var row = await fixture.Context.WorkflowExecutableSourceReferences.SingleAsync(x => x.ArtifactId == Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode("artifact-b"));
        row.ArtifactIdHash = Elsa.Persistence.EntityFramework.EfRelationalIdentity.Hash("artifact-a");
        await fixture.Context.SaveChangesAsync();

        var unreferenced = await fixture.Store.ListUnreferencedArtifactIdsAsync(new(["artifact-a", "artifact-b"]), DateTimeOffset.UtcNow);
        Assert.Equal(["artifact-a", "artifact-b"], unreferenced);
    }

    [Fact]
    public async Task Unreferenced_lookup_is_bounded_to_the_finite_candidate_set()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Store.SaveAsync(Reference("ref-live", "artifact-live"));
        await fixture.Store.SaveAsync(Reference("ref-other", "artifact-other"));

        var unreferenced = await fixture.Store.ListUnreferencedArtifactIdsAsync(new(["artifact-live", "artifact-missing"]), DateTimeOffset.UtcNow);

        Assert.Equal(["artifact-missing"], unreferenced);
    }

    [Fact]
    public async Task Permanent_and_explicit_max_expiry_references_remain_distinct_at_max_value()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var now = DateTimeOffset.MaxValue;
        var permanent = Reference("permanent-reference", "permanent-artifact");
        var explicitMax = Reference("explicit-max-reference", "explicit-max-artifact") with { ExpiresAt = now };

        await fixture.Store.SaveAsync(permanent);
        await fixture.Store.SaveAsync(explicitMax);

        var live = await fixture.Store.ListPageAsync(new(null, true, now, 10));
        Assert.Equal(["permanent-reference"], live.Items.Select(x => x.SourceReferenceId));

        var unreferenced = await fixture.Store.ListUnreferencedArtifactIdsAsync(
            new(["permanent-artifact", "explicit-max-artifact"]),
            now);
        Assert.Equal(["explicit-max-artifact"], unreferenced);

        var deleted = await fixture.Store.DeleteExpiredOrRetiredAsync(new(10), now);
        Assert.Equal(["explicit-max-reference"], deleted);
        Assert.NotNull(await fixture.Store.FindAsync("permanent-reference"));
        Assert.Null(await fixture.Store.FindAsync("explicit-max-reference"));
    }

    [Fact]
    public async Task Source_reference_expiry_projection_is_nullable_and_indexed()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var entity = fixture.Context.Model.FindEntityType(typeof(WorkflowExecutableSourceReferenceEntity))!;
        var expiry = entity.FindProperty(nameof(WorkflowExecutableSourceReferenceEntity.ExpiresAtUtcTicks))!;

        Assert.Equal(typeof(long?), expiry.ClrType);
        Assert.True(expiry.IsNullable);
        Assert.Equal("INTEGER", expiry.GetColumnType());
        Assert.Contains(
            entity.GetIndexes(),
            index => index.Properties.Select(property => property.Name).SequenceEqual(
                [
                    nameof(WorkflowExecutableSourceReferenceEntity.ScopeKeyHash),
                    nameof(WorkflowExecutableSourceReferenceEntity.IsRetired),
                    nameof(WorkflowExecutableSourceReferenceEntity.ExpiresAtUtcTicks)
                ]));
    }

    [Fact]
    public async Task Unreferenced_lookup_uses_exact_bounded_existence_queries_per_candidate()
    {
        await using var database = await Database.CreateAsync();
        var interceptor = new ReaderCommandInterceptor();
        await using var fixture = database.Open("tenant-a", interceptor);
        await fixture.Store.SaveAsync(Reference("ref-live", "artifact-live"));
        await fixture.Store.SaveAsync(Reference("ref-other", "artifact-other"));
        interceptor.Commands.Clear();

        var unreferenced = await fixture.Store.ListUnreferencedArtifactIdsAsync(
            new(["artifact-live", "artifact-missing"]),
            DateTimeOffset.UtcNow);

        Assert.Equal(["artifact-missing"], unreferenced);
        Assert.Equal(2, interceptor.Commands.Count);
        Assert.All(interceptor.Commands, command => Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Constrained_artifact_and_source_reference_inputs_are_rejected_before_database_access()
    {
        await using var database = await Database.CreateAsync();
        var interceptor = new ReaderCommandInterceptor();
        await using var fixture = database.Open("tenant-a", interceptor);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Executable.SaveAsync(
            Executable("artifact", new string('h', RuntimeArtifactEfModule.HashMaximumLength + 1))).AsTask());

        var reference = Reference("reference", "artifact");
        var invalidReferences = new[]
        {
            reference with { SourceReferenceId = new string('s', RuntimeArtifactEfModule.IdentityMaximumLength + 1) },
            reference with { ArtifactId = new string('a', RuntimeArtifactEfModule.IdentityMaximumLength + 1) },
            reference with { DefinitionId = new string('d', RuntimeArtifactEfModule.IdentityMaximumLength + 1) },
            reference with { DefinitionVersionId = new string('v', RuntimeArtifactEfModule.IdentityMaximumLength + 1) }
        };
        foreach (var invalid in invalidReferences)
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Store.SaveAsync(invalid).AsTask());

        Assert.Empty(interceptor.Commands);
    }

    [Fact]
    public async Task Cleanup_does_not_delete_a_successor_recreated_after_selection()
    {
        await using var database = await Database.CreateAsync();
        await using var seed = database.Open("tenant-a");
        var expired = Reference("cleanup-recreated-ref", "artifact-a") with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) };
        await seed.Store.SaveAsync(expired);
        await seed.DisposeAsync();

        await using var current = database.Open("tenant-a");
        var interleaving = new RecreateAfterClaimReadInterceptor(async () =>
        {
            Assert.True(await current.Store.DeleteAsync(expired.SourceReferenceId));
            await current.Store.SaveAsync(expired with { ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) });
        }, triggerAfterReaders: 1);
        await using var stale = database.Open("tenant-a", interleaving);

        var deleted = await stale.Store.DeleteExpiredOrRetiredAsync(new(1), DateTimeOffset.UtcNow);

        Assert.Empty(deleted);
        Assert.NotNull(await current.Store.FindAsync(expired.SourceReferenceId));
    }

    [Fact]
    public async Task Template_save_uses_content_comparison_and_authenticated_bound_cursor()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Template.SaveAsync(Template("template-a", "hash-a"));
        await fixture.Template.SaveAsync(Template("template-a", "hash-a"));
        await fixture.Template.SaveAsync(Template("template-b", "hash-b"));

        var first = await fixture.Template.ListPageAsync(new RuntimeStorePageRequest(1));
        Assert.Single(first.Items);
        Assert.NotNull(first.NextContinuationToken);
        var second = await fixture.Template.ListPageAsync(new RuntimeStorePageRequest(1, first.NextContinuationToken));
        Assert.Single(second.Items);
        Assert.Equal("template-b", second.Items[0].TemplateId);

        var row = await fixture.Context.ExecutableActivityTemplates.SingleAsync(x => x.TemplateId == Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode("template-a"));
        row.ContentJson = "{}";
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Template.FindAsync("template-a").AsTask());
    }

    [Fact]
    public async Task Template_idempotent_save_rejects_mismatched_existing_incarnations()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var template = Template("template-incarnation", "template-incarnation-hash");
        await fixture.Template.SaveAsync(template);

        var claim = await fixture.Context.ExecutableActivityTemplateHashClaims.SingleAsync();
        claim.IncarnationId = "different-incarnation";
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Template.SaveAsync(template).AsTask());
    }

    [Fact]
    public async Task Template_create_reconciliation_rejects_a_mismatched_winner_pair()
    {
        await using var database = await Database.CreateAsync();
        var template = Template("template-reconciliation", "template-reconciliation-hash");
        await using var winner = database.Open("tenant-a");
        await winner.Template.SaveAsync(template);
        var claim = await winner.Context.ExecutableActivityTemplateHashClaims.SingleAsync();
        claim.IncarnationId = "different-incarnation";
        await winner.Context.SaveChangesAsync();
        winner.Context.ChangeTracker.Clear();

        await using var contender = database.Open(
            "tenant-a",
            new HideTemplateLookupInterceptor(9),
            new AlwaysUniqueTemplateSaveInterceptor());

        await Assert.ThrowsAsync<InvalidDataException>(() => contender.Template.SaveAsync(template).AsTask());
    }

    [Fact]
    public async Task Template_hash_claims_reject_collisions_and_corrupt_owners()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Template.SaveAsync(Template("template-a", "shared-hash"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Template.SaveAsync(Template("template-b", "shared-hash")).AsTask());

        var claim = await fixture.Context.ExecutableActivityTemplateHashClaims.SingleAsync();
        claim.ContentJson = "{\"templateHash\":\"shared-hash\",\"templateId\":\"other\"}";
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Template.FindByHashAsync("shared-hash").AsTask());
    }

    [Fact]
    public async Task Template_hash_lookup_rejects_a_row_projection_that_disagrees_with_its_claim()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Template.SaveAsync(Template("template-hash-projection", "hash-projection"));

        var row = await fixture.Context.ExecutableActivityTemplates.SingleAsync();
        row.TemplateHash = "different-hash";
        row.TemplateHashHash = Elsa.Persistence.EntityFramework.EfRelationalIdentity.Hash("different-hash");
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Template.FindByHashAsync("hash-projection").AsTask());
    }

    [Fact]
    public async Task Template_delete_rejects_a_row_hash_that_no_longer_matches_its_claim()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Template.SaveAsync(Template("template-delete-hash", "hash-before"));

        var row = await fixture.Context.ExecutableActivityTemplates.SingleAsync();
        var envelope = JsonNode.Parse(row.ContentJson)!.AsObject();
        envelope["templateHash"] = Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode("hash-after");
        envelope["template"]!.AsObject()["templateHash"] = "hash-after";
        row.TemplateHash = "hash-after";
        row.TemplateHashHash = Elsa.Persistence.EntityFramework.EfRelationalIdentity.Hash("hash-after");
        row.ContentJson = envelope.ToJsonString();
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Template.DeleteAsync("template-delete-hash").AsTask());
    }

    [Fact]
    public async Task Source_reference_definition_projection_and_cursor_shape_are_verified()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Store.SaveAsync(Reference("ref-a", "artifact-a"));
        var first = await fixture.Store.ListByArtifactPageAsync(new("artifact-a", 1));
        Assert.Single(first.Items);
        Assert.Null(first.NextContinuationToken);

        var row = await fixture.Context.WorkflowExecutableSourceReferences.SingleAsync();
        row.DefinitionId = "different-definition";
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.FindAsync("ref-a").AsTask());
    }

    [Fact]
    public async Task Source_reference_page_cursor_is_authenticated_and_query_bound()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Store.SaveAsync(Reference("ref-a", "artifact-a"));
        await fixture.Store.SaveAsync(Reference("ref-b", "artifact-b"));
        var first = await fixture.Store.ListPageAsync(new(null, false, null, 1));
        Assert.NotNull(first.NextContinuationToken);
        var token = first.NextContinuationToken!;
        var payloadStart = token.IndexOf('.') + 1;
        var tampered = token[..payloadStart] + (token[payloadStart] == 'A' ? 'B' : 'A') + token[(payloadStart + 1)..];
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.ListPageAsync(new(null, false, null, 1, tampered)).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.ListPageAsync(new(WorkflowExecutableReferenceScope.Published, false, null, 1, token)).AsTask());
    }

    [Fact]
    public async Task Definition_version_pages_allow_only_authorized_across_scope_reads_with_collision_safe_ordering()
    {
        await using var database = await Database.CreateAsync();
        await using var tenantA = database.Open("tenant-a");
        await using var tenantB = database.Open("tenant-b");
        var version = "shared-definition-version";
        await tenantA.Store.SaveAsync(Reference("same-ref", "artifact-a") with { DefinitionVersionId = version, TenantId = "tenant-a" });
        await tenantB.Store.SaveAsync(Reference("same-ref", "artifact-b") with { DefinitionVersionId = version, TenantId = "tenant-b" });

        var ordinary = await tenantA.Store.ListByDefinitionVersionPageAsync(new(version, 10));
        Assert.Equal(["tenant-a"], ordinary.Items.Select(item => item.TenantId));

        await using var across = database.Open(PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("definition-export")));
        var first = await across.Store.ListByDefinitionVersionPageAsync(new(version, 1));
        Assert.Single(first.Items);
        Assert.NotNull(first.NextContinuationToken);
        var second = await across.Store.ListByDefinitionVersionPageAsync(new(version, 1, first.NextContinuationToken));
        Assert.Single(second.Items);
        Assert.Null(second.NextContinuationToken);
        Assert.Equal(["tenant-a", "tenant-b"], new[] { first.Items[0].TenantId, second.Items[0].TenantId }.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Definition_version_pages_order_scopes_beyond_artifact_bound_with_supplementary_unicode()
    {
        await using var database = await Database.CreateAsync();
        var firstScope = "scope-" + new string('a', 200);
        var secondScope = "scope-" + string.Concat(Enumerable.Repeat("😀", 100));
        await using var first = database.Open(firstScope);
        await using var second = database.Open(secondScope);
        await first.Store.SaveAsync(Reference("same-ref", "artifact-a") with { DefinitionVersionId = "shared-version", TenantId = firstScope });
        await second.Store.SaveAsync(Reference("same-ref", "artifact-b") with { DefinitionVersionId = "shared-version", TenantId = secondScope });

        await using var across = database.Open(PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("definition-export")));
        var page = await across.Store.ListByDefinitionVersionPageAsync(new("shared-version", 10));

        Assert.Equal([firstScope, secondScope], page.Items.Select(item => item.TenantId!).ToArray());
    }

    [Fact]
    public async Task Definition_version_pages_reject_global_access_and_cross_shape_continuations()
    {
        await using var database = await Database.CreateAsync();
        await using var tenant = database.Open("tenant-a");
        await tenant.Store.SaveAsync(Reference("ref-a", "artifact-a"));
        var scoped = await tenant.Store.ListByDefinitionVersionPageAsync(new("definition-version", 1));
        Assert.Null(scoped.NextContinuationToken);

        await using var global = database.Open(PersistenceAccessContext.Global);
        await Assert.ThrowsAsync<InvalidOperationException>(() => global.Store.ListByDefinitionVersionPageAsync(new("definition", 1)).AsTask());

        await tenant.Store.SaveAsync(Reference("ref-b", "artifact-b") with { DefinitionVersionId = "definition-version" });
        scoped = await tenant.Store.ListByDefinitionVersionPageAsync(new("definition-version", 1));
        Assert.NotNull(scoped.NextContinuationToken);
        await using var across = database.Open(PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("definition-export")));
        await Assert.ThrowsAsync<ArgumentException>(() => across.Store.ListByDefinitionVersionPageAsync(new("definition-version", 1, scoped.NextContinuationToken)).AsTask());
    }

    [Fact]
    public async Task Source_reference_stale_conditional_update_cannot_touch_a_recreated_successor()
    {
        await using var database = await Database.CreateAsync();
        await using var seed = database.Open("tenant-a");
        var original = Reference("recreated-ref", "artifact-a");
        await seed.Store.SaveAsync(original);
        await seed.DisposeAsync();

        await using var current = database.Open("tenant-a");
        var interleaving = new RecreateBeforeSaveInterceptor(async () =>
        {
            Assert.True(await current.Store.DeleteAsync(original.SourceReferenceId));
            await current.Store.SaveAsync(original);
        });
        await using var fixture = database.Open("tenant-a", interleaving);
        var stale = await fixture.Store.FindAsync(original.SourceReferenceId);

        var result = await fixture.Store.TryRetireAsync(stale!, stale!.Retire(DateTimeOffset.UtcNow, "stale"));

        Assert.False(result);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
        Assert.Null((await fixture.Store.FindAsync(original.SourceReferenceId))!.DeletedAt);
    }

    [Fact]
    public async Task Source_reference_stale_conditional_restore_cannot_touch_a_recreated_successor()
    {
        await using var database = await Database.CreateAsync();
        await using var seed = database.Open("tenant-a");
        var retired = Reference("recreated-retired-ref", "artifact-a").Retire(DateTimeOffset.UtcNow, "retired");
        await seed.Store.SaveAsync(retired);
        await seed.DisposeAsync();

        await using var current = database.Open("tenant-a");
        var interleaving = new RecreateBeforeSaveInterceptor(async () =>
        {
            Assert.True(await current.Store.DeleteAsync(retired.SourceReferenceId));
            await current.Store.SaveAsync(retired with { DeletedAt = null, DeletedReason = null });
        });
        await using var fixture = database.Open("tenant-a", interleaving);
        var stale = await fixture.Store.FindAsync(retired.SourceReferenceId);

        var result = await fixture.Store.TryRestoreAsync(stale!, stale! with { DeletedAt = null, DeletedReason = null });

        Assert.False(result);
        Assert.Null((await fixture.Store.FindAsync(retired.SourceReferenceId))!.DeletedAt);
    }

    [Fact]
    public async Task Workflow_executable_stale_pair_delete_cannot_touch_a_recreated_successor()
    {
        await using var database = await Database.CreateAsync();
        await using var stale = database.Open("tenant-a");
        await using var current = database.Open("tenant-a");
        await current.Executable.SaveAsync(Executable("recreated-artifact"));
        var staleArtifact = await stale.Context.WorkflowExecutables.SingleAsync();
        var staleCoordination = await stale.Context.WorkflowExecutableCoordinations.SingleAsync();

        Assert.True(await current.Executable.DeleteAsync("recreated-artifact"));
        await current.Executable.SaveAsync(Executable("recreated-artifact"));
        stale.Context.RemoveRange(staleArtifact, staleCoordination);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.Context.SaveChangesAsync());
        Assert.NotNull(await current.Executable.FindAsync("recreated-artifact"));
    }

    [Fact]
    public async Task Template_stale_pair_delete_cannot_touch_a_recreated_successor()
    {
        await using var database = await Database.CreateAsync();
        await using var stale = database.Open("tenant-a");
        await using var current = database.Open("tenant-a");
        await current.Template.SaveAsync(Template("recreated-template", "recreated-hash"));
        var staleTemplate = await stale.Context.ExecutableActivityTemplates.SingleAsync();
        var staleClaim = await stale.Context.ExecutableActivityTemplateHashClaims.SingleAsync();

        Assert.True(await current.Template.DeleteAsync("recreated-template"));
        await current.Template.SaveAsync(Template("recreated-template", "recreated-hash"));
        stale.Context.RemoveRange(staleTemplate, staleClaim);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.Context.SaveChangesAsync());
        Assert.NotNull(await current.Template.FindAsync("recreated-template"));
    }

    [Fact]
    public async Task Template_delete_returns_false_when_same_id_is_recreated_with_a_different_hash()
    {
        await using var database = await Database.CreateAsync();
        await using var seed = database.Open("tenant-a");
        await seed.Template.SaveAsync(Template("recreated-template-different-hash", "old-hash"));
        await seed.DisposeAsync();

        await using var current = database.Open("tenant-a");
        var interleaving = new RecreateAfterClaimReadInterceptor(async () =>
        {
            Assert.True(await current.Template.DeleteAsync("recreated-template-different-hash"));
            await current.Template.SaveAsync(Template("recreated-template-different-hash", "new-hash"));
        });
        await using var stale = database.Open("tenant-a", interleaving);

        Assert.False(await stale.Template.DeleteAsync("recreated-template-different-hash"));
        Assert.Empty(stale.Context.ChangeTracker.Entries());
        Assert.Equal("new-hash", (await current.Template.FindAsync("recreated-template-different-hash"))!.TemplateHash);
    }

    [Fact]
    public async Task Executable_save_is_idempotent_and_batch_failure_rolls_back_new_rows()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");

        await fixture.Executable.SaveAsync(Executable("same-artifact"));
        await fixture.Executable.SaveAsync(Executable("same-artifact"));
        Assert.NotNull(await fixture.Executable.FindAsync("same-artifact"));

        await fixture.Executable.SaveAsync(Executable("incomplete"));
        var coordination = await fixture.Context.WorkflowExecutableCoordinations
            .SingleAsync(x => x.ArtifactId == Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode("incomplete"));
        fixture.Context.WorkflowExecutableCoordinations.Remove(coordination);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Executable.SaveBatchAsync(
            [Executable("new-artifact"), Executable("incomplete")]).AsTask());
        Assert.Null(await fixture.Executable.FindAsync("new-artifact"));
    }

    [Fact]
    public async Task Executable_idempotent_save_rejects_mismatched_existing_incarnations()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Executable.SaveAsync(Executable("mismatched-incarnation"));

        var coordination = await fixture.Context.WorkflowExecutableCoordinations
            .SingleAsync(x => x.ArtifactId == Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode("mismatched-incarnation"));
        coordination.IncarnationId = "different-incarnation";
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Executable
            .SaveAsync(Executable("mismatched-incarnation"))
            .AsTask());
    }

    [Fact]
    public async Task Concurrent_idempotent_executable_saves_reconcile_a_complete_winner()
    {
        await using var database = await Database.CreateFileAsync();
        await using var winner = database.Open("tenant-a");
        var candidate = Executable("concurrent-artifact");
        var interleaving = new RecreateAfterExecutableReadsInterceptor(() => winner.Executable.SaveAsync(candidate).AsTask());
        await using var loser = database.Open("tenant-a", interleaving);

        await loser.Executable.SaveAsync(candidate);

        Assert.NotNull(await loser.Executable.FindAsync(candidate.Identity.ArtifactId));
        Assert.Empty(loser.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Executable_save_reconciles_a_complete_winner_after_a_transient_race_the_provider_execution_strategy_wrapped()
    {
        await using var database = await Database.CreateFileAsync();
        await using var winner = database.Open("tenant-a");
        var candidate = Executable("wrapped-concurrent-artifact");
        var interleaving = new RecreateAfterExecutableReadsInterceptor(() => winner.Executable.SaveAsync(candidate).AsTask());
        var saves = FailingSaveInterceptor.WrappedDeadlock();
        await using var loser = database.Open("tenant-a", interleaving, saves);

        await loser.Executable.SaveAsync(candidate);

        Assert.Equal(1, saves.Attempts);
        Assert.NotNull(await loser.Executable.FindAsync(candidate.Identity.ArtifactId));
        Assert.Empty(loser.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Executable_save_does_not_reconcile_a_wrapped_provider_failure_that_is_not_a_write_race()
    {
        await using var database = await Database.CreateFileAsync();
        await using var winner = database.Open("tenant-a");
        var candidate = Executable("wrapped-failure-artifact");
        var interleaving = new RecreateAfterExecutableReadsInterceptor(() => winner.Executable.SaveAsync(candidate).AsTask());
        var saves = FailingSaveInterceptor.WrappedProviderFailure();
        await using var loser = database.Open("tenant-a", interleaving, saves);

        var failure = await Assert.ThrowsAsync<RuntimeArtifactEntityFrameworkPersistenceException>(() => loser.Executable.SaveAsync(candidate).AsTask());

        Assert.Equal("saving", failure.Operation);
        Assert.Equal(1, saves.Attempts);
    }

    [Fact]
    public async Task Executable_save_rejects_a_complete_winner_with_different_content()
    {
        await using var database = await Database.CreateAsync();
        await using var winner = database.Open("tenant-a");
        var winning = Executable("different-content", "winner-hash");
        await winner.Executable.SaveAsync(winning);
        await using var contender = database.Open("tenant-a");

        await Assert.ThrowsAsync<InvalidOperationException>(() => contender.Executable.SaveAsync(Executable("different-content", "contender-hash")).AsTask());
    }

    [Fact]
    public async Task Provider_write_failures_are_normalized_at_runtime_artifact_boundaries()
    {
        await using var database = await Database.CreateAsync();

        await using var executable = database.Open("tenant-a", new ThrowingSaveInterceptor());
        var executableFailure = await Assert.ThrowsAsync<RuntimeArtifactEntityFrameworkPersistenceException>(() => executable.Executable.SaveAsync(Executable("provider-failure")).AsTask());
        Assert.Equal("saving", executableFailure.Operation);
        Assert.Equal("provider-failure", executableFailure.Identity);

        await using var template = database.Open("tenant-b", new ThrowingSaveInterceptor());
        var templateFailure = await Assert.ThrowsAsync<RuntimeArtifactEntityFrameworkPersistenceException>(() => template.Template.SaveAsync(Template("provider-failure", "provider-hash")).AsTask());
        Assert.Equal("saving", templateFailure.Operation);
        Assert.Equal("provider-failure", templateFailure.Identity);

        await using var reference = database.Open("tenant-c", new ThrowingSaveInterceptor());
        var referenceFailure = await Assert.ThrowsAsync<RuntimeArtifactEntityFrameworkPersistenceException>(() => reference.Store.SaveAsync(Reference("provider-failure", "provider-artifact")).AsTask());
        Assert.Equal("saving", referenceFailure.Operation);
        Assert.Equal("provider-failure", referenceFailure.Identity);
    }

    [Fact]
    public async Task Provider_invalid_operation_failures_are_normalized_on_delete_and_source_write_boundaries()
    {
        await using var database = await Database.CreateAsync();
        await using (var seed = database.Open("tenant-a"))
        {
            await seed.Executable.SaveAsync(Executable("delete-provider-failure"));
            await seed.Template.SaveAsync(Template("delete-provider-failure", "delete-provider-hash"));
        }

        await using var failing = database.Open("tenant-a", new ThrowingInvalidOperationSaveInterceptor());
        var executableFailure = await Assert.ThrowsAsync<RuntimeArtifactEntityFrameworkPersistenceException>(() =>
            failing.Executable.DeleteAsync("delete-provider-failure").AsTask());
        Assert.Equal("deleting", executableFailure.Operation);
        Assert.Equal("delete-provider-failure", executableFailure.Identity);

        var templateFailure = await Assert.ThrowsAsync<RuntimeArtifactEntityFrameworkPersistenceException>(() =>
            failing.Template.DeleteAsync("delete-provider-failure").AsTask());
        Assert.Equal("deleting", templateFailure.Operation);
        Assert.Equal("delete-provider-failure", templateFailure.Identity);

        var sourceFailure = await Assert.ThrowsAsync<RuntimeArtifactEntityFrameworkPersistenceException>(() =>
            failing.Store.SaveAsync(Reference("source-provider-failure", "artifact-provider-failure")).AsTask());
        Assert.Equal("saving", sourceFailure.Operation);
        Assert.Equal("source-provider-failure", sourceFailure.Identity);
    }

    [Fact]
    public async Task Template_save_retries_a_transient_conflict_the_provider_execution_strategy_wrapped()
    {
        await using var database = await Database.CreateAsync();
        var saves = FailingSaveInterceptor.WrappedDeadlock(failures: 1);
        await using var fixture = database.Open("tenant-a", saves);

        await fixture.Template.SaveAsync(Template("wrapped-transient", "wrapped-transient-hash"));

        Assert.Equal(2, saves.Attempts);
        Assert.NotNull(await fixture.Template.FindAsync("wrapped-transient"));
    }

    [Fact]
    public async Task Template_delete_exhausts_its_budget_on_wrapped_transient_conflicts_as_a_persistence_failure()
    {
        await using var database = await Database.CreateAsync();
        await using (var seed = database.Open("tenant-a"))
            await seed.Template.SaveAsync(Template("wrapped-contention", "wrapped-contention-hash"));
        var saves = FailingSaveInterceptor.WrappedDeadlock();
        await using var failing = database.Open("tenant-a", saves);

        var failure = await Assert.ThrowsAsync<RuntimeArtifactEntityFrameworkPersistenceException>(() =>
            failing.Template.DeleteAsync("wrapped-contention").AsTask());

        Assert.Equal("deleting", failure.Operation);
        Assert.Equal(EfWriteRetry.DefaultMaxAttempts, saves.Attempts);
        await using var verification = database.Open("tenant-a");
        Assert.NotNull(await verification.Template.FindAsync("wrapped-contention"));
    }

    [Fact]
    public async Task Template_save_fails_a_wrapped_provider_failure_that_is_not_a_transient_conflict_without_a_retry()
    {
        await using var database = await Database.CreateAsync();
        var saves = FailingSaveInterceptor.WrappedProviderFailure();
        await using var fixture = database.Open("tenant-a", saves);

        var failure = await Assert.ThrowsAsync<RuntimeArtifactEntityFrameworkPersistenceException>(() =>
            fixture.Template.SaveAsync(Template("wrapped-provider-failure", "wrapped-provider-failure-hash")).AsTask());

        Assert.Equal("saving", failure.Operation);
        Assert.Equal(1, saves.Attempts);
    }

    [Fact]
    public async Task Activity_publication_commit_retries_a_transient_race_the_provider_execution_strategy_wrapped()
    {
        await using var database = await Database.CreateAsync();
        var saves = FailingSaveInterceptor.WrappedDeadlock(failures: 1);
        await using var fixture = database.Open("tenant-a", saves);
        var commit = new EfActivityPublicationRuntimeCommit(fixture.Template, fixture.Store);

        Assert.True(await commit.CommitAsync(Template("wrapped-publication", "wrapped-publication-hash"), Reference("wrapped-publication-reference", "wrapped-publication")));

        Assert.Equal(2, saves.Attempts);
        Assert.NotNull(await fixture.Template.FindAsync("wrapped-publication"));
        Assert.NotNull(await fixture.Store.FindAsync("wrapped-publication-reference"));
    }

    [Fact]
    public async Task Activity_publication_commit_fails_a_wrapped_provider_failure_that_is_not_a_write_race_without_a_retry()
    {
        await using var database = await Database.CreateAsync();
        var saves = FailingSaveInterceptor.WrappedProviderFailure();
        await using var fixture = database.Open("tenant-a", saves);
        var commit = new EfActivityPublicationRuntimeCommit(fixture.Template, fixture.Store);

        var failure = await Assert.ThrowsAsync<RuntimeArtifactEntityFrameworkPersistenceException>(() =>
            commit.CommitAsync(Template("wrapped-publication-failure", "wrapped-publication-failure-hash"), Reference("wrapped-publication-failure-reference", "wrapped-publication-failure")).AsTask());

        Assert.Equal("publishing", failure.Operation);
        Assert.Equal(1, saves.Attempts);
        Assert.Null(await fixture.Template.FindAsync("wrapped-publication-failure"));
    }

    [Fact]
    public async Task Provider_read_failures_are_normalized_for_every_artifact_reader()
    {
        await using var database = await Database.CreateAsync();
        await using (var seed = database.Open("tenant-a"))
        {
            await seed.Executable.SaveAsync(Executable("read-failure"));
            await seed.Template.SaveAsync(Template("read-failure", "read-failure-hash"));
            await seed.Store.SaveAsync(Reference("read-failure", "read-failure-artifact"));
        }

        await using var failing = database.Open("tenant-a", new ThrowingReadInterceptor());
        await Assert.ThrowsAsync<RuntimeArtifactEntityFrameworkPersistenceException>(() => failing.Executable.FindAsync("read-failure").AsTask());
        await Assert.ThrowsAsync<RuntimeArtifactEntityFrameworkPersistenceException>(() => failing.Executable.ListPageAsync(new RuntimeStorePageRequest(1)).AsTask());
        await Assert.ThrowsAsync<RuntimeArtifactEntityFrameworkPersistenceException>(() => failing.Template.FindAsync("read-failure").AsTask());
        await Assert.ThrowsAsync<RuntimeArtifactEntityFrameworkPersistenceException>(() => failing.Template.FindByHashAsync("read-failure-hash").AsTask());
        await Assert.ThrowsAsync<RuntimeArtifactEntityFrameworkPersistenceException>(() => failing.Template.ListPageAsync(new RuntimeStorePageRequest(1)).AsTask());
        await Assert.ThrowsAsync<RuntimeArtifactEntityFrameworkPersistenceException>(() => failing.Store.FindAsync("read-failure").AsTask());
        await Assert.ThrowsAsync<RuntimeArtifactEntityFrameworkPersistenceException>(() => failing.Store.ListPageAsync(new WorkflowExecutableSourceReferencePageQuery(limit: 1)).AsTask());
    }

    [Fact]
    public async Task Root_write_lease_reacquisition_returns_the_latest_fencing_token_after_aba()
    {
        await using var database = await Database.CreateAsync();
        await using var first = database.Open("tenant-a");
        await first.Executable.SaveAsync(Executable("lease-aba"));
        var now = DateTimeOffset.UtcNow;
        var original = await first.Executable.TryAcquireRootWriteLeaseAsync("lease-aba", "lease", now.AddHours(1), now);
        Assert.NotNull(original);

        await using var second = database.Open("tenant-a");
        await second.Executable.ReleaseRootWriteLeaseAsync(original!);
        var replacement = await second.Executable.TryAcquireRootWriteLeaseAsync("lease-aba", "lease", now.AddHours(2), now);
        Assert.NotNull(replacement);
        Assert.NotEqual(original.ConcurrencyToken, replacement!.ConcurrencyToken);

        var reacquired = await first.Executable.TryAcquireRootWriteLeaseAsync("lease-aba", "lease", now.AddHours(3), now);

        Assert.Equal(replacement.ConcurrencyToken, reacquired!.ConcurrencyToken);
    }

    [Fact]
    public async Task Deletion_guard_reacquisition_returns_the_latest_fencing_token_after_aba()
    {
        await using var database = await Database.CreateAsync();
        await using var first = database.Open("tenant-a");
        await first.Executable.SaveAsync(Executable("guard-aba"));
        var now = DateTimeOffset.UtcNow;
        var original = await first.Executable.TryBeginDeletionAsync("guard-aba", "delete", now.AddHours(1), now);
        Assert.NotNull(original);

        await using var second = database.Open("tenant-a");
        Assert.True(await second.Executable.CancelDeletionAsync(original!));
        var replacement = await second.Executable.TryBeginDeletionAsync("guard-aba", "delete", now.AddHours(2), now);
        Assert.NotNull(replacement);
        Assert.NotEqual(original.ConcurrencyToken, replacement!.ConcurrencyToken);

        var reacquired = await first.Executable.TryBeginDeletionAsync("guard-aba", "delete", now.AddHours(3), now);

        Assert.Equal(replacement.ConcurrencyToken, reacquired!.ConcurrencyToken);
    }

    [Fact]
    public async Task Release_root_write_lease_reloads_after_contention_and_preserves_newer_leases()
    {
        await using var database = await Database.CreateAsync();
        await using var seed = database.Open("tenant-a");
        await seed.Executable.SaveAsync(Executable("release-contention"));
        var now = DateTimeOffset.UtcNow;
        var original = await seed.Executable.TryAcquireRootWriteLeaseAsync("release-contention", "original", now.AddMinutes(5), now);
        Assert.NotNull(original);

        await using var current = database.Open("tenant-a");
        WorkflowExecutableRootWriteLease? newer = null;
        var interleaving = new RecreateBeforeSaveInterceptor(async () =>
        {
            newer = await current.Executable.TryAcquireRootWriteLeaseAsync("release-contention", "newer", now.AddMinutes(5), now);
            Assert.NotNull(newer);
        });
        await using var releasing = database.Open("tenant-a", interleaving);

        await releasing.Executable.ReleaseRootWriteLeaseAsync(original!);

        Assert.NotNull(newer);
        Assert.False(await current.Executable.RenewRootWriteLeaseAsync(original!, now.AddMinutes(6), now));
        Assert.True(await current.Executable.RenewRootWriteLeaseAsync(newer!, now.AddMinutes(6), now));
    }

    [Fact]
    public async Task Renew_root_write_lease_retries_after_unrelated_coordination_contention()
    {
        await using var database = await Database.CreateAsync();
        await using var seed = database.Open("tenant-a");
        await seed.Executable.SaveAsync(Executable("renew-contention"));
        var now = DateTimeOffset.UtcNow;
        var lease = await seed.Executable.TryAcquireRootWriteLeaseAsync("renew-contention", "lease", now.AddMinutes(5), now);
        Assert.NotNull(lease);

        await using var current = database.Open("tenant-a");
        var interleaving = new RecreateBeforeSaveInterceptor(async () =>
        {
            var row = await current.Context.WorkflowExecutableCoordinations.SingleAsync();
            row.Revision++;
            await current.Context.SaveChangesAsync();
            current.Context.ChangeTracker.Clear();
        });
        await using var renewing = database.Open("tenant-a", interleaving);

        Assert.True(await renewing.Executable.RenewRootWriteLeaseAsync(lease!, now.AddMinutes(10), now));
        Assert.NotNull(await current.Executable.FindAsync("renew-contention"));
    }

    [Fact]
    public async Task Cancel_deletion_guard_retries_after_unrelated_coordination_contention()
    {
        await using var database = await Database.CreateAsync();
        await using var seed = database.Open("tenant-a");
        await seed.Executable.SaveAsync(Executable("cancel-contention"));
        var now = DateTimeOffset.UtcNow;
        var guard = await seed.Executable.TryBeginDeletionAsync("cancel-contention", "delete", now.AddMinutes(5), now);
        Assert.NotNull(guard);

        await using var current = database.Open("tenant-a");
        var interleaving = new RecreateBeforeSaveInterceptor(async () =>
        {
            var row = await current.Context.WorkflowExecutableCoordinations.SingleAsync();
            row.Revision++;
            await current.Context.SaveChangesAsync();
            current.Context.ChangeTracker.Clear();
        });
        await using var cancelling = database.Open("tenant-a", interleaving);

        Assert.True(await cancelling.Executable.CancelDeletionAsync(guard!));
        Assert.NotNull(await current.Executable.FindAsync("cancel-contention"));
        Assert.NotNull(await current.Executable.TryAcquireRootWriteLeaseAsync("cancel-contention", "new-lease", now.AddMinutes(5), now));
    }

    [Fact]
    public async Task Executable_artifact_hash_projection_rejects_valid_json_tampering()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Executable.SaveAsync(Executable("artifact-hash-corrupt"));
        var row = await fixture.Context.WorkflowExecutables.SingleAsync(x => x.ArtifactId == Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode("artifact-hash-corrupt"));
        var payload = JsonNode.Parse(row.ContentJson)!.AsObject();
        payload["identity"]!.AsObject()["artifactHash"] = "tampered-artifact-hash";
        row.ContentJson = payload.ToJsonString();
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Executable.FindAsync("artifact-hash-corrupt").AsTask());
    }

    [Fact]
    public async Task Executable_overlong_persisted_artifact_hash_fails_closed_on_find_list_and_idempotent_save()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Executable.SaveAsync(Executable("artifact-hash-overlong"));

        var row = await fixture.Context.WorkflowExecutables.SingleAsync(x => x.ArtifactId == Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode("artifact-hash-overlong"));
        var oversizedHash = new string('h', RuntimeArtifactEfModule.HashMaximumLength + 1);
        var payload = JsonNode.Parse(row.ContentJson)!.AsObject();
        payload["identity"]!.AsObject()["artifactHash"] = oversizedHash;
        row.ArtifactHash = oversizedHash;
        row.ContentJson = payload.ToJsonString();
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Executable.FindAsync("artifact-hash-overlong").AsTask());
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Executable.ListPageAsync(new RuntimeStorePageRequest(10)).AsTask());
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Executable.SaveAsync(Executable("artifact-hash-overlong")).AsTask());
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Ordinary_and_guarded_deletes_remove_the_pair_and_reject_stale_guards()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await using var other = database.Open("tenant-a");
        var now = DateTimeOffset.UtcNow;

        await fixture.Executable.SaveAsync(Executable("ordinary"));
        Assert.True(await fixture.Executable.DeleteAsync("ordinary"));
        Assert.Null(await fixture.Executable.FindAsync("ordinary"));

        await fixture.Executable.SaveAsync(Executable("guarded"));
        var guard = await fixture.Executable.TryBeginDeletionAsync("guarded", "operation", now.AddMinutes(5), now);
        Assert.NotNull(guard);

        var row = await other.Context.WorkflowExecutableCoordinations.SingleAsync(x => x.ArtifactId == Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode("guarded"));
        row.ContentJson = "{\"Leases\":{},\"Guard\":null}";
        row.Revision++;
        await other.Context.SaveChangesAsync();
        Assert.False(await fixture.Executable.DeleteAsync(guard!, now));
        Assert.NotNull(await fixture.Executable.FindAsync("guarded"));
    }

    [Fact]
    public async Task Leases_and_guards_are_mutually_exclusive_and_expired_state_recovers()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Executable.SaveAsync(Executable("coordination"));
        var now = DateTimeOffset.UtcNow;

        var lease = await fixture.Executable.TryAcquireRootWriteLeaseAsync("coordination", "lease", now.AddMinutes(1), now);
        Assert.NotNull(lease);
        Assert.Null(await fixture.Executable.TryBeginDeletionAsync("coordination", "operation", now.AddMinutes(5), now));
        await fixture.Executable.ReleaseRootWriteLeaseAsync(lease!);

        var guard = await fixture.Executable.TryBeginDeletionAsync("coordination", "operation", now.AddMinutes(1), now);
        Assert.NotNull(guard);
        Assert.Null(await fixture.Executable.TryAcquireRootWriteLeaseAsync("coordination", "other", now.AddMinutes(5), now));

        var recovered = await fixture.Executable.TryAcquireRootWriteLeaseAsync("coordination", "other", now.AddMinutes(5), now.AddMinutes(2));
        Assert.NotNull(recovered);
        Assert.False(await fixture.Executable.RenewRootWriteLeaseAsync(lease!, now.AddMinutes(6), now.AddMinutes(2)));
    }

    [Fact]
    public async Task Optimistic_coordination_conflict_clears_tracker_and_reloads_before_retry()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await using var other = database.Open("tenant-a");
        await fixture.Executable.SaveAsync(Executable("conflict"));
        var now = DateTimeOffset.UtcNow;
        var first = await fixture.Executable.TryAcquireRootWriteLeaseAsync("conflict", "first", now.AddMinutes(5), now);
        Assert.NotNull(first);

        var row = await other.Context.WorkflowExecutableCoordinations.SingleAsync(x => x.ArtifactId == Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode("conflict"));
        row.ContentJson = "{\"Leases\":{},\"Guard\":null}";
        row.Revision++;
        await other.Context.SaveChangesAsync();

        var second = await fixture.Executable.TryAcquireRootWriteLeaseAsync("conflict", "second", now.AddMinutes(5), now);
        Assert.NotNull(second);
    }

    [Fact]
    public async Task Coordination_operations_reject_orphan_and_corrupt_executable_pairs()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var now = DateTimeOffset.UtcNow;
        await fixture.Executable.SaveAsync(Executable("orphan"));
        var executable = await fixture.Context.WorkflowExecutables.SingleAsync(x => x.ArtifactId == Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode("orphan"));
        fixture.Context.WorkflowExecutables.Remove(executable);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Executable
            .TryAcquireRootWriteLeaseAsync("orphan", "lease", now.AddMinutes(5), now).AsTask());
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Executable
            .TryBeginDeletionAsync("orphan", "operation", now.AddMinutes(5), now).AsTask());
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Executable
            .RenewRootWriteLeaseAsync(new("orphan", "lease", "token"), now.AddMinutes(5), now).AsTask());
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Executable
            .ReleaseRootWriteLeaseAsync(new("orphan", "lease", "token")).AsTask());
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Executable
            .CancelDeletionAsync(new("orphan", "operation", "token")).AsTask());

        await fixture.Executable.SaveAsync(Executable("corrupt-executable"));
        var corrupt = await fixture.Context.WorkflowExecutables.SingleAsync(x => x.ArtifactId == Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode("corrupt-executable"));
        corrupt.ContentJson = "{";
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Executable
            .TryAcquireRootWriteLeaseAsync("corrupt-executable", "lease", now.AddMinutes(5), now).AsTask());
    }

    [Fact]
    public async Task Corrupt_coordination_payloads_fail_closed_as_invalid_data()
    {
        var payloads = new[]
        {
            "null",
            "{",
            "{\"Leases\":null,\"Guard\":null}",
            "{\"Leases\":{\"key\":{\"Id\":\"other\",\"Token\":\"token\",\"ExpiresAt\":\"2030-01-01T00:00:00+00:00\"}},\"Guard\":null}",
            "{\"Leases\":{\"one\":{\"Id\":\"one\",\"Token\":\"\",\"ExpiresAt\":\"2030-01-01T00:00:00+00:00\"},\"two\":{\"Id\":\"two\",\"Token\":\"\",\"ExpiresAt\":\"2030-01-01T00:00:00+00:00\"}},\"Guard\":null}",
            "{\"Leases\":{\"one\":{\"Id\":\"one\",\"id\":\"one\",\"Token\":\"token\",\"ExpiresAt\":\"2030-01-01T00:00:00+00:00\"}},\"Guard\":null}",
            "{\"Leases\":{\"one\":{\"Token\":\"token\",\"token\":\"token-duplicate\",\"ExpiresAt\":\"2030-01-01T00:00:00+00:00\"}},\"Guard\":null}",
            "{\"Leases\":{\"one\":{\"ExpiresAt\":\"2030-01-01T00:00:00+00:00\",\"expiresAt\":\"2030-01-02T00:00:00+00:00\",\"Id\":\"one\",\"Token\":\"token\"}},\"Guard\":null}",
            "{\"Leases\":{\"one\":{\"Id\":\"one\",\"Token\":\"token\",\"ExpiresAt\":\"0001-01-01T00:00:00+00:00\"}},\"Guard\":null}",
            "{\"Leases\":{},\"Guard\":{\"OperationId\":\"\",\"Token\":\"token\",\"ExpiresAt\":\"2030-01-01T00:00:00+00:00\"}}",
            "{\"Leases\":{},\"leases\":{},\"Guard\":null}"
        };

        foreach (var payload in payloads)
        {
            await using var database = await Database.CreateAsync();
            await using var fixture = database.Open("tenant-a");
            await fixture.Executable.SaveAsync(Executable("corrupt"));
            var row = await fixture.Context.WorkflowExecutableCoordinations.SingleAsync(x => x.ArtifactId == Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode("corrupt"));
            row.ContentJson = payload;
            await fixture.Context.SaveChangesAsync();
            fixture.Context.ChangeTracker.Clear();

            var exception = await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Executable
                .TryBeginDeletionAsync("corrupt", "operation", DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow)
                .AsTask());
            Assert.IsNotType<JsonException>(exception);
            Assert.IsNotType<NullReferenceException>(exception);
        }
    }

    [Fact]
    public async Task Lease_dictionary_keys_remain_case_sensitive()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Executable.SaveAsync(Executable("case-sensitive-leases"));
        var row = await fixture.Context.WorkflowExecutableCoordinations
            .SingleAsync(x => x.ArtifactId == Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode("case-sensitive-leases"));
        var encodedA = Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode("A");
        var encodedAInsensitive = Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode("a");
        row.ContentJson = $"{{\"Leases\":{{\"{encodedA}\":{{\"Id\":\"{encodedA}\",\"Token\":\"{Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode("token-a")}\",\"ExpiresAt\":\"2030-01-01T00:00:00+00:00\"}},\"{encodedAInsensitive}\":{{\"Id\":\"{encodedAInsensitive}\",\"Token\":\"{Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode("token-b")}\",\"ExpiresAt\":\"2030-01-01T00:00:00+00:00\"}}}},\"Guard\":null}}";
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        Assert.Null(await fixture.Executable.TryBeginDeletionAsync(
            "case-sensitive-leases",
            "operation",
            DateTimeOffset.UtcNow.AddMinutes(5),
            DateTimeOffset.UtcNow));
    }

    private static WorkflowExecutableSourceReference Reference(string id, string artifact) => new(
        id, artifact, "WorkflowDefinition", "definition", "1", "definition", "definition-version", "1",
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, WorkflowExecutableReferenceScope.Published);

    private static WorkflowExecutable Executable(
        string artifactId,
        string? artifactHash = null,
        IReadOnlyDictionary<string, string>? compatibilityMetadata = null)
    {
        var node = new ExecutableNode("node", "node", "test", "1", "consumer", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), outputCaptures: new Dictionary<string, RuntimeOutputCapture>());
        return new WorkflowExecutable(new WorkflowExecutableIdentity(artifactId, "definition", "version", "1", artifactHash ?? $"hash-{artifactId}"), node, new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UtcNow, compatibilityMetadata ?? new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
    }

    private static ExecutableActivityTemplate Template(string id, string hash)
    {
        var node = new ExecutableNode("node", "node", "test", "1", "consumer", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), outputCaptures: new Dictionary<string, RuntimeOutputCapture>());
        return new ExecutableActivityTemplate(id, hash, node, new Dictionary<string, WorkflowExecutableResumeTarget>(), [], [], [], "fingerprint", new Dictionary<string, string>(), DateTimeOffset.UtcNow);
    }

    private sealed class Database : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly string connectionString;
        private readonly string? databasePath;
        private Database(SqliteConnection connection, string connectionString, string? databasePath = null)
        {
            this.connection = connection;
            this.connectionString = connectionString;
            this.databasePath = databasePath;
        }

        public static async Task<Database> CreateAsync()
        {
            var connectionString = $"Data Source=runtime-artifacts-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            await using var context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            return new Database(connection, connectionString);
        }

        public static async Task<Database> CreateFileAsync()
        {
            var fileName = $"elsa-runtime-artifacts-{Guid.NewGuid():N}.db";
            var databasePath = Path.Join(Path.GetTempPath(), fileName);
            var connectionString = $"Data Source={databasePath};Pooling=False";
            var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
                await command.ExecuteNonQueryAsync();
            }
            await using var context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            return new Database(connection, connectionString, databasePath);
        }

        public Fixture Open(string scope, params IInterceptor[] interceptors) => Open(PersistenceAccessContext.Scoped(new PersistenceScope(scope)), interceptors);

        public Fixture Open(PersistenceAccessContext access, params IInterceptor[] interceptors)
        {
            var fixtureConnection = new SqliteConnection(connectionString);
            var ownershipTransferred = false;
            try
            {
                fixtureConnection.Open();
                var options = new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(fixtureConnection);
                foreach (var interceptor in interceptors)
                    options.AddInterceptors(interceptor);
                var context = new BookmarkStateSqliteDbContext(options.Options);
                var codec = new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions
                {
                    SigningKey = "ef-runtime-test-recovery-signing-key-32-bytes",
                    AllowEphemeralDevelopmentKey = false
                }));
                var fixture = new Fixture(
                    context,
                    new EfWorkflowExecutableSourceReferenceStore(context, new Accessor(access), codec),
                    new EfWorkflowExecutableStore(context, new Accessor(access)),
                    new EfExecutableActivityTemplateStore(context, new Accessor(access), codec),
                    fixtureConnection);
                ownershipTransferred = true;
                return fixture;
            }
            finally
            {
                if (!ownershipTransferred)
                    fixtureConnection.Dispose();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await connection.DisposeAsync();
            if (databasePath is not null)
            {
                File.Delete(databasePath);
                File.Delete(databasePath + "-wal");
                File.Delete(databasePath + "-shm");
            }
        }
    }

    private sealed class Fixture(BookmarkStateSqliteDbContext context, EfWorkflowExecutableSourceReferenceStore store, EfWorkflowExecutableStore executable, EfExecutableActivityTemplateStore template, SqliteConnection connection) : IAsyncDisposable
    {
        public BookmarkStateSqliteDbContext Context { get; } = context;
        public EfWorkflowExecutableSourceReferenceStore Store { get; } = store;
        public EfWorkflowExecutableStore Executable { get; } = executable;
        public EfExecutableActivityTemplateStore Template { get; } = template;
        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class Accessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class ReaderCommandInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ThrowingReadInterceptor : DbCommandInterceptor
    {
        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result) =>
            throw new SqliteException("provider read failure", 1, 1);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<InterceptionResult<DbDataReader>>(new SqliteException("provider read failure", 1, 1));
    }

    private sealed class ThrowingSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("provider save failure");
    }

    private sealed class ThrowingInvalidOperationSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("provider save failure");
    }

    private sealed class RecreateBeforeSaveInterceptor(Func<Task> recreate) : SaveChangesInterceptor
    {
        private int invoked;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref invoked, 1) == 0)
                await recreate();
            return result;
        }
    }

    private sealed class RecreateAfterClaimReadInterceptor(Func<Task> recreate, int triggerAfterReaders = 2) : DbCommandInterceptor
    {
        private int readerCount;

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref readerCount);
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref readerCount) == triggerAfterReaders &&
                Interlocked.CompareExchange(ref readerCount, triggerAfterReaders + 1, triggerAfterReaders) == triggerAfterReaders)
                await recreate();
            return result;
        }
    }

    private sealed class RecreateAfterExecutableReadsInterceptor(Func<Task> recreate) : DbCommandInterceptor
    {
        private int readerCount;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref readerCount) == 2)
                await recreate();
            return result;
        }
    }

    private sealed class HideTemplateLookupInterceptor(int lookupCount) : DbCommandInterceptor
    {
        private int remaining = lookupCount;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Decrement(ref remaining) >= 0)
            {
                foreach (DbParameter parameter in command.Parameters)
                {
                    if (parameter.Value is string)
                        parameter.Value = "__hidden__";
                }
            }

            return result;
        }
    }

    private sealed class AlwaysUniqueTemplateSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<InterceptionResult<int>>(
                new DbUpdateException("template create conflict", new SqliteException("template create conflict", 19, 2067)));
    }
}
