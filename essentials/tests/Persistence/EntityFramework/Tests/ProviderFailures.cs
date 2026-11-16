using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// Provider failures EF store tests inject without a real server. Each EF module's test project that injects them
/// compiles this file in.
/// </summary>
internal static class ProviderFailures
{
    /// <summary>SQL Server's deadlock-victim error, a transient write conflict.</summary>
    public const int Deadlock = 1205;

    /// <summary>
    /// The shape SQL Server's default execution strategy gives an operation that failed with an error it treats as
    /// transient: the provider error of a query, or the <see cref="DbUpdateException"/> of a save.
    /// </summary>
    public static InvalidOperationException WrappedByExecutionStrategy(Exception failure) =>
        new("An exception has been raised that is likely due to a transient failure.", failure);

    /// <summary>The shape SQL Server's default execution strategy gives a save that failed with <paramref name="providerError"/>.</summary>
    public static InvalidOperationException WrappedSaveFailure(DbException providerError) =>
        WrappedByExecutionStrategy(new DbUpdateException("An error occurred while saving the entity changes.", providerError));

    /// <summary>Fails the first <paramref name="failures"/> saves with <paramref name="failure"/>, then lets saves through.</summary>
    public sealed class FailingSaveInterceptor(Func<Exception> failure, int failures = int.MaxValue) : SaveChangesInterceptor
    {
        private int attempts;

        /// <summary>Fails saves with a deadlock, a transient write conflict, wrapped by SQL Server's execution strategy.</summary>
        public static FailingSaveInterceptor WrappedDeadlock(int failures = int.MaxValue) =>
            new(() => WrappedSaveFailure(new SqlException(Deadlock)), failures);

        /// <summary>Fails every save with a provider error that is no write conflict, wrapped by SQL Server's execution strategy.</summary>
        public static FailingSaveInterceptor WrappedProviderFailure() =>
            new(() => WrappedSaveFailure(new SyntheticProviderException()));

        public int Attempts => Volatile.Read(ref attempts);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            Interlocked.Increment(ref attempts) <= failures ? throw failure() : ValueTask.FromResult(result);
    }

    /// <summary>Fails the first read with <paramref name="failure"/>, then lets reads through.</summary>
    public sealed class FailingReadInterceptor(Func<Exception> failure) : DbCommandInterceptor
    {
        private int attempts;

        public int Attempts => Volatile.Read(ref attempts);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default) =>
            Interlocked.Increment(ref attempts) == 1 ? throw failure() : ValueTask.FromResult(result);
    }

    public sealed class SyntheticProviderException() : DbException("synthetic provider failure");

    /// <summary>Carries a SQL Server error number the way the shared classifier reads it, by type name and <c>Number</c>.</summary>
    public sealed class SqlException(int number) : DbException($"synthetic SQL Server error {number}")
    {
        public int Number { get; } = number;
    }
}
