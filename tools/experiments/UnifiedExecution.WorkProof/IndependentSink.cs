using Microsoft.Data.Sqlite;

namespace Elsa.UnifiedExecution.WorkProof;

/// <summary>Independent destination database with deterministic idempotency keys.</summary>
public sealed class IndependentSink
{
    private readonly string _connectionString;
    private readonly IWorkScopeAuthorizer _scopeAuthorizer;

    public IndependentSink(string path, IWorkScopeAuthorizer scopeAuthorizer)
    {
        _scopeAuthorizer = scopeAuthorizer;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared, DefaultTimeout = 20 }.ToString();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS effects(
                tenant TEXT NOT NULL,persistence_scope TEXT NOT NULL,authority TEXT NOT NULL,execution_id TEXT NOT NULL,
                effect_key TEXT NOT NULL,payload TEXT NOT NULL,created_utc TEXT NOT NULL,
                PRIMARY KEY(tenant,persistence_scope,authority,execution_id,effect_key));
            CREATE TABLE IF NOT EXISTS deliveries(
                id INTEGER PRIMARY KEY AUTOINCREMENT,tenant TEXT NOT NULL,persistence_scope TEXT NOT NULL,
                authority TEXT NOT NULL,execution_id TEXT NOT NULL,effect_key TEXT NOT NULL,duplicate INTEGER NOT NULL,
                created_utc TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=20000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    /// <returns>true when the destination had already accepted this exact effect.</returns>
    public bool Write(WorkScope scope, string executionId, string effectKey, string payload)
    {
        _scopeAuthorizer.EnsureAuthorized(scope);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        bool duplicate;
        string? prior;
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT payload FROM effects WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution AND effect_key=$key";
            SqliteScopeParameters.Add(select, scope);
            select.Parameters.AddWithValue("$execution", executionId);
            select.Parameters.AddWithValue("$key", effectKey);
            prior = select.ExecuteScalar() as string;
        }
        duplicate = prior is not null;
        if (prior is not null && prior != payload)
            throw new InvalidOperationException($"Destination key '{effectKey}' was reused for different content.");
        if (prior is null)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO effects(tenant,persistence_scope,authority,execution_id,effect_key,payload,created_utc) VALUES($tenant,$scope,$authority,$execution,$key,$payload,$now)";
            SqliteScopeParameters.Add(insert, scope);
            insert.Parameters.AddWithValue("$execution", executionId);
            insert.Parameters.AddWithValue("$key", effectKey);
            insert.Parameters.AddWithValue("$payload", payload);
            insert.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            insert.ExecuteNonQuery();
        }
        using (var delivery = connection.CreateCommand())
        {
            delivery.Transaction = transaction;
            delivery.CommandText = "INSERT INTO deliveries(tenant,persistence_scope,authority,execution_id,effect_key,duplicate,created_utc) VALUES($tenant,$scope,$authority,$execution,$key,$duplicate,$now)";
            SqliteScopeParameters.Add(delivery, scope);
            delivery.Parameters.AddWithValue("$execution", executionId);
            delivery.Parameters.AddWithValue("$key", effectKey);
            delivery.Parameters.AddWithValue("$duplicate", duplicate ? 1 : 0);
            delivery.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            delivery.ExecuteNonQuery();
        }
        transaction.Commit();
        return duplicate;
    }

    public int CountEffects(WorkScope scope, string executionId)
    {
        _scopeAuthorizer.EnsureAuthorized(scope);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM effects WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution";
        SqliteScopeParameters.Add(command, scope);
        command.Parameters.AddWithValue("$execution", executionId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public int CountDeliveries(WorkScope scope, string executionId)
    {
        _scopeAuthorizer.EnsureAuthorized(scope);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM deliveries WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution";
        SqliteScopeParameters.Add(command, scope);
        command.Parameters.AddWithValue("$execution", executionId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

}
