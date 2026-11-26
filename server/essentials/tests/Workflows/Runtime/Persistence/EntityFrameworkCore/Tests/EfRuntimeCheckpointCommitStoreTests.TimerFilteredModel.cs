using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Timer_history_filtered_model_cannot_discharge_past_an_active_source(bool named)
    {
        await using var f = await TimerHistoryFixture.Create(native: false);
        await f.Store.SaveAsync(HistoryTimer("retired")); await f.Store.DeleteAsync("workflow-a", "retired");
        await f.Store.SaveAsync(HistoryTimer("active"));
        await using var context = named ? OpenTimerFilter<TimerFilterNamed>(f) : OpenTimerFilter<TimerFilter>(f);
        var store = new EfDurableTimerStore(context, f.Access, new NoopContinuationCodec(), f.Clock);
        bool? discharged = null;
        var failure = await Record.ExceptionAsync(async () => discharged = await store.DischargeUnrecordedCompletionAsync(f.Completion(), 1));
        Assert.Null(discharged); Assert.IsType<NotSupportedException>(failure);
        Assert.False(store.SupportsHistoryResponsibility); Assert.False(store.CanDischargeUnrecordedCompletion(new EfRuntimeCheckpointCommitStore(context, f.Access, f.Clock)));
        Assert.Equal(2, (await f.Store.ListPendingHistoryAsync(new())).Items.Count);
        Assert.NotNull(await f.Store.FindAsync("workflow-a", "active"));
        var owner = EfTimerHistoryOwner.Read(await f.Context.ExecutionLivenessStates.AsNoTracking().SingleAsync(), "tenant-a", "workflow-a");
        Assert.Null(owner!.Completion); Assert.False(owner.LostFacts);
        Assert.All((await f.Store.ListPendingHistoryAsync(new())).Items, timer => Assert.Null(timer.HistoryResponsibility!.NoRecordCompletion));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Timer_history_filtered_owner_model_never_reports_false_native_absence(bool named)
    {
        await using var f = await TimerHistoryFixture.Create(native: false); await f.Store.SaveAsync(HistoryTimer());
        await using var context = named ? OpenTimerFilter<OwnerFilterNamed>(f) : OpenTimerFilter<OwnerFilter>(f);
        IRuntimeCheckpointCommitStore writer = new EfRuntimeCheckpointCommitStore(context, f.Access, f.Clock);
        bool? requires = null;
        var failure = await Record.ExceptionAsync(async () => requires = await writer.RequiresUnrecordedTimerDischargeAsync("workflow-a"));
        Assert.Null(requires); Assert.IsType<RuntimeSchedulerWorkUnavailableException>(failure);
        Assert.True(await f.Store.HasPendingHistoryAsync("workflow-a"));
        Assert.Null(EfTimerHistoryOwner.Read(await f.Context.ExecutionLivenessStates.AsNoTracking().SingleAsync(), "tenant-a", "workflow-a")!.Completion);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Timer_history_filtered_native_model_cannot_erase_original_choice_in_legacy_scheduling(bool named)
    {
        await using var f = await TimerHistoryFixture.Create();
        await using var context = named ? OpenTimerFilter<NativeFilterNamed>(f) : OpenTimerFilter<NativeFilter>(f);
        var store = new EfDurableTimerStore(context, f.Access, new NoopContinuationCodec(), f.Clock);
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => store.SaveAsync(HistoryTimer() with { HistoryResponsibility = null }).AsTask());
        Assert.Empty(await f.Context.DurableTimers.AsNoTracking().ToArrayAsync());
        Assert.NotNull(await f.Context.WorkflowExecutionStates.AsNoTracking().SingleAsync());
        Assert.Null((await f.Context.ExecutionLivenessStates.AsNoTracking().SingleAsync()).TimerHistoryOwnerJson);
        Assert.False(store.SupportsHistoryResponsibility);
    }

    [Theory]
    [InlineData("filter")] [InlineData("named")] [InlineData("table")] [InlineData("column")] [InlineData("converter")] [InlineData("view")] [InlineData("schema")] [InlineData("discriminator")]
    public async Task Timer_delete_refuses_a_model_that_can_hide_the_actual_captured_source(string mapping)
    {
        await using var f = await TimerHistoryFixture.Create(); await f.Store.SaveAsync(HistoryTimer("active"));
        if (mapping == "table")
            await f.Context.Database.ExecuteSqlRawAsync("CREATE TABLE elsa_runtime_hidden_timer AS SELECT * FROM elsa_runtime_durable_timer WHERE 0");
        if (mapping == "view")
            await f.Context.Database.ExecuteSqlRawAsync("CREATE VIEW elsa_runtime_hidden_view AS SELECT * FROM elsa_runtime_durable_timer WHERE 0");
        if (mapping == "column")
            await f.Context.Database.ExecuteSqlRawAsync("ALTER TABLE elsa_runtime_durable_timer ADD COLUMN hidden_id TEXT NULL");
        await using var context = mapping switch
        {
            "filter" => OpenTimerFilter<TimerFilter>(f), "named" => OpenTimerFilter<TimerFilterNamed>(f),
            "table" => OpenTimerFilter<TimerTable>(f), "column" => OpenTimerFilter<TimerColumn>(f),
            "view" => OpenTimerFilter<TimerView>(f), "schema" => OpenTimerFilter<TimerSchema>(f),
            "discriminator" => OpenTimerFilter<TimerDiscriminator>(f), _ => OpenTimerFilter<TimerConverter>(f)
        };
        var store = new EfDurableTimerStore(context, f.Access, new NoopContinuationCodec(), f.Clock);
        var supports = store.SupportsHistoryResponsibility;
        var failure = await Record.ExceptionAsync(async () => await store.DeleteAsync("workflow-a", "active"));
        var retained = await f.Store.FindAsync("workflow-a", "active"); Assert.NotNull(retained);
        Assert.False(retained.HistoryResponsibility!.Retired); Assert.Single(retained.HistoryResponsibility.PendingEvents);
        Assert.True(failure is RuntimeSchedulerWorkUnavailableException,
            $"The deletion lookup must refuse an unreadable mapping, actual failure={failure?.GetType().Name ?? "none"}, supported={supports}.");
        Assert.False(supports);
        Assert.Null(context.Database.CurrentTransaction); Assert.False(context.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Timer_history_non_key_converter_cannot_falsely_discharge_active_sources()
    {
        await using var f = await TimerHistoryFixture.Create(native: false);
        await f.Store.SaveAsync(HistoryTimer("active"));
        await using var context = OpenTimerFilter<TimerScopeConverter>(f);
        var store = new EfDurableTimerStore(context, f.Access, new NoopContinuationCodec(), f.Clock);
        bool? discharged = null;
        var failure = await Record.ExceptionAsync(async () => discharged = await store.DischargeUnrecordedCompletionAsync(f.Completion(), 1));
        var owner = EfTimerHistoryOwner.Read(await f.Context.ExecutionLivenessStates.AsNoTracking().SingleAsync(), "tenant-a", "workflow-a");
        Assert.Null(owner!.Completion); Assert.False(owner.LostFacts);
        Assert.Null(discharged); Assert.IsType<NotSupportedException>(failure);
        Assert.False(store.SupportsHistoryResponsibility);
        Assert.NotNull(await f.Store.FindAsync("workflow-a", "active"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task Timer_history_atomic_cleanup_refuses_hidden_sources_before_marker_or_sibling_changes(bool named) =>
        CheckCleanupMapping(named ? "named" : "filter");

    [Fact]
    public Task Timer_model_cleanup_requires_canonical_write_behavior() => CheckCleanupMapping("ignore-retired");

    [Fact]
    public Task Timer_model_cleanup_requires_canonical_change_comparison() => CheckCleanupMapping("comparer");

    [Fact]
    public Task Timer_model_cleanup_refuses_overridden_comparer_dispatch() => CheckCleanupMapping("override-comparer");

    [Fact]
    public Task Timer_model_key_comparison_cannot_hide_a_second_source_from_cleanup() => CheckCleanupMapping("key-comparer");

    [Theory]
    [InlineData("no-detect")] [InlineData("no-track")] [InlineData("identity")]
    public Task Timer_cleanup_refuses_unqualified_tracking_settings(string mode) => CheckCleanupMapping(mode);

    private static async Task CheckCleanupMapping(string mapping)
    {
        await using var f = await TimerHistoryFixture.Create(); await f.Store.SaveAsync(HistoryTimer("active"));
        string[] timerIds = mapping == "key-comparer" ? ["active", "second"] : ["active"];
        if (timerIds.Length == 2) await f.Store.SaveAsync(HistoryTimer("second"));
        await f.CreateHead();
        var before = await f.Context.WorkflowExecutionStates.AsNoTracking().SingleAsync();
        var sibling = new BookmarkState("cleanup-sibling", "workflow-a", "activity", "node", "resume", "Timer", "sibling", null,
            new Dictionary<string, string>(), OccurredAt, null);
        var raw = Commit("filtered-cleanup") with
        {
            ExpectedFence = f.Completion().ExpectedFence,
            StateChanges = new(null, null, [], [new("cleanup-sibling", RuntimeStateChangeOperation.Upsert, sibling, new Dictionary<string, string>())],
                [], [], [], activityScopeCleanups: [new("workflow-a", "activity-scope", ["activity"], [], timerIds, [])])
        };
        raw = raw with { HistoryBatches = [new(TimerHistoryPolicy, raw.Checkpoint.CheckpointId, raw.Checkpoint.OccurredAt, false, [])] };
        await using var context = mapping == "key-comparer" ? OpenTimerFilter<TimerKeyComparer>(f)
            : mapping is "no-detect" or "no-track" or "identity" ? f.Database.Open("tenant-a")
            : mapping == "override-comparer" ? OpenTimerFilter<TimerComparerOverride>(f)
            : mapping == "comparer" ? OpenTimerFilter<TimerComparer>(f)
            : mapping == "ignore-retired" ? OpenTimerFilter<TimerIgnoreRetired>(f)
            : mapping == "named" ? OpenTimerFilter<TimerFilterNamed>(f) : OpenTimerFilter<TimerFilter>(f);
        if (mapping == "no-detect") context.ChangeTracker.AutoDetectChangesEnabled = false;
        else if (mapping == "no-track") context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        else if (mapping == "identity") context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTrackingWithIdentityResolution;
        var writer = new EfRuntimeCheckpointCommitStore(context, f.Access, f.Clock, rootWriteLeaseManager: new PassThroughRootWriteLeaseManager());
        var failure = await Record.ExceptionAsync(async () => await writer.CommitAsync(raw, Decision()));
        var after = await f.Context.WorkflowExecutionStates.AsNoTracking().SingleAsync();
        Assert.Equal(before.ContentJson, after.ContentJson); Assert.Equal(before.Revision, after.Revision);
        Assert.Empty(await f.Context.Bookmarks.AsNoTracking().ToArrayAsync());
        Assert.NotNull(failure); Assert.Single(await f.Context.RuntimeCheckpointCommits.AsNoTracking().ToArrayAsync());
        foreach (var timerId in timerIds)
        {
            var retained = await f.Store.FindAsync("workflow-a", timerId); Assert.NotNull(retained);
            Assert.False(retained.HistoryResponsibility!.Retired); Assert.Single(retained.HistoryResponsibility.PendingEvents);
        }
    }

    [Fact]
    public async Task Timer_model_effective_converter_cannot_hide_active_sources()
    {
        await using var f = await TimerHistoryFixture.Create(native: false); await f.Store.SaveAsync(HistoryTimer("active"));
        await using var context = OpenTimerFilter<TimerEffectiveConverter>(f);
        var property = context.Model.FindEntityType(typeof(DurableTimerEntity))!.FindProperty(nameof(DurableTimerEntity.ScopeKeyHash))!;
        Assert.Null(property.GetValueConverter()); Assert.Null(property.GetProviderClrType()); Assert.NotNull(property.GetTypeMapping().Converter);
        var store = new EfDurableTimerStore(context, f.Access, new NoopContinuationCodec(), f.Clock);
        var failure = await Record.ExceptionAsync(async () => await store.DischargeUnrecordedCompletionAsync(f.Completion()));
        var owner = EfTimerHistoryOwner.Read(await f.Context.ExecutionLivenessStates.AsNoTracking().SingleAsync(), "tenant-a", "workflow-a");
        Assert.Null(owner!.Completion); Assert.False(owner.LostFacts); Assert.NotNull(failure);
        Assert.NotNull(await f.Store.FindAsync("workflow-a", "active"));
    }

    [Fact]
    public async Task Timer_model_recorded_native_and_head_cannot_be_hidden_to_discharge_owed_facts()
    {
        await using var f = await TimerHistoryFixture.Create(); await f.Store.SaveAsync(HistoryTimer());
        await f.Store.DeleteAsync("workflow-a", "timer"); await f.CreateHead();
        await CreateEvidenceViews(f, head: true);
        await using var context = OpenTimerFilter<NativeHeadViews>(f);
        var store = new EfDurableTimerStore(context, f.Access, new NoopContinuationCodec(), f.Clock);
        var failure = await Record.ExceptionAsync(async () => await store.DischargeUnrecordedCompletionAsync(f.Completion()));
        var owner = EfTimerHistoryOwner.Read(await f.Context.ExecutionLivenessStates.AsNoTracking().SingleAsync(), "tenant-a", "workflow-a");
        Assert.Null(owner!.Completion); Assert.False(owner.LostFacts); Assert.NotNull(failure); Assert.False(store.SupportsHistoryResponsibility);
        Assert.Equal(2, Assert.Single((await f.Store.ListPendingHistoryAsync(new())).Items).HistoryResponsibility!.PendingEvents.Count);
        Assert.Single(await f.Context.WorkflowExecutionStates.AsNoTracking().ToArrayAsync());
        Assert.Single(await f.Context.Set<RuntimeHistoryHeadEntity>().AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task Timer_model_original_native_choice_cannot_be_hidden_during_legacy_admission()
    {
        await using var f = await TimerHistoryFixture.Create(); await CreateEvidenceViews(f, head: false);
        await using var context = OpenTimerFilter<NativeView>(f);
        var store = new EfDurableTimerStore(context, f.Access, new NoopContinuationCodec(), f.Clock);
        var failure = await Record.ExceptionAsync(async () => await store.SaveAsync(HistoryTimer() with { HistoryResponsibility = null }));
        Assert.Empty(await f.Context.DurableTimers.AsNoTracking().ToArrayAsync()); Assert.NotNull(failure);
        Assert.NotNull((await f.Context.WorkflowExecutionStates.AsNoTracking().SingleAsync()).ContentJson);
        Assert.Null((await f.Context.ExecutionLivenessStates.AsNoTracking().SingleAsync()).TimerHistoryOwnerJson);
    }

    [Fact]
    public async Task Timer_model_first_party_configured_schema_preserves_ordinary_native_save()
    {
        await using var f = await TimerHistoryFixture.Create(native: false);
        var options = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(f.Database.Connection);
        ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(new EfSchemaOptionsExtension("elsa"));
        await using var context = new RuntimeSqliteDbContext(options.Options);
        var states = new EfWorkflowExecutionStateStore(context, f.Access, new NoopContinuationCodec());
        await states.SaveAsync(Execution("workflow-a", "tenant-a") with { HistoryCaptureEnabledAtAdmission = false });
        Assert.NotNull(await states.FindAsync("workflow-a"));
        Assert.True(new EfDurableTimerStore(context, f.Access, new NoopContinuationCodec(), f.Clock).SupportsHistoryResponsibility);
    }

    private static async Task CreateEvidenceViews(TimerHistoryFixture f, bool head)
    {
        var sql = f.Context.GetService<ISqlGenerationHelper>();
        var nativeTable = f.Context.Model.FindEntityType(typeof(WorkflowExecutionStateEntity))!.GetTableName()!;
        await f.Context.Database.ExecuteSqlRawAsync("CREATE VIEW hidden_native AS SELECT * FROM " + sql.DelimitIdentifier(nativeTable) + " WHERE 0");
        if (!head) return;
        var headTable = f.Context.Model.FindEntityType(typeof(RuntimeHistoryHeadEntity))!.GetTableName()!;
        await f.Context.Database.ExecuteSqlRawAsync("CREATE VIEW hidden_head AS SELECT * FROM " + sql.DelimitIdentifier(headTable) + " WHERE 0");
    }

    private static RuntimeDbContext OpenTimerFilter<TFilter>(TimerHistoryFixture f) => new TimerFilteredContext<TFilter>(
        new DbContextOptionsBuilder<TimerFilteredContext<TFilter>>().UseSqlite(f.Database.Connection).Options);
    private sealed class TimerFilter;
    private sealed class TimerFilterNamed;
    private sealed class TimerTable;
    private sealed class TimerColumn;
    private sealed class TimerConverter;
    private sealed class TimerScopeConverter;
    private sealed class TimerEffectiveConverter;
    private sealed class TimerIgnoreRetired;
    private sealed class TimerComparer;
    private sealed class TimerComparerOverride;
    private sealed class TimerKeyComparer;
    private sealed class OwnerFieldBinding;
    private sealed class SuppressingTimerComparer : ValueComparer<bool>
    {
        private static readonly ValueComparer Ordinary = ValueComparer.CreateDefault(typeof(bool), false);
        public SuppressingTimerComparer() : base(
            (Expression<Func<bool, bool, bool>>)Ordinary.EqualsExpression,
            (Expression<Func<bool, int>>)Ordinary.HashCodeExpression,
            (Expression<Func<bool, bool>>)Ordinary.SnapshotExpression) { }
        public override bool Equals(object? left, object? right) => true;
    }
    private sealed class NativeView;
    private sealed class NativeHeadViews;
    private sealed class TimerView;
    private sealed class TimerSchema;
    private sealed class TimerDiscriminator;
    private sealed class OwnerFilter;
    private sealed class OwnerFilterNamed;
    private sealed class NativeFilter;
    private sealed class NativeFilterNamed;
    private sealed class TimerFilteredContext<TFilter>(DbContextOptions<TimerFilteredContext<TFilter>> options) : RuntimeDbContext(options)
    {
        protected override void ConfigureProvider(ModelBuilder model) => model.UseCollation("BINARY");

        protected override void OnModelCreating(ModelBuilder model)
        {
            base.OnModelCreating(model);
            if (typeof(TFilter) == typeof(TimerFilter)) model.Entity<DurableTimerEntity>().HasQueryFilter(row => EF.Property<bool>(row, "HistoryRetired"));
            else if (typeof(TFilter) == typeof(TimerFilterNamed)) model.Entity<DurableTimerEntity>().HasQueryFilter("retired", row => EF.Property<bool>(row, "HistoryRetired"));
            else if (typeof(TFilter) == typeof(TimerTable)) model.Entity<DurableTimerEntity>().ToTable("elsa_runtime_hidden_timer");
            else if (typeof(TFilter) == typeof(TimerColumn)) model.Entity<DurableTimerEntity>().Property(row => row.Id).HasColumnName("hidden_id");
            else if (typeof(TFilter) == typeof(TimerConverter)) model.Entity<DurableTimerEntity>().Property(row => row.Id).HasConversion(value => "hidden/" + value, value => value);
            else if (typeof(TFilter) == typeof(TimerScopeConverter)) model.Entity<DurableTimerEntity>().Property(row => row.ScopeKeyHash).HasConversion(value => "hidden/" + value, value => value);
            else if (typeof(TFilter) == typeof(TimerEffectiveConverter)) model.Entity<DurableTimerEntity>().Property(row => row.ScopeKeyHash).Metadata
                .SetTypeMapping(new StringTypeMapping("TEXT", System.Data.DbType.String).WithComposedConverter(new ValueConverter<string, string>(value => "hidden/" + value, value => value)));
            else if (typeof(TFilter) == typeof(TimerIgnoreRetired)) model.Entity<DurableTimerEntity>().Property("HistoryRetired").Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            else if (typeof(TFilter) == typeof(TimerComparer)) model.Entity<DurableTimerEntity>().Property<bool>("HistoryRetired").Metadata
                .SetValueComparer(new ValueComparer<bool>((left, right) => true, value => 0, value => value));
            else if (typeof(TFilter) == typeof(TimerComparerOverride)) model.Entity<DurableTimerEntity>().Property<bool>("HistoryRetired").Metadata
                .SetValueComparer(new SuppressingTimerComparer());
            else if (typeof(TFilter) == typeof(TimerKeyComparer)) model.Entity<DurableTimerEntity>().Property(row => row.Id).Metadata
                .SetTypeMapping(new StringTypeMapping("TEXT", System.Data.DbType.String).WithComposedConverter(null,
                    keyComparer: new ValueComparer<string>((left, right) => true, value => 0, value => value)));
            else if (typeof(TFilter) == typeof(OwnerFieldBinding))
            {
                var heartbeat = model.Entity<ExecutionLivenessStateEntity>().Property(row => row.HeartbeatOwnerId);
                heartbeat.Metadata.FieldInfo = null;
                heartbeat.UsePropertyAccessMode(PropertyAccessMode.Property);
                model.Entity<ExecutionLivenessStateEntity>().Property(row => row.TimerHistoryOwnerJson)
                    .HasField("<HeartbeatOwnerId>k__BackingField").UsePropertyAccessMode(PropertyAccessMode.Field);
            }
            else if (typeof(TFilter) == typeof(NativeView) || typeof(TFilter) == typeof(NativeHeadViews))
            {
                model.Entity<WorkflowExecutionStateEntity>().ToView("hidden_native");
                if (typeof(TFilter) == typeof(NativeHeadViews)) model.Entity<RuntimeHistoryHeadEntity>().ToView("hidden_head");
            }
            else if (typeof(TFilter) == typeof(TimerView)) model.Entity<DurableTimerEntity>().ToView("elsa_runtime_hidden_view");
            else if (typeof(TFilter) == typeof(TimerSchema)) model.Entity<DurableTimerEntity>().ToTable("elsa_runtime_durable_timer", "alternate");
            else if (typeof(TFilter) == typeof(TimerDiscriminator)) model.Entity<DurableTimerEntity>().HasDiscriminator(row => row.SchemaVersion)
                .HasValue<DurableTimerEntity>("other").IsComplete(false);
            else if (typeof(TFilter) == typeof(OwnerFilter)) model.Entity<ExecutionLivenessStateEntity>().HasQueryFilter(row => row.TimerHistoryOwnerJson == null);
            else if (typeof(TFilter) == typeof(OwnerFilterNamed)) model.Entity<ExecutionLivenessStateEntity>().HasQueryFilter("uncaptured", row => row.TimerHistoryOwnerJson == null);
            else if (typeof(TFilter) == typeof(NativeFilter)) model.Entity<WorkflowExecutionStateEntity>().HasQueryFilter(row => false);
            else model.Entity<WorkflowExecutionStateEntity>().HasQueryFilter("hidden-native", row => false);
        }
    }
}
