using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Opens a SQLite context's connection itself, one open at a time per connection string, so that no two opens ever check
/// out a connection from the same Microsoft.Data.Sqlite pool at once (#2209). <see cref="EfRelationalProviderBinding"/>
/// adds it to every SQLite binding. The other providers never carry it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why.</b> Microsoft.Data.Sqlite 10.0.10 can lend one pooled native connection to two open connections at once
/// (dotnet/efcore#39008). A checkout leaves the pool's lock and only then activates the connection, and it marks it active
/// before it records the owner. A second checkout of the same pool can find the pool empty in that gap. It then sees an
/// active connection with no owner, takes it for a leaked one, reclaims it and lends it out again. The two owners share
/// one handle. When the second opens, it registers EF's functions and collations again on that handle. If the first has a
/// statement running, SQLite refuses with error 5, "unable to delete/modify user-function due to active statements". That
/// is the shell activation failure of #2209: a Workbench start's module migrators, finalization gates and backfills
/// open connections to the same file together. When SQLite does not refuse, the two owners silently share one
/// transaction state. Upstream fixes the order in 10.0.13 (dotnet/efcore#39012). Until Elsa pins that version, this
/// interceptor prevents the overlap. Removing it once that version is pinned is tracked by #2220, and a test fails as
/// soon as it is.
/// </para>
/// <para>
/// <b>What holds.</b> The race needs two checkouts of one pool at the same time, and a pool serves exactly one
/// connection string. Every open through a SQLite binding takes that connection string's gate, opens, and releases the
/// gate. So no two checkouts of a pool overlap, provided every opener of the connection string comes through here.
/// The gate covers only the open, not the connection's use, so an open waits at most for other opens of its connection
/// string, never for their queries or transactions. An open that
/// fails, or is cancelled while it waits, reports exactly what it would without the gate: the engine's exception, or
/// the cancellation. It leaves the gate free. Connections with different connection strings never wait for each other.
/// </para>
/// <para>
/// <b>What it does not cover.</b> An opener of the same connection string that bypasses the binding, and the pool clearing
/// Microsoft.Data.Sqlite does on request (<c>SqliteConnection.ClearPool</c> and <c>ClearAllPools</c>, which EF's
/// <c>EnsureDeleted</c> calls). Pool clearing scans for leaked connections without a checkout, so it can still meet an
/// activation in its gap. No first-party store does either on a running host.
/// </para>
/// <para>
/// Turning pooling off would prevent the race too. But EF creates SQLite databases in WAL mode, and there every unpooled
/// open re-reads the schema, and every close of the last connection checkpoints the log. Every query of every module
/// would pay for that.
/// </para>
/// </remarks>
internal sealed class EfSqliteSerialOpenInterceptor : DbConnectionInterceptor
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);

    private EfSqliteSerialOpenInterceptor()
    {
    }

    /// <summary>The one instance every SQLite binding carries.</summary>
    public static EfSqliteSerialOpenInterceptor Instance { get; } = new();

    /// <summary>Adds <see cref="Instance"/> to <paramref name="builder"/> unless its options already carry it.</summary>
    public static DbContextOptionsBuilder EnsureAdded(DbContextOptionsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.Options.FindExtension<CoreOptionsExtension>()?.Interceptors?.Contains(Instance) != true)
            builder.AddInterceptors(Instance);
        return builder;
    }

    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        // Another interceptor took over the open, so there is nothing for this gate to order.
        if (result.IsSuppressed)
            return result;
        var gate = GateOf(connection);
        gate.Wait();
        try
        {
            connection.Open();
        }
        finally
        {
            gate.Release();
        }

        // Opened here, so EF does not open it again. EF still logs the open, and logs an exception from it as it would its own.
        return InterceptionResult.Suppress();
    }

    public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        if (result.IsSuppressed)
            return result;
        var gate = GateOf(connection);
        await gate.WaitAsync(cancellationToken);
        try
        {
            await connection.OpenAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }

        return InterceptionResult.Suppress();
    }

    /// <summary>The gate of the pool <paramref name="connection"/> checks out from, which Microsoft.Data.Sqlite keys by the exact connection string.</summary>
    private static SemaphoreSlim GateOf(DbConnection connection) =>
        Gates.GetOrAdd(connection.ConnectionString, static _ => new SemaphoreSlim(1, 1));
}
