using Elsa.Activities.Design.Core.Contracts;

namespace Elsa3.Mapping.Models;

public sealed record ActivityDefinitionImport(
    string Id,
    string ActivityTypeKey,
    string SourceKind,
    string SourceId,
    DateTimeOffset ReconciledAt,
    string? ReconciledBy,
    string Category,
    string? DisplayName,
    string? Description
)
: IActivityDefinition;
