using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeRecurringTriggerSchedulePostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_recurring_trigger_schedule_model_crud_query_transaction_and_cas() =>
        RuntimeRecurringTriggerScheduleProviderSmoke.RunAsync(fixture, connection =>
            new RuntimePostgreSqlDbContext(new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(connection).Options),
            RuntimePostgreSqlDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeRecurringTriggerScheduleSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_recurring_trigger_schedule_model_crud_query_transaction_and_cas() =>
        RuntimeRecurringTriggerScheduleProviderSmoke.RunAsync(fixture, connection =>
            new RuntimeSqlServerDbContext(new DbContextOptionsBuilder<RuntimeSqlServerDbContext>().UseSqlServer(connection).Options),
            RuntimeSqlServerDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeRecurringTriggerScheduleMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_recurring_trigger_schedule_model_crud_query_transaction_and_cas() =>
        RuntimeRecurringTriggerScheduleProviderSmoke.RunAsync(fixture, connection =>
            new RuntimeMySqlDbContext(new DbContextOptionsBuilder<RuntimeMySqlDbContext>().UseMySQL(connection).Options),
            RuntimeMySqlDbContext.ExpectedProviderName);
}

internal static class RuntimeRecurringTriggerScheduleProviderSmoke
{
    private const string SigningKey = "ef-runtime-r27-recurring-schedule-native-key-32-bytes";

    public static async Task RunAsync(
        RuntimeBookmarksProviderFixture fixture,
        Func<string, RuntimeDbContext> createContext,
        string expectedProvider)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var scope = $"native-r27-{Guid.NewGuid():N}";
        var now = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var lease = TimeSpan.FromMinutes(1);
        RecurringTriggerOccurrenceClaim lapsedClaim;

        await using (var context = createContext(fixture.ConnectionString))
        {
            Assert.Equal(expectedProvider, context.Database.ProviderName);
            await context.Database.EnsureCreatedAsync();
            var scheduleModel = context.Model.FindEntityType(typeof(RecurringTriggerScheduleEntity))!;
            Assert.Equal(RuntimeOperationalStateEfModule.RecurringScheduleIdProjectionMaximumLength,
                scheduleModel.FindProperty(nameof(RecurringTriggerScheduleEntity.ScheduleId))!.GetMaxLength());
            Assert.Equal(RuntimeOperationalStateEfModule.RecurringScheduleIdOrderKeyMaximumLength,
                scheduleModel.FindProperty(nameof(RecurringTriggerScheduleEntity.ScheduleIdOrderKey))!.GetMaxLength());
            var store = Store(context, scope);
            var early = Schedule("artifact-order", "a", now.AddMinutes(-2));
            var late = Schedule("artifact-order", "b", now.AddMinutes(-1));
            await store.SaveAsync(Schedule("artifact-order", "c", now.AddMinutes(-3)));
            await store.SaveAsync(late);
            await store.SaveAsync(early);

            var ordered = await store.ClaimDueAsync(new RecurringTriggerOccurrenceClaimRequest("native-order", now, lease, 10));
            Assert.Equal(new[] { "artifact-order:c", "artifact-order:a", "artifact-order:b" }, ordered.Select(x => x.Schedule.ScheduleId));
            lapsedClaim = ordered.First();
            var firstPage = await store.ListByArtifactPageAsync(new RecurringTriggerScheduleArtifactPageQuery("artifact-order", 2));
            Assert.Equal(2, firstPage.Items.Count);
            Assert.NotNull(firstPage.NextContinuationToken);
            Assert.Single((await store.ListByArtifactPageAsync(new RecurringTriggerScheduleArtifactPageQuery("artifact-order", 2, firstPage.NextContinuationToken))).Items);

            var boundaryArtifact = new string('%', RuntimeOperationalStateEfModule.IdentityMaximumLength);
            var boundaryNode = new string(':', RuntimeOperationalStateEfModule.IdentityMaximumLength);
            var boundaryActivation = new string('%', RuntimeOperationalStateEfModule.IdentityMaximumLength);
            var boundaryStimulusHash = new string(':', RuntimeOperationalStateEfModule.IdentityMaximumLength);
            var boundary = new RecurringTriggerSchedule(
                RecurringTriggerSchedule.BuildFanOutId(boundaryActivation, boundaryArtifact, boundaryNode, boundaryStimulusHash),
                boundaryArtifact,
                boundaryNode,
                "Timer",
                boundaryStimulusHash,
                RecurringScheduleKind.Interval,
                "PT1M",
                now,
                DateTimeOffset.UnixEpoch,
                boundaryActivation,
                "slot",
                true);
            Assert.Equal(RuntimeOperationalStateEfModule.RecurringScheduleIdMaximumLength, boundary.ScheduleId.Length);
            await store.SaveAsync(boundary);
            Assert.Equal(boundary, await store.FindAsync(boundary.ScheduleId));
            var boundaryClaim = Assert.Single(await store.ClaimDueAsync(new RecurringTriggerOccurrenceClaimRequest("native-boundary", now, lease, 10)));
            Assert.True(await store.SettleClaimAsync(boundaryClaim, now.AddMinutes(1)));
            await store.DeleteAsync(boundary.ScheduleId);
            Assert.Null(await store.FindAsync(boundary.ScheduleId));

            await using var transaction = await context.Database.BeginTransactionAsync();
            var rolledBack = Schedule("artifact-rollback", "node", now);
            await store.SaveAsync(rolledBack);
            await transaction.RollbackAsync();
        }

        await using (var verification = createContext(fixture.ConnectionString))
        {
            var store = Store(verification, scope);
            Assert.Null(await store.FindAsync(RecurringTriggerSchedule.BuildId("artifact-rollback", "node")));

            // The occurrence claim columns (#2198) round-trip: the claims taken above lapse, a re-claim fences them out, a
            // release after a failure counts it, and a settlement moves the cursor.
            var lapsed = now + lease;
            var failed = Assert.Single(await store.ClaimDueAsync(new RecurringTriggerOccurrenceClaimRequest("native-pump", lapsed, lease, 1)));
            Assert.Equal(RecurringTriggerSchedule.BuildId("artifact-order", "c"), failed.Schedule.ScheduleId);
            Assert.True(failed.FencingToken > lapsedClaim.FencingToken);
            Assert.False(await store.SettleClaimAsync(lapsedClaim, now));
            Assert.True(await store.ReleaseClaimAsync(failed, lapsed.AddSeconds(10)));
            var retried = Assert.Single(await store.ClaimDueAsync(new RecurringTriggerOccurrenceClaimRequest("native-pump", lapsed.AddSeconds(10), lease, 1)));
            Assert.Equal((failed.Schedule.ScheduleId, 1), (retried.Schedule.ScheduleId, retried.FailureCount));
            Assert.True(await store.SettleClaimAsync(retried, now.AddMinutes(5)));
            Assert.Equal(now.AddMinutes(5), (await store.FindAsync(failed.Schedule.ScheduleId))!.NextOccurrence);

            var activationSchedule = Schedule("artifact-activation", "node", now, "activation", "slot");
            await store.PrepareActivationAsync("activation", [activationSchedule]);
            Assert.False((await store.FindAsync(activationSchedule.ScheduleId))!.IsActive);
            await store.ActivateAsync("activation", null);
            Assert.True((await store.FindAsync(activationSchedule.ScheduleId))!.IsActive);

            for (var index = 0; index < 257; index++)
            {
                var activationId = $"many-{index:D3}";
                await store.PrepareActivationAsync(activationId,
                    [Schedule("artifact-many", $"node-{index:D3}", now, activationId, "slot")]);
            }
            await store.DeleteByArtifactAsync("artifact-many");
            Assert.Empty((await store.ListByArtifactPageAsync(new RecurringTriggerScheduleArtifactPageQuery("artifact-many", 10))).Items);
        }
    }

    private static EfRecurringTriggerScheduleStore Store(RuntimeDbContext context, string scope) =>
        new(context, new FixedAccessor(scope), new HmacRuntimeRecoveryContinuationCodec(
            Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = SigningKey })));

    private static RecurringTriggerSchedule Schedule(string artifact, string node, DateTimeOffset next, string? activation = null, string? slot = null) =>
        new(activation is null ? RecurringTriggerSchedule.BuildId(artifact, node) : RecurringTriggerSchedule.BuildId(activation, artifact, node), artifact, node, "Timer", "hash-" + node, RecurringScheduleKind.Interval, "PT1M", next, DateTimeOffset.UnixEpoch, activation, slot);

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
