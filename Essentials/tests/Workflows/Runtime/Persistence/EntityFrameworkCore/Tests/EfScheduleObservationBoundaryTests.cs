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
    [Fact]
    public async Task Observation_wraps_a_physical_read_failure_with_its_scope_schedule_and_original_cause()
    {
        var fault = new FailingRead();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>()
            .UseSqlite(connection).AddInterceptors(fault).Options);
        await context.Database.EnsureCreatedAsync();
        var schedules = new EfRecurringTriggerScheduleStore(context, new Access(), new AdmissionCodec());
        fault.Armed = true;
        var failure = await Assert.ThrowsAsync<TriggerOperationException>(() => schedules.ObserveAsync("schedule").AsTask());
        Assert.Equal("unavailable", failure.Code); Assert.Same(fault.Cause, failure.InnerException);
        Assert.Equal("physical-shell", failure.Fields!["scope"]); Assert.Equal("schedule", failure.Fields["identity"]);
        fault.Armed = false;
        Assert.Null(await schedules.ObserveAsync("schedule"));
        Assert.Empty(await context.RecurringTriggerSchedules.ToArrayAsync());
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
