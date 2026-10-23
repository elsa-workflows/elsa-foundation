using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Events;

/// <summary>
/// Mutation event for an input updated on a placed activity. Published by
/// <c>IUpdateActivityInputInDraftCommand</c>. <see cref="InputReferenceKey"/> is the stable
/// identity (the argument-level <c>ReferenceKey</c> is unchanged).
/// </summary>
public sealed class ActivityInputUpdatedInDraft(
    string draftId,
    string nodeId,
    string inputReferenceKey,
    ArgumentState oldValue,
    ArgumentState newValue) : IEvent
{
    public string DraftId { get; } = draftId;
    public string NodeId { get; } = nodeId;
    public string InputReferenceKey { get; } = inputReferenceKey;
    public ArgumentState OldValue { get; } = oldValue;
    public ArgumentState NewValue { get; } = newValue;
}
