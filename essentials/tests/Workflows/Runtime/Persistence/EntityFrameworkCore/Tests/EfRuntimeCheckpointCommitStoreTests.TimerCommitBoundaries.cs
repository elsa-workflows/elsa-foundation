using System.Data.Common;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    [Fact]
    public async Task Timer_model_split_read_cannot_hide_a_captured_source_during_delete()
    {
        await using var f = await TimerHistoryFixture.Create();
        await f.Store.SaveAsync(HistoryTimer("active"));
        await f.Context.Database.ExecuteSqlRawAsync("CREATE TABLE hidden_timer_fragment (Id TEXT NOT NULL PRIMARY KEY, RequiredJoin INTEGER NOT NULL)");
        await using var context = new SplitTimerHistoryContext(
            new DbContextOptionsBuilder<SplitTimerHistoryContext>().UseSqlite(f.Database.Connection).Options);
        // The canonical row exists, but this otherwise canonical entity query joins an empty required fragment.
        Assert.Null(await context.DurableTimers.AsNoTracking().SingleOrDefaultAsync());
        var store = new EfDurableTimerStore(context, f.Access, new NoopContinuationCodec(), f.Clock);
        var failure = await Record.ExceptionAsync(async () => await store.DeleteAsync("workflow-a", "active"));
        Assert.NotNull(failure);
        Assert.False(store.SupportsHistoryResponsibility);
        var retained = await f.Store.FindAsync("workflow-a", "active");
        Assert.NotNull(retained);
        Assert.False(retained.HistoryResponsibility!.Retired);
        Assert.Single(retained.HistoryResponsibility.PendingEvents);
    }

    [Theory]
    [InlineData(false, "same")] [InlineData(true, "same")]
    [InlineData(false, "scope")] [InlineData(true, "scope")]
    [InlineData(false, "cancel")] [InlineData(true, "cancel")]
    public async Task Timer_owned_commit_checks_access_and_cancellation_before_acknowledgement(bool discharge, string change)
    {
        await using var f = await TimerHistoryFixture.Create(native: false);
        if (discharge)
        {
            await f.Store.SaveAsync(HistoryTimer());
            await f.Store.DeleteAsync("workflow-a", "timer");
        }
        var access = new TimerHistoryAccess();
        var held = new HeldTimerHistoryCommit();
        using var cancellation = new CancellationTokenSource();
        await using var context = f.Database.Open("tenant-a", held);
        var store = new EfDurableTimerStore(context, access, new NoopContinuationCodec(), f.Clock);
        bool acknowledged = false;
        async Task Invoke()
        {
            if (discharge) acknowledged = await store.DischargeUnrecordedCompletionAsync(f.Completion(), cancellationToken: cancellation.Token);
            else { await store.SaveAsync(HistoryTimer(), cancellation.Token); acknowledged = true; }
        }
        var pending = Invoke();
        try
        {
            await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            if (change == "scope") access.Current = PersistenceAccessContext.Scoped(new("tenant-b"));
            else if (change == "cancel") cancellation.Cancel();
        }
        finally { held.Proceed.TrySetResult(); }
        var failure = await Record.ExceptionAsync(() => pending);
        if (change == "same") { Assert.Null(failure); Assert.True(acknowledged); }
        else
        {
            Assert.False(acknowledged);
            if (change == "scope") Assert.IsType<InvalidOperationException>(failure);
            else Assert.Equal(cancellation.Token, Assert.IsAssignableFrom<OperationCanceledException>(failure).CancellationToken);
        }
        // A completed physical commit is not rolled back by a failed return-boundary check.
        if (discharge)
        {
            Assert.False(await f.Store.HasPendingHistoryAsync("workflow-a"));
            Assert.NotNull(EfTimerHistoryOwner.Read(await f.Context.ExecutionLivenessStates.AsNoTracking().SingleAsync(),
                "tenant-a", "workflow-a")!.Completion);
        }
        else Assert.NotNull(await f.Store.FindAsync("workflow-a", "timer"));
        Assert.Null(context.Database.CurrentTransaction);
        Assert.False(context.ChangeTracker.HasChanges());
    }

    [Theory]
    [InlineData("no-detect", false)] [InlineData("no-detect", true)]
    [InlineData("no-track", false)] [InlineData("no-track", true)]
    [InlineData("identity", false)] [InlineData("identity", true)]
    public async Task Timer_tracking_settings_cannot_acknowledge_an_omitted_owner_write(string mode, bool discharge)
    {
        await using var f = await TimerHistoryFixture.Create(native: false);
        if (discharge)
        {
            await f.Store.SaveAsync(HistoryTimer());
            await f.Store.DeleteAsync("workflow-a", "timer");
        }
        await using var context = f.Database.Open("tenant-a");
        if (mode == "no-detect") context.ChangeTracker.AutoDetectChangesEnabled = false;
        else context.ChangeTracker.QueryTrackingBehavior = mode == "identity"
            ? QueryTrackingBehavior.NoTrackingWithIdentityResolution : QueryTrackingBehavior.NoTracking;
        var store = new EfDurableTimerStore(context, f.Access, new NoopContinuationCodec(), f.Clock);
        var failure = await Record.ExceptionAsync(async () =>
        {
            if (discharge) await store.DischargeUnrecordedCompletionAsync(f.Completion());
            else await store.SaveAsync(HistoryTimer());
        });
        var owner = EfTimerHistoryOwner.Read(await f.Context.ExecutionLivenessStates.AsNoTracking().SingleAsync(), "tenant-a", "workflow-a");
        if (discharge)
        {
            Assert.Null(owner!.Completion);
            var retained = Assert.Single((await f.Store.ListPendingHistoryAsync(new())).Items);
            Assert.True(retained.HistoryResponsibility!.Retired);
            Assert.Equal(2, retained.HistoryResponsibility.PendingEvents.Count);
        }
        else
        {
            Assert.Null(owner);
            Assert.Empty(await f.Context.DurableTimers.AsNoTracking().ToArrayAsync());
        }
        Assert.IsType<NotSupportedException>(failure);
        Assert.False(store.SupportsHistoryResponsibility);
        Assert.Null(context.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Timer_member_binding_cannot_commit_a_source_without_its_original_owner_pin()
    {
        await using var f = await TimerHistoryFixture.Create(native: false);
        await using var context = OpenTimerFilter<OwnerFieldBinding>(f);
        var store = new EfDurableTimerStore(context, f.Access, new NoopContinuationCodec(), f.Clock);
        var failure = await Record.ExceptionAsync(async () => await store.SaveAsync(HistoryTimer()));
        Assert.Empty(await f.Context.DurableTimers.AsNoTracking().ToArrayAsync());
        Assert.Null((await f.Context.ExecutionLivenessStates.AsNoTracking().SingleAsync()).TimerHistoryOwnerJson);
        Assert.IsType<NotSupportedException>(failure);
        Assert.False(store.SupportsHistoryResponsibility);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task Timer_tracking_change_after_qualification_cannot_drop_pin_or_closure(bool noTracking, bool discharge)
    {
        await using var f = await TimerHistoryFixture.Create(native: false);
        if (discharge)
        {
            await f.Store.SaveAsync(HistoryTimer());
            await f.Store.DeleteAsync("workflow-a", "timer");
        }
        var held = new HeldTimerHistoryRead("elsa_runtime_durable_timer");
        await using var context = f.Database.Open("tenant-a", held);
        var store = new EfDurableTimerStore(context, f.Access, new NoopContinuationCodec(), f.Clock);
        var acknowledged = false;
        async Task Invoke()
        {
            if (discharge) acknowledged = await store.DischargeUnrecordedCompletionAsync(f.Completion());
            else { await store.SaveAsync(HistoryTimer()); acknowledged = true; }
        }
        var pending = Invoke();
        try
        {
            await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            if (noTracking) context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            else context.ChangeTracker.AutoDetectChangesEnabled = false;
        }
        finally { held.Proceed.TrySetResult(); }
        var failure = await Record.ExceptionAsync(() => pending);
        Assert.False(acknowledged);
        Assert.IsType<NotSupportedException>(failure);
        var owner = EfTimerHistoryOwner.Read(await f.Context.ExecutionLivenessStates.AsNoTracking().SingleAsync(), "tenant-a", "workflow-a");
        if (discharge)
        {
            Assert.Null(owner!.Completion);
            Assert.Equal(2, Assert.Single((await f.Store.ListPendingHistoryAsync(new())).Items).HistoryResponsibility!.PendingEvents.Count);
        }
        else
        {
            Assert.Null(owner);
            Assert.Empty(await f.Context.DurableTimers.AsNoTracking().ToArrayAsync());
        }
        Assert.Null(context.Database.CurrentTransaction);
    }

    private sealed class SplitTimerHistoryContext(DbContextOptions<SplitTimerHistoryContext> options) : RuntimeDbContext(options)
    {
        protected override void ConfigureProvider(ModelBuilder model) => model.UseCollation("BINARY");
        protected override void OnModelCreating(ModelBuilder model)
        {
            base.OnModelCreating(model);
            var timer = model.Entity<DurableTimerEntity>();
            timer.Property<int>("RequiredJoin").IsRequired();
            timer.SplitToTable("hidden_timer_fragment", fragment => fragment.Property<int>("RequiredJoin"));
        }
    }

    private sealed class HeldTimerHistoryCommit : DbTransactionInterceptor
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Proceed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Proceed.Task;
        }
    }
}
