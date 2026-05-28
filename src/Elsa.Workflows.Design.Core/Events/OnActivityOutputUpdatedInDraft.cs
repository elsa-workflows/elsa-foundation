using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Events;

/// <summary>
/// Mutation event for an output updated on a placed activity. Published by
/// <c>IUpdateActivityOutputInDraftCommand</c>. <see cref="OutputReferenceKey"/> is the stable
/// identity per Unit C FR-010.
/// </summary>
public sealed class OnActivityOutputUpdatedInDraft(
    string draftId,
    string nodeId,
    string outputReferenceKey,
    ArgumentState oldValue,
    ArgumentState newValue) : IDomainEvent
{
    public string DraftId { get; } = draftId;
    public string NodeId { get; } = nodeId;
    public string OutputReferenceKey { get; } = outputReferenceKey;
    public ArgumentState OldValue { get; } = oldValue;
    public ArgumentState NewValue { get; } = newValue;
}
