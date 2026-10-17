using Microsoft.Data.Sqlite;

namespace UnifiedExecution.BpmnProof;

internal sealed record SequenceEnvelopeRecord(
    string SequenceId,
    string Status,
    string CurrentChildScopeId,
    string? ChildOutcome,
    int Revision);

/// <summary>
/// A persisted host-side sequence adapter: invoke one BPMN child, retain its outcome, then resume the
/// parent sequence by starting its next BPMN child. This demonstrates a composition boundary only; it
/// is not an Elsa workflow implementation.
/// </summary>
internal sealed class SequenceEnvelopeStore : IDisposable
{
    private readonly SqliteConnection _connection;

    public SequenceEnvelopeStore(string path)
    {
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 10
        }.ToString());
        _connection.Open();
        using var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS sequence_envelopes (
                sequence_id TEXT PRIMARY KEY,
                status TEXT NOT NULL,
                current_child_scope_id TEXT NOT NULL,
                child_outcome TEXT NULL,
                revision INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS sequence_envelope_trace (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                sequence_id TEXT NOT NULL REFERENCES sequence_envelopes(sequence_id),
                status TEXT NOT NULL,
                child_scope_id TEXT NOT NULL,
                child_outcome TEXT NULL,
                revision INTEGER NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();

    public void Create(string sequenceId, string firstChildScopeId)
    {
        using var transaction = _connection.BeginTransaction();
        using var insert = _connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO sequence_envelopes(sequence_id,status,current_child_scope_id) VALUES ($id,'waiting-child',$scope);";
        insert.Parameters.AddWithValue("$id", sequenceId);
        insert.Parameters.AddWithValue("$scope", firstChildScopeId);
        insert.ExecuteNonQuery();
        WriteTrace(sequenceId, "waiting-child", firstChildScopeId, null, 0, transaction);
        transaction.Commit();
    }

    public SequenceEnvelopeRecord Get(string sequenceId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT sequence_id,status,current_child_scope_id,child_outcome,revision FROM sequence_envelopes WHERE sequence_id=$id;";
        command.Parameters.AddWithValue("$id", sequenceId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException($"Sequence envelope '{sequenceId}' does not exist.");
        return new SequenceEnvelopeRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetInt32(4));
    }

    public IReadOnlyList<SequenceEnvelopeRecord> GetTrace(string sequenceId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT $id,status,child_scope_id,child_outcome,revision FROM sequence_envelope_trace WHERE sequence_id=$id ORDER BY id;";
        command.Parameters.AddWithValue("$id", sequenceId);
        using var reader = command.ExecuteReader();
        var events = new List<SequenceEnvelopeRecord>();
        while (reader.Read())
            events.Add(new SequenceEnvelopeRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetInt32(4)));
        return events;
    }

    public void RecordChildOutcome(string sequenceId, string outcome)
    {
        var current = Get(sequenceId);
        if (current.Status == "child-outcome-recorded" && current.ChildOutcome == outcome)
            return;
        if (current.Status != "waiting-child")
            throw new InvalidOperationException($"Cannot record child outcome while sequence is '{current.Status}'.");
        Update(sequenceId, "child-outcome-recorded", current.CurrentChildScopeId, outcome);
    }

    public SequenceEnvelopeRecord PrepareTail(string sequenceId)
    {
        var current = Get(sequenceId);
        if (current.Status == "next-child-pending" || current.Status == "waiting-tail")
            return current;
        if (current.Status != "child-outcome-recorded" || current.ChildOutcome != "complete")
            throw new InvalidOperationException("The sequence cannot continue until its BPMN child completes normally.");
        Update(sequenceId, "next-child-pending", $"{sequenceId}:tail", current.ChildOutcome);
        return Get(sequenceId);
    }

    public void MarkWaitingTail(string sequenceId)
    {
        var current = Get(sequenceId);
        if (current.Status == "waiting-tail")
            return;
        if (current.Status != "next-child-pending")
            throw new InvalidOperationException($"Cannot mark the next child live while sequence is '{current.Status}'.");
        Update(sequenceId, "waiting-tail", current.CurrentChildScopeId, current.ChildOutcome);
    }

    public void MarkComplete(string sequenceId, string outcome)
    {
        var current = Get(sequenceId);
        if (current.Status == "complete")
            return;
        if (current.Status != "waiting-tail")
            throw new InvalidOperationException($"Cannot complete sequence while it is '{current.Status}'.");
        Update(sequenceId, "complete", current.CurrentChildScopeId, outcome);
    }

    private void Update(string sequenceId, string status, string childScopeId, string? outcome)
    {
        var current = Get(sequenceId);
        using var transaction = _connection.BeginTransaction();
        var revision = current.Revision + 1;
        using var update = _connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE sequence_envelopes SET status=$status,current_child_scope_id=$scope,child_outcome=$outcome,revision=$revision WHERE sequence_id=$id AND revision=$expected;";
        update.Parameters.AddWithValue("$status", status);
        update.Parameters.AddWithValue("$scope", childScopeId);
        update.Parameters.AddWithValue("$outcome", outcome is null ? DBNull.Value : outcome);
        update.Parameters.AddWithValue("$revision", revision);
        update.Parameters.AddWithValue("$id", sequenceId);
        update.Parameters.AddWithValue("$expected", current.Revision);
        if (update.ExecuteNonQuery() != 1)
            throw new InvalidOperationException($"Sequence envelope '{sequenceId}' changed during the state transition.");
        WriteTrace(sequenceId, status, childScopeId, outcome, revision, transaction);
        transaction.Commit();
    }

    private void WriteTrace(string sequenceId, string status, string childScopeId, string? outcome, int revision, SqliteTransaction transaction)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO sequence_envelope_trace(sequence_id,status,child_scope_id,child_outcome,revision) VALUES ($id,$status,$scope,$outcome,$revision);";
        command.Parameters.AddWithValue("$id", sequenceId);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$scope", childScopeId);
        command.Parameters.AddWithValue("$outcome", outcome is null ? DBNull.Value : outcome);
        command.Parameters.AddWithValue("$revision", revision);
        command.ExecuteNonQuery();
    }
}
