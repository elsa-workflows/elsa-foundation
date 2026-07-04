using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Api.Models;

public sealed record WorkflowDefinitionDetailsView(
    WorkflowDefinitionView Definition,
    WorkflowDefinitionStateView? Draft,
    IEnumerable<WorkflowDefinitionVersionSummary> Versions
);
