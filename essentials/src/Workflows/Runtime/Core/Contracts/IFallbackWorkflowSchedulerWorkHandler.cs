namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Marks a scheduler work handler as a fallback that runs after ordinary handlers.
/// </summary>
public interface IFallbackWorkflowSchedulerWorkHandler : IWorkflowSchedulerWorkHandler
{
}
