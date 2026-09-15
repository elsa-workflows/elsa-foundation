using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
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
            new BookmarkStatePostgreSqlDbContext(new DbContextOptionsBuilder<BookmarkStatePostgreSqlDbContext>().UseNpgsql(connection).Options),
            BookmarkStatePostgreSqlDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeRecurringTriggerScheduleSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_recurring_trigger_schedule_model_crud_query_transaction_and_cas() =>
        RuntimeRecurringTriggerScheduleProviderSmoke.RunAsync(fixture, connection =>
            new BookmarkStateSqlServerDbContext(new DbContextOptionsBuilder<BookmarkStateSqlServerDbContext>().UseSqlServer(connection).Options),
            BookmarkStateSqlServerDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeRecurringTriggerScheduleMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_recurring_trigger_schedule_model_crud_query_transaction_and_cas() =>
        RuntimeRecurringTriggerScheduleProviderSmoke.RunAsync(fixture, connection =>
            new BookmarkStateMySqlDbContext(new DbContextOptionsBuilder<BookmarkStateMySqlDbContext>().UseMySQL(connection).Options),
            BookmarkStateMySqlDbContext.ExpectedProviderName);
}

internal static class RuntimeRecurringTriggerScheduleProviderSmoke
{
    private const string SigningKey = "ef-runtime-r27-recurring-schedule-native-key-32-bytes";

    public static async Task RunAsync(
        RuntimeBookmarksProviderFixture fixture,
        Func<string, BookmarkStateDbContext> createContext,
        string expectedProvider)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var scope = $"native-r27-{Guid.NewGuid():N}";
        var now = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

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

            Assert.Equal(new[] { "artifact-order:c", "artifact-order:a", "artifact-order:b" },
                (await store.ListDueAsync(now, 10)).Select(x => x.ScheduleId));
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
            Assert.True(await store.TryAdvanceAsync(boundary.ScheduleId, now, now.AddMinutes(1)));
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
            var expected = Schedule("artifact-order", "a", now.AddMinutes(-2)).NextOccurrence;
            Assert.True(await store.TryAdvanceAsync(RecurringTriggerSchedule.BuildId("artifact-order", "a"), expected, now));
            Assert.False(await store.TryAdvanceAsync(RecurringTriggerSchedule.BuildId("artifact-order", "a"), expected, now));
            Assert.Equal(now, (await store.FindAsync(RecurringTriggerSchedule.BuildId("artifact-order", "a")))!.NextOccurrence);

            var activationSchedule = Schedule("artifact-activation", "node", now, "activation", "slot");
            await store.PrepareActivationAsync("activation", [activationSchedule]);
            Assert.DoesNotContain(RecurringTriggerSchedule.BuildId("activation", "artifact-activation", "node"),
                (await store.ListDueAsync(now, 10)).Select(x => x.ScheduleId));
            await store.ActivateAsync("activation", null);
            Assert.Contains(RecurringTriggerSchedule.BuildId("activation", "artifact-activation", "node"),
                (await store.ListDueAsync(now, 10)).Select(x => x.ScheduleId));

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

    private static EfRecurringTriggerScheduleStore Store(BookmarkStateDbContext context, string scope) =>
        new(context, new FixedAccessor(scope), new HmacRuntimeRecoveryContinuationCodec(
            Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = SigningKey })));

    private static RecurringTriggerSchedule Schedule(string artifact, string node, DateTimeOffset next, string? activation = null, string? slot = null) =>
        new(activation is null ? RecurringTriggerSchedule.BuildId(artifact, node) : RecurringTriggerSchedule.BuildId(activation, artifact, node), artifact, node, "Timer", "hash-" + node, RecurringScheduleKind.Interval, "PT1M", next, DateTimeOffset.UnixEpoch, activation, slot);

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
