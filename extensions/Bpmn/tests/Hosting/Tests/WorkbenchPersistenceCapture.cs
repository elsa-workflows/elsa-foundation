using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

/// <summary>
/// Parses Workbench EF Core console logs into the same metric families as
/// <c>CommandCaptureInterceptor</c> (every command is a round-trip; INSERT/UPDATE/DELETE are writes)
/// plus transaction, lease/fence, and Cancel-command counts. Test-only; not a budget gate.
/// </summary>
internal sealed class WorkbenchPersistenceCapture
{
    private static readonly Regex Executed = new(@"Executed DbCommand \((?<ms>[0-9.]+)\s*(?<unit>ms|s)\)", RegexOptions.Compiled);
    private readonly object gate = new();
    private readonly List<CapturedCommand> commands = [];
    private readonly List<string> transactions = [];
    private readonly StringBuilder sql = new();
    private bool inCommand;
    private int commandMark;
    private int transactionMark;

    internal void Append(string line)
    {
        lock (gate)
        {
            if (line.Contains("Executed DbCommand", StringComparison.Ordinal))
            {
                FlushCommand();
                inCommand = true;
                sql.Clear();
                sql.AppendLine(line);
                return;
            }

            if (line.Contains("Began transaction", StringComparison.Ordinal) ||
                line.Contains("Beginning transaction", StringComparison.Ordinal) ||
                line.Contains("Committed transaction", StringComparison.Ordinal) ||
                line.Contains("Rolled back transaction", StringComparison.Ordinal) ||
                line.Contains("Rolling back transaction", StringComparison.Ordinal))
            {
                FlushCommand();
                transactions.Add(line);
                return;
            }

            if (inCommand && (line.StartsWith(' ') || line.StartsWith('\t')))
            {
                sql.AppendLine(line);
                return;
            }

            FlushCommand();
        }
    }

    internal void Mark()
    {
        lock (gate)
        {
            FlushCommand();
            commandMark = commands.Count;
            transactionMark = transactions.Count;
        }
    }

    internal PersistenceCounters Delta()
    {
        lock (gate)
        {
            FlushCommand();
            return Sum(commands.Skip(commandMark), transactions.Skip(transactionMark),
                commands.Skip(commandMark).Sum(item => item.DurationMs));
        }
    }

    internal PersistenceCounters Total()
    {
        lock (gate)
        {
            FlushCommand();
            return Sum(commands, transactions, commands.Sum(item => item.DurationMs));
        }
    }

    private void FlushCommand()
    {
        if (!inCommand) return;
        inCommand = false;
        var text = sql.ToString();
        var durationMs = 0d;
        var match = Executed.Match(text);
        if (match.Success)
        {
            durationMs = double.Parse(match.Groups["ms"].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (match.Groups["unit"].Value == "s") durationMs *= 1000;
        }

        commands.Add(new CapturedCommand(text, durationMs));
        sql.Clear();
    }

    private static PersistenceCounters Sum(IEnumerable<CapturedCommand> captured, IEnumerable<string> txLines, double durationMs)
    {
        var list = captured.ToArray();
        var txs = txLines.ToArray();
        var writes = 0;
        var runtimeRoundTrips = 0;
        var runtimeWrites = 0;
        var leaseOps = 0;
        var fenceOps = 0;
        var schedulerCancel = 0;
        var cancelScope = 0;
        var cancelEmbedded = 0;
        foreach (var command in list)
        {
            var sql = command.Text;
            var runtime = sql.Contains("elsa_runtime_", StringComparison.OrdinalIgnoreCase);
            if (runtime) runtimeRoundTrips++;
            if (IsWrite(sql))
            {
                writes++;
                if (runtime) runtimeWrites++;
            }

            if (sql.Contains("elsa_runtime_workflow_executable_root_write_lease", StringComparison.OrdinalIgnoreCase))
                leaseOps++;
            if (IsFenceOrClaim(sql)) fenceOps++;
            if (IsSchedulerInsert(sql) && IsCommandKind(sql, 4, "Cancel")) schedulerCancel++;
            if (IsSchedulerInsert(sql) && IsCommandKind(sql, 15, "CancelActivityScope")) cancelScope++;
            if (sql.Contains("Elsa.Bpmn.CancelEmbedded.v1", StringComparison.Ordinal)) cancelEmbedded++;
        }

        return new PersistenceCounters(
            RoundTrips: list.Length,
            Writes: writes,
            RuntimeWrites: runtimeWrites,
            RuntimeRoundTrips: runtimeRoundTrips,
            TransactionsStarted: txs.Count(line =>
                line.Contains("Began transaction", StringComparison.Ordinal) ||
                line.Contains("Beginning transaction", StringComparison.Ordinal)),
            TransactionsCommitted: txs.Count(line => line.Contains("Committed transaction", StringComparison.Ordinal)),
            TransactionsRolledBack: txs.Count(line =>
                line.Contains("Rolled back transaction", StringComparison.Ordinal) ||
                line.Contains("Rolling back transaction", StringComparison.Ordinal)),
            LeaseOps: leaseOps,
            FenceOps: fenceOps,
            SchedulerCancelCommands: schedulerCancel,
            CancelActivityScopeCommands: cancelScope,
            CancelEmbeddedMentions: cancelEmbedded,
            EfCommandDurationMs: durationMs);
    }

    private static bool IsWrite(string sql)
    {
        var body = StripLogPrefix(sql);
        return ContainsVerb(body, "INSERT") || ContainsVerb(body, "UPDATE") || ContainsVerb(body, "DELETE");
    }

    private static bool IsSchedulerInsert(string sql) =>
        sql.Contains("elsa_runtime_scheduler_work_item", StringComparison.OrdinalIgnoreCase) &&
        ContainsVerb(StripLogPrefix(sql), "INSERT");

    private static bool IsFenceOrClaim(string sql) =>
        sql.Contains("ClaimOwnerId", StringComparison.OrdinalIgnoreCase) ||
        sql.Contains("ClaimToken", StringComparison.OrdinalIgnoreCase) ||
        sql.Contains("FencingToken", StringComparison.OrdinalIgnoreCase) ||
        sql.Contains("ExpectedFence", StringComparison.OrdinalIgnoreCase) ||
        sql.Contains("VisibleAfterUtcTicks", StringComparison.OrdinalIgnoreCase);

    private static bool IsCommandKind(string sql, int numeric, string name) =>
        sql.Contains($"\"commandKind\":{numeric}", StringComparison.OrdinalIgnoreCase) ||
        sql.Contains($"\"commandKind\": {numeric}", StringComparison.OrdinalIgnoreCase) ||
        sql.Contains($"\"CommandKind\":{numeric}", StringComparison.Ordinal) ||
        sql.Contains($"\"commandKind\":\"{name}\"", StringComparison.OrdinalIgnoreCase);

    private static string StripLogPrefix(string sql)
    {
        var start = sql.IndexOf("Executed DbCommand", StringComparison.Ordinal);
        return start < 0 ? sql : sql[start..];
    }

    private static bool ContainsVerb(string sql, string verb)
    {
        var index = sql.IndexOf(verb, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return false;
        var before = index == 0 || char.IsWhiteSpace(sql[index - 1]) || sql[index - 1] is '"' or '\'';
        var after = index + verb.Length == sql.Length || char.IsWhiteSpace(sql[index + verb.Length]) ||
                    sql[index + verb.Length] is '"' or '(' or '\r' or '\n';
        return before && after;
    }

    private readonly record struct CapturedCommand(string Text, double DurationMs);
}

internal sealed record PersistenceCounters(
    int RoundTrips,
    int RuntimeRoundTrips,
    int Writes,
    int RuntimeWrites,
    int TransactionsStarted,
    int TransactionsCommitted,
    int TransactionsRolledBack,
    int LeaseOps,
    int FenceOps,
    int SchedulerCancelCommands,
    int CancelActivityScopeCommands,
    int CancelEmbeddedMentions,
    double EfCommandDurationMs);

internal sealed record CancelCommandLedger(
    int SchedulerCancelCommands,
    int CancelActivityScopeCommands,
    int CancelEmbeddedIntents,
    int NativeRunBpmnCancelled,
    int HttpCancelApplied,
    int HttpCancelReplayed,
    int HttpCancelConflicted);

internal static class RuntimeSqliteCancelLedger
{
    internal static async Task<CancelCommandLedger> ReadAsync(string database, int httpApplied, int httpReplayed, int httpConflicted,
        int nativeRunBpmnCancelled)
    {
        await using var connection = LegacyUpgradeInspection.Read(database);
        await connection.OpenAsync();
        var schedulerCancel = 0;
        var cancelScope = 0;
        var cancelEmbedded = 0;
        foreach (var sql in new[]
                 {
                     "SELECT ContentJson FROM elsa_runtime_scheduler_work_item",
                     "SELECT ContentJson FROM elsa_runtime_checkpoint_commit",
                     "SELECT ContentJson FROM elsa_runtime_post_commit_outbox"
                 })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            try
            {
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var json = reader.GetString(0);
                    schedulerCancel += CountCommandKind(json, 4, "Cancel");
                    cancelScope += CountCommandKind(json, 15, "CancelActivityScope");
                    if (json.Contains("Elsa.Bpmn.CancelEmbedded.v1", StringComparison.Ordinal)) cancelEmbedded++;
                }
            }
            catch (SqliteException) when (sql.Contains("elsa_runtime_post_commit_outbox", StringComparison.Ordinal))
            {
                // Outbox table name is stable; a missing table means this host never wrote one.
            }
        }

        return new CancelCommandLedger(schedulerCancel, cancelScope, cancelEmbedded, nativeRunBpmnCancelled,
            httpApplied, httpReplayed, httpConflicted);
    }

    private static int CountCommandKind(string json, int numeric, string name)
    {
        var count = 0;
        var needle = $"\"commandKind\":{numeric}";
        var alt = $"\"commandKind\": {numeric}";
        var named = $"\"commandKind\":\"{name}\"";
        count += CountOccurrences(json, needle, StringComparison.OrdinalIgnoreCase);
        if (!json.Contains(needle, StringComparison.OrdinalIgnoreCase))
            count += CountOccurrences(json, alt, StringComparison.OrdinalIgnoreCase);
        count += CountOccurrences(json, named, StringComparison.OrdinalIgnoreCase);
        return count;
    }

    private static int CountOccurrences(string text, string needle, StringComparison comparison)
    {
        var count = 0;
        var start = 0;
        while (true)
        {
            var found = text.IndexOf(needle, start, comparison);
            if (found < 0) return count;
            count++;
            start = found + needle.Length;
        }
    }
}

internal sealed record MeasuredStep(string Step, long LatencyMs, PersistenceCounters Counters);
