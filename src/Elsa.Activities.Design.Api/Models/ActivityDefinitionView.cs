namespace Elsa.Activities.Design.Api.Models;

public sealed record ActivityDefinitionView(
    string Id,
    string ActivityTypeKey,
    string SourceKind,
    string SourceId,
    DateTimeOffset ReconciledAt,
    string? ReconciledBy,
    string Category,
    string? DisplayName,
    string? Description
);
