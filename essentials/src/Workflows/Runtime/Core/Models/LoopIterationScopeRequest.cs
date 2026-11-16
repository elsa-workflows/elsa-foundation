using System.Collections.ObjectModel;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Typed values a loop owner publishes when scheduling one body iteration. The request travels in
/// the scheduler command and is materialized as a durable <see cref="VariableFrameState"/>; values
/// never travel through scheduling metadata.
/// </summary>
/// <remarks>
/// Variable keys are stable authored identities. The concrete iteration id supplies activation
/// identity, so parallel or repeated iterations cannot share mutable storage.
/// </remarks>
public sealed record LoopIterationScopeRequest
{
    public LoopIterationScopeRequest(
        string ownerNodeId,
        string iterationId,
        IReadOnlyDictionary<string, ValueEnvelope> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(iterationId);
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
            throw new ArgumentException("An iteration frame must contain at least one value.", nameof(values));
        if (values.Keys.Any(string.IsNullOrWhiteSpace) || values.Values.Any(value => value is null))
            throw new ArgumentException("Iteration frame keys and envelopes cannot be blank or null.", nameof(values));

        OwnerNodeId = ownerNodeId;
        IterationId = iterationId;
        Values = new ReadOnlyDictionary<string, ValueEnvelope>(
            values.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
    }

    /// <summary>Executable lexical scope declared by the loop owner.</summary>
    public string OwnerNodeId { get; }

    /// <summary>Concrete iteration activation identity.</summary>
    public string IterationId { get; }

    /// <summary>Stable variable key to protected value envelope.</summary>
    public IReadOnlyDictionary<string, ValueEnvelope> Values { get; }
}
