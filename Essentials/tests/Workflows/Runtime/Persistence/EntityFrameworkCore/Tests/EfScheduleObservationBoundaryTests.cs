using System.Data.Common;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfScheduleObservationBoundaryTests
{
    [Theory]
    [InlineData("observe")]
    [InlineData("exhaust")]
    [InlineData("release")]
    public async Task Schedule_operations_wrap_physical_failure_without_mutating_the_held_claim(string operation)
    {
        var fault = new FailingRead();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>()
            .UseSqlite(connection).AddInterceptors(fault).Options);
        await context.Database.EnsureCreatedAsync();
        var schedules = new EfRecurringTriggerScheduleStore(context, new Access(), new AdmissionCodec());
        var now = DateTimeOffset.UtcNow;
        var schedule = RecurringTriggerSchedule.ForResource(new("resource", 1, 1, "timer", "Test.Timer", new("physical-shell"), null),
            RecurringScheduleKind.Interval, "PT5M", now, now);
        await schedules.SaveAsync(schedule);
        var claim = Assert.Single(await schedules.ClaimDueAsync(new("owner", now, TimeSpan.FromMinutes(1), 1)));
        fault.Armed = true;
        async Task Call()
        {
            if (operation == "observe") _ = await schedules.ObserveAsync(schedule.ScheduleId);
            else if (operation == "exhaust") _ = await schedules.ExhaustClaimAsync(claim);
            else _ = await schedules.ReleaseClaimWithFailureAsync(claim, now.AddMinutes(1), RecurringTriggerScheduleFailureReason.NoEligibleSubscribers);
        }
        var failure = await Assert.ThrowsAsync<TriggerOperationException>(Call);
        Assert.Equal("unavailable", failure.Code); Assert.Same(fault.Cause, failure.InnerException);
        Assert.Equal("physical-shell", failure.Fields!["scope"]); Assert.Equal(schedule.ScheduleId, failure.Fields["identity"]);
        fault.Armed = false;
        Assert.True((await schedules.ObserveAsync(schedule.ScheduleId))!.OccurrenceInFlight);
        Assert.True(await schedules.ReleaseClaimAsync(claim, now.AddMinutes(1)));
        Assert.False(await schedules.ExhaustClaimAsync(claim));
        Assert.False(await schedules.ReleaseClaimWithFailureAsync(claim, now.AddMinutes(1), RecurringTriggerScheduleFailureReason.NoEligibleSubscribers));
    }
    private sealed class Access : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new("physical-shell")); }
    private sealed class FailingRead : DbCommandInterceptor
    {
        internal bool Armed;
        internal SqliteException Cause { get; } = new("physical observation failed", 1);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) =>
            Armed ? ValueTask.FromException<InterceptionResult<DbDataReader>>(Cause) : ValueTask.FromResult(result);
    }
}
