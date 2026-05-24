namespace Elsa.Activities.Design.Api.Models;

public sealed record ActivityDefinitionDetailsView(
    ActivityDefinitionView Definition,
    IEnumerable<ActivityDefinitionVersionView> Versions
);
