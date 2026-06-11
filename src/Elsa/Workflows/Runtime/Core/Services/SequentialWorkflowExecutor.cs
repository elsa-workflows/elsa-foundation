using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class SequentialWorkflowExecutor : IWorkflowExecutor
{
    private readonly IActivityFactory _activityFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly IRuntimeActivityInputMaterializer _inputMaterializer;

    public SequentialWorkflowExecutor(
        IActivityFactory activityFactory,
        IServiceProvider serviceProvider,
        IRuntimeActivityInputMaterializer inputMaterializer)
    {
        ArgumentNullException.ThrowIfNull(activityFactory);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(inputMaterializer);

        _activityFactory = activityFactory;
        _serviceProvider = serviceProvider;
        _inputMaterializer = inputMaterializer;
    }

    public async ValueTask<WorkflowExecutionResult> ExecuteAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executable);

        var workflowExecutionId = $"wfexec-{Guid.NewGuid():N}";
        var startedAt = DateTimeOffset.UtcNow;
        var activityResults = new List<ActivityExecutionResult>();

        try
        {
            ExecutableNode? current = GetStartNode(executable);
            var visited = new HashSet<string>(StringComparer.Ordinal);

            while (current is not null)
            {
                if (!visited.Add(current.ExecutableNodeId))
                    throw new InvalidOperationException($"Sequential execution detected a cycle at executable node '{current.ExecutableNodeId}'.");

                var activityResult = await ExecuteActivity(workflowExecutionId, current, cancellationToken);
                activityResults.Add(activityResult);

                if (activityResult.Status == ActivityExecutionResultStatus.Faulted)
                    return new WorkflowExecutionResult(workflowExecutionId, executable.Identity.ArtifactId, WorkflowExecutionResultStatus.Faulted, startedAt, DateTimeOffset.UtcNow, activityResults, activityResult.Error);

                current = GetNextNode(executable, current);
            }

            return new WorkflowExecutionResult(workflowExecutionId, executable.Identity.ArtifactId, WorkflowExecutionResultStatus.Completed, startedAt, DateTimeOffset.UtcNow, activityResults, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            return new WorkflowExecutionResult(workflowExecutionId, executable.Identity.ArtifactId, WorkflowExecutionResultStatus.Faulted, startedAt, DateTimeOffset.UtcNow, activityResults, e.Message);
        }
    }

    private async ValueTask<ActivityExecutionResult> ExecuteActivity(string workflowExecutionId, ExecutableNode node, CancellationToken cancellationToken)
    {
        var activityExecutionId = $"actexec-{Guid.NewGuid():N}";
        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            var inputs = _inputMaterializer.MaterializeInputs(node);
            var activity = await _activityFactory.Create(node.DescriptorType, node.DescriptorPayload, inputs.ToDictionary(x => x.Name, x => x.Argument, StringComparer.OrdinalIgnoreCase), outputs: null, cancellationToken);
            activity.NodeId = node.ExecutableNodeId;
            activity.Id = activityExecutionId;

            var context = new SimpleActivityExecutionContext(_serviceProvider, activity, cancellationToken);
            RuntimeActivityInputMemory.Seed(context, inputs);

            if (!await activity.CanExecuteAsync(context))
                return new ActivityExecutionResult(activityExecutionId, node.ExecutableNodeId, node.ActivityType, ActivityExecutionResultStatus.Skipped, startedAt, DateTimeOffset.UtcNow, null);

            await activity.ExecuteAsync(context);

            return new ActivityExecutionResult(activityExecutionId, node.ExecutableNodeId, node.ActivityType, ActivityExecutionResultStatus.Completed, startedAt, DateTimeOffset.UtcNow, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            return new ActivityExecutionResult(activityExecutionId, node.ExecutableNodeId, node.ActivityType, ActivityExecutionResultStatus.Faulted, startedAt, DateTimeOffset.UtcNow, e.Message);
        }
    }

    private static ExecutableNode GetStartNode(WorkflowExecutable executable)
    {
        if (executable.StartNodeIds.Count != 1)
            throw new InvalidOperationException($"Sequential execution requires exactly one start node, but artifact '{executable.Identity.ArtifactId}' has {executable.StartNodeIds.Count}.");

        return executable.NodesById[executable.StartNodeIds.Single()];
    }

    private static ExecutableNode? GetNextNode(WorkflowExecutable executable, ExecutableNode current)
    {
        var outgoing = executable.Edges.Where(edge => edge.SourceNodeId == current.ExecutableNodeId).ToArray();

        if (outgoing.Length > 1)
            throw new InvalidOperationException($"Sequential execution does not support fan-out from executable node '{current.ExecutableNodeId}'.");

        if (outgoing.Length == 0)
            return null;

        return executable.NodesById[outgoing[0].TargetNodeId];
    }

}
