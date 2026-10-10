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
    private const string Unreadable = "reports what it reads in a form this tool cannot interpret; counts as reading nothing";

    /// <summary>The <c>cluster.availability</c> markers this tool prints something of its own for; any other says why in its note. The names are the tooling contract's.</summary>
    private const string ClusterRead = "read";
    private const string ClusterNoMembershipProvider = "no-membership-provider";

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
                break;
            case WorkerCommands.Plan:
                WritePlan(output, tooling.GetProperty("plan"));
                break;
            case WorkerCommands.Script:
                WriteScript(output, tooling.GetProperty("script"),
                    tooling.TryGetProperty("configurationContext", out var scriptContext) ? scriptContext : null);
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
            case WorkerCommands.Hold or WorkerCommands.Release or WorkerCommands.Status:
                WriteFinalization(output, command, tooling.GetProperty("finalization"));
                break;
        }

        if (tooling.TryGetProperty("configurationContext", out var context))
            WriteConfigurationContext(output, context);

        return response.ExitCode;
    }

    /// <summary>
    /// Each family's finalization status (spec 181, FR-022): the finalized version, each pending version and the holds
    /// that keep it, any intent in flight and every hold, and what the finish record holds: the completion version, a
    /// claimed backfill run and a withdrawn completion (spec 186, FR-021). Under each pending version it names the counted
    /// members that cannot read it (FR-022), and after the families it lists the cluster's members, both read from the
    /// membership table in the database. How far a backfill has got is known only to a running host, whose memory this tool
    /// does not read.
    /// </summary>
    internal static void WriteFinalization(TextWriter output, string command, JsonElement finalization)
    {
        var families = finalization.GetProperty("families").EnumerateArray().ToArray();
        foreach (var family in families)
        {
            var finalized = Optional(family, "finalizedVersion");
            output.WriteLine($"{Text(family, "family")} ({Text(family, "module")}): " +
                             (finalized is null ? "no finalization record in this database yet" : $"finalized at {finalized}") +
                             $"; this host reads [{string.Join(", ", family.GetProperty("readableVersions").EnumerateArray().Select(version => version.GetString()))}]");
            if (family.TryGetProperty("intent", out var intent) && intent.ValueKind == JsonValueKind.Object)
                output.WriteLine($"  intent to finalize {Text(intent, "version")} by {Text(intent, "member")} at {Text(intent, "at")}");
            foreach (var pending in family.GetProperty("pending").EnumerateArray())
            {
                var heldBy = pending.GetProperty("heldBy").EnumerateArray().Select(reason => reason.GetString()).ToArray();
                var waitsFor = pending.TryGetProperty("waitsFor", out var waiting) && waiting.ValueKind == JsonValueKind.Array
                    ? waiting.EnumerateArray().ToArray()
                    : null;
                output.WriteLine($"  {Text(pending, "version")}: {Text(pending, "state")}{PendingSuffix(heldBy, waitsFor)}");
                foreach (var member in waitsFor ?? [])
                    output.WriteLine($"    waits for: {Text(member, "hostId")} ({(member.GetProperty("reportReadable").GetBoolean() ? $"reads {Versions(member.GetProperty("reads"))}" : Unreadable)})");
            }

            foreach (var hold in family.GetProperty("holds").EnumerateArray())
                output.WriteLine($"  hold on {Optional(hold, "version") ?? "the whole family"} by {Text(hold, "placedBy")} at {Text(hold, "placedAt")}: {Text(hold, "reason")}");
            if (Optional(family, "completionVersion") is { } completion)
                output.WriteLine($"  complete from {completion}");
            if (family.TryGetProperty("backfillRun", out var run) && run.ValueKind == JsonValueKind.Object)
                output.WriteLine($"  backfill to {Text(run, "targetVersion")} claimed by {Text(run, "member")} until {Text(run, "expiresAt")}");
            if (family.TryGetProperty("completionWithdrawn", out var withdrawn) && withdrawn.ValueKind == JsonValueKind.Object)
                output.WriteLine($"  completion at {Text(withdrawn, "version")} withdrawn by {Text(withdrawn, "withdrawnBy")} at {Text(withdrawn, "at")}: {Optional(withdrawn, "reason")}");
        }

        if (finalization.TryGetProperty("cluster", out var cluster) && cluster.ValueKind == JsonValueKind.Object)
            WriteCluster(output, cluster, families);
        else if (command == WorkerCommands.Status)
        {
            // A tooling build that predates the property says nothing about the cluster, which is not the same as saying there is none.
            output.WriteLine();
            output.WriteLine("members: cluster membership not reported by this host's tooling.");
        }

        output.WriteLine();
        output.WriteLine(command switch
        {
            WorkerCommands.Hold => "Hold placed. Nothing it applies to finalizes until it is released.",
            WorkerCommands.Release => "Hold released. The version finalizes at the next evaluation once every counted member reads it.",
            _ => $"{families.Length} family(ies)."
        });
    }

    /// <summary>What follows a pending version: the holds that keep it, or what the members read says of the rest, and no more than that.</summary>
    private static string PendingSuffix(string?[] heldBy, JsonElement[]? waitsFor)
    {
        if (heldBy.Length > 0)
            return $", held: {string.Join("; ", heldBy)}";

        return waitsFor is { Length: 0 }
            ? ", held by nothing; no member counted here blocks it"
            : ", held by nothing; waits for every counted member to read it";
    }

    /// <summary>
    /// The membership table's members, judged on this tool's clock: host id, status, whether it is live, its last heartbeat
    /// and, for each family listed above, the versions it reads, all as the host's tooling computed them. A live member that
    /// reports nothing about a family is said not to be counted for it.
    /// </summary>
    private static void WriteCluster(TextWriter output, JsonElement cluster, JsonElement[] families)
    {
        output.WriteLine();
        switch (Optional(cluster, "availability"))
        {
            case ClusterNoMembershipProvider:
                output.WriteLine("members: this host only (this host's closure carries no cluster membership provider).");
                return;
            case not null and not ClusterRead:
                output.WriteLine($"members: {Optional(cluster, "note") ?? "not read here."}");
                return;
        }

        var members = cluster.GetProperty("members").EnumerateArray().ToArray();
        output.WriteLine($"members: {members.Length} in {Text(cluster, "module")}, judged at {Moment(cluster, "judgedAt")} with a skew allowance of {Text(cluster, "skewAllowance")}");
        foreach (var member in members)
        {
            var live = member.GetProperty("live").GetBoolean();
            var state = live
                ? member.GetProperty("displaced").GetBoolean() ? "live, displaced by a later incarnation" : "live"
                : Text(member, "status") == "Left" ? "left" : "expired";
            output.WriteLine($"  {Text(member, "hostId")}: {Text(member, "status")}, {state}, last heartbeat {Moment(member, "lastHeartbeatAt")}");
            if (!member.GetProperty("reportReadable").GetBoolean())
            {
                output.WriteLine($"    {Unreadable}");
                continue;
            }

            var reads = member.GetProperty("reads").EnumerateArray().ToDictionary(entry => Text(entry, "family"), entry => entry.GetProperty("versions"), StringComparer.Ordinal);
            foreach (var name in families.Select(family => Text(family, "family")))
            {
                if (reads.TryGetValue(name, out var versions))
                    output.WriteLine($"    {name}: reads {Versions(versions)}");
                else if (live)
                    output.WriteLine($"    {name}: reports nothing, so it is not counted for it");
            }
        }
    }

    /// <summary>An instant to the second, in UTC: what a presenter reads off a heartbeat, without the fraction of a tick the table keeps.</summary>
    private static string Moment(JsonElement element, string name) =>
        DateTimeOffset.TryParse(Text(element, name), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var instant)
            ? instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture)
            : Text(element, name);

    private static string Versions(JsonElement versions) =>
        versions.GetArrayLength() == 0 ? "nothing" : string.Join(", ", versions.EnumerateArray().Select(version => version.GetString()));

    private static string? Optional(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

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

    private static void WriteScript(TextWriter output, JsonElement script, JsonElement? context)
    {
        foreach (var file in script.GetProperty("files").EnumerateArray())
            output.WriteLine($"{Text(file, "file")}  {Text(file, "sha256")}  {Text(file, "module")}");
        output.WriteLine($"{Text(script, "manifest")}  {Text(script, "manifestSha256")}");
        output.WriteLine(context is { } selected
            ? $"note: providerAgreement was checked against {Text(selected, "source")}; runtime parity remains unobserved."
            : ProviderAgreementNote);
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
