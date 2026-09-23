using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Publishing;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

public sealed class EfPublicationPolicyProjectionStoreTests
{
    [Fact]
    public async Task Policy_round_trip_uses_collision_safe_host_and_definition_keys_and_revision_cas()
    {
        await using var database = await Database.CreateAsync();
        var host = Policy(null, "host-slot");
        await using (var context = database.Context())
        {
            var store = database.Policies(context, "tenant-a");
            var created = await store.TrySaveAsync(host, 0);
            Assert.True(created.Succeeded);
            Assert.Equal(1, created.Policy.Revision);

            var loser = await store.TrySaveAsync(host with { DefaultSlotName = "loser" }, 0);
            Assert.False(loser.Succeeded);
            Assert.Equal("host-slot", loser.Policy.DefaultSlotName);

            var definitionNamedHost = Policy("host", "definition-slot");
            Assert.True((await store.TrySaveAsync(definitionNamedHost, 0)).Succeeded);
            Assert.NotEqual(
                (await store.FindAsync(null))!.DefaultSlotName,
                (await store.FindAsync("host"))!.DefaultSlotName);
        }

        await using var restarted = database.Context();
        var loaded = await database.Policies(restarted, "tenant-a").FindAsync("host");
        Assert.Equal("definition-slot", loaded!.DefaultSlotName);
        Assert.Equal(1, loaded.Revision);
    }

    [Fact]
    public async Task Policy_update_race_returns_a_truthful_winner_without_corrupting_the_row()
    {
        await using var database = await Database.CreateAsync();
        await using (var setup = database.Context())
            await database.Policies(setup, "tenant-a").TrySaveAsync(Policy("cas", "first"), 0);

        await using var first = database.Context();
        await using var second = database.Context();
        var firstStore = database.Policies(first, "tenant-a");
        var secondStore = database.Policies(second, "tenant-a");
        var results = await Task.WhenAll(
            firstStore.TrySaveAsync(Policy("cas", "winner-a"), 1).AsTask(),
            secondStore.TrySaveAsync(Policy("cas", "winner-b"), 1).AsTask());

        Assert.Single(results, result => result.Succeeded);
        var winner = results.Single(result => result.Succeeded).Policy;
        Assert.Equal(2, winner.Revision);
        await using var verify = database.Context();
        var persisted = await database.Policies(verify, "tenant-a").FindAsync("cas");
        Assert.Equal(winner, persisted);
    }

    [Fact]
    public async Task Opaque_utf16_identities_are_encoded_round_trip_and_cas_safe()
    {
        await using var database = await Database.CreateAsync();
        const string tenant = "tenant-\0-\uD800";
        const string definition = "definition-\0-\uD801";
        const string slot = "slot-\0-\uD802";
        const string intentId = "intent-\0-\uD803";
        const string publicationId = "publication-\0-\uD804";
        const string projectionKind = "kind-\0-\uD805";
        const string failureCode = "failure-\0-\uD806";
        const string failureMessage = "message-\0-\uD807";

        var policy = Policy(definition, slot);
        var intent = Intent(intentId, publicationId) with
        {
            ProjectionKind = projectionKind,
            Status = PublicationProjectionIntentStatus.Failed,
            AttemptCount = 2,
            LastFailure = new PublicationFailure(failureCode, failureMessage)
        };
        await using (var context = database.Context())
        {
            var policyStore = database.Policies(context, tenant);
            var intentStore = database.Intents(context, tenant);
            Assert.True((await policyStore.TrySaveAsync(policy, 0)).Succeeded);
            await intentStore.SaveAsync(intent);

            var storedPolicy = await context.Policies.AsNoTracking().SingleAsync();
            Assert.Equal(EfRelationalIdentity.Encode(tenant), storedPolicy.TenantId);
            Assert.Equal(EfRelationalIdentity.Encode($"workflow:{definition.Length}:{definition}"), storedPolicy.PolicyKey);
            Assert.Equal(EfRelationalIdentity.Encode(definition), storedPolicy.WorkflowDefinitionId);
            Assert.Equal(EfRelationalIdentity.Encode(slot), storedPolicy.DefaultSlotName);

            var storedIntent = await context.ProjectionIntents.AsNoTracking().SingleAsync();
            Assert.Equal(EfRelationalIdentity.Encode(tenant), storedIntent.TenantId);
            Assert.Equal(EfRelationalIdentity.Encode(intentId), storedIntent.IntentId);
            Assert.Equal(EfRelationalIdentity.Encode(publicationId), storedIntent.PublicationId);
            Assert.Equal(EfRelationalIdentity.Encode(projectionKind), storedIntent.ProjectionKind);
            Assert.Equal(EfRelationalIdentity.Encode(failureCode), storedIntent.LastFailureCode);
            Assert.Equal(EfRelationalIdentity.Encode(failureMessage), storedIntent.LastFailureMessage);

            Assert.Equal(policy with { Revision = 1 }, (await policyStore.FindAsync(definition)));
            Assert.Equal(intent, (await intentStore.FindAsync(intentId)));
            Assert.Equal(intent, Assert.Single(await intentStore.ListByPublicationAsync(publicationId)));

            var transitioned = intent with { Status = PublicationProjectionIntentStatus.Delivering, AttemptCount = 3 };
            var cas = await intentStore.TryTransitionAsync(transitioned, PublicationProjectionIntentStatus.Failed);
            Assert.True(cas.Succeeded);
            Assert.Equal(transitioned, cas.Intent);
        }

        await using var restarted = database.Context();
        Assert.Equal(policy with { Revision = 1 }, await database.Policies(restarted, tenant).FindAsync(definition));
        Assert.Equal(intent with { Status = PublicationProjectionIntentStatus.Delivering, AttemptCount = 3 },
            await database.Intents(restarted, tenant).FindAsync(intentId));
    }

    [Fact]
    public async Task Hash_only_candidates_fail_closed_when_the_encoded_residual_does_not_match()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        var policyStore = database.Policies(context, "tenant-a");
        await policyStore.TrySaveAsync(Policy("definition", "slot"), 0);
        var policyRow = await context.Policies.SingleAsync();
        policyRow.PolicyKey = EfRelationalIdentity.Encode("workflow:9:not-the-key");
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => policyStore.FindAsync("definition").AsTask());

        var intentStore = database.Intents(context, "tenant-a");
        var intent = Intent("intent", "publication");
        await intentStore.SaveAsync(intent);
        var intentRow = await context.ProjectionIntents.SingleAsync();
        intentRow.PublicationId = EfRelationalIdentity.Encode("not-the-publication");
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => intentStore.ListByPublicationAsync(intent.PublicationId).AsTask());
    }

    [Fact]
    public async Task Projection_intents_are_idempotent_restart_safe_scoped_and_ordered_by_full_ordinal_identity()
    {
        await using var database = await Database.CreateAsync();
        var longId = new string('x', PublishingPolicyProjectionEfModule.IdentityMaximumLength);
        await using (var context = database.Context())
        {
            var store = database.Intents(context, "tenant-a");
            var first = Intent("z", "publication-1");
            var second = Intent("a", "publication-1");
            var longIntent = Intent(longId, "publication-1") with
            {
                Status = PublicationProjectionIntentStatus.Failed,
                AttemptCount = 2,
                NextAttemptAt = database.Now.AddMinutes(2),
                LastFailure = new PublicationFailure("failed", "retry")
            };
            await store.SaveAsync(first);
            await store.SaveAsync(first);
            await store.SaveAsync(second);
            await store.SaveAsync(longIntent);

            var listed = await store.ListByPublicationAsync("publication-1");
            Assert.Equal(new[] { "a", longId, "z" }, listed.Select(intent => intent.IntentId));
            Assert.Equal(longIntent, listed.Single(intent => intent.IntentId == longId));

            var delivering = first with { Status = PublicationProjectionIntentStatus.Delivering, AttemptCount = 1 };
            Assert.True((await store.TryTransitionAsync(delivering, PublicationProjectionIntentStatus.Pending)).Succeeded);
            var stale = await store.TryTransitionAsync(first, PublicationProjectionIntentStatus.Pending);
            Assert.False(stale.Succeeded);
            Assert.Equal(PublicationProjectionIntentStatus.Delivering, stale.Intent.Status);
            Assert.Throws<InvalidOperationException>(() => store.TryTransitionAsync(
                delivering with { PublicationId = "other-publication" },
                PublicationProjectionIntentStatus.Delivering).AsTask().GetAwaiter().GetResult());
        }

        await using (var restarted = database.Context())
        {
            var store = database.Intents(restarted, "tenant-a");
            var retry = await store.FindAsync(longId);
            Assert.Equal(2, retry!.AttemptCount);
            Assert.Equal("failed", retry.LastFailure!.Code);
            Assert.Equal(database.Now.AddMinutes(2), retry.NextAttemptAt);
            Assert.Empty(await database.Intents(restarted, "tenant-b").ListByPublicationAsync("publication-1"));
        }
    }

    [Fact]
    public async Task Explicit_global_access_is_separate_from_scoped_policy_and_intent_rows()
    {
        await using var database = await Database.CreateAsync();
        await using (var context = database.Context())
        {
            var globalPolicies = database.Policies(context, PersistenceAccessContext.Global);
            var globalIntents = database.Intents(context, PersistenceAccessContext.Global);
            Assert.True((await globalPolicies.TrySaveAsync(Policy(null, "global"), 0)).Succeeded);
            await globalIntents.SaveAsync(Intent("global-intent", "publication-global"));

            Assert.NotNull(await globalPolicies.FindAsync(null));
            Assert.NotNull(await globalIntents.FindAsync("global-intent"));
        }

        await using (var scoped = database.Context())
        {
            Assert.Null(await database.Policies(scoped, "tenant-a").FindAsync(null));
            Assert.Null(await database.Intents(scoped, "tenant-a").FindAsync("global-intent"));
        }
    }

    [Fact]
    public async Task Projection_intent_status_cas_returns_one_winner_and_rereads_the_loser()
    {
        await using var database = await Database.CreateAsync();
        var pending = Intent("transition-race", "publication-race");
        await using (var setup = database.Context())
            await database.Intents(setup, "tenant-a").SaveAsync(pending);

        await using var first = database.Context();
        await using var second = database.Context();
        var delivering = pending with { Status = PublicationProjectionIntentStatus.Delivering, AttemptCount = 1 };
        var results = await Task.WhenAll(
            database.Intents(first, "tenant-a").TryTransitionAsync(delivering, PublicationProjectionIntentStatus.Pending).AsTask(),
            database.Intents(second, "tenant-a").TryTransitionAsync(delivering, PublicationProjectionIntentStatus.Pending).AsTask());

        Assert.Single(results, result => result.Succeeded);
        Assert.All(results, result => Assert.Equal(PublicationProjectionIntentStatus.Delivering, result.Intent.Status));
        await using var verify = database.Context();
        Assert.Equal(delivering, await database.Intents(verify, "tenant-a").FindAsync(pending.IntentId));
    }

    [Fact]
    public async Task Stored_projection_drift_is_rejected_before_returning_intent()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        context.ProjectionIntents.Add(new PublicationProjectionIntentEntity
        {
            Id = "drift",
            IntentId = "intent",
            IntentIdHash = EfRelationalIdentity.Hash("intent"),
            IntentIdOrderKey = [1],
            PublicationId = "publication",
            PublicationIdHash = EfRelationalIdentity.Hash("publication"),
            ProjectionKind = "Triggers",
            ProjectionKindHash = EfRelationalIdentity.Hash("Triggers"),
            Operation = nameof(PublicationProjectionOperation.Prepare),
            Status = nameof(PublicationProjectionIntentStatus.Pending),
            TenantId = "tenant-a",
            TenantIdHash = EfRelationalIdentity.Hash("tenant-a"),
            SchemaVersion = PublishingPolicyProjectionEfModule.SchemaVersion,
            Revision = 1
        });
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Intents(context, "tenant-a").FindAsync("intent").AsTask());
    }

    [Fact]
    public async Task A_failed_policy_write_is_not_committed_by_a_later_save_on_the_same_context()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        var policies = database.Policies(context, "tenant-a");
        Assert.True((await policies.TrySaveAsync(Policy("zombie", "original"), 0)).Succeeded);

        // A non-concurrency failure such as a deadlock or timeout: the caller sees the write fail.
        await context.Database.ExecuteSqlRawAsync(
            $"CREATE TRIGGER reject_policy_update BEFORE UPDATE ON {PublishingPolicyProjectionEfModule.PolicyTableName} BEGIN SELECT RAISE(ABORT, 'transient'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => policies.TrySaveAsync(Policy("zombie", "failed"), 1).AsTask());
        await context.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_policy_update;");

        await database.Intents(context, "tenant-a").SaveAsync(Intent("unrelated", "publication"));

        await using var verify = database.Context();
        Assert.Equal("original", (await database.Policies(verify, "tenant-a").FindAsync("zombie"))!.DefaultSlotName);
    }

    [Fact]
    public async Task Policy_rows_written_by_another_module_version_report_skew_not_corruption()
    {
        await using var database = await Database.CreateAsync();
        await using (var setup = database.Context())
        {
            var store = database.Policies(setup, "tenant-a");
            Assert.True((await store.TrySaveAsync(Policy("readable", "slot"), 0)).Succeeded);
            Assert.True((await store.TrySaveAsync(Policy("drift-schema", "slot"), 0)).Succeeded);
            // A second write takes the update path, which has to stamp the column too.
            Assert.True((await store.TrySaveAsync(Policy("readable", "updated-slot"), 1)).Succeeded);
        }

        await using var context = database.Context();
        Assert.Equal([PublishingPolicyProjectionEfModule.SchemaVersion],
            await context.Policies.AsNoTracking().Select(row => row.SchemaVersion).Distinct().ToArrayAsync());
        await context.Policies
            .Where(row => row.WorkflowDefinitionId == EfRelationalIdentity.Encode("drift-schema"))
            .ExecuteUpdateAsync(row => row.SetProperty(x => x.SchemaVersion, "2.0.0"));

        await using var verify = database.Context();
        var store2 = database.Policies(verify, "tenant-a");
        // Stamping is what makes the refusal meaningful: an unmolested row still round-trips.
        Assert.Equal("updated-slot", (await store2.FindAsync("readable"))!.DefaultSlotName);
        await AssertSkewAsync(() => store2.FindAsync("drift-schema").AsTask(), "2.0.0");
    }

    [Fact]
    public async Task Projection_intent_rows_written_by_another_module_version_report_skew_not_corruption()
    {
        await using var database = await Database.CreateAsync();
        var readable = Intent("readable", "publication-1");
        await using (var setup = database.Context())
        {
            var store = database.Intents(setup, "tenant-a");
            await store.SaveAsync(readable);
            await store.SaveAsync(Intent("drift-schema", "publication-1"));
            // A transition takes the update path, which has to stamp the column too.
            Assert.True((await store.TryTransitionAsync(
                readable with { Status = PublicationProjectionIntentStatus.Delivering, AttemptCount = 1 },
                PublicationProjectionIntentStatus.Pending)).Succeeded);
        }

        await using var context = database.Context();
        Assert.Equal([PublishingPolicyProjectionEfModule.SchemaVersion],
            await context.ProjectionIntents.AsNoTracking().Select(row => row.SchemaVersion).Distinct().ToArrayAsync());
        await context.ProjectionIntents
            .Where(row => row.IntentId == EfRelationalIdentity.Encode("drift-schema"))
            .ExecuteUpdateAsync(row => row.SetProperty(x => x.SchemaVersion, "2.0.0"));

        await using var verify = database.Context();
        var store2 = database.Intents(verify, "tenant-a");
        // Stamping is what makes the refusal meaningful: an unmolested row still round-trips.
        Assert.Equal(PublicationProjectionIntentStatus.Delivering, (await store2.FindAsync("readable"))!.Status);
        await AssertSkewAsync(() => store2.FindAsync("drift-schema").AsTask(), "2.0.0");
        await AssertSkewAsync(() => store2.ListByPublicationAsync("publication-1").AsTask(), "2.0.0");
    }

    private static async Task AssertSkewAsync(Func<Task> read, string found)
    {
        // A version this build does not run wrote the row. Nothing is damaged, so reporting it as
        // corruption would send an operator looking for data damage that does not exist (ADR 0077).
        var skew = await Assert.ThrowsAsync<EfSchemaVersionSkewException>(read);
        Assert.Equal("PublishingPolicyProjection", skew.Module);
        Assert.Equal(found, skew.Found);
        Assert.Equal(PublishingPolicyProjectionEfModule.SchemaVersion, skew.Expected);
    }

    private static PublicationPolicy Policy(string? definitionId, string slot) =>
        new(definitionId, PublicationPolicyDefaultAction.ReplaceDefaultSlot, slot, 0, new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.FromHours(2)));

    private static PublicationProjectionIntent Intent(string intentId, string publicationId) =>
        new(intentId, publicationId, PublicationProjectionKinds.TriggerBindings, PublicationProjectionOperation.Prepare, PublicationProjectionIntentStatus.Pending, 0, null, null);

    private sealed class Database : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public DateTimeOffset Now { get; } = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
        private Database(SqliteConnection connection) => this.connection = connection;
        public static async Task<Database> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var database = new Database(connection);
            await using var context = database.Context();
            await context.Database.EnsureCreatedAsync();
            return database;
        }
        public PublishingSnapshotReviewSqliteDbContext Context() =>
            new(new DbContextOptionsBuilder<PublishingSnapshotReviewSqliteDbContext>().UseSqlite(connection).Options);
        public EfPublicationPolicyStore Policies(PublishingSnapshotReviewDbContext context, string tenant) =>
            Policies(context, PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));
        public EfPublicationPolicyStore Policies(PublishingSnapshotReviewDbContext context, PersistenceAccessContext access) =>
            new(context, new FixedAccess(access));
        public EfPublicationProjectionIntentStore Intents(PublishingSnapshotReviewDbContext context, string tenant) =>
            Intents(context, PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));
        public EfPublicationProjectionIntentStore Intents(PublishingSnapshotReviewDbContext context, PersistenceAccessContext access) =>
            new(context, new FixedAccess(access));
        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    private sealed class FixedAccess(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class ForeignPolicyStore : IPublicationPolicyStore
    {
        public ValueTask<PublicationPolicy?> FindAsync(string? workflowDefinitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<PublicationPolicyWriteResult> TrySaveAsync(PublicationPolicy policy, long expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
