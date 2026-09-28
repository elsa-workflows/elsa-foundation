using System.Globalization;
using Elsa.Cluster.Core.Models;

namespace Elsa.Cluster.Core.Services;

/// <summary>
/// How a dormant feature says why (spec 182, FR-008), in the two forms FR-011 separates: a caller-neutral reason that a
/// domain API may return to anyone, and an operator reason that adds the finalization gate's status (spec 181, FR-022),
/// including the members that cannot read a version and the holds' own words, for the catalog, Attention and the CLI.
/// </summary>
public static class SchemaDormancyReasons
{
    /// <summary>The caller-neutral reason for one unmet requirement. It names no host and no operator's words.</summary>
    public static string ForCaller(SchemaVersionRequirement requirement, SchemaDormancyKind kind)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        var version = $"version '{requirement.Version}' of schema family '{requirement.Family}'";
        return kind switch
        {
            SchemaDormancyKind.WaitingForHosts => $"It becomes available once every host can read {version}.",
            SchemaDormancyKind.Held => $"It is held by an operator: {version} is not finalized until the hold is released.",
            SchemaDormancyKind.WaitingForCompleteness =>
                $"It becomes available once existing records of schema family '{requirement.Family}' have been upgraded to version '{requirement.Version}'.",
            SchemaDormancyKind.NotYetAdopted =>
                $"It becomes available once this host adopts {version}, which is finalized but which this host may not write until it has rejoined the cluster.",
            SchemaDormancyKind.NotObserved =>
                $"This host has not read the finalization record of schema family '{requirement.Family}', so it cannot tell whether {version} is available.",
            SchemaDormancyKind.UnknownVersion => $"This build does not read {version}.",
            SchemaDormancyKind.WritesRefused =>
                $"This host cannot read the finalized version of schema family '{requirement.Family}', so every write to the family is refused.",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "An unmet requirement has a reason for every kind.")
        };
    }

    /// <summary>
    /// The operator reason for <paramref name="unmet"/>: its caller reason, then what the gate's status says about the
    /// family (spec 181, FR-022). <paramref name="status"/> is the family's status from an operator read, with blockers,
    /// or <see langword="null"/> when none could be read; the observation <paramref name="unmet"/> came from is used then.
    /// </summary>
    public static string ForOperator(UnmetSchemaRequirement unmet, SchemaFamilyObservation? status)
    {
        ArgumentNullException.ThrowIfNull(unmet);
        var parts = new List<string> { unmet.Reason };
        var requirement = unmet.Requirement;

        if (status?.FinalizedVersion is { } finalized)
            parts.Add($"Schema family '{requirement.Family}' is finalized at '{finalized}', and this host writes '{status.WriteVersion ?? "nothing yet"}'.");

        switch (unmet.Kind)
        {
            case SchemaDormancyKind.WaitingForHosts when status?.PendingOf(requirement.Version) is { } pending:
                if (pending.Blockers is { Count: > 0 } blockers)
                    parts.Add($"Hosts that cannot read '{requirement.Version}' yet: {string.Join("; ", blockers)}.");
                else if (pending.ReadableEverywhere)
                    parts.Add($"Every counted host can read '{requirement.Version}', and its finalization is being confirmed.");
                break;
            case SchemaDormancyKind.Held:
                var holds = status?.PendingOf(requirement.Version)?.HeldBy is { Count: > 0 } current ? current : unmet.Holds;
                parts.AddRange(holds.Select(Describe));
                break;
            case SchemaDormancyKind.WaitingForCompleteness:
                parts.Add(status?.CompletionVersion is { } completion
                    ? $"Its finish record names '{completion}'; the post-finalization backfill has not yet recorded '{requirement.Version}'."
                    : "No finish record stands for it; the post-finalization backfill records one once no older row remains.");
                break;
            case SchemaDormancyKind.WritesRefused when status is not null:
                parts.Add($"This host reads only [{string.Join(", ", status.ReadableVersions)}]. Run a version that reads the finalized one.");
                break;
        }

        if (status?.Intent is { } intent)
            parts.Add($"An intent to finalize '{intent.Version}' was recorded by {intent.Member} at {Format(intent.At)}.");

        return string.Join(" ", parts);
    }

    private static string Describe(SchemaHoldObservation hold) =>
        $"{(hold.Version is null ? "The family" : $"Version '{hold.Version}'")} is held by {hold.PlacedBy} since {Format(hold.PlacedAt)}: {hold.Reason}";

    private static string Format(DateTimeOffset at) => at.ToString("u", CultureInfo.InvariantCulture);
}
