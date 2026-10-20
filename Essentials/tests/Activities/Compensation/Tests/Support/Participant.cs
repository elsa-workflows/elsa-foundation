namespace Elsa.Activities.Compensation.Tests.Support;

/// <summary>
/// A stand-in for an external system that persists independently of the workflow database. It applies each effect
/// once per invocation id, as an idempotent participant does, and records every call so tests can tell a repeated
/// call from a repeated effect. One instance is shared by every host generation of a test.
/// </summary>
public sealed class Participant
{
    private readonly object _gate = new();
    private readonly List<ParticipantCall> _calls = [];
    private readonly List<ParticipantCall> _effects = [];
    private readonly HashSet<string> _faultingNodes = new(StringComparer.Ordinal);
    private string? _killAfterEffectOf;
    private string? _killBeforeEffectOf;

    /// <summary>Every call, including repeats the participant de-duplicated.</summary>
    public IReadOnlyList<ParticipantCall> Calls { get { lock (_gate) return _calls.ToArray(); } }

    /// <summary>The effects applied, one per distinct (node, invocation id), in order.</summary>
    public IReadOnlyList<ParticipantCall> Effects { get { lock (_gate) return _effects.ToArray(); } }

    /// <summary>The node ids of the applied effects, in order.</summary>
    public IReadOnlyList<string> EffectOrder => Effects.Select(effect => effect.NodeId).ToArray();

    /// <summary>Makes calls from these nodes fault, the way a participant rejects a request.</summary>
    public Participant Rejecting(params string[] nodeIds)
    {
        lock (_gate)
            foreach (var nodeId in nodeIds)
                _faultingNodes.Add(nodeId);
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

    /// <summary>Applies the effect unless this invocation already applied it. Returns whether the host must stop now.</summary>
    public bool Apply(string nodeId, string invocationId)
    {
        lock (_gate)
        {
            var call = new ParticipantCall(nodeId, invocationId);
            _calls.Add(call);
            if (!_effects.Contains(call))
                _effects.Add(call);

            if (!StringComparer.Ordinal.Equals(_killAfterEffectOf, nodeId))
                return false;

            _killAfterEffectOf = null;
            return true;
        }
    }
}

public sealed record ParticipantCall(string NodeId, string InvocationId);
