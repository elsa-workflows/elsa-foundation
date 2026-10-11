using Elsa.Activities.Design.Core.Contracts;

namespace Elsa3.Mapping.Models;

public sealed record ActivityDefinitionImport(
    string Id,
    string ActivityTypeKey,
    string SourceKind,
    string SourceId,
    string Category,
    string? DisplayName,
    string? Description
)
: IActivityDefinition;
