using Elsa.Activities.Sequence.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Sequence.Internal;

/// <summary>
/// The Sequence's answer to the start-path rule (spec 238): every run begins at the first child, whichever trigger
/// started it, so a start trigger is valid inside a Sequence only through child 0.
/// </summary>
public sealed class SequenceStartEntryProvider : IStructuralStartEntryProvider
{
    public string StructureKind => SequenceActivity.StructureKind;

    /// <exception cref="InvalidOperationException">The Sequence's structure is malformed, so its order cannot be read.</exception>
    public ExecutableNode? SelectStartChild(ExecutableNode composite, string triggerNodeId)
    {
        ArgumentNullException.ThrowIfNull(composite);
        ArgumentException.ThrowIfNullOrWhiteSpace(triggerNodeId);

        try
        {
            return SequenceNavigator.From(composite).SelectFirst();
        }
        catch (SequenceExecutionException exception)
        {
            throw new InvalidOperationException($"Sequence '{composite.ExecutableNodeId}' has a malformed structure: {exception.Message}", exception);
        }
    }
}
