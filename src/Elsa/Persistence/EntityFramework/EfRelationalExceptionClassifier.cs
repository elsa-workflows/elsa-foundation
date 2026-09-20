using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Classifies provider exceptions that represent a relational uniqueness conflict without taking
/// dependencies on provider-specific exception assemblies.
/// </summary>
public static class EfRelationalExceptionClassifier
{
    public static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return IsUniqueConstraintViolation((Exception)exception);
    }

    /// <summary>
    /// Classifies a relational uniqueness violation through an exception wrapper. Design
    /// persistence surfaces normalize provider failures into domain exceptions, so callers that
    /// receive that public shape must still be able to inspect the provider's inner exception.
    /// </summary>
    public static bool IsUniqueConstraintViolation(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (var current = exception; current is not null; current = current.InnerException)
        {
            var type = current.GetType();
            var fullName = type.FullName ?? "";
            if (fullName.Contains("SqliteException", StringComparison.Ordinal))
            {
                if (type.GetProperty("SqliteExtendedErrorCode")?.GetValue(current) is 1555 or 2067)
                    return true;
                if (type.GetProperty("SqliteErrorCode")?.GetValue(current) is 19 &&
                    (current.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
                     current.Message.Contains("PRIMARY KEY", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }

            if (fullName.Contains("PostgresException", StringComparison.Ordinal) &&
                type.GetProperty("SqlState")?.GetValue(current) as string == "23505")
                return true;

            if (type.Name.Equals("SqlException", StringComparison.Ordinal) &&
                type.GetProperty("Number")?.GetValue(current) is 2627 or 2601)
                return true;

            if (fullName.Contains("MySqlException", StringComparison.Ordinal) &&
                type.GetProperty("Number")?.GetValue(current) is 1062)
                return true;
        }

        return false;
    }

    /// <summary>Returns whether <paramref name="exception"/> is one of the lost write races named by <paramref name="conflicts"/>.</summary>
    public static bool IsWriteConflict(Exception exception, EfWriteConflict conflicts)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return conflicts.HasFlag(EfWriteConflict.Concurrency) && exception is DbUpdateConcurrencyException ||
               conflicts.HasFlag(EfWriteConflict.UniqueKey) && exception is DbUpdateException && IsUniqueConstraintViolation(exception) ||
               conflicts.HasFlag(EfWriteConflict.Transient) && IsTransientWriteConflict(exception);
    }

    /// <summary>
    /// Returns whether SaveChanges reported one of the lost write races named by <paramref name="conflicts"/>, however
    /// the report was wrapped on its way out. The default execution strategies of SQL Server and PostgreSQL raise an
    /// error they treat as transient, a deadlock included, as an <see cref="InvalidOperationException"/> around the
    /// <see cref="DbUpdateException"/>, and a store's persistence boundary may wrap that again. A race raised by a query
    /// carries no <see cref="DbUpdateException"/> and never matches.
    /// </summary>
    public static bool IsSaveConflict(Exception exception, EfWriteConflict conflicts)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return FindSaveFailure(exception) is { } update && IsWriteConflict(update, conflicts);
    }

    /// <summary>
    /// Returns the <see cref="DbUpdateException"/> a save failure carries, however many wrappers sit above it, or
    /// <c>null</c> when the exception reports no save failure at all. Callers that must read the save's own detail,
    /// such as its entries or the violated constraint's name, need the inner exception rather than the wrapper.
    /// </summary>
    public static DbUpdateException? FindSaveFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbUpdateException update)
                return update;
        }

        return null;
    }

    /// <summary>
    /// Returns whether <paramref name="exception"/> is a provider failure that an execution strategy re-raised as an
    /// <see cref="InvalidOperationException"/> rather than letting the provider's own exception surface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The repository configures no retry, but EF 10's SQL Server strategy and Npgsql's still wrap any error they
    /// judge transient, which covers connection-level failures such as 10054 as well as conflicts. A store whose
    /// failure mapping is keyed to <see cref="DbException"/> or <see cref="DbUpdateException"/> by type therefore
    /// never sees those, and the raw wrapper escapes past the store's own persistence exception, often leaving the
    /// change tracker dirty.
    /// </para>
    /// <para>
    /// Conflicts are already reconciled by <see cref="IsSaveConflict"/> and <see cref="IsTransientWriteConflict"/>,
    /// so this predicate deliberately says nothing about whether the failure is retryable: it answers only "did a
    /// provider fail here". Order a catch clause using it after the conflict clauses, so a conflict keeps its own
    /// handling.
    /// </para>
    /// <para>
    /// A bare <see cref="InvalidOperationException"/> that carries no provider exception does not match. That matters,
    /// because stores raise that type themselves for scope and identity violations, and those must keep travelling.
    /// </para>
    /// </remarks>
    public static bool IsExecutionStrategyWrapped(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is not InvalidOperationException)
            return false;

        for (var current = exception.InnerException; current is not null; current = current.InnerException)
        {
            if (current is DbException or DbUpdateException)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns whether <paramref name="exception"/> reports that a relational provider failed, whether it surfaced as
    /// the provider's own exception or was re-raised by an execution strategy. This is the predicate a store's
    /// outermost provider-failure clause wants, in place of catching <see cref="DbException"/> and
    /// <see cref="DbUpdateException"/> by type and missing the wrapped form.
    /// </summary>
    public static bool IsProviderFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception is DbException or DbUpdateException || IsExecutionStrategyWrapped(exception);
    }

    /// <summary>
    /// Returns whether a store's outermost boundary should normalize <paramref name="exception"/> into its own
    /// persistence exception. Deliberately wider than <see cref="IsProviderFailure"/>: it also takes a bare
    /// <see cref="InvalidOperationException"/>, because that is what EF raises for infrastructure faults that carry no
    /// provider exception at all, among them "a second operation was started on this context", "sequence contains more
    /// than one element" from a scalar read, and an already-open transaction. A boundary that ignored those would let
    /// them escape unnormalized with the change tracker left dirty.
    /// </summary>
    /// <remarks>
    /// This over-reaches, knowingly. A store raises <see cref="InvalidOperationException"/> itself for scope and
    /// identity violations, and those are swallowed here too. Type alone cannot separate them, so fixing it needs a
    /// distinct exception type for those violations rather than a cleverer predicate. Use
    /// <see cref="IsProviderFailure"/> for an inner clause, and this only where the alternative is an unnormalized
    /// escape.
    /// </remarks>
    public static bool IsStoreBoundaryFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception is not (InvalidDataException or OperationCanceledException) &&
               (IsProviderFailure(exception) || exception is InvalidOperationException);
    }

    /// <summary>
    /// Returns whether a relational provider reported a bounded-retry write conflict such as a
    /// serialization failure, deadlock, lock timeout, or SQLite busy/locked result.
    /// </summary>
    public static bool IsTransientWriteConflict(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (var current = exception; current is not null; current = current.InnerException)
        {
            var type = current.GetType();
            var fullName = type.FullName ?? "";
            if (fullName.Contains("SqliteException", StringComparison.Ordinal) &&
                type.GetProperty("SqliteErrorCode")?.GetValue(current) is 5 or 6)
                return true;

            if (fullName.Contains("PostgresException", StringComparison.Ordinal) &&
                type.GetProperty("SqlState")?.GetValue(current) as string is "40001" or "40P01" or "55P03")
                return true;

            if (type.Name.Equals("SqlException", StringComparison.Ordinal) &&
                type.GetProperty("Number")?.GetValue(current) is 1205 or 1222 or 3960)
                return true;

            if (fullName.Contains("MySqlException", StringComparison.Ordinal) &&
                type.GetProperty("Number")?.GetValue(current) is 1205 or 1213)
                return true;
        }

        return false;
    }
}
