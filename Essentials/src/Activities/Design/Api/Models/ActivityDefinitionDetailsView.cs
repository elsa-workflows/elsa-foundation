using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Api.Models;

public sealed record ActivityDefinitionDetailsView(
    ActivityDefinitionView Definition,
    IEnumerable<ActivityDefinitionVersionSummary> Versions
);
