using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.EntityFrameworkCore.TransactionTopology.Tests;

internal sealed class TopologyOperation : IAsyncDisposable
{
    private bool committed;
    private bool commitAttempted;
    private bool disposed;
    private readonly Func<Task> commit;

    private TopologyOperation(
        DbConnection connection,
        DbTransaction transaction,
        string target,
        string tenant,
        Func<DbTransaction, Task>? commit)
    {
        Connection = connection;
        Transaction = transaction;
        Target = target;
        Tenant = tenant;
        this.commit = commit is null
            ? () => transaction.CommitAsync()
            : () => commit(transaction);
    }

    public DbConnection Connection { get; }
    public DbTransaction Transaction { get; }
    public string Target { get; }
    public string Tenant { get; }
    public bool CommitWasAttempted => commitAttempted;
    public int ConnectionDisposeCount { get; private set; }

    public static async Task<TopologyOperation> BeginAsync(
        DbConnection connection,
        string target,
        string tenant,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        Func<DbTransaction, Task>? commit = null)
    {
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        var transaction = await connection.BeginTransactionAsync(isolationLevel);
        return new TopologyOperation(connection, transaction, target, tenant, commit);
    }

    public void Enlist(TopologyDbContext borrower, string target, string tenant)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!string.Equals(Target, target, StringComparison.Ordinal))
            throw new InvalidOperationException($"Transaction target mismatch: operation '{Target}', borrower '{target}'.");
        if (!string.Equals(Tenant, tenant, StringComparison.Ordinal))
            throw new InvalidOperationException($"Transaction tenant mismatch: operation '{Tenant}', borrower '{tenant}'.");
        if (!ReferenceEquals(Connection, borrower.Database.GetDbConnection()))
            throw new InvalidOperationException("The borrower uses a different physical connection and cannot enlist.");

        borrower.Database.UseTransaction(Transaction);
    }

    public async Task CommitAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (commitAttempted)
            throw new InvalidOperationException("The transaction commit attempt is terminal; inspect the outcome before recovery.");

        commitAttempted = true;
        try
        {
            await commit();
            committed = true;
        }
        catch (Exception exception)
        {
            throw new CommitOutcomeUnknownException("The commit outcome is unknown; recovery must inspect durable markers.", exception);
        }
    }

    public async Task RollbackAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!committed && !commitAttempted)
            await Transaction.RollbackAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
            return;

        disposed = true;
        if (!committed && !commitAttempted)
        {
            try
            {
                await Transaction.RollbackAsync();
            }
            catch (InvalidOperationException)
            {
                // The provider may have already closed the transaction while tearing down.
            }
        }

        await Transaction.DisposeAsync();
        ConnectionDisposeCount++;
        await Connection.DisposeAsync();
    }
}

internal sealed class CommitOutcomeUnknownException(string message, Exception innerException)
    : Exception(message, innerException);

internal static class TopologyContexts
{
    public static TopologyDbContext Create(string connectionString, TopologyProvider provider, TopologyLane lane) =>
        lane switch
        {
            TopologyLane.Runtime => new RuntimeTopologyDbContext(Build<RuntimeTopologyDbContext>(provider, null, connectionString)),
            TopologyLane.Design => new DesignTopologyDbContext(Build<DesignTopologyDbContext>(provider, null, connectionString)),
            TopologyLane.Publishing => new PublishingTopologyDbContext(Build<PublishingTopologyDbContext>(provider, null, connectionString)),
            _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, null)
        };

    public static TopologyDbContext Create(DbConnection connection, TopologyProvider provider, TopologyLane lane) =>
        lane switch
        {
            TopologyLane.Runtime => new RuntimeTopologyDbContext(Build<RuntimeTopologyDbContext>(provider, connection, null)),
            TopologyLane.Design => new DesignTopologyDbContext(Build<DesignTopologyDbContext>(provider, connection, null)),
            TopologyLane.Publishing => new PublishingTopologyDbContext(Build<PublishingTopologyDbContext>(provider, connection, null)),
            _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, null)
        };

    private static DbContextOptions<TContext> Build<TContext>(
        TopologyProvider provider,
        DbConnection? connection,
        string? connectionString)
        where TContext : DbContext
    {
        if (connection is null && connectionString is null)
            throw new ArgumentException("Either a physical connection or connection string is required.");

        var builder = new DbContextOptionsBuilder<TContext>();
        switch (provider)
        {
            case TopologyProvider.Sqlite:
                if (connection is not null)
                    builder.UseSqlite(connection);
                else
                    builder.UseSqlite(connectionString!);
                break;
            case TopologyProvider.PostgreSql:
                if (connection is not null)
                    builder.UseNpgsql(connection);
                else
                    builder.UseNpgsql(connectionString!);
                break;
            case TopologyProvider.SqlServer:
                if (connection is not null)
                    builder.UseSqlServer(connection);
                else
                    builder.UseSqlServer(connectionString!);
                break;
            case TopologyProvider.MySql:
                if (connection is not null)
                    builder.UseMySQL(connection);
                else
                    builder.UseMySQL(connectionString!);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(provider), provider, null);
        }

        return builder.Options;
    }
}
