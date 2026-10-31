using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Receives the consumed structure-owned inputs once on a fresh activity during ordinary activation.
/// Values come from the pinned snapshot after normal external-envelope dereferencing, not re-evaluation.
/// </summary>
public interface IRuntimeStructureInputConsumer
{
    void BindStructureInputs(IReadOnlyDictionary<string, ValueEnvelope> inputs);
}
