using System.Text.RegularExpressions;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Activity-owned request to keep the child fault a structural child-fault evaluation is processing blocking until an
/// operator intervenes: the named incident stays blocking with a wait-for-intervention outcome and the faulted child's
/// subtree is left as it is, atomically with the parent's deferred continuation. The request may offer the decisions an
/// operator can take on the wait (spec 260 R-5); the runtime carries them as opaque codes.
/// </summary>
public sealed partial class RuntimeChildFaultInterventionRequest
{
    /// <summary>The most decisions one intervention request may offer.</summary>
    public const int MaximumDecisionCount = 8;

    /// <summary>The longest decision code, in UTF-16 code units.</summary>
    public const int MaximumDecisionCodeLength = 64;

    /// <summary>The prefix reserved for runtime-owned codes; no offered decision may start with it.</summary>
    public const string ReservedDecisionPrefix = "runtime.";

    public RuntimeChildFaultInterventionRequest(
        string incidentId,
        string reason,
        IReadOnlyDictionary<string, string>? metadata = null,
        IReadOnlyCollection<string>? decisions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(incidentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        IncidentId = incidentId;
        Reason = reason;
        Metadata = RuntimeModelMetadata.Snapshot(metadata ?? new Dictionary<string, string>());
        Decisions = decisions is null ? [] : ValidateDecisions(decisions);
    }

    public string IncidentId { get; }
    public string Reason { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>The decision codes offered to an operator, in offer order; empty when the request offers none.</summary>
    public IReadOnlyList<string> Decisions { get; }

    /// <summary>
    /// Checks an offer: 1 to <see cref="MaximumDecisionCount"/> distinct codes, each dotted and lower case (at least two
    /// segments), at most <see cref="MaximumDecisionCodeLength"/> characters, and not starting with
    /// <see cref="ReservedDecisionPrefix"/>. Throws <see cref="ArgumentException"/> on a violation.
    /// </summary>
    public static IReadOnlyList<string> ValidateDecisions(IReadOnlyCollection<string> decisions)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        if (decisions.Count is 0 or > MaximumDecisionCount)
            throw new ArgumentException($"An intervention request offers 1 to {MaximumDecisionCount} decisions.", nameof(decisions));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var decision in decisions)
        {
            if (!IsValidDecisionCode(decision))
                throw new ArgumentException(
                    $"Intervention decision '{decision}' is not a dotted lower-case code of at most {MaximumDecisionCodeLength} characters outside the '{ReservedDecisionPrefix}' prefix.",
                    nameof(decisions));
            if (!seen.Add(decision))
                throw new ArgumentException($"Intervention decision '{decision}' is offered more than once.", nameof(decisions));
        }

        return decisions.ToArray();
    }

    /// <summary>Whether <paramref name="decision"/> is a well-formed decision code.</summary>
    public static bool IsValidDecisionCode(string? decision) =>
        decision is not null &&
        decision.Length <= MaximumDecisionCodeLength &&
        !decision.StartsWith(ReservedDecisionPrefix, StringComparison.Ordinal) &&
        DecisionCodePattern().IsMatch(decision);

    [GeneratedRegex(@"^[a-z0-9]+(\.[a-z0-9-]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex DecisionCodePattern();
}
