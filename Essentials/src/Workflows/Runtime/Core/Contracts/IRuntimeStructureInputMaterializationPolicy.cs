using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Keyed replacement contract: exactly one policy owns each structure kind/schema pair.
/// Selects its structure-owned inputs before the invocation's immutable input snapshot is committed.
/// Implementations await each request and must not resolve services or evaluate expressions themselves.
/// </summary>
public interface IRuntimeStructureInputMaterializationPolicy
{
    string Kind { get; }
    string SchemaVersion { get; }
    ValueTask MaterializeAsync(IRuntimeStructureInputMaterializationContext context, CancellationToken cancellationToken);
}

/// <summary>Constrained access to the current node's ordinary, typed input materialization.</summary>
public interface IRuntimeStructureInputMaterializationContext
{
    ExecutableNode Node { get; }

    /// <summary>
    /// Materializes a declared input once, recording its durable envelope in the pending snapshot.
    /// Returns a readable transient view for selection, preserving protection/withheld-value rules.
    /// A later activation consumes the committed values without evaluating the binding again.
    /// </summary>
    ValueTask<ValueEnvelope> MaterializeAsync(string inputKey, CancellationToken cancellationToken);
}
