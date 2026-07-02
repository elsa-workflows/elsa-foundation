namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Ambient access to the runtime pipeline context of the dispatch currently on the call stack. The dispatcher pushes
/// the context before invoking the pipeline so the terminal scheduler work handler — which the pipeline invokes as a
/// bare delegate and cannot receive the context by parameter — can stage phase results on the shared workspace during
/// the Move 2 transition. Mirrors <see cref="IWorkflowExecutionAmbientServicesAccessor"/>.
/// </summary>
public interface IRuntimePipelineContextAccessor
{
    /// <summary>The context of the dispatch currently executing on this async flow, or null when none is active.</summary>
    IRuntimePipelineContext? Current { get; }

    /// <summary>Pushes <paramref name="context"/> as the current context until the returned scope is disposed.</summary>
    IDisposable Push(IRuntimePipelineContext context);
}
