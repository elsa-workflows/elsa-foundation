namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Async-flow scoped access to non-durable services supplied by an in-process request-affine dispatch.
/// </summary>
public interface IWorkflowExecutionAmbientServicesAccessor
{
    IServiceProvider? Current { get; }

    IDisposable Push(IServiceProvider? services);
}
