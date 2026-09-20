using Elsa.Persistence.EntityFramework;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

public sealed class EfRelationalExceptionClassifierTests
{
    [Theory]
    [InlineData(19, 1555, true)]
    [InlineData(19, 2067, true)]
    [InlineData(19, 787, false)]
    [InlineData(5, 5, false)]
    public void Classifies_sqlite_constraint_errors(int errorCode, int extendedErrorCode, bool expected)
    {
        var exception = new DbUpdateException("write failed", new SqliteException(errorCode, extendedErrorCode));

        Assert.Equal(expected, EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception));
    }

    [Theory]
    [InlineData("23505", true)]
    [InlineData("40001", false)]
    public void Classifies_postgresql_unique_errors(string sqlState, bool expected)
    {
        var exception = new DbUpdateException("write failed", new PostgresException(sqlState));

        Assert.Equal(expected, EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception));
    }

    [Theory]
    [InlineData(2627, true)]
    [InlineData(2601, true)]
    [InlineData(1205, false)]
    public void Classifies_sql_server_unique_errors(int number, bool expected)
    {
        var exception = new DbUpdateException("write failed", new SqlException(number));

        Assert.Equal(expected, EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception));
    }

    [Theory]
    [InlineData(1062, true)]
    [InlineData(1213, false)]
    public void Classifies_mysql_duplicate_key_errors(int number, bool expected)
    {
        var exception = new DbUpdateException("write failed", new MySqlException(number));

        Assert.Equal(expected, EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception));
    }

    [Fact]
    public void Does_not_classify_an_unknown_update_failure()
    {
        var exception = new DbUpdateException("write failed", new InvalidOperationException("offline"));

        Assert.False(EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception));
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(6, true)]
    [InlineData(19, false)]
    public void Classifies_sqlite_transient_write_conflicts(int errorCode, bool expected)
    {
        var exception = new DbUpdateException("write failed", new SqliteException(errorCode, errorCode));

        Assert.Equal(expected, EfRelationalExceptionClassifier.IsTransientWriteConflict(exception));
    }

    [Theory]
    [InlineData("40001", true)]
    [InlineData("40P01", true)]
    [InlineData("55P03", true)]
    [InlineData("23505", false)]
    public void Classifies_postgresql_transient_write_conflicts(string sqlState, bool expected)
    {
        var exception = new DbUpdateException("write failed", new PostgresException(sqlState));

        Assert.Equal(expected, EfRelationalExceptionClassifier.IsTransientWriteConflict(exception));
    }

    [Theory]
    [InlineData(1205, true)]
    [InlineData(1222, true)]
    [InlineData(3960, true)]
    [InlineData(2627, false)]
    public void Classifies_sql_server_transient_write_conflicts(int number, bool expected)
    {
        var exception = new DbUpdateException("write failed", new SqlException(number));

        Assert.Equal(expected, EfRelationalExceptionClassifier.IsTransientWriteConflict(exception));
    }

    [Theory]
    [InlineData(1205, true)]
    [InlineData(1213, true)]
    [InlineData(1062, false)]
    public void Classifies_mysql_transient_write_conflicts(int number, bool expected)
    {
        var exception = new DbUpdateException("write failed", new MySqlException(number));

        Assert.Equal(expected, EfRelationalExceptionClassifier.IsTransientWriteConflict(exception));
    }

    [Fact]
    public void A_save_conflict_is_recognized_however_its_report_was_wrapped()
    {
        var deadlock = new DbUpdateException("write failed", new SqlException(ProviderFailures.Deadlock));
        var wrapped = ProviderFailures.WrappedByExecutionStrategy(deadlock);

        Assert.True(EfRelationalExceptionClassifier.IsSaveConflict(deadlock, EfWriteConflict.Transient));
        Assert.True(EfRelationalExceptionClassifier.IsSaveConflict(wrapped, EfWriteConflict.Transient));
        Assert.True(EfRelationalExceptionClassifier.IsSaveConflict(new InvalidOperationException("store boundary", wrapped), EfWriteConflict.Transient));
        Assert.True(EfRelationalExceptionClassifier.IsSaveConflict(
            new InvalidOperationException("store boundary", new DbUpdateConcurrencyException("lost race")), EfWriteConflict.Concurrency));
    }

    [Fact]
    public void A_query_race_another_conflict_kind_or_another_save_failure_is_no_save_conflict()
    {
        var query = ProviderFailures.WrappedByExecutionStrategy(new SqlException(ProviderFailures.Deadlock));
        var deadlock = ProviderFailures.WrappedByExecutionStrategy(new DbUpdateException("write failed", new SqlException(ProviderFailures.Deadlock)));
        var connectionReset = ProviderFailures.WrappedByExecutionStrategy(new DbUpdateException("write failed", new SqlException(10054)));

        Assert.False(EfRelationalExceptionClassifier.IsSaveConflict(query, EfWriteConflict.Transient));
        Assert.False(EfRelationalExceptionClassifier.IsSaveConflict(deadlock, EfWriteConflict.Concurrency | EfWriteConflict.UniqueKey));
        Assert.False(EfRelationalExceptionClassifier.IsSaveConflict(connectionReset, EfWriteConflict.Transient));
    }

    [Fact]
    public void A_provider_failure_is_recognized_whether_or_not_a_strategy_wrapped_it()
    {
        var queryFailure = ProviderFailures.WrappedByExecutionStrategy(new ProviderFailures.SqlException(10054));
        var saveFailure = ProviderFailures.WrappedSaveFailure(new ProviderFailures.SqlException(10054));

        Assert.True(EfRelationalExceptionClassifier.IsExecutionStrategyWrapped(queryFailure));
        Assert.True(EfRelationalExceptionClassifier.IsExecutionStrategyWrapped(saveFailure));
        Assert.True(EfRelationalExceptionClassifier.IsExecutionStrategyWrapped(new InvalidOperationException("store boundary", saveFailure)));

        Assert.True(EfRelationalExceptionClassifier.IsProviderFailure(queryFailure));
        Assert.True(EfRelationalExceptionClassifier.IsProviderFailure(saveFailure));
        Assert.True(EfRelationalExceptionClassifier.IsProviderFailure(new ProviderFailures.SqlException(10054)));
        Assert.True(EfRelationalExceptionClassifier.IsProviderFailure(new DbUpdateException("write failed", new ProviderFailures.SqlException(10054))));
    }

    /// <summary>
    /// The unwrapped provider exceptions are what a store already catches by type, so they must not be reported as
    /// wrapped; otherwise a clause ordered for the wrapped form would start swallowing the plain one too.
    /// </summary>
    [Fact]
    public void An_unwrapped_provider_exception_is_not_reported_as_wrapped()
    {
        Assert.False(EfRelationalExceptionClassifier.IsExecutionStrategyWrapped(new ProviderFailures.SqlException(10054)));
        Assert.False(EfRelationalExceptionClassifier.IsExecutionStrategyWrapped(new DbUpdateException("write failed", new ProviderFailures.SqlException(10054))));
    }

    /// <summary>
    /// Stores raise <see cref="InvalidOperationException"/> themselves for scope and identity violations, and those
    /// are not provider failures. Normalizing them would turn a detected corruption into a generic persistence error.
    /// </summary>
    [Fact]
    public void An_exception_carrying_no_provider_failure_never_matches()
    {
        Assert.False(EfRelationalExceptionClassifier.IsExecutionStrategyWrapped(
            new InvalidOperationException("The requested resource does not belong to the current persistence scope.")));
        Assert.False(EfRelationalExceptionClassifier.IsExecutionStrategyWrapped(
            new InvalidOperationException("corrupt projection", new InvalidDataException("not valid current data"))));
        Assert.False(EfRelationalExceptionClassifier.IsProviderFailure(
            new InvalidOperationException("The requested resource does not belong to the current persistence scope.")));
        Assert.False(EfRelationalExceptionClassifier.IsProviderFailure(new OperationCanceledException()));
        Assert.False(EfRelationalExceptionClassifier.IsProviderFailure(new InvalidDataException("not valid current data")));
    }

    [Fact]
    public async Task Classifies_a_real_sqlite_unique_constraint_failure()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE records (id TEXT PRIMARY KEY)";
            await create.ExecuteNonQueryAsync();
        }
        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO records (id) VALUES ('same')";
            await insert.ExecuteNonQueryAsync();
        }
        await using var duplicate = connection.CreateCommand();
        duplicate.CommandText = "INSERT INTO records (id) VALUES ('same')";

        var providerException = await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => duplicate.ExecuteNonQueryAsync());
        var updateException = new DbUpdateException("write failed", providerException);

        Assert.True(EfRelationalExceptionClassifier.IsUniqueConstraintViolation(updateException));
    }

    private sealed class SqliteException(int errorCode, int extendedErrorCode) : Exception
    {
        public int SqliteErrorCode { get; } = errorCode;
        public int SqliteExtendedErrorCode { get; } = extendedErrorCode;
    }

    private sealed class PostgresException(string sqlState) : Exception
    {
        public string SqlState { get; } = sqlState;
    }

    private sealed class SqlException(int number) : Exception
    {
        public int Number { get; } = number;
    }

    private sealed class MySqlException(int number) : Exception
    {
        public int Number { get; } = number;
    }
}
