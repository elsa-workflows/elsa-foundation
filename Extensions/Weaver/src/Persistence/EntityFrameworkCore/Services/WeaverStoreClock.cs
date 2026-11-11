using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Weaver.Persistence.EntityFrameworkCore.Services;

/// <summary>The time leases are measured in. Durable stores use database time, so every host shares one clock.</summary>
internal interface IWeaverStoreClock
{
    Task<DateTimeOffset> NowAsync(DbContext context, CancellationToken cancellationToken);
}

/// <summary>Reads the database's own UTC clock in .NET ticks, one live expression per provider.</summary>
internal sealed class WeaverDatabaseClock : IWeaverStoreClock
{
    public static WeaverDatabaseClock Instance { get; } = new();

    public async Task<DateTimeOffset> NowAsync(DbContext context, CancellationToken cancellationToken)
    {
        var sql = context.Database.ProviderName switch
        {
            // SQLite freezes now only within one statement; integer seconds plus milliseconds avoid float tick loss.
            EfProviderNames.Sqlite => "SELECT 621355968000000000 + CAST(strftime('%s', 'now') AS INTEGER) * 10000000 " +
                                      "+ CAST(substr(strftime('%f', 'now'), 4, 3) AS INTEGER) * 10000 AS Value /* weaver.utc-clock */",
            EfProviderNames.SqlServer => "SELECT DATEDIFF_BIG(MICROSECOND, CONVERT(datetime2(7), '0001-01-01T00:00:00.0000000'), " +
                                         "SYSUTCDATETIME()) * 10 AS Value /* weaver.utc-clock */",
            EfProviderNames.PostgreSql => "SELECT (TRUNC(EXTRACT(EPOCH FROM clock_timestamp()) * 10000000)::bigint + 621355968000000000) " +
                                          "AS \"Value\" /* weaver.utc-clock */",
            EfProviderNames.MySql => "SELECT CAST(621355968000000000 + TIMESTAMPDIFF(MICROSECOND, " +
                                     "CAST('1970-01-01 00:00:00.000000' AS DATETIME(6)), UTC_TIMESTAMP(6)) * 10 AS SIGNED) AS Value /* weaver.utc-clock */",
            var provider => throw new NotSupportedException($"The agent store has no database clock for provider '{provider}'.")
        };
        var ticks = await context.Database.SqlQueryRaw<long>(sql).SingleAsync(cancellationToken);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}

/// <summary>A process clock; for tests that drive lease time deterministically.</summary>
internal sealed class WeaverTimeProviderClock(TimeProvider clock) : IWeaverStoreClock
{
    public Task<DateTimeOffset> NowAsync(DbContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(clock.GetUtcNow());
    }
}
