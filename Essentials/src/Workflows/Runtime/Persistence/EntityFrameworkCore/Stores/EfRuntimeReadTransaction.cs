using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Starts an owned consistent read without reserving SQLite's single writer slot.</summary>
internal static class EfRuntimeReadTransaction
{
    private static readonly ReadInterceptor Interceptor = new();
    // Like the shared provider binding, prepare the public API on the actual loaded provider type.
    // No provider assembly is loaded or retained by name and no engine package is added here.
    private static readonly ConditionalWeakTable<Type, Func<DbConnection, DbTransaction>> Factories = new();

    internal sealed class Request
    {
        internal DbTransaction? Created { get; set; }
        internal bool Pending { get; set; }
    }

    internal static void Configure(DbContextOptionsBuilder options)
    {
        if (options.Options.FindExtension<CoreOptionsExtension>()?.Interceptors?.Contains(Interceptor) != true)
            options.AddInterceptors(Interceptor);
    }

    internal static async Task<IDbContextTransaction> BeginAsync(RuntimeDbContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.ReadTransactionRequest is not null)
            throw new InvalidOperationException("An owned read transaction is already being started on this context.");
        if (context.Database.ProviderName != RuntimeSqliteDbContext.ExpectedProviderName)
            return await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        // Keep one explicit EF open reference during acquisition. Begin adds its own reference even
        // when a starting/started interceptor fails, so both success and failure can balance it without
        // closing a connection the caller already had open. The returned transaction remains EF-owned.
        var request = new Request();
        context.ReadTransactionRequest = request;
        Exception? failure = null;
        var opened = false;
        var beginAttempted = false;
        var accepted = false;
        try
        {
            await context.Database.OpenConnectionAsync(cancellationToken);
            opened = true;
            request.Pending = true;
            beginAttempted = true;
            var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            accepted = true;
            return transaction;
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            context.ReadTransactionRequest = null;
            var cleanup = new List<Exception>();
            if (!accepted && beginAttempted)
            {
                // EF has not returned ownership if a later starting/started callback failed. Dispose
                // the actual provider transaction, which rolls it back, before releasing either open.
                if (request.Created is { } created)
                    await CleanupAsync(() => created.DisposeAsync().AsTask(), cleanup);
                await CleanupAsync(() => context.Database.CloseConnectionAsync(), cleanup);
            }
            if (opened) await CleanupAsync(() => context.Database.CloseConnectionAsync(), cleanup);
            if (cleanup.Count != 0)
            {
                var cause = cleanup.Count == 1 ? cleanup[0] : new AggregateException(cleanup);
                if (failure is not null)
                {
                    // Some provider exceptions expose read-only Data. Supplementary diagnostics must
                    // never replace the original cancellation, hard refusal or provider cause.
                    try { failure.Data["runtime.ef.readTransactionCleanupFailure"] = cause; }
                    catch { }
                }
                else
                    ExceptionDispatchInfo.Capture(cause).Throw();
            }
        }
    }

    private static async Task CleanupAsync(Func<Task> cleanup, List<Exception> failures)
    {
        try { await cleanup(); }
        catch (Exception failure) { failures.Add(failure); }
    }

    private static Func<DbConnection, DbTransaction> Prepare(Type connectionType)
    {
        var method = connectionType.GetMethod("BeginTransaction", [typeof(IsolationLevel), typeof(bool)]);
        if (method is null || !typeof(DbTransaction).IsAssignableFrom(method.ReturnType))
            throw new NotSupportedException($"The configured SQLite connection '{connectionType.FullName}' does not expose BeginTransaction(IsolationLevel, bool) for consistent deferred reads.");
        var connection = Expression.Parameter(typeof(DbConnection), "connection");
        var begin = Expression.Call(Expression.Convert(connection, method.DeclaringType!), method,
            Expression.Constant(IsolationLevel.Serializable), Expression.Constant(true));
        return Expression.Lambda<Func<DbConnection, DbTransaction>>(Expression.Convert(begin, typeof(DbTransaction)), connection).Compile();
    }

    private sealed class ReadInterceptor : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not RuntimeDbContext { ReadTransactionRequest: { Pending: true } request })
                return ValueTask.FromResult(result);
            // Consume one start only. A later start in a callback cannot inherit this read request.
            request.Pending = false;
            // Honor any preceding supplied result. A host may also append interceptors after this one.
            if (result.HasResult) request.Created = result.Result;
            cancellationToken.ThrowIfCancellationRequested();
            var transaction = result.HasResult ? result.Result : Factories.GetValue(connection.GetType(), Prepare)(connection);
            request.Created = transaction;
            return ValueTask.FromResult(InterceptionResult<DbTransaction>.SuppressWithResult(transaction));
        }
    }
}
