namespace Elsa.Activities.Compensation.Tests.Support;

/// <summary>
/// A stand-in for an external system that persists independently of the workflow database. It follows the spec 251
/// participant contract: it applies each forward effect once per operation key, undoes a compensation's target key
/// when present and otherwise remembers it so a late forward request is refused, and records every call so tests can
/// tell a repeated call from a repeated effect. One instance is shared by every host generation of a test.
/// </summary>
public sealed class Participant
{
    private readonly object _gate = new();
    private readonly List<ParticipantCall> _calls = [];
    private readonly List<ParticipantCall> _effects = [];
    private readonly HashSet<string> _faultingNodes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _present = new(StringComparer.Ordinal);
    private readonly HashSet<string> _tombstones = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _failuresLeft = new(StringComparer.Ordinal);
    private readonly HashSet<string> _losingResponse = new(StringComparer.Ordinal);
    private readonly HashSet<string> _delaying = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _delayed = new(StringComparer.Ordinal);
    private string? _killAfterEffectOf;
    private string? _killBeforeEffectOf;

    /// <summary>Every call, including repeats the participant de-duplicated.</summary>
    public IReadOnlyList<ParticipantCall> Calls { get { lock (_gate) return _calls.ToArray(); } }

    /// <summary>The effects applied, one per distinct (node, operation key), in order.</summary>
    public IReadOnlyList<ParticipantCall> Effects { get { lock (_gate) return _effects.ToArray(); } }

    /// <summary>The node ids of the applied effects, in order.</summary>
    public IReadOnlyList<string> EffectOrder => Effects.Select(effect => effect.NodeId).ToArray();

    /// <summary>The forward operation keys whose effect exists now: applied and not undone.</summary>
    public IReadOnlyCollection<string> Present { get { lock (_gate) return _present.ToArray(); } }

    /// <summary>The forward keys a compensation saw absent; a later forward request for them is refused.</summary>
    public IReadOnlyCollection<string> Tombstones { get { lock (_gate) return _tombstones.ToArray(); } }

    /// <summary>Makes calls from these nodes fault, the way a participant rejects a request.</summary>
    public Participant Rejecting(params string[] nodeIds)
    {
        lock (_gate)
            foreach (var nodeId in nodeIds)
                _faultingNodes.Add(nodeId);
        return this;
    }

    /// <summary>Makes the node's next <paramref name="times"/> calls fail before any effect, like an unavailable service.</summary>
    public Participant FailingTimes(string nodeId, int times)
    {
        lock (_gate)
            _failuresLeft[nodeId] = times;
        return this;
    }

    /// <summary>The node's first call applies its effect and then loses the response: the activity cannot tell (J1).</summary>
    public Participant LosingResponseAfterEffectOf(string nodeId)
    {
        lock (_gate)
            _losingResponse.Add(nodeId);
        return this;
    }

    /// <summary>The node's first call holds its effect and loses the response; <see cref="ApplyLate"/> delivers it (J2).</summary>
    public Participant DelayingEffectOf(string nodeId)
    {
        lock (_gate)
            _delaying.Add(nodeId);
        return this;
    }

    /// <summary>Stops the host once, right after the named node's effect is applied and before its completion commits.</summary>
    public Participant KillingHostAfterEffectOf(string nodeId)
    {
        lock (_gate)
            _killAfterEffectOf = nodeId;
        return this;
    }

    /// <summary>Stops the host once, when the named node is called and before its effect is applied.</summary>
    public Participant KillingHostBeforeEffectOf(string nodeId)
    {
        lock (_gate)
            _killBeforeEffectOf = nodeId;
        return this;
    }

    /// <summary>Whether the host must stop now, before this node's effect. One-shot.</summary>
    public bool StopsBefore(string nodeId)
    {
        lock (_gate)
        {
            if (!StringComparer.Ordinal.Equals(_killBeforeEffectOf, nodeId))
                return false;

            _killBeforeEffectOf = null;
            return true;
        }
    }

    public bool Rejects(string nodeId)
    {
        lock (_gate)
            return _faultingNodes.Contains(nodeId);
    }

    /// <summary>Whether this call fails as unavailable; counts down <see cref="FailingTimes"/>.</summary>
    public bool FailsNow(string nodeId, string invocationId, string operationKey)
    {
        lock (_gate)
        {
            if (!_failuresLeft.TryGetValue(nodeId, out var left) || left <= 0)
                return false;

            _failuresLeft[nodeId] = left - 1;
            _calls.Add(new ParticipantCall(nodeId, invocationId, operationKey));
            return true;
        }
    }

    /// <summary>
    /// Applies a forward effect once per operation key. Returns <see cref="ForwardResult.Refused"/> for a key a
    /// compensation already saw absent, and <see cref="ForwardResult.ResponseLost"/> when the test loses the response.
    /// </summary>
    public ForwardResult Apply(string nodeId, string invocationId, string operationKey, out bool stopHost)
    {
        lock (_gate)
        {
            var call = new ParticipantCall(nodeId, invocationId, operationKey);
            _calls.Add(call);
            stopHost = false;

            if (_tombstones.Contains(operationKey))
                return ForwardResult.Refused;

            if (_delaying.Remove(nodeId))
            {
                _delayed[operationKey] = nodeId;
                return ForwardResult.ResponseLost;
            }

            if (!_effects.Any(effect => effect.NodeId == nodeId && effect.OperationKey == operationKey))
            {
                _effects.Add(call);
                _present.Add(operationKey);
            }

            if (StringComparer.Ordinal.Equals(_killAfterEffectOf, nodeId))
            {
                _killAfterEffectOf = null;
                stopHost = true;
            }

            return _losingResponse.Remove(nodeId) ? ForwardResult.ResponseLost : ForwardResult.Applied;
        }
    }

    /// <summary>Undoes the target's effect if present; otherwise remembers the target so a late forward is refused.</summary>
    public bool Undo(string nodeId, string invocationId, string operationKey, string targetOperationKey)
    {
        lock (_gate)
        {
            var call = new ParticipantCall(nodeId, invocationId, operationKey, targetOperationKey);
            _calls.Add(call);
            if (!_effects.Any(effect => effect.NodeId == nodeId && effect.OperationKey == operationKey))
                _effects.Add(call);

            if (!_present.Remove(targetOperationKey))
                _tombstones.Add(targetOperationKey);

            if (!StringComparer.Ordinal.Equals(_killAfterEffectOf, nodeId))
                return false;

            _killAfterEffectOf = null;
            return true;
        }
    }

    /// <summary>Delivers a held forward request now (J2). A tombstoned key is refused, so no effect remains.</summary>
    public ForwardResult ApplyLate(string operationKey)
    {
        lock (_gate)
        {
            if (!_delayed.Remove(operationKey, out var nodeId))
                throw new InvalidOperationException($"No forward request for '{operationKey}' is held.");
            if (_tombstones.Contains(operationKey))
                return ForwardResult.Refused;

            _present.Add(operationKey);
            _effects.Add(new ParticipantCall(nodeId, "late", operationKey));
            return ForwardResult.Applied;
        }
    }
}

public enum ForwardResult
{
    Applied,
    ResponseLost,
    Refused
}

public sealed record ParticipantCall(string NodeId, string InvocationId, string? OperationKey = null, string? TargetOperationKey = null);
