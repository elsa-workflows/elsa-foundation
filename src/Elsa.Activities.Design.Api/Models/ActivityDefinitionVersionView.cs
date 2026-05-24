using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Api.Models;

public record ActivityDefinitionVersionView(
    string Id,
    int Version, 
    ActivityKind Kind,
    DateTimeOffset CreatedAt
);
