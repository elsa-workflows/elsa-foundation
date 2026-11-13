using Elsa.Primitives.Models;
namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>What the commit rule decided for one write (spec 1221 contracts/commit-rule.md).</summary>
public enum VariableCommitOutcome
{
    /// <summary>The writer never read the variable: last commit wins among unordered writers (D-8).</summary>
    Blind,

    /// <summary>The writer's base is the current version.</summary>
    Current,

    /// <summary>Default mode, stale base: the write applies (last write wins) and raises a lost-update warning.</summary>
    LostUpdateWarning,

    /// <summary><c>Fault</c> mode, stale base whose value came back (equal digests): the write applies; the mismatch is recorded.</summary>
    ReturnedValue,

    /// <summary><c>Fault</c> mode, stale base whose value changed: the write does not apply and the step faults.</summary>
    LostUpdateFault
}

/// <summary>The decision for one write and the version the variable has once it is applied.</summary>
public sealed record VariableCommitDecision(
    VariableCommitOutcome Outcome,
    VariableWriteBase? Base,
    VariableVersion Current,
    VariableVersion Resulting)
{
    /// <summary>Whether the write applies at all; only a <c>Fault</c> lost update does not.</summary>
    public bool Applies => Outcome != VariableCommitOutcome.LostUpdateFault;

    /// <summary>Whether the applied write changes the value (an equal digest applies nothing and keeps the version).</summary>
    public bool ChangesValue => Applies && Resulting.Version != Current.Version;

    /// <summary>Whether the write was judged against a stale base.</summary>
    public bool IsLostUpdate => Outcome is VariableCommitOutcome.LostUpdateWarning or VariableCommitOutcome.LostUpdateFault;
}

/// <summary>
/// The one commit rule every variable write site evaluates (spec 1221 FR-009/FR-010, ADR 0092 decision 5): identical
/// for captures, intrinsics, alterations and session writes, inline and background, frames and rows.
/// <list type="bullet">
/// <item>No base → blind write → apply.</item>
/// <item>Base version equals the current version → apply.</item>
/// <item>Stale base, default mode → apply with a lost-update warning; no digest is compared, so <c>0 → 1 → 0</c> warns.</item>
/// <item>Stale base, <c>Fault</c> → compare stored digests: equal → apply and record the mismatch; different → fault.</item>
/// </list>
/// Once a write applies, a new value whose digest equals the current digest applies nothing and keeps the version.
/// </summary>
internal static class VariableCommitRule
{
    public static VariableCommitDecision Evaluate(
        VariableWriteBase? writeBase,
        VariableVersion current,
        string newDigest,
        LostUpdateMode mode)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentException.ThrowIfNullOrWhiteSpace(newDigest);

        VariableCommitOutcome outcome;
        if (writeBase is not { } baseVersion)
            outcome = VariableCommitOutcome.Blind;
        else if (baseVersion.Version == current.Version)
            outcome = VariableCommitOutcome.Current;
        else if (mode == LostUpdateMode.Fault)
            // The exact check compares two stored digests and runs only on a version mismatch. A base or current value
            // without a stored digest cannot be shown to have come back, so it counts as changed.
            outcome = baseVersion.Digest is { } baseDigest && current.Digest is { } currentDigest &&
                      StringComparer.Ordinal.Equals(baseDigest, currentDigest)
                ? VariableCommitOutcome.ReturnedValue
                : VariableCommitOutcome.LostUpdateFault;
        else
            outcome = VariableCommitOutcome.LostUpdateWarning;

        if (outcome == VariableCommitOutcome.LostUpdateFault)
            return new VariableCommitDecision(outcome, writeBase, current, current);

        var resulting = current.Digest is { } existing && StringComparer.Ordinal.Equals(existing, newDigest)
            ? current
            : new VariableVersion(checked(current.Version + 1), newDigest);
        return new VariableCommitDecision(outcome, writeBase, current, resulting);
    }
}
