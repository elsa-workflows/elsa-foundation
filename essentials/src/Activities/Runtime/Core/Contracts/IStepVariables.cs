using System.Text.Json.Nodes;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Core.Contracts;

/// <summary>
/// The variable session of one step (ADR 0090 decisions 1, 2; spec 1231 FR-002, FR-023): <c>context.Variables</c>.
/// A read returns the variable's committed value at the moment of the read plus this step's own pending writes
/// (read-your-writes), as a copy: changing it writes nothing (C2). A write is an explicit intent, applied in the step's
/// checkpoint commit under the commit rule (ADR 0092) and discarded when the step faults or is cancelled. It exposes
/// variables only; it is not a service locator (ADR 0045 §6's one sanctioned exception).
/// </summary>
/// <remarks>
/// Only a step body has a session: a leaf activity's execute or resume. Input bindings, structural evaluations and
/// background workers have none, and every call on their <c>Variables</c> throws.
/// </remarks>
public interface IStepVariables
{
    /// <summary>Reads the variable a read-write reference is bound to.</summary>
    T Get<T>(VariableReference<T> reference);

    /// <summary>Reads the variable a read-only reference is bound to.</summary>
    T Get<T>(ReadOnlyVariableReference<T> reference);

    /// <summary>Records a write of the whole value to the variable the reference is bound to.</summary>
    void Set<T>(VariableReference<T> reference, T value);

    /// <summary>A read-only reference cannot be written: this always throws, and the step faults.</summary>
    [Obsolete("A ReadOnlyVariableReference cannot be written; this call always throws. Declare the input as VariableReference<T> to write it.")]
    void Set<T>(ReadOnlyVariableReference<T> reference, T value);

    /// <summary>
    /// Reads a variable by free name, resolved to the nearest visible declaration. Only an activity type marked
    /// <c>[AccessesUndeclaredVariables]</c> may call it; on any other it throws.
    /// </summary>
    T Get<T>(string name);

    /// <summary>
    /// Records a write to a variable by free name, resolved to the nearest visible declaration. Only an activity type
    /// marked <c>[AccessesUndeclaredVariables]</c> may call it; on any other it throws. An undeclared name faults the step.
    /// </summary>
    void Set<T>(string name, T value);
}

/// <summary>
/// The engine-facing side of a step's variable session, for engine-owned script activities (<c>RunJavaScript</c>,
/// spec 1222 FR-033–FR-035, spec 1231 FR-006a, FR-020a): reads and writes by a pinned target (declaring scope and key)
/// or by a name resolved at run time, as JSON. Every read is recorded as a session read; a withheld or transient value
/// is refused on the host side (spec 1222 FR-017a), so a script cannot catch it away.
/// </summary>
public interface IStepVariableSession : IStepVariables
{
    /// <summary>Resolves a name to the nearest visible declaration, or returns false when no visible variable has it.</summary>
    bool TryResolve(string name, out StepVariableTarget target);

    /// <summary>Reads a pinned target: the committed value plus this step's own pending write, as a JSON copy.</summary>
    JsonNode? ReadJson(StepVariableTarget target);

    /// <summary>Records a whole-value write of a pinned target, as JSON.</summary>
    void WriteJson(StepVariableTarget target, JsonNode? value);

    /// <summary>
    /// The variables the script body names literally, pinned at publish to their declaring scope (spec 1222 FR-004, the
    /// body read plan), by name. A literal read reads its pinned target, never the run-time lookup.
    /// </summary>
    IReadOnlyDictionary<string, StepVariableTarget> PinnedReads { get; }

    /// <summary>Whether the script body also reads variables by a computed or opaque name (the plan's fallback marker).</summary>
    bool ReadsComputedNames { get; }

    /// <summary>The names of the variables visible to the step, nearest scope first, each once.</summary>
    IReadOnlyList<string> VisibleNames { get; }

    /// <summary>
    /// Whether a read this session recorded held a protected value (sensitive, encrypted or redacted): the step's
    /// diagnostics then use their value-free form (spec 1229 FR-006), and its result takes that protection (spec 1228 US7).
    /// </summary>
    bool HasReadProtectedValue { get; }

    /// <summary>Observes the policy of a script-held pending value without advancing a concurrency read base.</summary>
    /// <remarks>The compatible default performs an ordinary read; the runtime session records protection separately.</remarks>
    void ObservePendingRead(StepVariableTarget target) { _ = ReadJson(target); }

    /// <summary>
    /// Whether a target is read-only to a step: a loop's item or index (spec 1231 FR-006). A script gets it frozen, so a
    /// change throws where the script makes it.
    /// </summary>
    bool IsReadOnly(StepVariableTarget target);
}

/// <summary>A variable a session reads or writes: its declaring scope and key, and its declared name.</summary>
public sealed record StepVariableTarget(string ScopeId, string VariableKey, string Name);

/// <summary>
/// An engine-owned activity whose body reads and writes variables by names it resolves itself (<c>RunJavaScript</c>,
/// through <see cref="IStepVariableSession"/>): its session loads the visible frame chain even though no input binds a
/// variable. Implementing it counts as <c>[AccessesUndeclaredVariables]</c> wherever that is pinned (the contract, the
/// descriptor, the may-write set and background eligibility), so the engine surface is never a way around them.
/// </summary>
public interface IStepVariableSessionConsumer;
