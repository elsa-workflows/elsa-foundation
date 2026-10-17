using Microsoft.Data.Sqlite;

namespace Elsa.UnifiedExecution.WorkProof;

public sealed record PartitionRecord(int Ordinal, string Payload);
public sealed record PartitionBatchInput(string PartitionId, long Cursor, int BatchSize);
public sealed record PartitionPipelineInput(IReadOnlyList<PartitionBatchInput> Partitions);
public sealed record PartitionBatchResult(string PartitionId, long InputCursor, long NextCursor, int Fetched, bool IsEmpty);
public sealed record PartitionMeasurement(int Fetched, int CurrentInFlight, string Status);

/// <summary>
/// ETL-specific source and in-flight measurements only. Claims, fencing, durable dispatch,
/// terminal receipts, and coordinator transitions all use DurableWorkStore.
/// </summary>
public sealed class PartitionSourceStore
{
    private readonly string _connectionString;
    private readonly IWorkScopeAuthorizer _scopeAuthorizer;

    public PartitionSourceStore(string path, IWorkScopeAuthorizer scopeAuthorizer)
    {
        _scopeAuthorizer = scopeAuthorizer;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared, DefaultTimeout = 20 }.ToString();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS partition_configs(
                tenant TEXT NOT NULL,persistence_scope TEXT NOT NULL,authority TEXT NOT NULL,execution_id TEXT NOT NULL,
                partition_id TEXT NOT NULL,batch_size INTEGER NOT NULL,max_batch_records INTEGER NOT NULL,
                PRIMARY KEY(tenant,persistence_scope,authority,execution_id,partition_id));
            CREATE TABLE IF NOT EXISTS partition_source(
                tenant TEXT NOT NULL,persistence_scope TEXT NOT NULL,authority TEXT NOT NULL,execution_id TEXT NOT NULL,
                partition_id TEXT NOT NULL,ordinal INTEGER NOT NULL,payload TEXT NOT NULL,
                PRIMARY KEY(tenant,persistence_scope,authority,execution_id,partition_id,ordinal));
            CREATE TABLE IF NOT EXISTS partition_measurements(
                tenant TEXT NOT NULL,persistence_scope TEXT NOT NULL,authority TEXT NOT NULL,execution_id TEXT NOT NULL,
                invocation_id TEXT NOT NULL,attempt INTEGER NOT NULL,fetched INTEGER NOT NULL,in_flight INTEGER NOT NULL,
                status TEXT NOT NULL,PRIMARY KEY(tenant,persistence_scope,authority,execution_id,invocation_id,attempt));
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout=20000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    public void CreatePartition(WorkScope scope, string executionId, string partitionId, int batchSize, int maxBatchRecords, IEnumerable<string> payloads)
    {
        _scopeAuthorizer.EnsureAuthorized(scope);
        if (batchSize < 1 || maxBatchRecords < batchSize) throw new ArgumentOutOfRangeException(nameof(batchSize), "The record limit must be at least one configured batch.");
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO partition_configs(tenant,persistence_scope,authority,execution_id,partition_id,batch_size,max_batch_records) VALUES($tenant,$scope,$authority,$execution,$partition,$batch,$max)";
            SqliteScopeParameters.Add(insert, scope);
            insert.Parameters.AddWithValue("$execution", executionId);
            insert.Parameters.AddWithValue("$partition", partitionId);
            insert.Parameters.AddWithValue("$batch", batchSize);
            insert.Parameters.AddWithValue("$max", maxBatchRecords);
            insert.ExecuteNonQuery();
        }
        int ordinal = 0;
        foreach (string payload in payloads)
        {
            using var insertSource = connection.CreateCommand();
            insertSource.Transaction = transaction;
            insertSource.CommandText = "INSERT INTO partition_source(tenant,persistence_scope,authority,execution_id,partition_id,ordinal,payload) VALUES($tenant,$scope,$authority,$execution,$partition,$ordinal,$payload)";
            SqliteScopeParameters.Add(insertSource, scope);
            insertSource.Parameters.AddWithValue("$execution", executionId);
            insertSource.Parameters.AddWithValue("$partition", partitionId);
            insertSource.Parameters.AddWithValue("$ordinal", ordinal++);
            insertSource.Parameters.AddWithValue("$payload", payload);
            insertSource.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public IReadOnlyList<PartitionRecord> ReadBatch(WorkScope scope, string executionId, string invocationId, int attempt, PartitionBatchInput input)
    {
        _scopeAuthorizer.EnsureAuthorized(scope);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        int configuredBatchSize;
        int configuredMax;
        using (var config = connection.CreateCommand())
        {
            config.Transaction = transaction;
            config.CommandText = "SELECT batch_size,max_batch_records FROM partition_configs WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution AND partition_id=$partition";
            SqliteScopeParameters.Add(config, scope);
            config.Parameters.AddWithValue("$execution", executionId);
            config.Parameters.AddWithValue("$partition", input.PartitionId);
            using var reader = config.ExecuteReader();
            if (!reader.Read()) throw new UnauthorizedAccessException("Partition source is absent from the supplied scope.");
            configuredBatchSize = reader.GetInt32(0);
            configuredMax = reader.GetInt32(1);
        }
        if (input.BatchSize != configuredBatchSize || input.BatchSize > configuredMax)
            throw new InvalidOperationException("The pinned batch size differs from the authorized partition configuration.");
        using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = "SELECT ordinal,payload FROM partition_source WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution AND partition_id=$partition AND ordinal>$cursor ORDER BY ordinal LIMIT $limit";
        SqliteScopeParameters.Add(select, scope);
        select.Parameters.AddWithValue("$execution", executionId);
        select.Parameters.AddWithValue("$partition", input.PartitionId);
        select.Parameters.AddWithValue("$cursor", input.Cursor);
        select.Parameters.AddWithValue("$limit", input.BatchSize);
        using var reader2 = select.ExecuteReader();
        var rows = new List<PartitionRecord>();
        while (reader2.Read()) rows.Add(new PartitionRecord(reader2.GetInt32(0), reader2.GetString(1)));
        reader2.Close();
        if (rows.Count > input.BatchSize) throw new InvalidOperationException("Partition query exceeded its bounded batch.");
        using (var metric = connection.CreateCommand())
        {
            metric.Transaction = transaction;
            metric.CommandText = "INSERT INTO partition_measurements(tenant,persistence_scope,authority,execution_id,invocation_id,attempt,fetched,in_flight,status) VALUES($tenant,$scope,$authority,$execution,$invocation,$attempt,$fetched,$fetched,'inflight')";
            SqliteScopeParameters.Add(metric, scope);
            metric.Parameters.AddWithValue("$execution", executionId);
            metric.Parameters.AddWithValue("$invocation", invocationId);
            metric.Parameters.AddWithValue("$attempt", attempt);
            metric.Parameters.AddWithValue("$fetched", rows.Count);
            metric.ExecuteNonQuery();
        }
        transaction.Commit();
        return rows;
    }

    public void EndBatch(WorkScope scope, string executionId, string invocationId, int attempt, string status)
    {
        _scopeAuthorizer.EnsureAuthorized(scope);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE partition_measurements SET in_flight=0,status=$status WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution AND invocation_id=$invocation AND attempt=$attempt";
        SqliteScopeParameters.Add(command, scope);
        command.Parameters.AddWithValue("$execution", executionId);
        command.Parameters.AddWithValue("$invocation", invocationId);
        command.Parameters.AddWithValue("$attempt", attempt);
        command.Parameters.AddWithValue("$status", status);
        command.ExecuteNonQuery();
    }

    public int CurrentInFlight(WorkScope scope, params string[] executionIds)
    {
        _scopeAuthorizer.EnsureAuthorized(scope);
        if (executionIds.Length == 0) return 0;
        using var connection = Open();
        using var command = connection.CreateCommand();
        string[] names = executionIds.Select((_, i) => $"$e{i}").ToArray();
        command.CommandText = $"SELECT coalesce(sum(in_flight),0) FROM partition_measurements WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id IN ({string.Join(',', names)})";
        SqliteScopeParameters.Add(command, scope);
        for (int i = 0; i < executionIds.Length; i++) command.Parameters.AddWithValue(names[i], executionIds[i]);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public int PeakFetched(WorkScope scope, string executionId)
    {
        _scopeAuthorizer.EnsureAuthorized(scope);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT coalesce(max(fetched),0) FROM partition_measurements WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution";
        SqliteScopeParameters.Add(command, scope);
        command.Parameters.AddWithValue("$execution", executionId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public int SourceCount(WorkScope scope, string executionId, string partitionId)
    {
        _scopeAuthorizer.EnsureAuthorized(scope);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM partition_source WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution AND partition_id=$partition";
        SqliteScopeParameters.Add(command, scope);
        command.Parameters.AddWithValue("$execution", executionId);
        command.Parameters.AddWithValue("$partition", partitionId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

}

/// <summary>ETL semantics stores cursor state and schedules the next bounded batch through the shared work store.</summary>
public sealed class PartitionPipelinePolicy : ICompositionPolicy
{
    public string Id => "dataflow-partition-witness-v1";

    public CompositionPlan Start(string inputJson)
    {
        PartitionPipelineInput input = JsonSupport.Deserialize<PartitionPipelineInput>(inputJson);
        if (input.Partitions.Count == 0 || input.Partitions.Select(x => x.PartitionId).Distinct(StringComparer.Ordinal).Count() != input.Partitions.Count)
            throw new InvalidOperationException("A pipeline needs at least one uniquely named partition.");
        if (input.Partitions.Any(x => x.Cursor != -1 || x.BatchSize < 1))
            throw new InvalidOperationException("Initial partition cursors must be -1 and batch sizes must be positive.");

        PartitionState[] partitions = input.Partitions
            .Select(x => new PartitionState(x.PartitionId, x.Cursor, x.BatchSize, false))
            .ToArray();
        WorkDefinition[] work = partitions.Select(x => Definition(x.PartitionId, x.Cursor, x.BatchSize)).ToArray();
        return new CompositionPlan(JsonSupport.Serialize(new PartitionPipelineState(partitions)), work, [], false);
    }

    public CompositionPlan ApplyResult(string stateJson, WorkReceipt receipt)
    {
        PartitionPipelineState state = JsonSupport.Deserialize<PartitionPipelineState>(stateJson);
        PartitionBatchResult result = JsonSupport.Deserialize<PartitionBatchResult>(receipt.ResultJson);
        PartitionState partition = state.Partitions.SingleOrDefault(x => x.PartitionId == result.PartitionId)
            ?? throw new InvalidOperationException($"Unknown partition result '{result.PartitionId}'.");
        if (receipt.Status != "Succeeded" || partition.Complete || result.InputCursor != partition.Cursor ||
            receipt.InvocationId != Definition(partition.PartitionId, partition.Cursor, partition.BatchSize).InvocationId)
            throw new InvalidOperationException("Partition result does not match the pinned cursor and partition identity.");
        if (result.Fetched == 0)
        {
            if (!result.IsEmpty || result.NextCursor != partition.Cursor) throw new InvalidOperationException("Empty partition batch advanced its cursor.");
            PartitionPipelineState finishedPartition = state with
            {
                Partitions = state.Partitions.Select(x => x.PartitionId == partition.PartitionId ? x with { Complete = true } : x).ToArray()
            };
            return new CompositionPlan(JsonSupport.Serialize(finishedPartition), [], [], finishedPartition.Partitions.All(x => x.Complete));
        }
        if (result.IsEmpty || result.Fetched > partition.BatchSize || result.NextCursor <= partition.Cursor)
            throw new InvalidOperationException("Partition result violates its batch or cursor bounds.");
        PartitionPipelineState nextState = state with
        {
            Partitions = state.Partitions.Select(x => x.PartitionId == partition.PartitionId ? x with { Cursor = result.NextCursor } : x).ToArray()
        };
        return new CompositionPlan(JsonSupport.Serialize(nextState), [Definition(partition.PartitionId, result.NextCursor, partition.BatchSize)], [], false);
    }

    public CompositionPlan Continue(string stateJson, string continuationId)
        => throw new InvalidOperationException($"Partition pipeline has no semantic continuation '{continuationId}'.");

    private static WorkDefinition Definition(string partition, long cursor, int batchSize)
    {
        string invocation = $"partition:{partition}:after:{cursor}";
        return new WorkDefinition(invocation, "partition-batch", "1", JsonSupport.Serialize(new PartitionBatchInput(partition, cursor, batchSize)));
    }

    private sealed record PartitionPipelineState(PartitionState[] Partitions);
    private sealed record PartitionState(string PartitionId, long Cursor, int BatchSize, bool Complete);
}
