using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfRecurringTriggerScheduleStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SQLite_round_trips_the_maximum_escaped_activation_fanout_schedule_id()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = Context(connection);
        await context.Database.EnsureCreatedAsync();
        var store = Store(context, "tenant-a");
        var artifact = new string('%', RuntimeOperationalStateEfModule.IdentityMaximumLength);
        var node = new string(':', RuntimeOperationalStateEfModule.IdentityMaximumLength);
        var activation = new string('%', RuntimeOperationalStateEfModule.IdentityMaximumLength);
        var stimulusHash = new string(':', RuntimeOperationalStateEfModule.IdentityMaximumLength);
        var schedule = new RecurringTriggerSchedule(
            RecurringTriggerSchedule.BuildFanOutId(activation, artifact, node, stimulusHash),
            artifact,
            node,
            "Timer",
            stimulusHash,
            RecurringScheduleKind.Interval,
            "PT1M",
            Now,
            DateTimeOffset.UnixEpoch,
            activation,
            "slot",
            true);

        Assert.Equal(RuntimeOperationalStateEfModule.RecurringScheduleIdMaximumLength, schedule.ScheduleId.Length);
        await store.SaveAsync(schedule);
        Assert.Equal(schedule, await store.FindAsync(schedule.ScheduleId));
        Assert.True(await store.TryAdvanceAsync(schedule.ScheduleId, Now, Now.AddMinutes(1)));
        Assert.Equal(Now.AddMinutes(1), (await store.FindAsync(schedule.ScheduleId))!.NextOccurrence);
        await store.DeleteAsync(schedule.ScheduleId);
        Assert.Null(await store.FindAsync(schedule.ScheduleId));

        await store.SaveAsync(schedule);
        await store.PrepareActivationAsync(activation, [schedule]);
        Assert.Equal(schedule.ScheduleId, Assert.Single((await store.ListByActivationPageAsync(new RecurringTriggerScheduleActivationPageQuery(activation))).Items).ScheduleId);
        await store.ActivateAsync(activation, null);
        Assert.True(await store.TryAdvanceAsync(schedule.ScheduleId, Now, Now.AddMinutes(1)));
        await store.DeleteByActivationAsync(activation);
        Assert.Null(await store.FindAsync(schedule.ScheduleId));
    }

    [Fact]
    public async Task SQLite_round_trips_due_pages_scope_isolation_and_compare_and_swap()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = Context(connection);
        await context.Database.EnsureCreatedAsync();
        var store = Store(context, "tenant-a");
        var early = Schedule("artifact-a", "early", Now.AddMinutes(-5));
        var sameA = Schedule("artifact-a", "a", Now.AddMinutes(-2));
        var sameB = Schedule("artifact-a", "b", Now.AddMinutes(-2));
        await store.SaveAsync(early);
        await store.SaveAsync(sameA);
        await store.SaveAsync(sameB);
        await store.SaveAsync(Schedule("artifact-a", "future", Now.AddMinutes(10)));

        Assert.Equal([early.ScheduleId, sameA.ScheduleId, sameB.ScheduleId], (await store.ListDueAsync(Now, 10)).Select(x => x.ScheduleId));
        Assert.Equal([early.ScheduleId, sameA.ScheduleId], (await store.ListDueAsync(Now, 2)).Select(x => x.ScheduleId));
        var firstPage = await store.ListByArtifactPageAsync(new RecurringTriggerScheduleArtifactPageQuery("artifact-a", 2));
        Assert.Equal(2, firstPage.Items.Count);
        Assert.NotNull(firstPage.NextContinuationToken);
        Assert.Equal(2, (await store.ListByArtifactPageAsync(new RecurringTriggerScheduleArtifactPageQuery("artifact-a", 2, firstPage.NextContinuationToken))).Items.Count);

        Assert.False(await store.TryAdvanceAsync(early.ScheduleId, Now.AddMinutes(-4), Now));
        Assert.True(await store.TryAdvanceAsync(early.ScheduleId, early.NextOccurrence, Now));
        Assert.False(await store.TryAdvanceAsync(early.ScheduleId, early.NextOccurrence, Now));
        Assert.Equal(Now, (await store.FindAsync(early.ScheduleId))!.NextOccurrence);

        var other = Store(context, "tenant-b");
        await other.SaveAsync(early);
        Assert.Equal(4, (await store.ListByArtifactPageAsync(new RecurringTriggerScheduleArtifactPageQuery("artifact-a", 10))).Items.Count);
        Assert.NotNull(await other.FindAsync(early.ScheduleId));
    }

    [Fact]
    public async Task SQLite_activation_lifecycle_is_atomic_and_direct_mutation_is_rejected()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = Context(connection);
        await context.Database.EnsureCreatedAsync();
        var store = Store(context, "tenant-a");
        var old = Schedule("artifact-old", "old", Now.AddMinutes(-1), "publication-old", "slot-old");
        var oldSecond = Schedule("artifact-old", "old-2", Now.AddMinutes(1), "publication-old", "slot-old");
        var replacement = Schedule("artifact-new", "new", Now.AddMinutes(1), "publication-new", "slot-new");

        await store.PrepareActivationAsync("publication-old", [old, oldSecond]);
        await store.PrepareActivationAsync("publication-new", [replacement]);
        Assert.All((await store.ListByActivationPageAsync(new RecurringTriggerScheduleActivationPageQuery("publication-old"))).Items, x => Assert.False(x.IsActive));
        Assert.Empty(await store.ListDueAsync(Now, 10));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(old with { Expression = "PT2M" }).AsTask());

        await store.ActivateAsync("publication-old", null);
        Assert.Single(await store.ListDueAsync(Now, 10));
        await store.ActivateAsync("publication-new", "publication-old");
        Assert.True(Assert.Single(await ((IRecurringTriggerScheduleStore)store).ListByActivationAsync("publication-new")).IsActive);
        Assert.All(await ((IRecurringTriggerScheduleStore)store).ListByActivationAsync("publication-old"), x => Assert.False(x.IsActive));

        await store.DeleteByActivationAsync("publication-new");
        Assert.Empty(await ((IRecurringTriggerScheduleStore)store).ListByActivationAsync("publication-new"));
        Assert.Equal(2, (await ((IRecurringTriggerScheduleStore)store).ListByActivationAsync("publication-old")).Count);
        await store.DeleteByArtifactAsync("artifact-old");
        Assert.Empty(await ((IRecurringTriggerScheduleStore)store).ListByActivationAsync("publication-old"));
    }

    [Fact]
    public async Task SQLite_rejects_corrupt_projection_and_mixed_artifact_preparation_without_writes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = Context(connection);
        await context.Database.EnsureCreatedAsync();
        var store = Store(context, "tenant-a");
        var first = Schedule("artifact-a", "first", Now, "publication", "slot");
        var second = Schedule("artifact-b", "second", Now, "publication", "slot");
        await Assert.ThrowsAsync<ArgumentException>(() => store.PrepareActivationAsync("publication", [first, second]).AsTask());
        Assert.Empty(await ((IRecurringTriggerScheduleStore)store).ListByActivationAsync("publication"));

        await store.SaveAsync(Schedule("artifact-corrupt", "node", Now, null, null));
        var row = await context.RecurringTriggerSchedules.SingleAsync(x => x.ArtifactId == EfRelationalIdentity.Encode("artifact-corrupt"));
        row.ArtifactIdHash = "corrupt";
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ListDueAsync(Now, 10).AsTask());
        Assert.Equal(1, await context.RecurringTriggerSchedules.CountAsync());
    }

    [Fact]
    public async Task SQLite_activation_cleanup_rejects_a_corrupt_row_even_when_projection_state_is_missing()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = Context(connection);
        await context.Database.EnsureCreatedAsync();
        var store = Store(context, "tenant-a");
        var schedule = Schedule("artifact-corrupt", "node", Now, "publication-corrupt", "slot");
        await store.PrepareActivationAsync("publication-corrupt", [schedule]);

        context.RecurringTriggerScheduleProjectionStates.RemoveRange(context.RecurringTriggerScheduleProjectionStates);
        await context.SaveChangesAsync();
        var row = await context.RecurringTriggerSchedules.SingleAsync();
        row.ContentJson = "not-json";
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => store.DeleteByActivationAsync("publication-corrupt").AsTask());
        Assert.Equal(1, await context.RecurringTriggerSchedules.CountAsync());
        Assert.Empty(await context.RecurringTriggerScheduleProjectionStates.ToArrayAsync());
    }

    [Fact]
    public async Task SQLite_active_projection_allows_operational_advancement_and_exhaustion_without_losing_its_fence()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = Context(connection);
        await context.Database.EnsureCreatedAsync();
        var store = Store(context, "tenant-a");
        var advanced = Schedule("artifact-live", "advance", Now, "publication-live", "slot-live");
        var exhausted = Schedule("artifact-live", "exhaust", Now, "publication-live", "slot-live");
        await store.PrepareActivationAsync("publication-live", [advanced, exhausted]);
        await store.ActivateAsync("publication-live", null);

        Assert.True(await store.TryAdvanceAsync(advanced.ScheduleId, Now, Now.AddMinutes(1)));
        await store.DeleteAsync(exhausted.ScheduleId);
        await store.ActivateAsync("publication-live", null);
        Assert.Equal(Now.AddMinutes(1), (await store.FindAsync(advanced.ScheduleId))!.NextOccurrence);
        await store.DeleteByActivationAsync("publication-live");
        Assert.Null(await store.FindAsync(advanced.ScheduleId));
        Assert.Empty(context.RecurringTriggerScheduleProjectionStates);
    }

    [Fact]
    public async Task SQLite_corrupt_activation_state_fails_closed_without_rewriting_schedule_rows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = Context(connection);
        await context.Database.EnsureCreatedAsync();
        var store = Store(context, "tenant-a");
        var schedule = Schedule("artifact-live", "node", Now, "publication-live", "slot-live");
        await store.PrepareActivationAsync("publication-live", [schedule]);
        await store.ActivateAsync("publication-live", null);
        var state = await context.RecurringTriggerScheduleProjectionStates.SingleAsync();
        state.ScheduleFingerprintsJson = "{\"unexpected\":\"hash\"}";
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => store.ActivateAsync("publication-live", null).AsTask());
        Assert.True((await store.FindAsync(schedule.ScheduleId))!.IsActive);
    }

    [Fact]
    public async Task SQLite_artifact_cleanup_pages_past_256_projection_states()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = Context(connection);
        await context.Database.EnsureCreatedAsync();
        var store = Store(context, "tenant-a");
        for (var index = 0; index < 257; index++)
        {
            var activation = $"publication-{index:D3}";
            await store.PrepareActivationAsync(activation,
                [Schedule("artifact-many", $"node-{index:D3}", Now, activation, "slot")]);
        }
        Assert.Equal(257, await context.RecurringTriggerScheduleProjectionStates.CountAsync());

        await store.DeleteByArtifactAsync("artifact-many");

        Assert.Empty(await context.RecurringTriggerScheduleProjectionStates.ToArrayAsync());
        Assert.Empty((await store.ListByArtifactPageAsync(new RecurringTriggerScheduleArtifactPageQuery("artifact-many", 10))).Items);
    }

    [Fact]
    public void Registration_requires_shared_EF_operational_context_and_resolves_the_opt_in_store()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddRuntimeOperationalStateEntityFrameworkCore(new RuntimeOperationalStateEntityFrameworkCoreOptions { Provider = "Sqlite", ConnectionString = "Data Source=:memory:", RecoveryContinuationSigningKey = new string('k', 32) });
        services.AddRuntimeRecurringTriggerScheduleEntityFrameworkCore();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        Assert.IsType<EfRecurringTriggerScheduleStore>(scope.ServiceProvider.GetRequiredService<IRecurringTriggerScheduleStore>());
        Assert.IsType<BookmarkStateSqliteDbContext>(scope.ServiceProvider.GetRequiredService<BookmarkStateDbContext>());
    }

    private static BookmarkStateSqliteDbContext Context(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);

    private static EfRecurringTriggerScheduleStore Store(BookmarkStateDbContext context, string scope) =>
        new(context, new Accessor(scope), new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = new string('k', 32) })));

    private static RecurringTriggerSchedule Schedule(string artifact, string node, DateTimeOffset next, string? activation = null, string? slot = null) =>
        new(activation is null ? RecurringTriggerSchedule.BuildId(artifact, node) : RecurringTriggerSchedule.BuildId(activation, artifact, node), artifact, node, "Timer", "hash-" + node, RecurringScheduleKind.Interval, "PT1M", next, DateTimeOffset.UnixEpoch, activation, slot);

    private sealed class Accessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
