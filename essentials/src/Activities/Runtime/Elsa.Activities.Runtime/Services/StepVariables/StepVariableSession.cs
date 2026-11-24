using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Serialization.Core;
using Elsa.Expressions.Core.Contracts;
using Elsa.Workflows.Runtime.Services.Values;
using ExpressionVariableReference = Elsa.Expressions.Core.Models.VariableReference;

namespace Elsa.Activities.Runtime.Services.StepVariables;

/// <summary>
/// The variable session of one step (ADR 0090 decisions 1, 2, 6; ADR 0092 decisions 3, 4, 11; spec 1231 FR-002–FR-017):
/// created per step by the invoke and resume handlers over the step's visible frame chain, as committed when the step
/// started. Nothing else commits to the instance while an inline step runs (spec 1221 FR-021), so that is the committed
/// value at the moment of each read.
/// <list type="bullet">
/// <item>A read returns the committed value plus this step's own pending write (read-your-writes), as a copy, and is
/// recorded once per variable as a session read of this attempt (version; digest under <c>Fault</c>; protection policy).</item>
/// <item>A write is an intent holding the whole value and the base it was built on: the session read of that variable,
/// else the invocation's own advanced base or input read (<see cref="InvocationVariableBases"/>), else none (blind).</item>
/// <item>A withheld value, a transient value read as JSON, an undeclared free name and a failed type check fault the
/// step on the host side (<see cref="PendingFault"/>), even when the body catches the exception.</item>
/// </list>
/// The handler applies the intents in the step's checkpoint commit, before captures (<see cref="StepVariableCommit"/>).
/// </summary>
internal sealed class StepVariableSession : IStepVariableSession
{
    private readonly RuntimeVisibleVariableFrames? _frames;
    private readonly IReadOnlyDictionary<string, RuntimeVisibleVariable> _byName;
    private readonly ActivityExecutionState _invocation;
    private readonly ExecutableNode _node;
    private readonly IWorkflowExecutableInputValidator? _validator;
    private readonly bool _allowsFreeNames;
    private readonly LostUpdateMode _mode;
    private readonly bool _suspends;
    private readonly IReadOnlyDictionary<(string ScopeId, string Key), bool> _bound;
    private readonly bool _engineConsumer;
    private readonly WorkflowValuePolicies _valuePolicies;
    private readonly IWellKnownTypeRegistry? _registry;
    private readonly IRuntimeTypeNullability? _typeDescriptors;
    private IExecutionWarningSink? _warnings;
    internal void SetWarnings(IExecutionWarningSink warnings) => _warnings = warnings;
    private readonly Dictionary<(string ScopeId, string Key), VariableSessionRead> _reads = new();
    private readonly List<VariableSessionRead> _readOrder = [];
    private readonly HashSet<ValueProtectionPolicy> _pendingReadPolicies = [];
    internal IEnumerable<ValueProtectionPolicy> ReadPolicies => _readOrder.Select(read => read.Policy).Concat(_pendingReadPolicies);
    private readonly Dictionary<(string ScopeId, string Key), StepVariableWriteIntent> _intents = new();
    private readonly List<(string ScopeId, string Key)> _intentOrder = [];
    private readonly Dictionary<(string ScopeId, string Key), object?> _transient = new();
    // The stored value a typed (narrower) read deserialized from; compared with the value written back, at Set.
    private readonly Dictionary<(string ScopeId, string Key), JsonElement> _typedReads = new();

    /// <param name="frames">
    /// The step's visible frame chain; null for an activity that can bind nothing (no variable-reference input, no
    /// marker), whose every call then faults the step.
    /// </param>
    public StepVariableSession(
        RuntimeVisibleVariableFrames? frames,
        WorkflowExecutable executable,
        ActivityExecutionState invocation,
        ExecutableNode node,
        IWorkflowExecutableInputValidator? validator,
        string? attemptId = null,
        bool suspends = false,
        IReadOnlyDictionary<(string ScopeId, string Key), bool>? boundReferences = null,
        bool engineConsumer = false,
        IWellKnownTypeRegistry? registry = null,
        IRuntimeTypeNullability? typeDescriptors = null,
        IExecutionWarningSink? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(node);
        _frames = frames;
        _byName = frames is null
            ? new Dictionary<string, RuntimeVisibleVariable>(StringComparer.Ordinal)
            : RuntimeContainerScopeService.ProjectVisibleVariableTargets(executable, frames);
        _invocation = invocation;
        _node = node;
        _validator = validator;
        _allowsFreeNames = node.ActivityContract?.AccessesUndeclaredVariables == true;
        _mode = LostUpdateModes.Of(node);
        _suspends = suspends;
        _bound = boundReferences ?? new Dictionary<(string ScopeId, string Key), bool>();
        _engineConsumer = engineConsumer;
        _warnings = warnings;
        _valuePolicies = executable.ValuePolicies;
        _registry = registry;
        _typeDescriptors = typeDescriptors;
        AttemptId = attemptId
                    ?? invocation.Attempts?.LastOrDefault(attempt => attempt.EndedAt is null)?.AttemptId
                    ?? invocation.Attempts?.LastOrDefault()?.AttemptId
                    ?? string.Empty;
    }

    public string AttemptId { get; }

    // ---- the script body's read plan (spec 1222 FR-004, FR-033; T049, T050) ----

    public IReadOnlyDictionary<string, StepVariableTarget> PinnedReads => _pinnedReads ??= PinnedBodyReads();

    private IReadOnlyDictionary<string, StepVariableTarget>? _pinnedReads;

    public bool ReadsComputedNames => ScriptBodyReadPlan.Of(_node)?.VariableReadFallback is not null;

    public IReadOnlyList<string> VisibleNames => _byName.Keys.ToArray();

    public bool IsReadOnly(StepVariableTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        EnsureEngineSurface();
        return ResolvePinned(target.ScopeId, target.VariableKey, target.Name).Frame.Kind == VariableFrameKind.Iteration;
    }

    public bool HasReadProtectedValue => ReadPolicies.Any(policy =>
        policy.IsSensitive || policy.RequiresEncryption || !string.IsNullOrWhiteSpace(policy.RedactionMode));

    public void ObservePendingRead(StepVariableTarget target)
    {
        EnsureEngineSurface();
        var variable = ResolvePinned(target.ScopeId, target.VariableKey, target.Name);
        _pendingReadPolicies.Add(_intents.TryGetValue(variable.Address, out var intent) ? intent.Value.Policy : variable.Committed.Policy);
    }

    // The plan pins each literal body read to its declaring scope at publish, keyed variables.<name>; a name the step can
    // no longer see faults when it is read, as a pinned input read does.
    private IReadOnlyDictionary<string, StepVariableTarget> PinnedBodyReads()
    {
        var plan = ScriptBodyReadPlan.Of(_node);
        if (plan is null)
            return new Dictionary<string, StepVariableTarget>(StringComparer.Ordinal);
        const string prefix = Elsa.Expressions.Core.Models.ExpressionParameterSurfaces.VariablesKeyPrefix;
        return plan.Parameters.ToDictionary(
            parameter => parameter.Key.StartsWith(prefix, StringComparison.Ordinal) ? parameter.Key[prefix.Length..] : parameter.Key,
            parameter =>
            {
                var name = parameter.Key.StartsWith(prefix, StringComparison.Ordinal) ? parameter.Key[prefix.Length..] : parameter.Key;
                return new StepVariableTarget(parameter.Value.DeclaringScopeNodeId, parameter.Value.VariableKey, name);
            },
            StringComparer.Ordinal);
    }

    /// <summary>The reads this attempt made, in first-read order, each variable once.</summary>
    public IReadOnlyList<VariableSessionRead> Reads => _readOrder;

    /// <summary>The pending writes, in first-write order, one per variable (the latest value wins).</summary>
    public IReadOnlyList<StepVariableWriteIntent> Intents => _intentOrder.Select(key => _intents[key]).ToArray();

    /// <summary>
    /// A fault the session raised on the host side (FR-006b, FR-014): the step faults with it even if its body caught the
    /// exception the session threw.
    /// </summary>
    public Exception? PendingFault { get; private set; }

    // ---- C# surface ----

    public T Get<T>(VariableReference<T> reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        EnsureBound(reference, write: false);
        return GetTyped<T>(ResolvePinned(reference.ScopeId, reference.VariableKey, reference.Name));
    }

    public T Get<T>(ReadOnlyVariableReference<T> reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        EnsureBound(reference, write: false);
        return GetTyped<T>(ResolvePinned(reference.ScopeId, reference.VariableKey, reference.Name));
    }

    public void Set<T>(VariableReference<T> reference, T value)
    {
        ArgumentNullException.ThrowIfNull(reference);
        EnsureBound(reference, write: true);
        SetTyped(ResolvePinned(reference.ScopeId, reference.VariableKey, reference.Name), value);
    }

    // FR-023, FR-024: a reference reaches only what the workflow author bound to one of this node's inputs, in the
    // direction the input declares. A reference the activity built itself, or a read-only binding re-wrapped as writable,
    // would write outside the pinned may-write set; only a type marked [AccessesUndeclaredVariables] may do that (FR-025).
    private void EnsureBound(IVariableReference reference, bool write)
    {
        if (_allowsFreeNames)
            return;
        if (!_bound.TryGetValue((reference.ScopeId, reference.VariableKey), out var writable))
            throw Fault(new InvalidOperationException(
                $"Variable '{reference.Name}' is not bound to an input of node '{_node.ExecutableNodeId}'; an activity reads and writes only the variables its inputs are bound to."));
        if (write && !writable)
            throw Fault(new InvalidOperationException(
                $"Variable '{reference.Name}' is bound to a read-only input of node '{_node.ExecutableNodeId}'; the activity cannot write it."));
    }

    // The engine surface resolves names itself, so it is for engine-owned script activities (and marked types) only.
    private void EnsureEngineSurface()
    {
        if (!_engineConsumer && !_allowsFreeNames)
            throw Fault(new InvalidOperationException(
                $"Activity type '{_node.ActivityType}' is not an engine script activity; it reads and writes variables only through its bound references."));
    }

    [Obsolete("A ReadOnlyVariableReference cannot be written; this call always throws.")]
    public void Set<T>(ReadOnlyVariableReference<T> reference, T value)
    {
        ArgumentNullException.ThrowIfNull(reference);
        throw Fault(new InvalidOperationException(
            $"Variable '{reference.Name}' is bound through a read-only reference on node '{_node.ExecutableNodeId}'; the activity cannot write it. " +
            "Declare the input as VariableReference<T> to write it."));
    }

    public T Get<T>(string name) => GetTyped<T>(ResolveFreeName(name));

    public void Set<T>(string name, T value) => SetTyped(ResolveFreeName(name), value);

    // ---- engine surface (scripts) ----

    public bool TryResolve(string name, out StepVariableTarget target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        EnsureEngineSurface();
        if (_byName.TryGetValue(name, out var variable))
        {
            target = new StepVariableTarget(variable.Frame.ScopeId, variable.VariableKey, variable.Name);
            return true;
        }

        target = null!;
        return false;
    }

    public JsonNode? ReadJson(StepVariableTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        EnsureEngineSurface();
        var resolved = ResolvePinned(target.ScopeId, target.VariableKey, target.Name);
        if (resolved.IsTransient)
            throw Fault(new InvalidOperationException(
                $"Variable '{resolved.Name}' is transient: a script cannot read it (it holds a live .NET object, not a JSON value)."));
        var envelope = Read(resolved);
        return envelope.InlineValue is { } inline ? JsonNode.Parse(inline.GetRawText()) : null;
    }

    public void WriteJson(StepVariableTarget target, JsonNode? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        EnsureEngineSurface();
        var resolved = ResolvePinned(target.ScopeId, target.VariableKey, target.Name);
        if (resolved.IsTransient)
            throw Fault(new InvalidOperationException(
                $"Variable '{resolved.Name}' is transient: a script cannot write it."));
        var json = value is null ? (JsonElement?)null : JsonSerializer.SerializeToElement(value, Elsa.Serialization.Core.JsonBaseline.Options);
        Stage(resolved, json, StepVariableWriteKind.WholeAssignment, droppedMembers: null);
    }

    public void WriteUndefined(StepVariableTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        EnsureEngineSurface();
        var resolved = ResolvePinned(target.ScopeId, target.VariableKey, target.Name);
        if (resolved.IsTransient)
            throw Fault(new InvalidOperationException(
                $"Variable '{resolved.Name}' is transient: a script cannot write it."));
        if (!_valuePolicies.UndefinedAsNull)
            throw Fault(new UndefinedVariableAssignmentException(resolved.Name));
        Stage(resolved, null, StepVariableWriteKind.WholeAssignment, droppedMembers: null);
    }

    // ---- reads ----

    private T GetTyped<T>(ResolvedVariable variable)
    {
        if (variable.IsTransient)
        {
            EnsureTransientAllowed(variable);
            var live = _transient.TryGetValue(variable.Address, out var replaced) ? replaced : variable.Committed.TransientResource;
            return live switch
            {
                null => default!,
                T typed => typed,
                _ => throw Fault(new InvalidOperationException(
                    $"Transient variable '{variable.Name}' holds a '{live.GetType().FullName}', which cannot be read as '{typeof(T).FullName}' on node '{_node.ExecutableNodeId}'."))
            };
        }

        var envelope = Read(variable);
        if (envelope.Presence is ValuePresence.Absent or ValuePresence.ExplicitNull || envelope.InlineValue is not { } inline)
            return default!;

        // Get<T> on an Any variable (and an untyped read) defaults to a JSON node, so no member is lost (FR-017).
        if (typeof(T) == typeof(object) || typeof(T) == typeof(JsonNode))
            return (T)(object)JsonNode.Parse(inline.GetRawText())!;
        if (typeof(T) == typeof(JsonElement))
            return (T)(object)inline.Clone();

        T value;
        try
        {
            value = (T)Elsa.Serialization.Core.JsonConversionContract.For(typeof(T)).Read(inline, typeof(T),
                diagnostic =>
                {
                    var warning = Elsa.Expressions.Core.Models.ExecutionWarning.FromJsonConversion(diagnostic.Code, diagnostic.Path, diagnostic.TargetType, diagnostic.Converter, $"variables.{variable.Name}");
                    var policy = envelope.Policy;
                    _warnings?.Report(policy.IsSensitive || policy.RequiresEncryption || !string.IsNullOrWhiteSpace(policy.RedactionMode) ? warning.ProtectedForm! : warning);
                })!;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw Fault(new InvalidOperationException(
                $"Variable '{variable.Name}' cannot be read as '{typeof(T).FullName}' on node '{_node.ExecutableNodeId}'.", exception));
        }

        // A read as a narrower type may drop members of the stored value; a write of it back declares that loss (ADR 0046
        // V6). The comparison runs only when the value is written back, so a read serializes nothing. It is always made
        // against the stored value: a later read of the step's own pending write, already narrowed, does not replace it.
        if (inline.ValueKind is JsonValueKind.Object or JsonValueKind.Array && !_intents.ContainsKey(variable.Address))
            _typedReads.TryAdd(variable.Address, inline);

        return value;
    }

    private ValueEnvelope Read(ResolvedVariable variable)
    {
        // Pending reads do not establish or advance a concurrency base, but still carry protection.
        if (_intents.TryGetValue(variable.Address, out var intent))
        {
            _pendingReadPolicies.Add(intent.Value.Policy);
            return intent.Value;
        }

        var envelope = variable.Committed;
        if (envelope.Presence == ValuePresence.Withheld)
            throw Fault(SecretBindingDiagnostics.WithheldVariableNotResolved(variable.Name));
        if (envelope.ExternalReference is not null)
            throw Fault(new InvalidOperationException(
                $"Variable '{variable.Name}' is stored externally; a step session cannot read an externally stored variable yet. " +
                "Bind it to an input instead."));

        if (!_reads.ContainsKey(variable.Address))
        {
            var version = variable.Frame.VersionOf(variable.Key);
            var read = new VariableSessionRead(
                AttemptId,
                new VariableReadSetEntry(
                    variable.Frame.ScopeId,
                    variable.Key,
                    version.Version,
                    _mode == LostUpdateMode.Fault ? variable.Frame.CurrentDigestOf(variable.Key) : null),
                envelope.Policy);
            _reads[variable.Address] = read;
            _readOrder.Add(read);
        }

        if (envelope.Presence == ValuePresence.Absent &&
            !NullPolicyDefaults.AcceptsNull(envelope.Type.ToTypeReference(), _registry, _typeDescriptors) &&
            NullPolicyDefaults.TryGetTypeDefault(envelope.Type.Alias, _registry, out var typeDefault))
            return ValueEnvelope.Inline(envelope.Type, JsonSerializer.SerializeToElement(typeDefault, typeDefault!.GetType(), JsonBaseline.Options), envelope.Policy);
        return envelope;
    }

    // ---- writes ----

    private void SetTyped<T>(ResolvedVariable variable, T value)
    {
        if (variable.IsTransient)
        {
            EnsureTransientAllowed(variable);
            // A transient variable holds a live instance: Set replaces it in memory for this step only; nothing is committed
            // or recorded (FR-027).
            _transient[variable.Address] = value;
            return;
        }

        JsonElement? json;
        try
        {
            json = value is null ? null : Elsa.Serialization.Core.JsonConversionContract.For(typeof(T)).Write(value, typeof(T));
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw Fault(new InvalidOperationException(
                $"The value written to variable '{variable.Name}' on node '{_node.ExecutableNodeId}' cannot be serialized.", exception));
        }

        var dropped = json is { } written && _typedReads.TryGetValue(variable.Address, out var stored)
            ? DroppedMembers.Find(stored, written)
            : null;
        Stage(variable, json, StepVariableWriteKind.WholeAssignment, dropped);
    }

    // FR-027: a step that can suspend (a stateful or checkpoint-participating activity) cannot hold a live instance across
    // the suspension, so it may not touch a transient variable at all.
    private void EnsureTransientAllowed(ResolvedVariable variable)
    {
        if (_suspends)
            throw Fault(new InvalidOperationException(
                $"Variable '{variable.Name}' is transient, and node '{_node.ExecutableNodeId}' runs an activity that can suspend: a live instance cannot cross a suspension."));
    }

    private void Stage(ResolvedVariable variable, JsonElement? json, StepVariableWriteKind kind, IReadOnlyList<string>? droppedMembers)
    {
        if (variable.Frame.Kind == VariableFrameKind.Iteration)
            throw Fault(new InvalidOperationException(
                $"Variable '{variable.Name}' is a loop item or index and is read-only; a step cannot write it."));
        if (IsClosedScopeFrame(variable.Frame))
            throw Fault(new InvalidOperationException(
                $"Variable '{variable.Name}' belongs to a closed activity execution scope (a reusable activity's body); a step session cannot write it yet. " +
                "Capture an output into it instead."));

        var declared = variable.Committed;
        if (declared.Policy.RequiresEncryption)
            throw Fault(new InvalidOperationException(
                $"Variable '{variable.Name}' requires encryption; a step session cannot write it yet."));
        if (declared.Policy.Storage == DurableValueStorage.External)
            throw Fault(new InvalidOperationException(
                $"Variable '{variable.Name}' is stored externally; a step session cannot write it yet. Capture an output into it instead."));

        var envelope = json is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } inline
            ? ValueEnvelope.Inline(declared.Type, inline, declared.Policy)
            : ValueEnvelope.Null(declared.Type, declared.Policy);
        try
        {
            envelope = NullPolicyDefaults.NormalizeNull(envelope,
                NullPolicyDefaults.AcceptsNull(declared.Type.ToTypeReference(), _registry, _typeDescriptors), _valuePolicies,
                $"Variable '{variable.Name}'", _registry, _warnings);
        }
        catch (InvalidOperationException exception)
        {
            throw Fault(exception);
        }
        Validate(variable, envelope);

        var writeBase = _reads.TryGetValue(variable.Address, out var read)
            ? read.Entry.ToBase()
            : InvocationVariableBases.For(_invocation, variable.Frame.ScopeId, variable.Key);
        if (!_intents.ContainsKey(variable.Address))
            _intentOrder.Add(variable.Address);
        _intents[variable.Address] = new StepVariableWriteIntent(
            variable.Frame.ScopeId,
            variable.Key,
            variable.Name,
            variable.Frame.Kind == VariableFrameKind.Root ? null : variable.Frame.ActivationId,
            kind,
            envelope,
            writeBase,
            droppedMembers is { Count: > 0 } ? droppedMembers : null);
    }

    private void Validate(ResolvedVariable variable, ValueEnvelope value)
    {
        if (value.Presence != ValuePresence.Present || _validator is null ||
            value.Type is { CollectionKind: CollectionKind.Single } && TypeAliasConvention.IsAny(value.Type.Alias))
            return;

        // A collection of Any still has a shape to fit; Any resolves to its CLR type in the registry.
        var validationType = value.Type.ToTypeReference();
        var contract = new WorkflowExecutableInputContract(
            WorkflowExecutableInputContract.CurrentVersion,
            [new WorkflowDeclaredInput(variable.Name, validationType, isRequired: true)]);
        var validation = _validator.Validate(contract, [new KeyValuePair<string, JsonElement>(variable.Name, value.InlineValue!.Value)]);
        if (!validation.IsValid)
            throw Fault(new InvalidOperationException(
                $"The value written to variable '{variable.Name}' on node '{_node.ExecutableNodeId}' does not match its type '{value.Type.Alias}': " +
                string.Join(" ", validation.Findings.Select(finding => finding.Message))));
        var protect = value.Policy.IsSensitive || value.Policy.RequiresEncryption ||
                      !string.IsNullOrWhiteSpace(value.Policy.RedactionMode) || !string.IsNullOrWhiteSpace(value.Policy.RetentionPolicy);
        foreach (var warning in validation.Warnings)
            _warnings?.Report(protect ? warning.ProtectedForm ?? warning : warning);
    }

    // ---- resolution ----

    private ResolvedVariable ResolveFreeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_allowsFreeNames)
            throw Fault(new InvalidOperationException(
                $"Activity type '{_node.ActivityType}' reads or writes variable '{name}' by free name, but it is not marked [AccessesUndeclaredVariables]. " +
                "Bind a VariableReference<T> input instead, or mark the activity type."));
        if (!_byName.TryGetValue(name, out var variable))
            throw Fault(new InvalidOperationException(
                $"Variable '{name}' is not declared in any scope visible to node '{_node.ExecutableNodeId}'; a step never creates a variable."));
        return Resolved(variable.Frame, variable.VariableKey, variable.Name);
    }

    private ResolvedVariable ResolvePinned(string scopeId, string variableKey, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(variableKey);
        var frames = _frames?.Frames ?? [];
        for (var index = frames.Count - 1; index >= 0; index--)
        {
            var frame = frames[index];
            var matchesScope = StringComparer.Ordinal.Equals(frame.ScopeId, scopeId) ||
                               index == 0 && _frames!.ClosedScopeBoundary is not null &&
                               StringComparer.Ordinal.Equals(scopeId, ExpressionVariableReference.WorkflowScopeId);
            if (matchesScope && frame.Values.ContainsKey(variableKey))
                return Resolved(frame, variableKey, name);
        }

        throw Fault(new InvalidOperationException(
            $"Variable '{name}' (scope '{scopeId}', key '{variableKey}') is not visible to node '{_node.ExecutableNodeId}'."));
    }

    private static ResolvedVariable Resolved(VariableFrameState frame, string key, string name)
    {
        var committed = frame.Values[key];
        return new ResolvedVariable(frame, key, string.IsNullOrWhiteSpace(name) ? key : name, committed,
            committed.Policy.Lifecycle == DurableValueLifecycle.None);
    }

    private bool IsClosedScopeFrame(VariableFrameState frame) =>
        _frames?.ClosedScopeBoundary is not null && ReferenceEquals(frame, _frames.Frames[0]);

    private Exception Fault(Exception exception)
    {
        PendingFault ??= exception;
        return exception;
    }

    private sealed record ResolvedVariable(VariableFrameState Frame, string Key, string Name, ValueEnvelope Committed, bool IsTransient)
    {
        public (string ScopeId, string Key) Address => (Frame.ScopeId, Key);
    }
}

/// <summary>How a session write was made (C4 judges a no-op by it).</summary>
internal enum StepVariableWriteKind
{
    WholeAssignment,
    MemberChange
}

/// <summary>
/// One pending write of a step (data-model.md <c>WriteIntent</c>): the variable, the whole value (base plus the step's
/// changes, C3), the base it was built on (null = blind), its kind, and the members a narrower read dropped (FR-017).
/// <see cref="OwnerInvocationId"/> names the activity whose container frame holds the variable; null for the workflow root.
/// </summary>
internal sealed record StepVariableWriteIntent(
    string ScopeId,
    string VariableKey,
    string Name,
    string? OwnerInvocationId,
    StepVariableWriteKind Kind,
    ValueEnvelope Value,
    VariableWriteBase? Base,
    IReadOnlyList<string>? DroppedMembers);

/// <summary>Finds the members of a stored JSON value that a typed read did not keep.</summary>
internal static class DroppedMembers
{
    public static IReadOnlyList<string> Find(JsonElement stored, JsonElement kept)
    {
        var dropped = new List<string>();
        Walk(stored, kept, "$", dropped);
        return dropped;
    }

    private static void Walk(JsonElement stored, JsonElement kept, string path, List<string> dropped)
    {
        if (stored.ValueKind == JsonValueKind.Object && kept.ValueKind == JsonValueKind.Object)
        {
            foreach (var member in stored.EnumerateObject())
            {
                if (TryGetMember(kept, member.Name, out var keptMember))
                    Walk(member.Value, keptMember, $"{path}.{member.Name}", dropped);
                else
                    dropped.Add($"{path}.{member.Name}");
            }
        }
        else if (stored.ValueKind == JsonValueKind.Array && kept.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            using var keptItems = kept.EnumerateArray();
            foreach (var item in stored.EnumerateArray())
            {
                if (!keptItems.MoveNext())
                    break;
                Walk(item, keptItems.Current, $"{path}[{index++}]", dropped);
            }
        }
    }

    private static bool TryGetMember(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
            return true;
        foreach (var member in element.EnumerateObject())
        {
            if (string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = member.Value;
                return true;
            }
        }

        return false;
    }
}

internal sealed class UndefinedVariableAssignmentException(string variable) : InvalidOperationException($"VF-UNDEF-001: Variable '{variable}' cannot be assigned undefined; assign a value or null, or enable UndefinedAsNull."), Elsa.Expressions.Core.Contracts.IValueFreeExpressionFailure
{
    public string ValueFreeMessage => "VF-UNDEF-001: A variable cannot be assigned undefined; assign a value or null, or enable UndefinedAsNull. The variable name is withheld because a value it read is protected.";
}
