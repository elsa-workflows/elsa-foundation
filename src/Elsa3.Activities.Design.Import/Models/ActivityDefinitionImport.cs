using Elsa.Activities.Design.Core.Contracts;

namespace Elsa3.Mapping.Models;

public sealed record ActivityDefinitionImport(
    string Id,
    string UniqueName,
    string Category,
    string? DisplayName,
    string? Description,
    bool IsBrowsable    
) 
: IActivityDefinition;