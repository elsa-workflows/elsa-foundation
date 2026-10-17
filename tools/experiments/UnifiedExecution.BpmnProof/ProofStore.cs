using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bpmn.Model.State;
using Bpmn.Semantics;
using Microsoft.Data.Sqlite;

namespace UnifiedExecution.BpmnProof;

internal sealed class ProofStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SqliteConnection _connection;

    public ProofStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 10
        }.ToString());
        _connection.Open();

        using var pragma = _connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();

        using var schema = _connection.CreateCommand();
        schema.CommandText = """
            CREATE TABLE IF NOT EXISTS scopes (
                scope_id TEXT PRIMARY KEY,
                process_id TEXT NOT NULL,
                definition_fingerprint TEXT NULL,
                state_json TEXT NOT NULL,
                continuation_kind TEXT NOT NULL,
                continuation_json TEXT NULL,
                correlation_json TEXT NOT NULL,
                has_parent INTEGER NOT NULL,
                next_handle INTEGER NOT NULL DEFAULT 1,
                clock_ticks INTEGER NOT NULL DEFAULT 0,
                revision INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS work_items (
                handle TEXT PRIMARY KEY,
                scope_id TEXT NOT NULL REFERENCES scopes(scope_id),
                binding_ref TEXT NOT NULL,
                element_id TEXT NOT NULL,
                iteration_id TEXT NULL,
                work_kind TEXT NOT NULL,
                status TEXT NOT NULL,
                due_at_ticks INTEGER NULL,
                command_id TEXT NOT NULL UNIQUE
            );
            CREATE TABLE IF NOT EXISTS command_outbox (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                command_id TEXT NOT NULL UNIQUE,
                scope_id TEXT NOT NULL REFERENCES scopes(scope_id),
                revision INTEGER NOT NULL,
                ordinal INTEGER NOT NULL,
                command_json TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'pending'
            );
            CREATE TABLE IF NOT EXISTS incidents (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                scope_id TEXT NOT NULL REFERENCES scopes(scope_id),
                binding_ref TEXT NOT NULL,
                handle TEXT NOT NULL,
                message TEXT NOT NULL,
                revision INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS bridge_deliveries (
                delivery_id TEXT PRIMARY KEY,
                scope_id TEXT NOT NULL REFERENCES scopes(scope_id),
                handle TEXT NOT NULL,
                result_json TEXT NOT NULL,
                fingerprint TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'pending'
            );
            CREATE TABLE IF NOT EXISTS trace_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                scope_id TEXT NOT NULL REFERENCES scopes(scope_id),
                payload_json TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_outbox_pending ON command_outbox(scope_id, status, revision, ordinal);
            CREATE INDEX IF NOT EXISTS ix_work_live ON work_items(scope_id, status);
            """;
        schema.ExecuteNonQuery();

        using var columns = _connection.CreateCommand();
        columns.CommandText = "PRAGMA table_info(scopes);";
        using var reader = columns.ExecuteReader();
        var hasFingerprint = false;
        while (reader.Read())
            hasFingerprint |= string.Equals(reader.GetString(1), "definition_fingerprint", StringComparison.Ordinal);
        reader.Close();
        if (!hasFingerprint)
        {
            using var migration = _connection.CreateCommand();
            migration.CommandText = "ALTER TABLE scopes ADD COLUMN definition_fingerprint TEXT NULL;";
            migration.ExecuteNonQuery();
        }
    }

    public void Dispose() => _connection.Dispose();

    public void CreateScope(string scopeId, string processId, string definitionFingerprint, IReadOnlyDictionary<string, string>? correlation = null, bool hasParent = false)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO scopes(scope_id, process_id, definition_fingerprint, state_json, continuation_kind, correlation_json, has_parent)
            VALUES ($scope, $process, $fingerprint, $state, 'defer', $correlation, $hasParent);
            """;
        command.Parameters.AddWithValue("$scope", scopeId);
        command.Parameters.AddWithValue("$process", processId);
        command.Parameters.AddWithValue("$fingerprint", definitionFingerprint);
        command.Parameters.AddWithValue("$state", JsonSerializer.Serialize(new BpmnExecutionState(), JsonOptions));
        command.Parameters.AddWithValue("$correlation", JsonSerializer.Serialize(correlation ?? new Dictionary<string, string>(), JsonOptions));
        command.Parameters.AddWithValue("$hasParent", hasParent ? 1 : 0);
        command.ExecuteNonQuery();
        AppendTrace(scopeId, new HostTraceEvent("scope-created", Message: processId));
    }

    public ScopeRecord GetScope(string scopeId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT scope_id, process_id, definition_fingerprint, state_json, continuation_kind, continuation_json, correlation_json, has_parent, next_handle, clock_ticks, revision FROM scopes WHERE scope_id=$scope;";
        command.Parameters.AddWithValue("$scope", scopeId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException($"No proof scope '{scopeId}' is stored.");

        return new ScopeRecord(
            reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6), reader.GetInt32(7) != 0, reader.GetInt32(8), reader.GetInt64(9), reader.GetInt32(10));
    }

    public bool HasScope(string scopeId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM scopes WHERE scope_id=$scope);";
        command.Parameters.AddWithValue("$scope", scopeId);
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) != 0;
    }

    public BpmnExecutionState GetState(string scopeId) =>
        JsonSerializer.Deserialize<BpmnExecutionState>(GetScope(scopeId).StateJson, JsonOptions)
        ?? throw new InvalidOperationException($"Stored execution state for '{scopeId}' was null.");

    public IReadOnlyList<BpmnLiveWork> GetLiveWork(string scopeId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT binding_ref, iteration_id, handle FROM work_items WHERE scope_id=$scope AND status='live' ORDER BY rowid;";
        command.Parameters.AddWithValue("$scope", scopeId);
        using var reader = command.ExecuteReader();
        var items = new List<BpmnLiveWork>();
        while (reader.Read())
            items.Add(new BpmnLiveWork(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2)));
        return items;
    }

    public IReadOnlyList<WorkRecord> GetWorkItems(string scopeId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT handle, scope_id, binding_ref, element_id, iteration_id, work_kind, status, due_at_ticks, command_id FROM work_items WHERE scope_id=$scope ORDER BY rowid;";
        command.Parameters.AddWithValue("$scope", scopeId);
        using var reader = command.ExecuteReader();
        var items = new List<WorkRecord>();
        while (reader.Read())
            items.Add(ReadWork(reader));
        return items;
    }

    public WorkRecord GetWork(string handle)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT handle, scope_id, binding_ref, element_id, iteration_id, work_kind, status, due_at_ticks, command_id FROM work_items WHERE handle=$handle;";
        command.Parameters.AddWithValue("$handle", handle);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException($"Unknown work handle '{handle}'.");
        return ReadWork(reader);
    }

    public BridgeDeliveryRecord EnsureBridgeDelivery(string scopeId, string deliveryId, string handle, string resultJson)
    {
        string fingerprint = DeliveryFingerprint(scopeId, handle, resultJson);
        using var transaction = _connection.BeginTransaction(deferred: false);
        using (var ownedHandle = _connection.CreateCommand())
        {
            ownedHandle.Transaction = transaction;
            ownedHandle.CommandText = "SELECT 1 FROM work_items WHERE scope_id=$scope AND handle=$handle;";
            ownedHandle.Parameters.AddWithValue("$scope", scopeId);
            ownedHandle.Parameters.AddWithValue("$handle", handle);
            if (ownedHandle.ExecuteScalar() is null)
                throw new InvalidOperationException($"Bridge delivery '{deliveryId}' references a handle outside scope '{scopeId}'.");
        }

        BridgeDeliveryRecord? prior = ReadBridgeDelivery(deliveryId, transaction);
        if (prior is not null)
        {
            if (prior.ScopeId != scopeId || prior.Handle != handle || prior.Fingerprint != fingerprint)
                throw new InvalidOperationException($"Bridge delivery '{deliveryId}' was retried with different scope, handle, or result content.");
            transaction.Commit();
            return prior;
        }

        using (var insert = _connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO bridge_deliveries(delivery_id,scope_id,handle,result_json,fingerprint,status) VALUES($id,$scope,$handle,$result,$fingerprint,'pending');";
            insert.Parameters.AddWithValue("$id", deliveryId);
            insert.Parameters.AddWithValue("$scope", scopeId);
            insert.Parameters.AddWithValue("$handle", handle);
            insert.Parameters.AddWithValue("$result", resultJson);
            insert.Parameters.AddWithValue("$fingerprint", fingerprint);
            insert.ExecuteNonQuery();
        }
        transaction.Commit();
        return new BridgeDeliveryRecord(deliveryId, scopeId, handle, resultJson, fingerprint, "pending");
    }

    public BridgeDeliveryRecord GetBridgeDelivery(string scopeId, string deliveryId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT delivery_id,scope_id,handle,result_json,fingerprint,status FROM bridge_deliveries WHERE scope_id=$scope AND delivery_id=$id;";
        command.Parameters.AddWithValue("$scope", scopeId);
        command.Parameters.AddWithValue("$id", deliveryId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException($"No bridge delivery '{deliveryId}' is stored for scope '{scopeId}'.");
        return ReadBridgeDelivery(reader);
    }

    public BridgeDeliveryRecord? FindBridgeDelivery(string scopeId, string deliveryId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT delivery_id,scope_id,handle,result_json,fingerprint,status FROM bridge_deliveries WHERE scope_id=$scope AND delivery_id=$id;";
        command.Parameters.AddWithValue("$scope", scopeId);
        command.Parameters.AddWithValue("$id", deliveryId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadBridgeDelivery(reader) : null;
    }

    public IReadOnlyList<WorkRecord> GetDueTimers(string scopeId, long throughTicks)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT handle, scope_id, binding_ref, element_id, iteration_id, work_kind, status, due_at_ticks, command_id FROM work_items WHERE scope_id=$scope AND status='live' AND work_kind='timer' AND due_at_ticks <= $through ORDER BY due_at_ticks, rowid;";
        command.Parameters.AddWithValue("$scope", scopeId);
        command.Parameters.AddWithValue("$through", throughTicks);
        using var reader = command.ExecuteReader();
        var items = new List<WorkRecord>();
        while (reader.Read())
            items.Add(ReadWork(reader));
        return items;
    }

    public bool CommitEvaluation(
        string scopeId,
        BpmnEvaluation evaluation,
        IReadOnlyCollection<StoredHostCommand> commands,
        string? callbackHandle = null,
        string? callbackStatus = null,
        IncidentRecord? incident = null,
        HostTraceEvent? trigger = null,
        string? bridgeDeliveryId = null)
    {
        var commandBatch = commands.ToArray();
        using var transaction = _connection.BeginTransaction(deferred: bridgeDeliveryId is null);
        var scope = ReadScope(scopeId, transaction);
        BridgeDeliveryRecord? bridgeDelivery = null;
        if (bridgeDeliveryId is not null)
        {
            bridgeDelivery = ReadBridgeDelivery(bridgeDeliveryId, transaction);
            if (bridgeDelivery is null || bridgeDelivery.ScopeId != scopeId || bridgeDelivery.Handle != callbackHandle)
                throw new InvalidOperationException($"Bridge delivery '{bridgeDeliveryId}' does not match callback scope and handle.");
            if (bridgeDelivery.Status == "delivered")
            {
                transaction.Rollback();
                return false;
            }
            if (bridgeDelivery.Status != "pending")
                throw new InvalidOperationException($"Bridge delivery '{bridgeDeliveryId}' has unsupported state '{bridgeDelivery.Status}'.");
            using var workStatus = _connection.CreateCommand();
            workStatus.Transaction = transaction;
            workStatus.CommandText = "SELECT status FROM work_items WHERE scope_id=$scope AND handle=$handle;";
            workStatus.Parameters.AddWithValue("$scope", scopeId);
            workStatus.Parameters.AddWithValue("$handle", callbackHandle!);
            if (!string.Equals(workStatus.ExecuteScalar() as string, "live", StringComparison.Ordinal))
                throw new InvalidOperationException($"Bridge callback handle '{callbackHandle}' is not live.");
        }
        var revision = scope.Revision + 1;
        var continuation = SerializeContinuation(evaluation.Continuation);

        using (var update = _connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE scopes SET state_json=$state, continuation_kind=$kind, continuation_json=$continuation, revision=$revision WHERE scope_id=$scope;";
            update.Parameters.AddWithValue("$state", JsonSerializer.Serialize(evaluation.State.Prune(), JsonOptions));
            update.Parameters.AddWithValue("$kind", continuation.Kind);
            update.Parameters.AddWithValue("$continuation", continuation.Json is null ? DBNull.Value : continuation.Json);
            update.Parameters.AddWithValue("$revision", revision);
            update.Parameters.AddWithValue("$scope", scopeId);
            update.ExecuteNonQuery();
        }

        if (callbackHandle is not null)
        {
            using var updateWork = _connection.CreateCommand();
            updateWork.Transaction = transaction;
            updateWork.CommandText = "UPDATE work_items SET status=$status WHERE handle=$handle;";
            updateWork.Parameters.AddWithValue("$status", callbackStatus ?? "completed");
            updateWork.Parameters.AddWithValue("$handle", callbackHandle);
            updateWork.ExecuteNonQuery();
        }

        if (bridgeDelivery is not null)
        {
            using var delivered = _connection.CreateCommand();
            delivered.Transaction = transaction;
            delivered.CommandText = "UPDATE bridge_deliveries SET status='delivered' WHERE delivery_id=$id AND scope_id=$scope AND status='pending';";
            delivered.Parameters.AddWithValue("$id", bridgeDelivery.DeliveryId);
            delivered.Parameters.AddWithValue("$scope", scopeId);
            if (delivered.ExecuteNonQuery() != 1)
                throw new InvalidOperationException($"Bridge delivery '{bridgeDelivery.DeliveryId}' was concurrently applied.");
        }

        if (trigger is not null)
            InsertTrace(scopeId, trigger with { Revision = revision }, transaction);

        InsertTrace(scopeId, new HostTraceEvent(
            "evaluation", Continuation: continuation.Kind, Revision: revision), transaction);

        if (incident is not null)
        {
            using var addIncident = _connection.CreateCommand();
            addIncident.Transaction = transaction;
            addIncident.CommandText = "INSERT INTO incidents(scope_id, binding_ref, handle, message, revision) VALUES ($scope, $binding, $handle, $message, $revision);";
            addIncident.Parameters.AddWithValue("$scope", incident.ScopeId);
            addIncident.Parameters.AddWithValue("$binding", incident.BindingRef);
            addIncident.Parameters.AddWithValue("$handle", incident.Handle);
            addIncident.Parameters.AddWithValue("$message", incident.Message);
            addIncident.Parameters.AddWithValue("$revision", revision);
            addIncident.ExecuteNonQuery();
            InsertTrace(scopeId, new HostTraceEvent("incident-recorded", BindingRef: incident.BindingRef, Handle: incident.Handle, Message: incident.Message, Revision: revision), transaction);
        }

        for (var ordinal = 0; ordinal < commandBatch.Length; ordinal++)
        {
            var commandId = $"{scopeId}:{revision}:{ordinal}";
            using var add = _connection.CreateCommand();
            add.Transaction = transaction;
            add.CommandText = "INSERT INTO command_outbox(command_id, scope_id, revision, ordinal, command_json) VALUES ($id, $scope, $revision, $ordinal, $json);";
            add.Parameters.AddWithValue("$id", commandId);
            add.Parameters.AddWithValue("$scope", scopeId);
            add.Parameters.AddWithValue("$revision", revision);
            add.Parameters.AddWithValue("$ordinal", ordinal);
            add.Parameters.AddWithValue("$json", JsonSerializer.Serialize(commandBatch[ordinal], JsonOptions));
            add.ExecuteNonQuery();
        }

        transaction.Commit();
        return true;
    }

    public IReadOnlyList<PendingCommand> GetPendingCommands(string scopeId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT id, command_id, scope_id, command_json FROM command_outbox WHERE scope_id=$scope AND status='pending' ORDER BY revision, ordinal;";
        command.Parameters.AddWithValue("$scope", scopeId);
        using var reader = command.ExecuteReader();
        var pending = new List<PendingCommand>();
        while (reader.Read())
            pending.Add(new PendingCommand(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return pending;
    }

    public void ApplyStartCommand(PendingCommand pending, StoredHostCommand command)
    {
        using var transaction = _connection.BeginTransaction();
        if (!IsPending(pending.Id, transaction))
        {
            transaction.Rollback();
            return;
        }

        var scope = ReadScope(pending.ScopeId, transaction);
        var handle = $"{pending.ScopeId}:work-{scope.NextHandle}";
        var dueAt = command.TimerDurationTicks is { } duration ? scope.ClockTicks + duration : (long?)null;

        using (var addWork = _connection.CreateCommand())
        {
            addWork.Transaction = transaction;
            addWork.CommandText = "INSERT INTO work_items(handle, scope_id, binding_ref, element_id, iteration_id, work_kind, status, due_at_ticks, command_id) VALUES ($handle, $scope, $binding, $element, $iteration, $kind, 'live', $due, $command);";
            addWork.Parameters.AddWithValue("$handle", handle);
            addWork.Parameters.AddWithValue("$scope", pending.ScopeId);
            addWork.Parameters.AddWithValue("$binding", command.BindingRef!);
            addWork.Parameters.AddWithValue("$element", command.ElementId!);
            addWork.Parameters.AddWithValue("$iteration", command.IterationId is null ? DBNull.Value : command.IterationId);
            addWork.Parameters.AddWithValue("$kind", command.WorkKind);
            addWork.Parameters.AddWithValue("$due", dueAt is null ? DBNull.Value : dueAt.Value);
            addWork.Parameters.AddWithValue("$command", pending.CommandId);
            addWork.ExecuteNonQuery();
        }

        using (var bump = _connection.CreateCommand())
        {
            bump.Transaction = transaction;
            bump.CommandText = "UPDATE scopes SET next_handle=next_handle+1 WHERE scope_id=$scope;";
            bump.Parameters.AddWithValue("$scope", pending.ScopeId);
            bump.ExecuteNonQuery();
        }

        InsertTrace(pending.ScopeId, new HostTraceEvent("start-work", command.BindingRef, command.ElementId, handle), transaction);
        MarkApplied(pending.Id, transaction);
        transaction.Commit();
    }

    public void ApplyCancelCommand(PendingCommand pending, StoredHostCommand command)
    {
        using var transaction = _connection.BeginTransaction();
        if (!IsPending(pending.Id, transaction))
        {
            transaction.Rollback();
            return;
        }

        using (var cancel = _connection.CreateCommand())
        {
            cancel.Transaction = transaction;
            cancel.CommandText = "UPDATE work_items SET status='cancelled' WHERE handle=$handle AND status='live';";
            cancel.Parameters.AddWithValue("$handle", command.Handle!);
            cancel.ExecuteNonQuery();
        }

        InsertTrace(pending.ScopeId, new HostTraceEvent("cancel-work-subtree", ElementId: command.ElementId, Handle: command.Handle, Reason: command.Reason), transaction);
        MarkApplied(pending.Id, transaction);
        transaction.Commit();
    }

    public void ApplySignalCommand(PendingCommand pending, StoredHostCommand command)
    {
        using var transaction = _connection.BeginTransaction();
        if (!IsPending(pending.Id, transaction))
        {
            transaction.Rollback();
            return;
        }

        InsertTrace(pending.ScopeId, new HostTraceEvent("signal-enclosing-scope", Message: command.Code), transaction);
        MarkApplied(pending.Id, transaction);
        transaction.Commit();
    }

    public void SetClock(string scopeId, long ticks)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "UPDATE scopes SET clock_ticks=$ticks WHERE scope_id=$scope;";
        command.Parameters.AddWithValue("$ticks", ticks);
        command.Parameters.AddWithValue("$scope", scopeId);
        command.ExecuteNonQuery();
        AppendTrace(scopeId, new HostTraceEvent("clock-advanced", Message: TimeSpan.FromTicks(ticks).ToString("c", System.Globalization.CultureInfo.InvariantCulture)));
    }

    public void AppendHostTrace(string scopeId, HostTraceEvent trace) => AppendTrace(scopeId, trace);

    public IReadOnlyList<HostTraceEvent> GetTrace(string scopeId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM trace_events WHERE scope_id=$scope ORDER BY id;";
        command.Parameters.AddWithValue("$scope", scopeId);
        using var reader = command.ExecuteReader();
        var events = new List<HostTraceEvent>();
        while (reader.Read())
            events.Add(JsonSerializer.Deserialize<HostTraceEvent>(reader.GetString(0), JsonOptions)!);
        return events;
    }

    public IReadOnlyList<IncidentRecord> GetIncidents(string scopeId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT scope_id, binding_ref, handle, message, revision FROM incidents WHERE scope_id=$scope ORDER BY id;";
        command.Parameters.AddWithValue("$scope", scopeId);
        using var reader = command.ExecuteReader();
        var incidents = new List<IncidentRecord>();
        while (reader.Read())
            incidents.Add(new IncidentRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4)));
        return incidents;
    }

    public int PendingOutboxCount(string scopeId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM command_outbox WHERE scope_id=$scope AND status='pending';";
        command.Parameters.AddWithValue("$scope", scopeId);
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private bool IsPending(long id, SqliteTransaction transaction)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT status FROM command_outbox WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id);
        return string.Equals(command.ExecuteScalar() as string, "pending", StringComparison.Ordinal);
    }

    private void MarkApplied(long id, SqliteTransaction transaction)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE command_outbox SET status='applied' WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private void AppendTrace(string scopeId, HostTraceEvent trace)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "INSERT INTO trace_events(scope_id, payload_json) VALUES ($scope, $json);";
        command.Parameters.AddWithValue("$scope", scopeId);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(trace, JsonOptions));
        command.ExecuteNonQuery();
    }

    private void InsertTrace(string scopeId, HostTraceEvent trace, SqliteTransaction transaction)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO trace_events(scope_id, payload_json) VALUES ($scope, $json);";
        command.Parameters.AddWithValue("$scope", scopeId);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(trace, JsonOptions));
        command.ExecuteNonQuery();
    }

    private ScopeRecord ReadScope(string scopeId, SqliteTransaction transaction)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT scope_id, process_id, definition_fingerprint, state_json, continuation_kind, continuation_json, correlation_json, has_parent, next_handle, clock_ticks, revision FROM scopes WHERE scope_id=$scope;";
        command.Parameters.AddWithValue("$scope", scopeId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException($"No proof scope '{scopeId}' is stored.");
        return new ScopeRecord(
            reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6), reader.GetInt32(7) != 0, reader.GetInt32(8), reader.GetInt64(9), reader.GetInt32(10));
    }

    private static WorkRecord ReadWork(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5), reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetInt64(7), reader.GetString(8));

    private static BridgeDeliveryRecord ReadBridgeDelivery(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5));

    private BridgeDeliveryRecord? ReadBridgeDelivery(string deliveryId, SqliteTransaction transaction)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT delivery_id,scope_id,handle,result_json,fingerprint,status FROM bridge_deliveries WHERE delivery_id=$id;";
        command.Parameters.AddWithValue("$id", deliveryId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadBridgeDelivery(reader) : null;
    }

    private static string DeliveryFingerprint(string scopeId, string handle, string resultJson)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{scopeId}\0{handle}\0{resultJson}"))).ToLowerInvariant();

    private static (string Kind, string? Json) SerializeContinuation(BpmnContinuation continuation) => continuation switch
    {
        BpmnContinuation.Complete complete => ("complete", JsonSerializer.Serialize(new { complete.Outcome }, JsonOptions)),
        BpmnContinuation.Defer => ("defer", null),
        BpmnContinuation.Fault fault => ("fault", JsonSerializer.Serialize(new { fault.Code, fault.Message }, JsonOptions)),
        _ => throw new NotSupportedException($"Unknown continuation '{continuation.GetType().FullName}'.")
    };
}
