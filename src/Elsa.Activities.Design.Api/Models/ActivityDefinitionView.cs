namespace Elsa.Activities.Design.Api.Models;

public sealed record ActivityDefinitionView(
    string Id,
    string UniqueName,
    string Category,
    string? DisplayName,
    string? Description,
    bool IsBrowsable
);
