using Elsa.Activities.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Models;

/// <summary>
/// Current authored workflow context supplied to a design-time activity input options provider.
/// </summary>
public sealed record ActivityInputOptionsContext(
    WorkflowDefinitionState WorkflowState,
    ActivityNode Activity,
    InputDefinition Input);
