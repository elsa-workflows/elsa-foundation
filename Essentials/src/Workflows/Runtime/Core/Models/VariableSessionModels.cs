using System.Text.Json.Serialization;
using Elsa.Primitives.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// One read a step made through its variable session (spec 1231 FR-007, ADR 0092 decision 3): the variable, the version
/// read, the stored digest only when the node's lost-update mode is <see cref="LostUpdateMode.Fault"/>, and the
/// variable's protection policy (spec 1222 FR-035). Session reads belong to one attempt: they give that attempt's session
/// writes their base and are never carried to a new attempt or across a suspension; they are kept on the invocation for
/// inspection only.
/// </summary>
public sealed record VariableSessionRead
{
    [JsonConstructor]
    public VariableSessionRead(string attemptId, VariableReadSetEntry entry, ValueProtectionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(attemptId);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(policy);
        AttemptId = attemptId;
        Entry = entry;
        Policy = policy;
    }

    public string AttemptId { get; }
    public VariableReadSetEntry Entry { get; }
    public ValueProtectionPolicy Policy { get; }
}

/// <summary>
/// The base an invocation's write to a variable is judged against (ADR 0092 decision 4, spec 1231 FR-008–FR-010).
/// </summary>
public static class InvocationVariableBases
{
    /// <summary>
    /// The base for a capture or a session write that was not built on a session read: the base the invocation's own
    /// applied write advanced it to, else the input read set's entry, else none (a blind write). An invocation never
    /// conflicts with its own writes, so its own advanced base wins over the older input read.
    /// </summary>
    public static VariableWriteBase? For(ActivityExecutionState? invocation, string scopeId, string variableKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(variableKey);
        if (invocation is null)
            return null;

        if (invocation.OwnVariableBases is { } own)
        {
            foreach (var entry in own)
            {
                if (StringComparer.Ordinal.Equals(entry.ScopeId, scopeId) && StringComparer.Ordinal.Equals(entry.VariableKey, variableKey))
                    return entry.ToBase();
            }
        }

        return invocation.InputSnapshot?.InputReadSet?.BaseFor(scopeId, variableKey);
    }

    /// <summary>
    /// The invocation with its base for each applied, value-changing write of its own advanced to the version that write
    /// produced (FR-010). A record of a lost-update fault or of a write that changed nothing leaves the base as it was.
    /// </summary>
    public static ActivityExecutionState Advance(ActivityExecutionState invocation, IEnumerable<VariableWriteRecord> ownWrites)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(ownWrites);
        var bases = (invocation.OwnVariableBases ?? []).ToList();
        var changed = false;
        foreach (var record in ownWrites)
        {
            if (record.Outcome == VariableCommitOutcome.LostUpdateFault || record.NewVersion == record.CurrentVersion)
                continue;

            bases.RemoveAll(entry => StringComparer.Ordinal.Equals(entry.ScopeId, record.ScopeId) &&
                                     StringComparer.Ordinal.Equals(entry.VariableKey, record.VariableKey));
            bases.Add(new VariableReadSetEntry(record.ScopeId, record.VariableKey, record.NewVersion, record.Digest));
            changed = true;
        }

        return changed ? invocation with { OwnVariableBases = bases.AsReadOnly() } : invocation;
    }
}
