using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using System.Data.Common;
using System.Text.Json;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfRuntimeArtifactScopeTests
{
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

        var row = await fixture.Context.WorkflowExecutableSourceReferences.SingleAsync(x => x.ArtifactId == "artifact-b");
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

        var row = await fixture.Context.ExecutableActivityTemplates.SingleAsync(x => x.TemplateId == "template-a");
        row.ContentJson = "{}";
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Template.FindAsync("template-a").AsTask());
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
        await using var fixture = database.Open("tenant-a");
        var original = Reference("recreated-ref", "artifact-a");
        await fixture.Store.SaveAsync(original);
        var stale = await fixture.Store.FindAsync(original.SourceReferenceId);
        Assert.NotNull(stale?.ConcurrencyToken);

        Assert.True(await fixture.Store.DeleteAsync(original.SourceReferenceId));
        await fixture.Store.SaveAsync(original);
        var successor = await fixture.Store.FindAsync(original.SourceReferenceId);
        Assert.NotEqual(stale!.ConcurrencyToken, successor!.ConcurrencyToken);

        Assert.False(await fixture.Store.TryRetireAsync(stale, stale.Retire(DateTimeOffset.UtcNow, "stale")));
        Assert.Null((await fixture.Store.FindAsync(original.SourceReferenceId))!.DeletedAt);
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
    public async Task Executable_save_is_idempotent_and_batch_failure_rolls_back_new_rows()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");

        await fixture.Executable.SaveAsync(Executable("same-artifact"));
        await fixture.Executable.SaveAsync(Executable("same-artifact"));
        Assert.NotNull(await fixture.Executable.FindAsync("same-artifact"));

        await fixture.Executable.SaveAsync(Executable("incomplete"));
        var coordination = await fixture.Context.WorkflowExecutableCoordinations
            .SingleAsync(x => x.ArtifactId == "incomplete");
        fixture.Context.WorkflowExecutableCoordinations.Remove(coordination);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Executable.SaveBatchAsync(
            [Executable("new-artifact"), Executable("incomplete")]).AsTask());
        Assert.Null(await fixture.Executable.FindAsync("new-artifact"));
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

        var row = await other.Context.WorkflowExecutableCoordinations.SingleAsync(x => x.ArtifactId == "guarded");
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

        var row = await other.Context.WorkflowExecutableCoordinations.SingleAsync(x => x.ArtifactId == "conflict");
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
        var executable = await fixture.Context.WorkflowExecutables.SingleAsync(x => x.ArtifactId == "orphan");
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
        var corrupt = await fixture.Context.WorkflowExecutables.SingleAsync(x => x.ArtifactId == "corrupt-executable");
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
            "{\"Leases\":{\"one\":{\"Id\":\"one\",\"Token\":\"token\",\"ExpiresAt\":\"0001-01-01T00:00:00+00:00\"}},\"Guard\":null}",
            "{\"Leases\":{},\"Guard\":{\"OperationId\":\"\",\"Token\":\"token\",\"ExpiresAt\":\"2030-01-01T00:00:00+00:00\"}}",
            "{\"Leases\":{},\"leases\":{},\"Guard\":null}"
        };

        foreach (var payload in payloads)
        {
            await using var database = await Database.CreateAsync();
            await using var fixture = database.Open("tenant-a");
            await fixture.Executable.SaveAsync(Executable("corrupt"));
            var row = await fixture.Context.WorkflowExecutableCoordinations.SingleAsync(x => x.ArtifactId == "corrupt");
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
            .SingleAsync(x => x.ArtifactId == "case-sensitive-leases");
        row.ContentJson = "{\"Leases\":{\"A\":{\"Id\":\"A\",\"Token\":\"token-a\",\"ExpiresAt\":\"2030-01-01T00:00:00+00:00\"},\"a\":{\"Id\":\"a\",\"Token\":\"token-b\",\"ExpiresAt\":\"2030-01-01T00:00:00+00:00\"}},\"Guard\":null}";
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

    private static WorkflowExecutable Executable(string artifactId)
    {
        var node = new ExecutableNode("node", "node", "test", "1", "consumer", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), outputCaptures: new Dictionary<string, RuntimeOutputCapture>());
        return new WorkflowExecutable(new WorkflowExecutableIdentity(artifactId, "definition", "version", "1", $"hash-{artifactId}"), node, new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UtcNow, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
    }

    private static ExecutableActivityTemplate Template(string id, string hash)
    {
        var node = new ExecutableNode("node", "node", "test", "1", "consumer", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), outputCaptures: new Dictionary<string, RuntimeOutputCapture>());
        return new ExecutableActivityTemplate(id, hash, node, new Dictionary<string, WorkflowExecutableResumeTarget>(), [], [], [], "fingerprint", new Dictionary<string, string>(), DateTimeOffset.UtcNow);
    }

    private sealed class Database : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private Database(SqliteConnection connection) => this.connection = connection;

        public static async Task<Database> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            await using var context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            return new Database(connection);
        }

        public Fixture Open(string scope, ReaderCommandInterceptor? interceptor = null) => Open(PersistenceAccessContext.Scoped(new PersistenceScope(scope)), interceptor);

        public Fixture Open(PersistenceAccessContext access, ReaderCommandInterceptor? interceptor = null)
        {
            var options = new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection);
            if (interceptor is not null)
                options.AddInterceptors(interceptor);
            var context = new BookmarkStateSqliteDbContext(options.Options);
            var codec = new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions
            {
                SigningKey = "ef-runtime-test-recovery-signing-key-32-bytes",
                AllowEphemeralDevelopmentKey = false
            }));
            return new Fixture(
                context,
                new EfWorkflowExecutableSourceReferenceStore(context, new Accessor(access), codec),
                new EfWorkflowExecutableStore(context, new Accessor(access)),
                new EfExecutableActivityTemplateStore(context, new Accessor(access), codec));
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    private sealed class Fixture(BookmarkStateSqliteDbContext context, EfWorkflowExecutableSourceReferenceStore store, EfWorkflowExecutableStore executable, EfExecutableActivityTemplateStore template) : IAsyncDisposable
    {
        public BookmarkStateSqliteDbContext Context { get; } = context;
        public EfWorkflowExecutableSourceReferenceStore Store { get; } = store;
        public EfWorkflowExecutableStore Executable { get; } = executable;
        public EfExecutableActivityTemplateStore Template { get; } = template;
        public ValueTask DisposeAsync() => Context.DisposeAsync();
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
}
