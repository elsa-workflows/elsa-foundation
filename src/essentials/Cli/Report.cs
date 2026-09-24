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
            "Select the missing module too, or fix the declaration that names it.",
        ["post-migration-required"] =
            "A post-migration action rewrites data, so nothing runs one as a side effect of another command. " +
            "The migrations themselves are fine; run the command named above and then re-run this one."
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
                if (tooling.TryGetProperty("configurationContext", out var context))
                    WriteConfigurationContext(output, context);
                break;
            case WorkerCommands.Plan:
                WritePlan(output, tooling.GetProperty("plan"));
                break;
            case WorkerCommands.Script:
                WriteScript(output, tooling.GetProperty("script"));
                break;
            case WorkerCommands.Apply:
                WriteApply(output, tooling.GetProperty("apply"));
                break;
            case WorkerCommands.Validate:
                WriteValidate(output, tooling.GetProperty("validate"));
                break;
            case WorkerCommands.PostMigrate:
                WritePostMigrate(output, tooling.GetProperty("postMigrate"));
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

    private static void WriteConfigurationContext(TextWriter output, JsonElement context)
    {
        output.WriteLine();
        output.WriteLine($"Configuration context: {Safe(Text(context, "source"))}; " +
                         $"shell {Safe(Text(context, "shell"))}; " +
                         $"resource {Safe(Text(context, "resource"))}; " +
                         $"resolution {Safe(Text(context, "resolution"))}.");
        output.WriteLine($"Target verification: {Safe(Text(context, "targetVerification"))}; " +
                         $"runtime parity: {Safe(Text(context, "runtimeParity"))}.");
        foreach (var participant in context.GetProperty("participants").EnumerateArray())
            output.WriteLine($"  {Safe(Text(participant, "feature"))} → {Safe(Text(participant, "module"))}: " +
                             $"{Safe(Text(participant, "resource"))}, {Safe(Text(participant, "provider"))}, " +
                             $"connection reference {Safe(Text(participant, "connectionReference"))} " +
                             $"({Safe(Text(participant, "selection"))}).");
        foreach (var unresolved in context.GetProperty("unresolved").EnumerateArray())
            output.WriteLine($"  unresolved: {Safe(unresolved.GetString() ?? "")}");

        static string Safe(string value) => string.Concat(value.Select(character =>
            char.IsControl(character) ? $"\\u{(int)character:X4}" : character.ToString()));
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

    /// <summary>
    /// What the manifest's <c>host.providerAgreement</c> field cannot say for itself (FR-039): the check
    /// reads <c>shells.json</c> plus its environment overlay, not a running host's own environment-variable
    /// configuration overrides, so a live host's effective provider can differ from the one verified here.
    /// Stated here, where an operator reading the run actually sees it, rather than as a manifest field —
    /// the manifest's key set is frozen (FR-047).
    /// </summary>
    private const string ProviderAgreementNote =
        "note: providerAgreement reflects shells.json plus its shells.<environment>.json overlay only. A " +
        "running host's own environment-variable configuration overrides are invisible to this check, so " +
        "its effective provider can differ from the one verified here.";

    private static void WriteScript(TextWriter output, JsonElement script)
    {
        foreach (var file in script.GetProperty("files").EnumerateArray())
            output.WriteLine($"{Text(file, "file")}  {Text(file, "sha256")}  {Text(file, "module")}");
        output.WriteLine($"{Text(script, "manifest")}  {Text(script, "manifestSha256")}");
        output.WriteLine(ProviderAgreementNote);
    }

    private static void WriteApply(TextWriter output, JsonElement apply)
    {
        var rows = apply.GetProperty("modules").EnumerateArray()
            .Select(module => new[]
            {
                module.GetProperty("order").GetInt32().ToString("D2", System.Globalization.CultureInfo.InvariantCulture),
                Text(module, "module"),
                Text(module, "context"),
                Text(module, "historyTable"),
                $"{module.GetProperty("applied").GetArrayLength()}"
            })
            .ToArray();

        output.WriteLine($"provider: {Text(apply, "provider")}   schema: {Text(apply, "schema", "(none)")}");
        WriteTable(output, ["#", "MODULE", "CONTEXT", "HISTORY TABLE", "APPLIED"], rows);
    }

    private static void WriteValidate(TextWriter output, JsonElement validate)
    {
        var rows = validate.GetProperty("modules").EnumerateArray()
            .Select(module => new[]
            {
                module.GetProperty("order").GetInt32().ToString("D2", System.Globalization.CultureInfo.InvariantCulture),
                Text(module, "module"),
                Text(module, "context"),
                Text(module, "historyTable")
            })
            .ToArray();

        output.WriteLine($"provider: {Text(validate, "provider")}   schema: {Text(validate, "schema", "(none)")}");
        WriteTable(output, ["#", "MODULE", "CONTEXT", "HISTORY TABLE"], rows);
        output.WriteLine();
        output.WriteLine("No pending migrations.");
    }

    private static void WritePostMigrate(TextWriter output, JsonElement postMigrate)
    {
        var modules = postMigrate.GetProperty("modules").EnumerateArray().ToArray();
        var rows = modules
            .Select(module => new[]
            {
                module.GetProperty("order").GetInt32().ToString("D2", System.Globalization.CultureInfo.InvariantCulture),
                Text(module, "module"),
                // The names, not a count: an operator reading a run that did nothing still needs to see
                // which actions were considered, or "0 ran" is indistinguishable from "nothing declared".
                Names(module, "declared"),
                Names(module, "ran")
            })
            .ToArray();

        output.WriteLine($"provider: {Text(postMigrate, "provider")}   schema: {Text(postMigrate, "schema", "(none)")}");
        WriteTable(output, ["#", "MODULE", "DECLARED", "RAN"], rows);
        output.WriteLine();
        var ran = modules.Sum(module => module.GetProperty("ran").GetArrayLength());
        // "0 ran" is the ordinary answer on a database that needs nothing, not a sign the command did not
        // work — it audits first and runs only what is required, so saying so plainly avoids a re-run.
        output.WriteLine(ran == 0
            ? "Nothing required: no post-migration action had work to do."
            : $"{ran} post-migration action(s) ran; a re-audit reports nothing required.");
    }

    private static string Names(JsonElement element, string property) =>
        element.GetProperty(property).GetArrayLength() == 0
            ? "-"
            : string.Join(", ", element.GetProperty(property).EnumerateArray().Select(name => name.GetString()));

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
