using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Events;

/// <summary>
/// Mutation event for an edge added to the activity graph. Published by
/// <c>IAddConnectionToDraftCommand</c>. Per Unit C FR-018 connections-graph bullet.
/// </summary>
/// <remarks>
/// Payload carries the full <see cref="ActivityConnection"/> (source + target endpoints)
/// rather than a phantom <c>IActivityPortConnectionView</c> — the record is sealed and
/// immutable by structure.
/// </remarks>
public sealed class OnConnectionAddedToDraft(string draftId, ActivityConnection connection) : IDomainEvent
{
    public string DraftId { get; } = draftId;
    public ActivityConnection Connection { get; } = connection;
}
