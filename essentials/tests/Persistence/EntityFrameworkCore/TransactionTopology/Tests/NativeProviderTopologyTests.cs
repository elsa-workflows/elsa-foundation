using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using Npgsql;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.TransactionTopology.Tests;

[Collection(PostgreSqlTopologyFixture.CollectionName)]
public sealed class PostgreSqlTopologyTests(PostgreSqlTopologyFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_shared_transaction_commit_rollback_partial_failure_and_disposal()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker/PostgreSQL is unavailable.");
        return NativeProviderTopologySmoke.RunAsync(
            fixture.CreateIsolatedDatabaseAsync,
            TopologyProvider.PostgreSql,
            connection => new NpgsqlConnection(connection));
    }
}

[Collection(SqlServerTopologyFixture.CollectionName)]
public sealed class SqlServerTopologyTests(SqlServerTopologyFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_shared_transaction_commit_rollback_partial_failure_and_disposal()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker/SQL Server is unavailable.");
        return NativeProviderTopologySmoke.RunAsync(
            fixture.CreateIsolatedDatabaseAsync,
            TopologyProvider.SqlServer,
            connection => new SqlConnection(connection));
    }
}

[Collection(MySqlTopologyFixture.CollectionName)]
public sealed class MySqlTopologyTests(MySqlTopologyFixture fixture)
{
    [SkippableFact]
    public Task MySql_shared_transaction_commit_rollback_partial_failure_and_disposal()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker/MySQL is unavailable.");
        return NativeProviderTopologySmoke.RunAsync(
            fixture.CreateIsolatedDatabaseAsync,
            TopologyProvider.MySql,
            connection => new MySqlConnection(connection));
    }
}

internal static class NativeProviderTopologySmoke
{
    public static async Task RunAsync(
        Func<Task<string>> createDatabase,
        TopologyProvider provider,
        Func<string, DbConnection> createConnection)
    {
        var connectionString = await createDatabase();
        await using (var setup = TopologyContexts.Create(connectionString, provider, TopologyLane.Runtime))
        {
            await setup.Database.EnsureCreatedAsync();
        }

        {
            var ownerConnection = createConnection(connectionString);
            await using var operation = await TopologyOperation.BeginAsync(ownerConnection, "native", "tenant-native");
            await using (var runtime = Enlist(operation, ownerConnection, provider, TopologyLane.Runtime))
            await using (var design = Enlist(operation, ownerConnection, provider, TopologyLane.Design))
            await using (var publishing = Enlist(operation, ownerConnection, provider, TopologyLane.Publishing))
            {
                runtime.Rows.Add(Row("native-commit-runtime", TopologyLane.Runtime));
                design.Rows.Add(Row("native-commit-design", TopologyLane.Design));
                publishing.Rows.Add(Row("native-commit-publishing", TopologyLane.Publishing));
                await runtime.SaveChangesAsync();
                await design.SaveChangesAsync();
                await publishing.SaveChangesAsync();
            }

            Assert.Equal(System.Data.ConnectionState.Open, ownerConnection.State);
            Assert.Same(ownerConnection, operation.Transaction.Connection);
            await operation.CommitAsync();
        }

        Assert.Equal(3, await CountAsync(connectionString, provider));

        {
            var ownerConnection = createConnection(connectionString);
            await using var operation = await TopologyOperation.BeginAsync(ownerConnection, "native", "tenant-native");
            await using (var runtime = Enlist(operation, ownerConnection, provider, TopologyLane.Runtime))
            {
                runtime.Rows.Add(Row("native-savepoint-runtime", TopologyLane.Runtime));
                await runtime.SaveChangesAsync();
            }

            await SavepointAsync(ownerConnection, operation.Transaction, provider, "after_runtime");
            await using (var design = Enlist(operation, ownerConnection, provider, TopologyLane.Design))
            {
                design.Rows.Add(Row("native-savepoint-design-rolled-back", TopologyLane.Design));
                await design.SaveChangesAsync();
            }

            await RollbackToSavepointAsync(ownerConnection, operation.Transaction, provider, "after_runtime");
            await using (var publishing = Enlist(operation, ownerConnection, provider, TopologyLane.Publishing))
            {
                publishing.Rows.Add(Row("native-savepoint-publishing", TopologyLane.Publishing));
                await publishing.SaveChangesAsync();
            }

            await operation.CommitAsync();
        }

        Assert.Equal(5, await CountAsync(connectionString, provider));
        Assert.Equal(0, await CountByIdAsync(connectionString, provider, "native-savepoint-design-rolled-back"));

        {
            var ownerConnection = createConnection(connectionString);
            await using var operation = await TopologyOperation.BeginAsync(ownerConnection, "native", "tenant-native");
            await using var runtime = Enlist(operation, ownerConnection, provider, TopologyLane.Runtime);
            runtime.Rows.Add(Row("native-explicit-rollback", TopologyLane.Runtime));
            await runtime.SaveChangesAsync();
            await operation.RollbackAsync();
        }

        {
            var ownerConnection = createConnection(connectionString);
            await using var operation = await TopologyOperation.BeginAsync(ownerConnection, "native", "tenant-native");
            await using var runtime = Enlist(operation, ownerConnection, provider, TopologyLane.Runtime);
            runtime.Rows.Add(Row("native-forced-failure", TopologyLane.Runtime));
            await runtime.SaveChangesAsync();
            try
            {
                throw new InvalidOperationException("forced failure after an earlier SaveChanges");
            }
            catch (InvalidOperationException)
            {
                await operation.RollbackAsync();
            }
        }

        {
            var ownerConnection = createConnection(connectionString);
            await using var operation = await TopologyOperation.BeginAsync(ownerConnection, "native", "tenant-native");
            await using (var runtime = Enlist(operation, ownerConnection, provider, TopologyLane.Runtime))
            {
                runtime.Rows.Add(Row("native-disposed-borrower", TopologyLane.Runtime));
                await runtime.SaveChangesAsync();
            }

            await operation.CommitAsync();
        }

        Assert.Equal(6, await CountAsync(connectionString, provider));
        Assert.Equal(0, await CountByIdAsync(connectionString, provider, "native-explicit-rollback"));
        Assert.Equal(0, await CountByIdAsync(connectionString, provider, "native-forced-failure"));
    }

    private static TopologyDbContext Enlist(
        TopologyOperation operation,
        DbConnection connection,
        TopologyProvider provider,
        TopologyLane lane)
    {
        var context = TopologyContexts.Create(connection, provider, lane);
        operation.Enlist(context, "native", "tenant-native");
        return context;
    }

    private static TopologyRow Row(string id, TopologyLane lane) => new()
    {
        Id = id,
        Lane = lane.ToString(),
        TenantId = "tenant-native",
        Payload = id
    };

    private static async Task<int> CountAsync(string connectionString, TopologyProvider provider) =>
        await CountByIdAsync(connectionString, provider, null);

    private static async Task<int> CountByIdAsync(string connectionString, TopologyProvider provider, string? id)
    {
        await using var context = TopologyContexts.Create(connectionString, provider, TopologyLane.Runtime);
        return id is null
            ? await context.Rows.CountAsync()
            : await context.Rows.CountAsync(row => row.Id == id);
    }

    private static async Task SavepointAsync(
        DbConnection connection,
        DbTransaction transaction,
        TopologyProvider provider,
        string name)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = provider == TopologyProvider.SqlServer
            ? $"SAVE TRANSACTION {name}"
            : $"SAVEPOINT {name}";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task RollbackToSavepointAsync(
        DbConnection connection,
        DbTransaction transaction,
        TopologyProvider provider,
        string name)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = provider == TopologyProvider.SqlServer
            ? $"ROLLBACK TRANSACTION {name}"
            : $"ROLLBACK TO SAVEPOINT {name}";
        await command.ExecuteNonQueryAsync();
    }
}
