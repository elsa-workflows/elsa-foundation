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

        for (var current = (Exception?)exception; current is not null; current = current.InnerException)
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
