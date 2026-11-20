namespace Elsa.Workflows.Runtime.Core.Middleware;

/// <summary>
/// A module's request to place one workflow runtime middleware into the workflow execution pipeline. Collected from DI
/// and applied to the pipeline builder when the pipeline is composed.
/// </summary>
public sealed record WorkflowRuntimeMiddlewareContribution(Type MiddlewareType, string Slot, int Order, string? Name);

/// <summary>
/// A module's request to place one activity runtime middleware into the activity execution pipeline. Collected from DI
/// and applied to the pipeline builder when the pipeline is composed.
/// </summary>
public sealed record ActivityRuntimeMiddlewareContribution(Type MiddlewareType, string Slot, int Order, string? Name);
