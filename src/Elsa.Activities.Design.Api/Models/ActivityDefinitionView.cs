namespace Elsa.Activities.Design.Api.Models;

public sealed record ActivityDefinitionView(
    string Id,
    string ActivityTypeKey,
    string SourceKind,
    string SourceId,
    DateTimeOffset ProvisionedAt,
    string? ProvisionedBy,
    string Category,
    string? DisplayName,
    string? Description
);
