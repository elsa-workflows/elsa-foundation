using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class SequentialWorkflowExecutor(
    IActivityFactory activityFactory,
    IServiceProvider serviceProvider)
    : IWorkflowExecutor
{
    private const string InputTypeMetadataKey = "typeName";

    public async ValueTask<WorkflowExecutionResult> ExecuteAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executable);

        var workflowExecutionId = $"wfexec-{Guid.NewGuid():N}";
        var startedAt = DateTimeOffset.UtcNow;
        var activityResults = new List<ActivityExecutionResult>();

        try
        {
            var current = GetStartNode(executable);
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
            var inputs = CreateInputs(node);
            var activity = await activityFactory.Create(node.DescriptorType, node.DescriptorPayload, inputs.ToDictionary(x => x.Name, x => x.Argument, StringComparer.OrdinalIgnoreCase), outputs: null, cancellationToken);
            activity.NodeId = node.ExecutableNodeId;
            activity.Id = activityExecutionId;

            var context = new SimpleActivityExecutionContext(serviceProvider, activity, cancellationToken);
            SeedInputMemory(context, inputs);

            if (await activity.CanExecuteAsync(context))
                await activity.ExecuteAsync(context);

            return new ActivityExecutionResult(activityExecutionId, node.ExecutableNodeId, node.ActivityType, ActivityExecutionResultStatus.Completed, startedAt, DateTimeOffset.UtcNow, null);
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

    private static IReadOnlyList<MaterializedInput> CreateInputs(ExecutableNode node)
    {
        var inputs = new List<MaterializedInput>();

        foreach (var (inputName, binding) in node.InputBindings)
        {
            if (binding.Source != RuntimeInputBindingSource.Literal || !binding.LiteralValue.HasValue)
                throw new InvalidOperationException($"Input '{inputName}' on executable node '{node.ExecutableNodeId}' is not a supported literal binding.");

            if (!binding.Metadata.TryGetValue(InputTypeMetadataKey, out var typeName))
                throw new InvalidOperationException($"Input '{inputName}' on executable node '{node.ExecutableNodeId}' is missing '{InputTypeMetadataKey}' metadata.");

            var type = Type.GetType(typeName, throwOnError: true)!;
            var memoryReference = new LiteralMemoryBlockReference($"{node.ExecutableNodeId}:{inputName}");
            var argumentType = typeof(InputArgument<>).MakeGenericType(type);
            var argument = (InputArgument)Activator.CreateInstance(argumentType, memoryReference)!;
            var value = JsonSerializer.Deserialize(binding.LiteralValue.Value.GetRawText(), type);
            inputs.Add(new MaterializedInput(inputName, argument, value));
        }

        return inputs;
    }

    private static void SeedInputMemory(SimpleActivityExecutionContext context, IReadOnlyList<MaterializedInput> inputs)
    {
        foreach (var input in inputs)
            context.Set(input.Argument.MemoryBlockReference(), input.Value);
    }

    private sealed record MaterializedInput(string Name, InputArgument Argument, object? Value);

    private sealed class LiteralMemoryBlockReference(string id) : IMemoryBlockReference
    {
        public string Id { get; set; } = id;

        public IMemoryBlock Declare() => new LiteralMemoryBlock();

        public T? Get<T>(IMemoryRegister memoryRegister, IExpressionExecutionContext context)
        {
            var value = GetValue(memoryRegister);
            if (value is null)
                return default;

            if (value is T typed)
                return typed;

            return (T?)Convert.ChangeType(value, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
        }

        public object? Get(IExpressionExecutionContext context) => context.Get(this);

        public T? Get<T>(IExpressionExecutionContext context) => context.Get<T>(this);

        private object? GetValue(IMemoryRegister memoryRegister)
        {
            if (!memoryRegister.Blocks.TryGetValue(Id, out var block))
                block = memoryRegister.Declare(this);

            return block.Value;
        }
    }

    private sealed class LiteralMemoryBlock : IMemoryBlock
    {
        public object? Value { get; set; }
        public object? Metadata { get; set; }
    }
}
