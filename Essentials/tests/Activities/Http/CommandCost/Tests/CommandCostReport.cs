using System.Globalization;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Http.IntegrationTests;

namespace Elsa.Activities.Http.CommandCost;

/// <summary>
/// Writes measured runs to disk and turns every run found there into one report: totals per run, the fitted cost per
/// WriteLine and per run of the WriteLine series, and every cost source ranked with the scaling class it shows.
/// </summary>
internal static class CommandCostReport
{
    /// <summary>The WriteLine counts the series fits over.</summary>
    public static readonly int[] WriteLineSeries = [5, 10, 20, 40];

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    /// <summary>
    /// The phase of a run a command belongs to. <c>commit</c>: inside a durable checkpoint commit's transaction (one that
    /// wrote a commit marker). <c>checkpoint</c>: on the committer's path outside that transaction, such as the reads
    /// before it. <c>handler:Kind</c>: dispatching a work item of that command kind. <c>drain</c>: the drain loop
    /// between dispatches. <c>outside</c>: before or after the drain, such as start dispatch and ownership.
    /// </summary>
    public static string Phase(CommandCostEntry command, IReadOnlySet<int> commitTransactions) =>
        command.Transaction is { } transaction && commitTransactions.Contains(transaction) ? "commit"
        : command.Checkpoint != "-" ? "checkpoint"
        : command.Dispatch != "-" ? $"handler:{command.Dispatch}"
        : command.InDrain ? "drain"
        : "outside";

    public static IReadOnlySet<int> CommitTransactions(CommandCostRun run) =>
        run.Commands
            .Where(command => command.Transaction is not null && command.Statements.Any(statement => statement is { Kind: "INSERT", Table: "checkpoint_commit" }))
            .Select(command => command.Transaction!.Value)
            .ToHashSet();

    public static async Task WriteRunAsync(string directory, CommandCostRun run)
    {
        var name = $"{run.Provider}-{run.Workflow}-{run.Mode}-{run.Activities}";
        Directory.CreateDirectory(Path.Join(directory, "runs"));
        Directory.CreateDirectory(Path.Join(directory, "commands"));
        await File.WriteAllTextAsync(Path.Join(directory, "runs", $"{name}.json"), JsonSerializer.Serialize(run with { Commands = run.Commands.Select(command => command with { Sql = "" }).ToArray() }, Json));

        var commits = CommitTransactions(run);
        var lines = new List<string> { "ordinal\tstatements\tkinds\ttables\ttransaction\tphase\tcheckpoint\tstore\tcaller\tlocation\tframes\tsql" };
        lines.AddRange(run.Commands.Select(command => string.Join('\t',
            command.Ordinal.ToString(CultureInfo.InvariantCulture),
            command.Statements.Count.ToString(CultureInfo.InvariantCulture),
            string.Join(',', command.Statements.Select(statement => statement.Kind)),
            string.Join(',', command.Statements.Select(statement => statement.Table)),
            command.Transaction?.ToString(CultureInfo.InvariantCulture) ?? "-",
            Phase(command, commits),
            command.Checkpoint,
            command.Store,
            command.Caller,
            command.CallerLocation,
            command.Frames,
            Flatten(command.Sql, 400))));
        await File.WriteAllLinesAsync(Path.Join(directory, "commands", $"{name}.tsv"), lines);
    }

    /// <summary>
    /// Deletes what an earlier measurement of <paramref name="provider"/> left in <paramref name="directory"/>, so the
    /// summary is built from this measurement's runs and the other provider's latest, never from a stale run.
    /// </summary>
    public static void ClearRuns(string directory, string provider)
    {
        foreach (var (folder, extension) in new[] { ("runs", "json"), ("commands", "tsv") })
        {
            var path = Path.Join(directory, folder);
            if (Directory.Exists(path))
                foreach (var file in Directory.GetFiles(path, $"{provider}-*.{extension}"))
                    File.Delete(file);
        }
    }

    public static IReadOnlyList<CommandCostRun> ReadRuns(string directory) =>
        Directory.Exists(Path.Join(directory, "runs"))
            ? Directory.GetFiles(Path.Join(directory, "runs"), "*.json")
                .Order(StringComparer.Ordinal)
                .Select(ReadRun)
                .ToArray()
            : [];

    /// <summary>A run file an older version of the tool wrote lacks fields this version reads; say so rather than render nonsense.</summary>
    private static CommandCostRun ReadRun(string file) =>
        JsonSerializer.Deserialize<CommandCostRun>(File.ReadAllText(file), Json) is { Counts: not null, Commands: not null } run
            ? run
            : throw new InvalidDataException(
                $"{file} was written by another version of the command-cost tool. Measure both providers again into an empty directory.");

    public static string Render(IReadOnlyList<CommandCostRun> runs)
    {
        var report = new StringBuilder();
        report.AppendLine("# EF command cost measurement").AppendLine();
        report.AppendLine("Generated by `Elsa.Activities.Http.CommandCost.Tests`. Counts are deterministic; nothing is timed.").AppendLine();
        RenderTotals(report, runs);
        RenderScaling(report, runs);
        RenderSources(report, runs);
        return report.ToString();
    }

    private static void RenderTotals(StringBuilder report, IReadOnlyList<CommandCostRun> runs)
    {
        report.AppendLine("## Totals per run").AppendLine();
        report.AppendLine("| Provider | Workflow | Mode | Activities | Commands | Statements | Commits | Transactions | Savepoints | Round trips | Server responses | Wire statements | Simple queries | Connections opened |");
        report.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var run in runs.OrderBy(run => run.Provider).ThenBy(run => run.Workflow).ThenBy(run => run.Mode).ThenBy(run => run.Activities))
        {
            report.AppendLine(
                $"| {run.Provider} | {run.Workflow} | {run.Mode} | {run.Activities} | {run.Commands.Count} | {run.Statements} | {run.CommitMarkers} | " +
                $"{run.Counts.TransactionsCommitted} | {run.Counts.SavepointsCreated} | {Wire(run, wire => wire.RoundTrips)} | {Wire(run, wire => wire.Responses)} | " +
                $"{Wire(run, wire => wire.Executes)} | {Wire(run, wire => wire.SimpleQueryCount)} | {Wire(run, wire => wire.Connections)} |");
        }

        report.AppendLine();
        report.AppendLine("Commits counts durable checkpoint commits (one commit-marker row each). Round trips counts each time the client " +
                          "sent and waited for the server, after connection handshakes; server responses counts ReadyForQuery messages, which " +
                          "exceed round trips when the client writes several requests at once. Wire statements counts extended-protocol " +
                          "Execute messages; simple queries counts simple-protocol Query messages.").AppendLine();

        var simpleQueries = runs.Where(run => run.Wire is { } wire && wire.SimpleQueryCount > 0).ToArray();
        if (simpleQueries.Length == 0)
            return;
        report.AppendLine("### Simple queries by text").AppendLine();
        report.AppendLine("| Provider | Workflow | Mode | Activities | Query text | Count |");
        report.AppendLine("|---|---|---|---:|---|---:|");
        foreach (var run in simpleQueries.OrderBy(run => run.Workflow).ThenBy(run => run.Mode).ThenBy(run => run.Activities))
        foreach (var (text, count) in run.Wire!.SimpleQueries.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key, StringComparer.Ordinal))
            report.AppendLine($"| {run.Provider} | {run.Workflow} | {run.Mode} | {run.Activities} | `{text}` | {count} |");
        report.AppendLine();
    }

    private static void RenderScaling(StringBuilder report, IReadOnlyList<CommandCostRun> runs)
    {
        report.AppendLine("## WriteLine series fit").AppendLine();
        report.AppendLine($"Each count is fitted as `slope x WriteLines + intercept` over {string.Join(", ", WriteLineSeries)} WriteLines; " +
                          "the residual is the largest distance of a measured count from the line.").AppendLine();
        report.AppendLine("| Provider | Mode | Measure | Slope per WriteLine | Intercept | Max residual |");
        report.AppendLine("|---|---|---|---:|---:|---:|");
        foreach (var series in Series(runs))
        {
            var measures = new List<(string Name, Func<CommandCostRun, double> Count)>
            {
                ("commands", run => run.Commands.Count),
                ("statements", run => run.Statements),
                ("commits", run => run.CommitMarkers),
                ("transactions", run => run.Counts.TransactionsCommitted)
            };
            if (series.Runs.All(run => run.Wire is not null))
                measures.Add(("round trips", run => run.Wire!.RoundTrips));
            foreach (var phase in series.Runs.SelectMany(run => run.Commands.Select(command => Phase(command, CommitTransactions(run)))).Distinct().Order(StringComparer.Ordinal))
                measures.Add(($"commands in phase {phase}", run => run.Commands.Count(command => Phase(command, CommitTransactions(run)) == phase)));

            foreach (var (name, count) in measures)
            {
                var fit = Fit(series.Runs, count);
                report.AppendLine($"| {series.Provider} | {series.Mode} | {name} | {Number(fit.Slope)} | {Number(fit.Intercept)} | {Number(fit.Residual)} |");
            }
        }

        report.AppendLine();
    }

    private static void RenderSources(StringBuilder report, IReadOnlyList<CommandCostRun> runs)
    {
        foreach (var provider in Series(runs).Select(series => series.Provider).Distinct())
        {
            var byMode = Series(runs).Where(series => series.Provider == provider).ToDictionary(series => series.Mode);
            if (!byMode.ContainsKey(CommandCostMode.Durable.Name) || !byMode.ContainsKey(CommandCostMode.Balanced.Name))
                continue;

            report.AppendLine($"## Cost sources, {provider}").AppendLine();
            report.AppendLine("Statements grouped by verb, table, phase and calling Elsa frame, fitted over the WriteLine series in both modes. " +
                              "Per activity: grows with WriteLines in Balanced, where the commit count is fixed. Per commit: grows only in Durable, " +
                              "where every activity commits. Per run: grows in neither. At 10 WriteLines is the measured count.").AppendLine();
            report.AppendLine("| Statement | Phase | Caller | Location | Class | Balanced slope | Balanced at 10 | Durable slope | Durable at 10 |");
            report.AppendLine("|---|---|---|---|---|---:|---:|---:|---:|");

            var keys = byMode.Values.SelectMany(series => series.Runs).SelectMany(Sources).Select(source => source.Key).Distinct();
            var rows = keys.Select(key =>
                {
                    var balanced = Fit(byMode[CommandCostMode.Balanced.Name].Runs, run => Sources(run).Count(source => source.Key == key));
                    var durable = Fit(byMode[CommandCostMode.Durable.Name].Runs, run => Sources(run).Count(source => source.Key == key));
                    var location = byMode.Values.SelectMany(series => series.Runs).SelectMany(Sources).First(source => source.Key == key).Location;
                    var className = balanced.Slope > 0.01 ? "per activity" : durable.Slope > 0.01 ? "per commit" : "per run";
                    return (Key: key, Location: location, Class: className, Balanced: balanced, Durable: durable,
                        BalancedAt10: AtTen(byMode[CommandCostMode.Balanced.Name].Runs, key), DurableAt10: AtTen(byMode[CommandCostMode.Durable.Name].Runs, key));
                })
                .OrderByDescending(row => row.BalancedAt10)
                .ThenByDescending(row => row.DurableAt10)
                .ThenBy(row => row.Key.ToString(), StringComparer.Ordinal);

            foreach (var row in rows)
            {
                report.AppendLine(
                    $"| {row.Key.Kind} {row.Key.Table} | {row.Key.Phase} | `{row.Key.Caller}` | {row.Location} | {row.Class} | " +
                    $"{Number(row.Balanced.Slope)} | {row.BalancedAt10} | {Number(row.Durable.Slope)} | {row.DurableAt10} |");
            }

            report.AppendLine();
        }
    }

    private static int AtTen(IReadOnlyList<CommandCostRun> runs, SourceKey key) =>
        runs.FirstOrDefault(run => run.Activities == 11) is { } run ? Sources(run).Count(source => source.Key == key) : 0;

    private readonly record struct SourceKey(string Kind, string Table, string Phase, string Caller)
    {
        public override string ToString() => $"{Kind}|{Table}|{Phase}|{Caller}";
    }

    private static IEnumerable<(SourceKey Key, string Location)> Sources(CommandCostRun run)
    {
        var commits = CommitTransactions(run);
        foreach (var command in run.Commands)
        {
            var phase = Phase(command, commits);
            foreach (var statement in command.Statements)
                yield return (new SourceKey(statement.Kind, statement.Table, phase, command.Caller), command.CallerLocation);
        }
    }

    private sealed record SeriesRuns(string Provider, string Mode, IReadOnlyList<CommandCostRun> Runs);

    private static IEnumerable<SeriesRuns> Series(IReadOnlyList<CommandCostRun> runs) =>
        runs.Where(run => run.Workflow == ReferenceWorkflowRuns.WriteLineSequence)
            .GroupBy(run => (run.Provider, run.Mode))
            .Where(group => group.Select(run => run.Activities - 1).Distinct().Count() >= 2)
            .OrderBy(group => group.Key.Provider).ThenBy(group => group.Key.Mode)
            .Select(group => new SeriesRuns(group.Key.Provider, group.Key.Mode, group.OrderBy(run => run.Activities).ToArray()));

    /// <summary>Least-squares line through the counts against the WriteLine count (activities minus the Sequence).</summary>
    private static (double Slope, double Intercept, double Residual) Fit(IReadOnlyList<CommandCostRun> runs, Func<CommandCostRun, double> count)
    {
        var points = runs.Select(run => (X: (double)(run.Activities - 1), Y: count(run))).ToArray();
        var meanX = points.Average(point => point.X);
        var meanY = points.Average(point => point.Y);
        var spread = points.Sum(point => (point.X - meanX) * (point.X - meanX));
        var slope = spread == 0 ? 0 : points.Sum(point => (point.X - meanX) * (point.Y - meanY)) / spread;
        var intercept = meanY - slope * meanX;
        var residual = points.Max(point => Math.Abs(point.Y - (slope * point.X + intercept)));
        return (slope, intercept, residual);
    }

    private static string Wire(CommandCostRun run, Func<PostgreSqlWireCounts, int> select) =>
        run.Wire is { } wire ? select(wire).ToString(CultureInfo.InvariantCulture) : "-";

    private static string Number(double value) =>
        Math.Abs(value - Math.Round(value)) < 0.005 ? Math.Round(value).ToString(CultureInfo.InvariantCulture) : value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Flatten(string text, int limit)
    {
        var flat = text.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
        return flat.Length > limit ? flat[..limit] : flat;
    }
}
