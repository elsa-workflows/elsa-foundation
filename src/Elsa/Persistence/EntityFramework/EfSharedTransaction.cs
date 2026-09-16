using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// The single owner of one physical connection and one transaction shared by several module-owned
/// <see cref="DbContext"/> types that must commit as one unit.
/// </summary>
/// <remarks>
/// <para>
/// The owner never borrows a caller's context. It constructs a fresh instance of every configured context
/// from that context's own options, opens the connection and begins the transaction through the first one,
/// and binds the others to that same connection and transaction. It commits or rolls back exactly once and
/// disposes every context it constructed.
/// </para>
/// <para>
/// Every configured context must name the same provider and the same connection string. A split target
/// or a provider mismatch is refused before any connection is opened, because one local transaction
/// cannot span two databases.
/// </para>
/// <para>
/// Module write paths that begin their own transaction run inside the owner through
/// <see cref="BeginOperationAsync"/>, which hands them a non-owning transaction handle. Committing that
/// handle defers to the owner; rolling it back, or disposing it without committing, makes the owner
/// rollback-only, so an enlisted operation that failed or retried can never be committed half-applied.
/// </para>
/// </remarks>
public sealed class EfSharedTransaction : IAsyncDisposable
{
    private readonly DbContext[] contexts;
    private readonly IDbContextTransaction transaction;
    private EnlistedOperation? activeOperation;
    private State state = State.Active;
    private bool disposed;

    private EfSharedTransaction(DbContext[] contexts, IDbContextTransaction transaction)
    {
        this.contexts = contexts;
        this.transaction = transaction;
    }

    /// <summary>The physical connection every enlisted context uses.</summary>
    public DbConnection Connection => contexts[0].Database.GetDbConnection();

    /// <summary>The physical transaction every enlisted context uses.</summary>
    public DbTransaction Transaction => transaction.GetDbTransaction();

    /// <summary>True once an enlisted operation rolled back; the owner can then only roll back.</summary>
    public bool IsRollbackOnly => state == State.RollbackOnly;

    /// <summary>True once <see cref="CommitAsync"/> reached the provider, whatever its outcome.</summary>
    public bool CommitWasAttempted => state is State.CommitAttempted or State.Committed;

    /// <summary>
    /// The first failure met while releasing the owner. Release is best effort: an uncommitted
    /// transaction ends when its owned connection is disposed, so a cleanup failure cannot make a partial
    /// write durable, and it must not replace the failure that caused the rollback.
    /// </summary>
    public Exception? CleanupFailure { get; private set; }

    /// <summary>
    /// Constructs fresh instances of <paramref name="configuredContexts"/>, opens one connection for the
    /// first, begins one transaction, and enlists the rest in it.
    /// </summary>
    /// <exception cref="EfSharedTransactionTargetMismatchException">The contexts name different providers or connection strings.</exception>
    public static async Task<EfSharedTransaction> BeginAsync(
        IReadOnlyList<DbContext> configuredContexts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuredContexts);
        if (configuredContexts.Count == 0)
            throw new ArgumentException("A shared transaction requires at least one context.", nameof(configuredContexts));
        if (configuredContexts.Any(context => context is null))
            throw new ArgumentException("A shared transaction cannot enlist a null context.", nameof(configuredContexts));
        if (configuredContexts.Select(context => context.GetType()).Distinct().Count() != configuredContexts.Count)
            throw new ArgumentException("A shared transaction enlists each context type once.", nameof(configuredContexts));

        var providerName = EnsureSingleTarget(configuredContexts);
        cancellationToken.ThrowIfCancellationRequested();

        var created = new List<DbContext>(configuredContexts.Count);
        IDbContextTransaction? transaction = null;
        try
        {
            foreach (var configured in configuredContexts)
                created.Add(CreateFresh(configured));

            var anchor = created[0];
            transaction = await anchor.Database.BeginTransactionAsync(cancellationToken);
            var connection = anchor.Database.GetDbConnection();
            var dbTransaction = transaction.GetDbTransaction();
            foreach (var enlisted in created.Skip(1))
            {
                enlisted.Database.SetDbConnection(connection, contextOwnsConnection: false);
                enlisted.Database.UseTransaction(dbTransaction);
                EfProviderGuard.Ensure(enlisted, providerName);
                if (!ReferenceEquals(enlisted.Database.GetDbConnection(), connection) ||
                    !ReferenceEquals(enlisted.Database.CurrentTransaction?.GetDbTransaction(), dbTransaction))
                    throw new InvalidOperationException($"{enlisted.GetType().Name} did not enlist in the shared physical connection and transaction.");
            }

            return new EfSharedTransaction(created.ToArray(), transaction);
        }
        catch
        {
            var cleanup = new List<Exception>();
            for (var index = created.Count - 1; index >= 1; index--)
                await ReleaseAsync(() => created[index].DisposeAsync().AsTask(), cleanup);
            if (transaction is not null)
                await ReleaseAsync(() => transaction.DisposeAsync().AsTask(), cleanup);
            if (created.Count > 0)
                await ReleaseAsync(() => created[0].DisposeAsync().AsTask(), cleanup);
            throw;
        }
    }

    /// <summary>Returns the one enlisted context assignable to <typeparamref name="TContext"/>.</summary>
    public TContext Context<TContext>() where TContext : DbContext
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var matches = contexts.OfType<TContext>().ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException($"The shared transaction enlists {matches.Length} contexts assignable to {typeof(TContext).Name}; exactly one is required.");
    }

    /// <summary>
    /// Hands an enlisted write path a non-owning transaction handle. Pass this method where a module's
    /// atomic writer accepts a transaction factory. One enlisted operation runs at a time.
    /// </summary>
    public Task<IDbContextTransaction> BeginOperationAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureActive("begin an enlisted operation");
        if (activeOperation is not null)
            throw new InvalidOperationException("An enlisted operation is already running in the shared transaction.");
        activeOperation = new EnlistedOperation(this);
        return Task.FromResult<IDbContextTransaction>(activeOperation);
    }

    /// <summary>
    /// Commits the physical transaction once. A failure after the commit request reached the provider has
    /// an unknown outcome; the caller must reconcile from durable state rather than retry blindly.
    /// </summary>
    /// <exception cref="EfCommitOutcomeUnknownException">The provider reported a failure after the commit request.</exception>
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        EnsureActive("commit");
        if (activeOperation is not null)
            throw new InvalidOperationException("An enlisted operation has not completed; the shared transaction cannot commit.");
        var unsaved = contexts.Where(context => context.ChangeTracker.HasChanges()).Select(context => context.GetType().Name).ToArray();
        if (unsaved.Length > 0)
            throw new InvalidOperationException($"The shared transaction cannot commit while enlisted contexts hold unsaved changes: {string.Join(", ", unsaved)}.");
        cancellationToken.ThrowIfCancellationRequested();

        state = State.CommitAttempted;
        try
        {
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            throw new EfCommitOutcomeUnknownException(
                "The shared transaction commit outcome is unknown; reconcile from durable state before retrying.",
                exception);
        }

        state = State.Committed;
    }

    /// <summary>Rolls the physical transaction back once. Repeating it, or calling it after a commit attempt, is a no-op.</summary>
    public async Task RollbackAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (state is State.CommitAttempted or State.Committed or State.RolledBack)
            return;
        if (state == State.RollbackAttempted)
            throw new InvalidOperationException("The shared transaction rollback attempt is terminal.");

        state = State.RollbackAttempted;
        await transaction.RollbackAsync(CancellationToken.None);
        state = State.RolledBack;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
            return;
        disposed = true;

        var cleanup = new List<Exception>();
        for (var index = contexts.Length - 1; index >= 1; index--)
            await ReleaseAsync(() => contexts[index].DisposeAsync().AsTask(), cleanup);
        if (state is State.Active or State.RollbackOnly)
        {
            state = State.RollbackAttempted;
            await ReleaseAsync(async () =>
            {
                await transaction.RollbackAsync(CancellationToken.None);
                state = State.RolledBack;
            }, cleanup);
        }

        await ReleaseAsync(() => transaction.DisposeAsync().AsTask(), cleanup);
        // The first context opened and owns the physical connection; disposing it closes the connection.
        await ReleaseAsync(() => contexts[0].DisposeAsync().AsTask(), cleanup);
        CleanupFailure = cleanup.FirstOrDefault();
    }

    private void EnsureActive(string action)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (state == State.Active)
            return;
        if (state == State.RollbackOnly)
            throw new EfSharedTransactionRollbackOnlyException(
                $"An enlisted operation rolled back; the shared transaction can only roll back, so it cannot {action}. Retry the whole unit.");
        throw new InvalidOperationException($"The shared transaction is terminal; it cannot {action}.");
    }

    private void CompleteOperation(EnlistedOperation operation, bool succeeded)
    {
        if (!ReferenceEquals(activeOperation, operation))
            return;
        activeOperation = null;
        if (!succeeded && state == State.Active)
            state = State.RollbackOnly;
    }

    private static string EnsureSingleTarget(IReadOnlyList<DbContext> configuredContexts)
    {
        var first = configuredContexts[0];
        var (providerName, connectionString) = Target(first);
        foreach (var context in configuredContexts.Skip(1))
        {
            var (candidateProvider, candidateConnectionString) = Target(context);
            if (!StringComparer.Ordinal.Equals(candidateProvider, providerName))
                throw new EfSharedTransactionTargetMismatchException(
                    $"{context.GetType().Name} uses provider '{candidateProvider}' but {first.GetType().Name} uses '{providerName}'; " +
                    "one shared transaction cannot span two providers.");
            // Connection strings are compared exactly and never echoed: they can carry credentials, and two
            // textually different strings cannot be proven to name one database, so they are refused.
            if (!StringComparer.Ordinal.Equals(candidateConnectionString, connectionString))
                throw new EfSharedTransactionTargetMismatchException(
                    $"{context.GetType().Name} and {first.GetType().Name} are configured with different connection strings; " +
                    "one shared transaction cannot span split database targets. Configure both with the same connection string.");
        }

        return providerName;
    }

    private static (string ProviderName, string ConnectionString) Target(DbContext context)
    {
        if (!context.Database.IsRelational())
            throw new EfSharedTransactionTargetMismatchException($"{context.GetType().Name} is not configured for a relational provider.");
        var providerName = context.Database.ProviderName;
        if (string.IsNullOrWhiteSpace(providerName))
            throw new EfSharedTransactionTargetMismatchException($"{context.GetType().Name} has no configured provider.");
        var connectionString = context.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new EfSharedTransactionTargetMismatchException(
                $"{context.GetType().Name} has no configured connection string, so its database target cannot be proven equal.");
        return (providerName, connectionString);
    }

    private static DbContext CreateFresh(DbContext configured)
    {
        var options = configured.GetService<IDbContextOptions>();
        var type = configured.GetType();
        var constructor = type.GetConstructor([options.GetType()])
                          ?? throw new InvalidOperationException(
                              $"{type.Name} must expose a public constructor accepting its own DbContextOptions to enlist in a shared transaction.");
        return (DbContext)constructor.Invoke([options]);
    }

    private static async Task ReleaseAsync(Func<Task> release, List<Exception> failures)
    {
        try
        {
            await release();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private enum State
    {
        Active,
        RollbackOnly,
        CommitAttempted,
        Committed,
        RollbackAttempted,
        RolledBack
    }

    /// <summary>
    /// A non-owning handle for one enlisted write path. It never commits or rolls back the physical
    /// transaction; it reports its outcome to the owner.
    /// </summary>
    private sealed class EnlistedOperation(EfSharedTransaction owner) : IDbContextTransaction
    {
        public Guid TransactionId => owner.transaction.TransactionId;

        public void Commit() => owner.CompleteOperation(this, succeeded: true);

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            Commit();
            return Task.CompletedTask;
        }

        public void Rollback() => owner.CompleteOperation(this, succeeded: false);

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            Rollback();
            return Task.CompletedTask;
        }

        // Disposing without committing is an abandoned operation: treat it as a rollback.
        public void Dispose() => owner.CompleteOperation(this, succeeded: false);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>The configured contexts do not name one provider and one database target.</summary>
public sealed class EfSharedTransactionTargetMismatchException(string message) : InvalidOperationException(message);

/// <summary>
/// An enlisted operation rolled back, so the shared transaction can only roll back. Raised when a write path
/// tries to retry or continue inside it; the owner's caller retries the whole unit instead.
/// </summary>
public sealed class EfSharedTransactionRollbackOnlyException(string message) : InvalidOperationException(message);

/// <summary>A commit request reached the provider and failed; whether it was applied is unknown.</summary>
public sealed class EfCommitOutcomeUnknownException(string message, Exception innerException)
    : InvalidOperationException(message, innerException);
