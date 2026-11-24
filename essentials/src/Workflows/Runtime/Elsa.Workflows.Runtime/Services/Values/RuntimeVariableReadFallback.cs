using Elsa.Serialization.Core;
using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Workflows.Runtime.Services.Values;

/// <summary>
/// The run-time lookup behind an input expression's computed and opaque variable reads (spec 1222 FR-008 to FR-011,
/// FR-017 to FR-020, research R-4). Before evaluation it makes the visible variables readable (Q5: the script engine
/// reads synchronously and values may be stored externally). Each read resolves nearest scope first and is recorded
/// here, on the host side: a present read joins the input read set and taints the result with the variable's policy; a
/// name that matches nothing is recorded absent, with a warning; a withheld or transient match is refused and faults
/// the step even if the script catches the error (FR-017a). A visible variable the script never reads is not recorded
/// and does not block it, even when it is withheld.
/// </summary>
internal sealed class RuntimeVariableReadFallback : IExpressionVariableReader
{
    private readonly IReadOnlyDictionary<string, RuntimeReadableVariable> _variables;
    private readonly IReadOnlyDictionary<string, JsonElement> _values;
    private readonly List<RuntimeVariableComputedRead> _reads = [];
    private readonly List<RuntimeVariableReadRefusal> _refusals = [];

    private RuntimeVariableReadFallback(
        IReadOnlyList<RuntimeReadableVariable> variables,
        IReadOnlyDictionary<string, JsonElement> values)
    {
        _variables = variables.ToDictionary(variable => variable.Name, StringComparer.Ordinal);
        _values = values;
        CandidateNames = variables.Select(variable => variable.Name).ToArray();
    }

    public IReadOnlyList<string> CandidateNames { get; }

    /// <summary>Every read served, in order, each name once.</summary>
    public IReadOnlyList<RuntimeVariableComputedRead> Reads => _reads;

    /// <summary>The reads that were refused, in order.</summary>
    public IReadOnlyList<RuntimeVariableReadRefusal> Refusals => _refusals;

    /// <summary>Loads the readable values of the visible variables: durable, present and not withheld.</summary>
    public static async ValueTask<RuntimeVariableReadFallback> LoadAsync(
        IReadOnlyList<RuntimeReadableVariable> visibleVariables,
        IExternalPayloadStore? externalPayloadStore,
        CancellationToken cancellationToken, IWellKnownTypeRegistry? registry = null,
        IRuntimeTypeNullability? typeDescriptors = null)
    {
        ArgumentNullException.ThrowIfNull(visibleVariables);
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var variable in visibleVariables)
        {
            var envelope = variable.Envelope;
            if (envelope.Presence == ValuePresence.Withheld || envelope.Policy.Lifecycle == DurableValueLifecycle.None)
                continue;
            if (envelope.Presence == ValuePresence.Absent &&
                !NullPolicyDefaults.AcceptsNull(envelope.Type.ToTypeReference(), registry, typeDescriptors) &&
                NullPolicyDefaults.TryGetTypeDefault(envelope.Type.Alias, registry, out var typeDefault))
                values[variable.Name] = JsonSerializer.SerializeToElement(typeDefault, typeDefault!.GetType(), Elsa.Serialization.Core.JsonBaseline.Options);
            else if (envelope.Presence is ValuePresence.Absent or ValuePresence.ExplicitNull)
                values[variable.Name] = JsonSerializer.SerializeToElement<object?>(null, Elsa.Serialization.Core.JsonBaseline.Options);
            else if (envelope.InlineValue.HasValue)
                values[variable.Name] = envelope.InlineValue.Value.Clone();
            else if (envelope.ExternalReference is not null && externalPayloadStore is not null)
            {
                // A payload that cannot be read fails only a script that reads that variable; an unread candidate
                // never blocks the step.
                try
                {
                    values[variable.Name] = await externalPayloadStore.ReadAsync(envelope.ExternalReference, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                }
            }
        }

        return new RuntimeVariableReadFallback(visibleVariables, values);
    }

    public ExpressionVariableRead Read(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!_variables.TryGetValue(name, out var variable))
        {
            Record(new RuntimeVariableComputedRead(name, null));
            return ExpressionVariableRead.Missing;
        }

        Record(new RuntimeVariableComputedRead(name, variable));
        if (variable.Envelope.Presence == ValuePresence.Withheld)
            return Refuse(new RuntimeVariableReadRefusal(variable, RuntimeVariableReadRefusalKind.Withheld));
        if (variable.Envelope.Policy.Lifecycle == DurableValueLifecycle.None)
            return Refuse(new RuntimeVariableReadRefusal(variable, RuntimeVariableReadRefusalKind.Transient));
        if (!_values.TryGetValue(name, out var value))
            return Refuse(new RuntimeVariableReadRefusal(variable, RuntimeVariableReadRefusalKind.Unreadable));

        return ExpressionVariableRead.Present(value);
    }

    /// <summary>
    /// Takes the reads into the step's input: each present read into the input read set (spec 1221, FR-010) and every
    /// read, present or absent, into the recorded computed reads of the input snapshot. Returns the protection policy of
    /// <paramref name="policy"/> combined with every variable actually read.
    /// </summary>
    public ValueProtectionPolicy Commit(
        RuntimeVariableReadRecorder? recorder,
        ValueProtectionPolicy policy,
        string nodeId,
        string inputName)
    {
        foreach (var read in _reads)
        {
            recorder?.RecordComputed(read);
            if (read.Variable is not { } variable)
                continue;

            recorder?.Record(variable.Envelope);
            policy = ValuePolicyCombiner.Combine(
                policy,
                variable.Envelope.Policy,
                $"Computed variable read on portable expression input '{inputName}' on executable node '{nodeId}'");
        }

        return policy;
    }

    /// <summary>Faults the step for the first refused read, naming the node and the variable, never its value.</summary>
    public void ThrowIfRefused(string nodeId, string inputName)
    {
        if (_refusals.Count == 0)
            return;

        var refusal = _refusals[0];
        if (refusal.Kind == RuntimeVariableReadRefusalKind.Withheld)
            throw SecretBindingDiagnostics.WithheldVariableNotResolved(refusal.Variable.Name);

        throw new RuntimeVariableReadRefusedException(refusal.Describe(nodeId, inputName));
    }

    private void Record(RuntimeVariableComputedRead read)
    {
        if (!_reads.Any(existing => StringComparer.Ordinal.Equals(existing.Name, read.Name)))
            _reads.Add(read);
    }

    private ExpressionVariableRead Refuse(RuntimeVariableReadRefusal refusal)
    {
        if (!_refusals.Any(existing => ReferenceEquals(existing.Variable, refusal.Variable)))
            _refusals.Add(refusal);
        return ExpressionVariableRead.Refused(refusal.Describe(nodeId: null, inputName: null));
    }
}

/// <summary>Why a computed read was refused.</summary>
internal enum RuntimeVariableReadRefusalKind
{
    /// <summary>The value is withheld from this execution context (FR-020).</summary>
    Withheld,

    /// <summary>The variable is transient, so its value cannot enter the input snapshot (ADR 0089 R3, FR-017).</summary>
    Transient,

    /// <summary>The value is stored externally and could not be read (no payload reader, or the read failed).</summary>
    Unreadable
}

internal sealed record RuntimeVariableReadRefusal(RuntimeReadableVariable Variable, RuntimeVariableReadRefusalKind Kind)
{
    public string Describe(string? nodeId, string? inputName)
    {
        var where = nodeId is null ? "" : $" on executable node '{nodeId}' (input '{inputName}')";
        return Kind switch
        {
            RuntimeVariableReadRefusalKind.Withheld =>
                $"A script{where} read variable '{Variable.Name}' by a computed name, but its value is withheld from this execution context.",
            RuntimeVariableReadRefusalKind.Transient =>
                $"A script{where} read variable '{Variable.Name}' by a computed name, but the variable is transient: its value cannot enter the step's input snapshot (ADR 0089 R3).",
            _ =>
                $"A script{where} read variable '{Variable.Name}' by a computed name, but its externally stored value could not be read."
        };
    }
}

/// <summary>A computed or opaque variable read was refused; the message names the node and the variable.</summary>
public sealed class RuntimeVariableReadRefusedException(string message) : InvalidOperationException(message);
