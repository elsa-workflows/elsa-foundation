using System.Data;
using System.Data.Common;
using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.EntityFrameworkCore.TransactionTopology.Tests;

internal sealed class TopologyOperation : IAsyncDisposable
{
    private bool committed;
    private bool commitAttempted;
    private bool rollbackAttempted;
    private bool rolledBack;
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
        {
            try
            {
                await connection.OpenAsync();
            }
            catch
            {
                await DisposeConnectionPreservingFailureAsync(connection);
                throw;
            }
        }

        DbTransaction transaction;
        try
        {
            transaction = await connection.BeginTransactionAsync(isolationLevel);
        }
        catch
        {
            await DisposeConnectionPreservingFailureAsync(connection);
            throw;
        }

        return new TopologyOperation(connection, transaction, target, tenant, commit);
    }

    public void Enlist(TopologyDbContext borrower, string target, string tenant)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        EnsureCanMutate();
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
        EnsureCanMutate("inspect the outcome before recovery");

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
        if (rolledBack || committed || commitAttempted)
            return;
        if (rollbackAttempted)
            throw new InvalidOperationException("The transaction rollback attempt is terminal; inspect the outcome before recovery.");

        rollbackAttempted = true;
        await Transaction.RollbackAsync();
        rolledBack = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
            return;

        disposed = true;
        Exception? failure = null;
        try
        {
            if (!committed && !commitAttempted && !rollbackAttempted)
                await Transaction.RollbackAsync();
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            try
            {
                await Transaction.DisposeAsync();
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
            finally
            {
                ConnectionDisposeCount++;
                try
                {
                    await Connection.DisposeAsync();
                }
                catch (Exception exception)
                {
                    failure ??= exception;
                }
            }
        }

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private void EnsureCanMutate(string? suffix = null)
    {
        if (commitAttempted || rollbackAttempted || rolledBack)
        {
            var detail = suffix is null
                ? "no further enlistment or mutation is allowed."
                : $"{suffix}.";
            throw new InvalidOperationException($"The transaction operation is terminal; {detail}");
        }
    }

    private static async Task DisposeConnectionPreservingFailureAsync(DbConnection connection)
    {
        try
        {
            await connection.DisposeAsync();
        }
        catch
        {
            // Preserve the connection open or transaction-start failure when provider cleanup also fails.
        }
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
