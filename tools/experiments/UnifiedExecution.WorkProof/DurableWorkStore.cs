using Microsoft.Data.Sqlite;

namespace Elsa.UnifiedExecution.WorkProof;

/// <summary>
/// Experiment-local durable coordinator store. It persists decisions supplied by a composition
/// policy; it never derives workflow completion from the number of unfinished work rows.
/// </summary>
public sealed class DurableWorkStore
{
    private readonly string _connectionString;
    private readonly IWorkScopeAuthorizer _scopeAuthorizer;

    public DurableWorkStore(string path, IWorkScopeAuthorizer scopeAuthorizer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _scopeAuthorizer = scopeAuthorizer;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 20
        }.ToString();
        Initialize();
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

    private void Initialize()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS executions (
                tenant TEXT NOT NULL, persistence_scope TEXT NOT NULL, authority TEXT NOT NULL,
                execution_id TEXT NOT NULL, policy_id TEXT NOT NULL, state_json TEXT NOT NULL,
                status TEXT NOT NULL, owner TEXT NOT NULL, fence INTEGER NOT NULL, revision INTEGER NOT NULL DEFAULT 0, created_utc TEXT NOT NULL,
                PRIMARY KEY (tenant, persistence_scope, authority, execution_id));
            CREATE TABLE IF NOT EXISTS work (
                tenant TEXT NOT NULL, persistence_scope TEXT NOT NULL, authority TEXT NOT NULL,
                execution_id TEXT NOT NULL, invocation_id TEXT NOT NULL, handler_id TEXT NOT NULL,
                handler_version TEXT NOT NULL, input_json TEXT NOT NULL, status TEXT NOT NULL,
                attempt INTEGER NOT NULL DEFAULT 0, owner TEXT, fence INTEGER NOT NULL DEFAULT 0,
                lease_until INTEGER NOT NULL DEFAULT 0, result_json TEXT, result_fingerprint TEXT,
                unsupported_reason TEXT,
                PRIMARY KEY (tenant, persistence_scope, authority, execution_id, invocation_id),
                FOREIGN KEY (tenant, persistence_scope, authority, execution_id)
                    REFERENCES executions(tenant, persistence_scope, authority, execution_id));
            CREATE TABLE IF NOT EXISTS result_events (
                tenant TEXT NOT NULL, persistence_scope TEXT NOT NULL, authority TEXT NOT NULL,
                execution_id TEXT NOT NULL, invocation_id TEXT NOT NULL, fingerprint TEXT NOT NULL,
                result_json TEXT NOT NULL, processed INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (tenant, persistence_scope, authority, execution_id, invocation_id));
            CREATE TABLE IF NOT EXISTS continuations (
                tenant TEXT NOT NULL, persistence_scope TEXT NOT NULL, authority TEXT NOT NULL,
                execution_id TEXT NOT NULL, continuation_id TEXT NOT NULL, status TEXT NOT NULL DEFAULT 'pending',
                PRIMARY KEY (tenant, persistence_scope, authority, execution_id, continuation_id));
            CREATE TABLE IF NOT EXISTS commit_receipts (
                tenant TEXT NOT NULL, persistence_scope TEXT NOT NULL, authority TEXT NOT NULL,
                execution_id TEXT NOT NULL, commit_id TEXT NOT NULL, fingerprint TEXT NOT NULL,
                receipt_json TEXT NOT NULL,
                PRIMARY KEY (tenant, persistence_scope, authority, execution_id, commit_id));
            CREATE TABLE IF NOT EXISTS audit (
                id INTEGER PRIMARY KEY AUTOINCREMENT, tenant TEXT NOT NULL, persistence_scope TEXT NOT NULL,
                authority TEXT NOT NULL, execution_id TEXT NOT NULL, kind TEXT NOT NULL,
                subject TEXT NOT NULL, detail_json TEXT NOT NULL, created_utc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS barriers (
                barrier_id TEXT PRIMARY KEY, released INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS barrier_entries (
                barrier_id TEXT NOT NULL, worker_id TEXT NOT NULL, process_id INTEGER NOT NULL,
                invocation_id TEXT NOT NULL, entered_utc TEXT NOT NULL,
                PRIMARY KEY (barrier_id, worker_id));
            CREATE INDEX IF NOT EXISTS ix_work_owed ON work(status, execution_id);
            CREATE INDEX IF NOT EXISTS ix_results_pending ON result_events(processed, execution_id);
            """;
        command.ExecuteNonQuery();
    }

    public CommitReceipt CreateAndCommitInitialPlan(WorkScope scope, string executionId, string policyId, string owner, string commitId, CompositionPlan plan)
    {
        _scopeAuthorizer.EnsureAuthorized(scope);
        string content = JsonSupport.Serialize(new { PolicyId = policyId, Plan = plan, SourceResultInvocation = (string?)null, SourceContinuationId = (string?)null });
        string fingerprint = JsonSupport.Fingerprint(content);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        bool exists;
        using (var find = connection.CreateCommand())
        {
            find.Transaction = transaction;
            find.CommandText = "SELECT 1 FROM executions WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution";
            SqliteScopeParameters.Add(find, scope);
            find.Parameters.AddWithValue("$execution", executionId);
            exists = find.ExecuteScalar() is not null;
        }
        if (exists)
        {
            EnsureExecutionScope(connection, transaction, scope, executionId);
            CommitReceipt? prior = ReadCommitReceipt(connection, transaction, scope, executionId, commitId);
            if (prior is null) throw new InvalidOperationException($"Execution '{executionId}' exists without its atomic initial-plan receipt.");
            if (prior.Fingerprint != fingerprint) throw new InvalidOperationException($"Initial commit '{commitId}' was retried with different content.");
            transaction.Commit();
            return prior;
        }

        using (var insertExecution = connection.CreateCommand())
        {
            insertExecution.Transaction = transaction;
            insertExecution.CommandText = "INSERT INTO executions(tenant,persistence_scope,authority,execution_id,policy_id,state_json,status,owner,fence,revision,created_utc) VALUES($tenant,$scope,$authority,$execution,$policy,$state,$status,$owner,1,1,$created)";
            SqliteScopeParameters.Add(insertExecution, scope);
            insertExecution.Parameters.AddWithValue("$execution", executionId);
            insertExecution.Parameters.AddWithValue("$policy", policyId);
            insertExecution.Parameters.AddWithValue("$state", plan.StateJson);
            insertExecution.Parameters.AddWithValue("$status", plan.IsComplete ? "Completed" : "Running");
            insertExecution.Parameters.AddWithValue("$owner", owner);
            insertExecution.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
            insertExecution.ExecuteNonQuery();
        }
        InsertPlanWorkAndContinuations(connection, transaction, scope, executionId, plan);
        var receipt = new CommitReceipt(commitId, fingerprint, plan.StateJson, plan.IsComplete ? "Completed" : "Running", plan.Work.Select(x => x.InvocationId).ToArray(), plan.Continuations.ToArray());
        using (var insertReceipt = connection.CreateCommand())
        {
            insertReceipt.Transaction = transaction;
            insertReceipt.CommandText = "INSERT INTO commit_receipts(tenant,persistence_scope,authority,execution_id,commit_id,fingerprint,receipt_json) VALUES($tenant,$scope,$authority,$execution,$commit,$fingerprint,$receipt)";
            SqliteScopeParameters.Add(insertReceipt, scope);
            insertReceipt.Parameters.AddWithValue("$execution", executionId);
            insertReceipt.Parameters.AddWithValue("$commit", commitId);
            insertReceipt.Parameters.AddWithValue("$fingerprint", fingerprint);
            insertReceipt.Parameters.AddWithValue("$receipt", JsonSupport.Serialize(receipt));
            insertReceipt.ExecuteNonQuery();
        }
        Audit(connection, transaction, scope, executionId, "initial-plan-committed", commitId, content);
        transaction.Commit();
        return receipt;
    }

    public long AcquireExecution(WorkScope scope, string executionId, string newOwner)
    {
        _scopeAuthorizer.EnsureAuthorized(scope);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        EnsureExecutionScope(connection, transaction, scope, executionId);
        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE executions SET owner=$owner,fence=fence+1 WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution";
            SqliteScopeParameters.Add(update, scope);
            update.Parameters.AddWithValue("$execution", executionId);
            update.Parameters.AddWithValue("$owner", newOwner);
            update.ExecuteNonQuery();
        }
        long fence = ReadExecutionFence(connection, transaction, scope, executionId);
        transaction.Commit();
        return fence;
    }

    public CommitReceipt CommitPlan(
        WorkScope scope,
        string executionId,
        string owner,
        long fence,
        long expectedRevision,
        string commitId,
        CompositionPlan plan,
        string? sourceResultInvocation = null,
        string? sourceContinuationId = null)
    {
        return CommitPlanCore(scope, executionId, owner, fence, expectedRevision, commitId, plan, sourceResultInvocation, sourceContinuationId,
            ignoreFence: false, ignoreReceiptFingerprint: false);
    }

    private CommitReceipt CommitPlanCore(
        WorkScope scope,
        string executionId,
        string owner,
        long fence,
        long expectedRevision,
        string commitId,
        CompositionPlan plan,
        string? sourceResultInvocation,
        string? sourceContinuationId,
        bool ignoreFence,
        bool ignoreReceiptFingerprint)
    {
        _scopeAuthorizer.EnsureAuthorized(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(commitId);
        string content = JsonSupport.Serialize(new { Plan = plan, SourceResultInvocation = sourceResultInvocation, SourceContinuationId = sourceContinuationId });
        string fingerprint = JsonSupport.Fingerprint(content);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        // Scope authorization is deliberately checked before looking up a replay receipt.
        EnsureExecutionScope(connection, transaction, scope, executionId);
        CommitReceipt? prior = ReadCommitReceipt(connection, transaction, scope, executionId, commitId);
        if (prior is not null)
        {
            if (!ignoreReceiptFingerprint && prior.Fingerprint != fingerprint)
                throw new InvalidOperationException($"Commit '{commitId}' was already used with different content.");
            transaction.Commit();
            return prior;
        }

        if (!ignoreFence)
        {
            var (currentOwner, currentFence, currentRevision) = ReadExecutionOwnerFence(connection, transaction, scope, executionId);
            if (currentOwner != owner || currentFence != fence)
                throw new InvalidOperationException($"Stale coordinator fence for '{executionId}': supplied {owner}/{fence}, current {currentOwner}/{currentFence}.");
            if (currentRevision != expectedRevision)
                throw new InvalidOperationException($"Stale semantic state revision for '{executionId}': supplied {expectedRevision}, current {currentRevision}.");
        }

        InsertPlanWorkAndContinuations(connection, transaction, scope, executionId, plan);

        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE executions SET state_json=$state,status=$status,revision=revision+1 WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution AND revision=$revision";
            SqliteScopeParameters.Add(update, scope);
            update.Parameters.AddWithValue("$execution", executionId);
            update.Parameters.AddWithValue("$state", plan.StateJson);
            update.Parameters.AddWithValue("$status", plan.IsComplete ? "Completed" : "Running");
            update.Parameters.AddWithValue("$revision", expectedRevision);
            if (update.ExecuteNonQuery() != 1)
                throw new InvalidOperationException($"Concurrent semantic state update for '{executionId}' at revision {expectedRevision}.");
        }

        if (sourceResultInvocation is not null)
            MarkSourceProcessed(connection, transaction, scope, executionId, "result_events", "invocation_id", sourceResultInvocation);
        if (sourceContinuationId is not null)
            MarkSourceProcessed(connection, transaction, scope, executionId, "continuations", "continuation_id", sourceContinuationId);

        var receipt = new CommitReceipt(commitId, fingerprint, plan.StateJson, plan.IsComplete ? "Completed" : "Running",
            plan.Work.Select(x => x.InvocationId).ToArray(), plan.Continuations.ToArray());
        using (var insertReceipt = connection.CreateCommand())
        {
            insertReceipt.Transaction = transaction;
            insertReceipt.CommandText = "INSERT INTO commit_receipts(tenant,persistence_scope,authority,execution_id,commit_id,fingerprint,receipt_json) VALUES($tenant,$scope,$authority,$execution,$commit,$fingerprint,$receipt)";
            SqliteScopeParameters.Add(insertReceipt, scope);
            insertReceipt.Parameters.AddWithValue("$execution", executionId);
            insertReceipt.Parameters.AddWithValue("$commit", commitId);
            insertReceipt.Parameters.AddWithValue("$fingerprint", fingerprint);
            insertReceipt.Parameters.AddWithValue("$receipt", JsonSupport.Serialize(receipt));
            insertReceipt.ExecuteNonQuery();
        }
        Audit(connection, transaction, scope, executionId, "plan-committed", commitId, content);
        transaction.Commit();
        return receipt;
    }

    public CoordinatorSnapshot ReadCoordinator(WorkScope scope, string executionId)
    {
        _scopeAuthorizer.EnsureAuthorized(scope);
        using var connection = Open();
        EnsureExecutionScope(connection, null, scope, executionId);
        var (owner, fence, revision) = ReadExecutionOwnerFence(connection, null, scope, executionId);
        return new CoordinatorSnapshot(owner, fence, revision);
    }

    public ExecutionSnapshot ReadExecution(WorkScope scope, string executionId)
    {
        _scopeAuthorizer.EnsureAuthorized(scope);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT policy_id,state_json,status,owner,fence,revision FROM executions WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution";
        SqliteScopeParameters.Add(command, scope);
        command.Parameters.AddWithValue("$execution", executionId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new UnauthorizedAccessException("Execution is absent from the supplied tenant, persistence, or authority scope.");
        return new ExecutionSnapshot(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4), reader.GetInt64(5));
    }

    public IReadOnlyList<WorkSnapshot> ReadWork(WorkScope scope, string executionId)
    {
        _scopeAuthorizer.EnsureAuthorized(scope);
        using var connection = Open();
        EnsureExecutionScope(connection, null, scope, executionId);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT invocation_id,handler_id,handler_version,input_json,status,attempt,owner,fence,result_json,unsupported_reason FROM work WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution ORDER BY invocation_id";
        SqliteScopeParameters.Add(command, scope);
        command.Parameters.AddWithValue("$execution", executionId);
        using var reader = command.ExecuteReader();
        var rows = new List<WorkSnapshot>();
        while (reader.Read()) rows.Add(new WorkSnapshot(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetInt64(7), reader.IsDBNull(8) ? null : reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9)));
        return rows;
    }

    public IReadOnlyList<PendingResult> ReadPendingResults(WorkScope scope, string executionId)
    {
        _scopeAuthorizer.EnsureAuthorized(scope);
        using var connection = Open();
        EnsureExecutionScope(connection, null, scope, executionId);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT invocation_id,fingerprint,result_json FROM result_events WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution AND processed=0 ORDER BY rowid";
        SqliteScopeParameters.Add(command, scope);
        command.Parameters.AddWithValue("$execution", executionId);
        using var reader = command.ExecuteReader();
        var rows = new List<PendingResult>();
        while (reader.Read()) rows.Add(new PendingResult(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return rows;
    }

    public IReadOnlyList<string> ReadPendingContinuations(WorkScope scope, string executionId)
    {
        _scopeAuthorizer.EnsureAuthorized(scope);
        using var connection = Open();
        EnsureExecutionScope(connection, null, scope, executionId);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT continuation_id FROM continuations WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution AND status='pending' ORDER BY rowid";
        SqliteScopeParameters.Add(command, scope);
        command.Parameters.AddWithValue("$execution", executionId);
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read()) rows.Add(reader.GetString(0));
        return rows;
    }

    public WorkLease? TryClaimWork(WorkScope scope, string executionId, string invocationId, string workerId, string supportedHandlerId, string supportedHandlerVersion, int leaseSeconds = 30)
    {
        _scopeAuthorizer.EnsureAuthorized(scope);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        EnsureExecutionScope(connection, transaction, scope, executionId);
        string? handler = null, version = null, input = null, status = null, owner = null;
        long fence = 0, leaseUntil = 0;
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT handler_id,handler_version,input_json,status,owner,fence,lease_until FROM work WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution AND invocation_id=$invocation";
            SqliteScopeParameters.Add(select, scope);
            select.Parameters.AddWithValue("$execution", executionId);
            select.Parameters.AddWithValue("$invocation", invocationId);
            using var reader = select.ExecuteReader();
            if (!reader.Read()) throw new UnauthorizedAccessException("Work is absent from the supplied scope.");
            handler = reader.GetString(0); version = reader.GetString(1); input = reader.GetString(2); status = reader.GetString(3);
            owner = reader.IsDBNull(4) ? null : reader.GetString(4); fence = reader.GetInt64(5); leaseUntil = reader.GetInt64(6);
        }
        if (handler != supportedHandlerId || version != supportedHandlerVersion)
        {
            using var unsupported = connection.CreateCommand();
            unsupported.Transaction = transaction;
            unsupported.CommandText = "UPDATE work SET unsupported_reason=$reason WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution AND invocation_id=$invocation AND status='owed'";
            SqliteScopeParameters.Add(unsupported, scope);
            unsupported.Parameters.AddWithValue("$execution", executionId);
            unsupported.Parameters.AddWithValue("$invocation", invocationId);
            unsupported.Parameters.AddWithValue("$reason", $"Worker supports {supportedHandlerId}@{supportedHandlerVersion}; pinned work requires {handler}@{version}.");
            unsupported.ExecuteNonQuery();
            transaction.Commit();
            return null;
        }
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (status != "owed" && !(status == "claimed" && leaseUntil <= now))
        {
            transaction.Commit();
            return null;
        }
        int attempt;
        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE work SET status='claimed',owner=$owner,fence=fence+1,attempt=attempt+1,lease_until=$until,unsupported_reason=NULL WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution AND invocation_id=$invocation";
            SqliteScopeParameters.Add(update, scope);
            update.Parameters.AddWithValue("$execution", executionId);
            update.Parameters.AddWithValue("$invocation", invocationId);
            update.Parameters.AddWithValue("$owner", workerId);
            update.Parameters.AddWithValue("$until", now + leaseSeconds);
            update.ExecuteNonQuery();
        }
        using (var readBack = connection.CreateCommand())
        {
            readBack.Transaction = transaction;
            readBack.CommandText = "SELECT attempt,fence FROM work WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution AND invocation_id=$invocation";
            SqliteScopeParameters.Add(readBack, scope);
            readBack.Parameters.AddWithValue("$execution", executionId);
            readBack.Parameters.AddWithValue("$invocation", invocationId);
            using var reader = readBack.ExecuteReader();
            reader.Read();
            attempt = reader.GetInt32(0);
            fence = reader.GetInt64(1);
        }
        Audit(connection, transaction, scope, executionId, "work-claimed", invocationId, JsonSupport.Serialize(new { workerId, fence, attempt }));
        transaction.Commit();
        return new WorkLease(scope, executionId, invocationId, workerId, fence, attempt, handler!, version!, input!);
    }

    public WorkReceipt CompleteWork(WorkLease lease, string resultJson)
    {
        _scopeAuthorizer.EnsureAuthorized(lease.Scope);
        string fingerprint = JsonSupport.Fingerprint(resultJson);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        string status, currentOwner;
        long currentFence;
        string? existingFingerprint;
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT status,owner,fence,result_fingerprint FROM work WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution AND invocation_id=$invocation";
            SqliteScopeParameters.Add(select, lease.Scope);
            select.Parameters.AddWithValue("$execution", lease.ExecutionId);
            select.Parameters.AddWithValue("$invocation", lease.InvocationId);
            using var reader = select.ExecuteReader();
            if (!reader.Read()) throw new UnauthorizedAccessException("Work is absent from the supplied scope.");
            status = reader.GetString(0);
            currentOwner = reader.IsDBNull(1) ? "" : reader.GetString(1);
            currentFence = reader.GetInt64(2);
            existingFingerprint = reader.IsDBNull(3) ? null : reader.GetString(3);
        }
        if (status == "cancelled")
        {
            Audit(connection, transaction, lease.Scope, lease.ExecutionId, "late-result-rejected", lease.InvocationId, resultJson);
            transaction.Commit();
            return new WorkReceipt(lease.InvocationId, "LateRejected", resultJson, fingerprint);
        }
        if (status == "succeeded")
        {
            if (existingFingerprint != fingerprint) throw new InvalidOperationException($"Conflicting result for invocation '{lease.InvocationId}'.");
            string saved;
            using (var get = connection.CreateCommand())
            {
                get.Transaction = transaction;
                get.CommandText = "SELECT result_json FROM work WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution AND invocation_id=$invocation";
                SqliteScopeParameters.Add(get, lease.Scope);
                get.Parameters.AddWithValue("$execution", lease.ExecutionId);
                get.Parameters.AddWithValue("$invocation", lease.InvocationId);
                saved = (string)get.ExecuteScalar()!;
            }
            transaction.Commit();
            return new WorkReceipt(lease.InvocationId, "Succeeded", saved, fingerprint, Duplicate: true);
        }
        if (status != "claimed" || currentOwner != lease.Owner || currentFence != lease.Fence)
            throw new InvalidOperationException($"Stale worker fence for invocation '{lease.InvocationId}'.");

        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE work SET status='succeeded',result_json=$result,result_fingerprint=$fingerprint WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution AND invocation_id=$invocation";
            SqliteScopeParameters.Add(update, lease.Scope);
            update.Parameters.AddWithValue("$execution", lease.ExecutionId);
            update.Parameters.AddWithValue("$invocation", lease.InvocationId);
            update.Parameters.AddWithValue("$result", resultJson);
            update.Parameters.AddWithValue("$fingerprint", fingerprint);
            update.ExecuteNonQuery();
        }
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO result_events(tenant,persistence_scope,authority,execution_id,invocation_id,fingerprint,result_json,processed) VALUES($tenant,$scope,$authority,$execution,$invocation,$fingerprint,$result,0)";
            SqliteScopeParameters.Add(insert, lease.Scope);
            insert.Parameters.AddWithValue("$execution", lease.ExecutionId);
            insert.Parameters.AddWithValue("$invocation", lease.InvocationId);
            insert.Parameters.AddWithValue("$fingerprint", fingerprint);
            insert.Parameters.AddWithValue("$result", resultJson);
            insert.ExecuteNonQuery();
        }
        Audit(connection, transaction, lease.Scope, lease.ExecutionId, "work-result-accepted", lease.InvocationId, resultJson);
        transaction.Commit();
        return new WorkReceipt(lease.InvocationId, "Succeeded", resultJson, fingerprint);
    }

    // The proof uses this as a logical lease-expiry intervention; elapsed wall time is not the evidence.
    internal void ForceExpireWorkForProof(WorkScope scope, string executionId, string invocationId)
    {
        _scopeAuthorizer.EnsureAuthorized(scope);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE work SET lease_until=0 WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution AND invocation_id=$invocation AND status='claimed'";
        SqliteScopeParameters.Add(command, scope);
        command.Parameters.AddWithValue("$execution", executionId);
        command.Parameters.AddWithValue("$invocation", invocationId);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Proof could not expire a claimed work lease.");
    }

    public bool CancelWork(WorkScope scope, string executionId, string invocationId, string cancellationId)
    {
        _scopeAuthorizer.EnsureAuthorized(scope);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        EnsureExecutionScope(connection, transaction, scope, executionId);
        string status;
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT status FROM work WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution AND invocation_id=$invocation";
            SqliteScopeParameters.Add(select, scope);
            select.Parameters.AddWithValue("$execution", executionId);
            select.Parameters.AddWithValue("$invocation", invocationId);
            status = (string?)select.ExecuteScalar() ?? throw new UnauthorizedAccessException("Work is absent from the supplied scope.");
        }
        if (status == "succeeded") { transaction.Commit(); return false; }
        if (status == "cancelled") { transaction.Commit(); return true; }
        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE work SET status='cancelled',owner=NULL,lease_until=0 WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution AND invocation_id=$invocation";
            SqliteScopeParameters.Add(update, scope);
            update.Parameters.AddWithValue("$execution", executionId);
            update.Parameters.AddWithValue("$invocation", invocationId);
            update.ExecuteNonQuery();
        }
        Audit(connection, transaction, scope, executionId, "work-cancelled", invocationId, JsonSupport.Serialize(new { cancellationId }));
        transaction.Commit();
        return true;
    }

    public void CreateBarrier(string barrierId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO barriers(barrier_id,released) VALUES($id,0) ON CONFLICT(barrier_id) DO UPDATE SET released=0";
        command.Parameters.AddWithValue("$id", barrierId);
        command.ExecuteNonQuery();
    }

    public void EnterBarrier(string barrierId, string workerId, int processId, string invocationId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR REPLACE INTO barrier_entries(barrier_id,worker_id,process_id,invocation_id,entered_utc) VALUES($barrier,$worker,$pid,$invocation,$now)";
        command.Parameters.AddWithValue("$barrier", barrierId);
        command.Parameters.AddWithValue("$worker", workerId);
        command.Parameters.AddWithValue("$pid", processId);
        command.Parameters.AddWithValue("$invocation", invocationId);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public int BarrierEntryCount(string barrierId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM barrier_entries WHERE barrier_id=$barrier";
        command.Parameters.AddWithValue("$barrier", barrierId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public IReadOnlyList<int> BarrierProcessIds(string barrierId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT process_id FROM barrier_entries WHERE barrier_id=$barrier ORDER BY worker_id";
        command.Parameters.AddWithValue("$barrier", barrierId);
        using var reader = command.ExecuteReader();
        var result = new List<int>();
        while (reader.Read()) result.Add(reader.GetInt32(0));
        return result;
    }

    public void ReleaseBarrier(string barrierId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE barriers SET released=1 WHERE barrier_id=$barrier";
        command.Parameters.AddWithValue("$barrier", barrierId);
        command.ExecuteNonQuery();
    }

    public bool IsBarrierReleased(string barrierId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT released FROM barriers WHERE barrier_id=$barrier";
        command.Parameters.AddWithValue("$barrier", barrierId);
        return Convert.ToInt32(command.ExecuteScalar() ?? 0) != 0;
    }

    public int CountWorkRows(WorkScope scope, string executionId)
    {
        _scopeAuthorizer.EnsureAuthorized(scope);
        using var connection = Open();
        EnsureExecutionScope(connection, null, scope, executionId);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM work WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution";
        SqliteScopeParameters.Add(command, scope);
        command.Parameters.AddWithValue("$execution", executionId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public IReadOnlyList<string> ReadAuditKinds(WorkScope scope, string executionId)
    {
        _scopeAuthorizer.EnsureAuthorized(scope);
        using var connection = Open();
        EnsureExecutionScope(connection, null, scope, executionId);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT kind FROM audit WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution ORDER BY id";
        SqliteScopeParameters.Add(command, scope);
        command.Parameters.AddWithValue("$execution", executionId);
        using var reader = command.ExecuteReader();
        var result = new List<string>();
        while (reader.Read()) result.Add(reader.GetString(0));
        return result;
    }

    public IReadOnlyList<AuditSnapshot> ReadAudit(WorkScope scope, string executionId)
    {
        _scopeAuthorizer.EnsureAuthorized(scope);
        using var connection = Open();
        EnsureExecutionScope(connection, null, scope, executionId);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT kind,subject,detail_json,created_utc FROM audit WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution ORDER BY id";
        SqliteScopeParameters.Add(command, scope);
        command.Parameters.AddWithValue("$execution", executionId);
        using var reader = command.ExecuteReader();
        var rows = new List<AuditSnapshot>();
        while (reader.Read()) rows.Add(new AuditSnapshot(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return rows;
    }

    // Deliberate negative-control mutants. They are only called by the proof's mutation checks.
    internal void UnsafeCompleteIfNoWork(WorkScope scope, string executionId)
    {
        _scopeAuthorizer.EnsureAuthorized(scope);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE executions SET status='Completed' WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution AND NOT EXISTS (SELECT 1 FROM work WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution AND status IN ('owed','claimed'))";
        SqliteScopeParameters.Add(command, scope);
        command.Parameters.AddWithValue("$execution", executionId);
        command.ExecuteNonQuery();
    }

    internal CommitReceipt UnsafeCommitPlanIgnoringFence(WorkScope scope, string executionId, string owner, long fence, long expectedRevision, string commitId, CompositionPlan plan)
        => CommitPlanCore(scope, executionId, owner, fence, expectedRevision, commitId, plan, null, null, ignoreFence: true, ignoreReceiptFingerprint: false);

    internal CommitReceipt UnsafeCommitPlanIgnoringFingerprint(WorkScope scope, string executionId, string owner, long fence, long expectedRevision, string commitId, CompositionPlan plan)
        => CommitPlanCore(scope, executionId, owner, fence, expectedRevision, commitId, plan, null, null, ignoreFence: false, ignoreReceiptFingerprint: true);

    internal WorkSnapshot? UnsafeReadWorkIgnoringScope(string executionId, string invocationId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT tenant,persistence_scope,authority,invocation_id,handler_id,handler_version,input_json,status,attempt,owner,fence,result_json,unsupported_reason FROM work WHERE execution_id=$execution AND invocation_id=$invocation";
        command.Parameters.AddWithValue("$execution", executionId);
        command.Parameters.AddWithValue("$invocation", invocationId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new WorkSnapshot(reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetInt32(8), reader.IsDBNull(9) ? null : reader.GetString(9), reader.GetInt64(10), reader.IsDBNull(11) ? null : reader.GetString(11), reader.IsDBNull(12) ? null : reader.GetString(12))
        { TenantId = reader.GetString(0), PersistenceScope = reader.GetString(1), Authority = reader.GetString(2) };
    }

    private static void MarkSourceProcessed(SqliteConnection connection, SqliteTransaction transaction, WorkScope scope, string executionId, string table, string idColumn, string id)
    {
        string statusColumn = table == "continuations" ? "status='consumed'" : "processed=1";
        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = $"UPDATE {table} SET {statusColumn} WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution AND {idColumn}=$id AND {(table == "continuations" ? "status='pending'" : "processed=0")}";
        SqliteScopeParameters.Add(update, scope);
        update.Parameters.AddWithValue("$execution", executionId);
        update.Parameters.AddWithValue("$id", id);
        if (update.ExecuteNonQuery() != 1) throw new InvalidOperationException($"Source event '{id}' was not pending.");
    }

    private static void InsertPlanWorkAndContinuations(SqliteConnection connection, SqliteTransaction transaction, WorkScope scope, string executionId, CompositionPlan plan)
    {
        foreach (WorkDefinition work in plan.Work)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(work.InvocationId);
            ArgumentException.ThrowIfNullOrWhiteSpace(work.HandlerId);
            ArgumentException.ThrowIfNullOrWhiteSpace(work.HandlerVersion);
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO work(tenant,persistence_scope,authority,execution_id,invocation_id,handler_id,handler_version,input_json,status) VALUES($tenant,$scope,$authority,$execution,$invocation,$handler,$version,$input,'owed')";
            SqliteScopeParameters.Add(insert, scope);
            insert.Parameters.AddWithValue("$execution", executionId);
            insert.Parameters.AddWithValue("$invocation", work.InvocationId);
            insert.Parameters.AddWithValue("$handler", work.HandlerId);
            insert.Parameters.AddWithValue("$version", work.HandlerVersion);
            insert.Parameters.AddWithValue("$input", work.InputJson);
            insert.ExecuteNonQuery();
        }
        foreach (string continuation in plan.Continuations)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO continuations(tenant,persistence_scope,authority,execution_id,continuation_id,status) VALUES($tenant,$scope,$authority,$execution,$id,'pending')";
            SqliteScopeParameters.Add(insert, scope);
            insert.Parameters.AddWithValue("$execution", executionId);
            insert.Parameters.AddWithValue("$id", continuation);
            insert.ExecuteNonQuery();
        }
    }

    private static CommitReceipt? ReadCommitReceipt(SqliteConnection connection, SqliteTransaction transaction, WorkScope scope, string executionId, string commitId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT receipt_json FROM commit_receipts WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution AND commit_id=$commit";
        SqliteScopeParameters.Add(command, scope);
        command.Parameters.AddWithValue("$execution", executionId);
        command.Parameters.AddWithValue("$commit", commitId);
        string? json = command.ExecuteScalar() is string value ? value : null;
        return json is null ? null : JsonSupport.Deserialize<CommitReceipt>(json);
    }

    private static void EnsureExecutionScope(SqliteConnection connection, SqliteTransaction? transaction, WorkScope scope, string executionId)
    {
        scope.Validate();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM executions WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution";
        SqliteScopeParameters.Add(command, scope);
        command.Parameters.AddWithValue("$execution", executionId);
        if (command.ExecuteScalar() is null) throw new UnauthorizedAccessException("Execution is absent from the supplied tenant, persistence, or authority scope.");
    }

    private static long ReadExecutionFence(SqliteConnection connection, SqliteTransaction transaction, WorkScope scope, string executionId)
        => ReadExecutionOwnerFence(connection, transaction, scope, executionId).Fence;

    private static (string Owner, long Fence, long Revision) ReadExecutionOwnerFence(SqliteConnection connection, SqliteTransaction? transaction, WorkScope scope, string executionId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT owner,fence,revision FROM executions WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution";
        SqliteScopeParameters.Add(command, scope);
        command.Parameters.AddWithValue("$execution", executionId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new UnauthorizedAccessException("Execution is absent from the supplied scope.");
        return (reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    private static void Audit(SqliteConnection connection, SqliteTransaction transaction, WorkScope scope, string executionId, string kind, string subject, string detail)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO audit(tenant,persistence_scope,authority,execution_id,kind,subject,detail_json,created_utc) VALUES($tenant,$scope,$authority,$execution,$kind,$subject,$detail,$now)";
        SqliteScopeParameters.Add(command, scope);
        command.Parameters.AddWithValue("$execution", executionId);
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$subject", subject);
        command.Parameters.AddWithValue("$detail", detail);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

}

public sealed record ExecutionSnapshot(string PolicyId, string StateJson, string Status, string Owner, long Fence, long Revision);
public sealed record CoordinatorSnapshot(string Owner, long Fence, long Revision);
public sealed record PendingResult(string InvocationId, string Fingerprint, string ResultJson);
public sealed record WorkSnapshot(string InvocationId, string HandlerId, string HandlerVersion, string InputJson, string Status, int Attempt, string? Owner, long Fence, string? ResultJson, string? UnsupportedReason)
{
    public string? TenantId { get; init; }
    public string? PersistenceScope { get; init; }
    public string? Authority { get; init; }
}
public sealed record AuditSnapshot(string Kind, string Subject, string DetailJson, string CreatedUtc);
