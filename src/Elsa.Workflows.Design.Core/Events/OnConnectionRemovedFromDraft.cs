using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Events;

/// <summary>
/// Mutation event for an edge removed from the activity graph. Published by
/// <c>IRemoveConnectionFromDraftCommand</c>. Per Unit C FR-018 connections-graph bullet.
/// </summary>
/// <remarks>
/// Payload carries the full <see cref="ActivityConnection"/> that was removed (sealed record;
/// no separate <c>ConnectionId</c> concept exists on the connection — its identity IS the
/// source+target pair). The spec mentioned a <c>ConnectionId : string</c> field; Unit C
/// elects to pass the full edge so subscribers know which connection vanished.
/// </remarks>
public sealed class OnConnectionRemovedFromDraft(string draftId, ActivityConnection connection) : IDomainEvent
{
    public string DraftId { get; } = draftId;
    public ActivityConnection Connection { get; } = connection;
}
