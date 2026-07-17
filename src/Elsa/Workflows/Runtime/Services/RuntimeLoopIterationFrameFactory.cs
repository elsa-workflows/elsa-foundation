using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>Creates one durable, isolated frame for a scheduled loop iteration.</summary>
public sealed class RuntimeLoopIterationFrameFactory
{
    private readonly VariableFrameFactory _frameFactory = new();

    public VariableFrameState Create(
        LoopIterationScopeRequest request,
        string bodyActivityExecutionId,
        VariableFrameState parent)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyActivityExecutionId);
        ArgumentNullException.ThrowIfNull(parent);

        return _frameFactory.CreateIteration(
            request.OwnerNodeId,
            bodyActivityExecutionId,
            request.IterationId,
            parent,
            request.Values);
    }
}
