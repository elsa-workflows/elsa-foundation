namespace Elsa.Workflows.Design.Core.Models;

/// <summary>Explicit workflow value-boundary choices. Native workflows leave both choices off.</summary>
public sealed class WorkflowNullHandling
{
    public bool DefaultOnNull { get; set; }
    public bool UndefinedAsNull { get; set; }
}
