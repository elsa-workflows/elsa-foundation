using Elsa.Cli.Worker;
using System.Text.Json;

namespace Elsa.Cli;

/// <summary>
/// Turns one worker response into what the operator sees. Every refusal — the front end's own, the
/// worker's, and the host tooling's — renders through here, so an operator reads one shape of message
/// wherever the refusal was decided.
/// </summary>
internal static class Report
{
    /// <summary>
    /// Discovery is a whole-closure operation: one unreadable <c>[EfModule]</c> declaration withholds every
    /// module, by design (a module silently dropped from a migration artifact is worse than a refusal). The
    /// operator reading that refusal sees no modules at all, though, and the obvious conclusion — "my host
    /// is broken" — is the wrong one. These codes say which declaration is at fault and that the rest of
    /// the host is not.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Hints = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["module-discovery-failed"] =
            "Module discovery covers the whole host closure at once and fails closed, so one bad declaration hides every module. " +
            "The assemblies named above are the ones to fix; the rest of this host is not implicated.",
        ["dependency-cycle"] =
            "Discovery fails closed for the whole selection rather than applying migrations in an order it had to guess. " +
            "Only the modules on the cycle above are at fault.",
        ["dependency-missing"] =
            "Discovery fails closed for the whole selection rather than skipping a module another one depends on. " +
            "Select the missing module too, or fix the declaration that names it."
    };

    public static int Render(string command, WorkerResponse response, TextWriter output, TextWriter error)
    {
        if (response.Error is { } refusal)
        {
            WriteRefusal(error, refusal.Code, refusal.Message, refusal.Details);
            return response.ExitCode;
        }

        if (response.Tooling is not { } tooling)
        {
            WriteRefusal(error, "worker-no-response", "The worker answered with neither a result nor a refusal.", []);
            return ToolExitCode.ResolutionFailure;
        }

        if (tooling.TryGetProperty("error", out var toolingError))
        {
            WriteRefusal(
                error,
                Text(toolingError, "code"),
                Text(toolingError, "message"),
                toolingError.TryGetProperty("details", out var details)
                    ? [.. details.EnumerateArray().Select(detail => detail.GetString() ?? "")]
                    : []);
            return response.ExitCode;
        }

        switch (command)
        {
            case WorkerCommands.List:
                WriteList(output, tooling.GetProperty("list"));
                break;
            case WorkerCommands.Plan:
                WritePlan(output, tooling.GetProperty("plan"));
                break;
            case WorkerCommands.Script:
                WriteScript(output, tooling.GetProperty("script"));
                break;
        }

        return response.ExitCode;
    }

    public static void WriteRefusal(TextWriter error, string code, string message, IReadOnlyList<string> details)
    {
        error.WriteLine($"error [{code}]: {message}");
        foreach (var detail in details)
            error.WriteLine($"  {detail}");
        if (Hints.TryGetValue(code, out var hint))
            error.WriteLine($"  note: {hint}");
    }

    private static void WriteList(TextWriter output, JsonElement list)
    {
        var rows = list.GetProperty("modules").EnumerateArray()
            .Select(module => new[]
            {
                Text(module, "module"),
                Text(module, "assembly"),
                Text(module, "context"),
                Text(module, "historyTable"),
                string.Join(", ", module.GetProperty("providers").EnumerateArray().Select(provider => provider.GetString()))
            })
            .ToArray();

        WriteTable(output, ["MODULE", "ASSEMBLY", "CONTEXT", "HISTORY TABLE", "PROVIDERS"], rows);
        output.WriteLine();
        output.WriteLine($"{rows.Length} module(s).");
    }

    private static void WritePlan(TextWriter output, JsonElement plan)
    {
        var rows = plan.GetProperty("modules").EnumerateArray()
            .Select(module => new[]
            {
                module.GetProperty("order").GetInt32().ToString("D2", System.Globalization.CultureInfo.InvariantCulture),
                Text(module, "module"),
                Text(module, "context"),
                Text(module, "historyTable"),
                $"{module.GetProperty("count").GetInt32()}",
                $"{Text(module, "from")} -> {Text(module, "to")}"
            })
            .ToArray();

        output.WriteLine($"provider: {Text(plan, "provider")}   schema: {Text(plan, "schema", "(none)")}");
        WriteTable(output, ["#", "MODULE", "CONTEXT", "HISTORY TABLE", "MIGRATIONS", "RANGE"], rows);
    }

    private static void WriteScript(TextWriter output, JsonElement script)
    {
        foreach (var file in script.GetProperty("files").EnumerateArray())
            output.WriteLine($"{Text(file, "file")}  {Text(file, "sha256")}  {Text(file, "module")}");
        output.WriteLine($"{Text(script, "manifest")}  {Text(script, "manifestSha256")}");
    }

    private static void WriteTable(TextWriter output, string[] headers, IReadOnlyList<string[]> rows)
    {
        var widths = headers
            .Select((header, column) => rows.Select(row => row[column].Length).Append(header.Length).Max())
            .ToArray();

        output.WriteLine(Line(headers, widths));
        foreach (var row in rows)
            output.WriteLine(Line(row, widths));
    }

    private static string Line(string[] cells, int[] widths) =>
        string.Join("  ", cells.Select((cell, column) => column == cells.Length - 1 ? cell : cell.PadRight(widths[column]))).TrimEnd();

    private static string Text(JsonElement element, string property, string fallback = "") =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
}
