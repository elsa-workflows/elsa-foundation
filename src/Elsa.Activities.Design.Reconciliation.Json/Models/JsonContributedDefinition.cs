using Elsa.Activities.Design.Core.Contracts;

namespace Elsa.Activities.Design.Reconciliation.Json.Models;

/// <summary>
/// Read-only <see cref="IActivityDefinition"/> contribution produced by the JSON source.
/// The reconciler maps this to a persisted <c>ActivityDefinition</c>; the Id stays empty
/// so the reconciler generates one on first creation.
/// </summary>
internal sealed record JsonContributedDefinition(
    string ActivityTypeKey,
    string SourceKind,
    string SourceId,
    DateTimeOffset ProvisionedAt,
    string? ProvisionedBy,
    string Category,
    string? DisplayName,
    string? Description
) : IActivityDefinition
{
    public string Id => string.Empty;
}
