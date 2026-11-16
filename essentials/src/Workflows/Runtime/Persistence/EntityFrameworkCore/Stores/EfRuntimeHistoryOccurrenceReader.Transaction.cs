using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

// Provider-free module: use the installed SQLite provider's public deferred-transaction overload,
// then enlist that actual transaction in EF. Inspection must not reserve SQLite's writer slot.
internal sealed class EfRuntimeHistoryReadTransaction(IDbContextTransaction transaction, DbTransaction? raw = null,
    DbConnection? openedConnection = null) : IAsyncDisposable
{
    internal static async ValueTask<EfRuntimeHistoryReadTransaction> BeginAsync(RuntimeDbContext context, CancellationToken token)
    {
        if (context.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite")
            return new(await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token));
        var connection = context.Database.GetDbConnection(); var opened = connection.State == ConnectionState.Closed;
        DbTransaction? raw = null; IDbContextTransaction? enlisted = null;
        try
        {
            if (opened) await connection.OpenAsync(token);
            token.ThrowIfCancellationRequested();
            var method = connection.GetType().GetMethod("BeginTransaction", [typeof(IsolationLevel), typeof(bool)]);
            if (method is null || !typeof(DbTransaction).IsAssignableFrom(method.ReturnType))
                throw new NotSupportedException("The selected SQLite connection cannot create a deferred read transaction.");
            try { raw = method.Invoke(connection, [IsolationLevel.Serializable, true]) as DbTransaction; }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
            if (raw is null) throw new InvalidOperationException("The selected SQLite connection returned no deferred read transaction.");
            enlisted = await context.Database.UseTransactionAsync(raw, token)
                ?? throw new InvalidOperationException("The deferred read transaction could not be enlisted in EF.");
            return new(enlisted, raw, opened ? connection : null);
        }
        catch
        {
            try { if (enlisted is not null) await enlisted.DisposeAsync(); }
            finally
            {
                try { if (raw is not null) await raw.DisposeAsync(); }
                finally { if (opened) await connection.CloseAsync(); }
            }
            throw;
        }
    }
    internal Task CommitAsync(CancellationToken token) => transaction.CommitAsync(token);
    public async ValueTask DisposeAsync()
    {
        try { await transaction.DisposeAsync(); }
        finally
        {
            try { if (raw is not null) await raw.DisposeAsync(); }
            finally { if (openedConnection is not null) await openedConnection.CloseAsync(); }
        }
    }
}
