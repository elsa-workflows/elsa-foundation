using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.Core.Options;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class SequentialWorkflowExecutorTests
{
    private readonly List<string?> _messages = [];
    private readonly FakeActivityFactory _factory;
    private readonly SequentialWorkflowExecutor _executor;

    public SequentialWorkflowExecutorTests()
    {
        _factory = new FakeActivityFactory(_messages);
        _executor = new SequentialWorkflowExecutor(_factory, new EmptyServiceProvider(), new RuntimeActivityInputMaterializer());
    }

    [Fact]
    public async Task ExecutesSequentialLiteralInputsInGraphOrder()
    {
        var executable = Executable(
            [
                Node("write-one", "one"),
                Node("write-two", "two")
            ],
            [new ExecutableEdge("write-one", "Done", "write-two", "In")],
            ["write-one"]);

        var result = await _executor.ExecuteAsync(executable);

        Assert.Equal(WorkflowExecutionResultStatus.Completed, result.Status);
        Assert.Collection(
            _messages,
            message => Assert.Equal("one", message),
            message => Assert.Equal("two", message));
        Assert.Collection(
            result.Activities.Select(x => x.ExecutableNodeId),
            nodeId => Assert.Equal("write-one", nodeId),
            nodeId => Assert.Equal("write-two", nodeId));
        Assert.All(result.Activities, activity => Assert.Equal(ActivityExecutionResultStatus.Completed, activity.Status));
    }

    [Fact]
    public async Task FaultsWhenArtifactHasFanOut()
    {
        var executable = Executable(
            [
                Node("write-one", "one"),
                Node("write-two", "two"),
                Node("write-three", "three")
            ],
            [
                new ExecutableEdge("write-one", "A", "write-two", "In"),
                new ExecutableEdge("write-one", "B", "write-three", "In")
            ],
            ["write-one"]);

        var result = await _executor.ExecuteAsync(executable);

        Assert.Equal(WorkflowExecutionResultStatus.Faulted, result.Status);
        Assert.Contains("fan-out", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FaultsWhenArtifactDoesNotHaveExactlyOneStartNode()
    {
        var executable = Executable(
            [Node("write-one", "one")],
            [],
            []);

        var result = await _executor.ExecuteAsync(executable);

        Assert.Equal(WorkflowExecutionResultStatus.Faulted, result.Status);
        Assert.Contains("exactly one start", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReportsSkippedActivityWhenCanExecuteReturnsFalse()
    {
        var executable = Executable(
            [Node("write-one", "one", canExecute: false)],
            [],
            ["write-one"]);

        var result = await _executor.ExecuteAsync(executable);

        Assert.Equal(WorkflowExecutionResultStatus.Completed, result.Status);
        Assert.Empty(_messages);
        var activity = Assert.Single(result.Activities);
        Assert.Equal(ActivityExecutionResultStatus.Skipped, activity.Status);
    }

    [Fact]
    public async Task PropagatesCancellationDuringActivityExecution()
    {
        var executor = new SequentialWorkflowExecutor(new CancelingActivityFactory(), new EmptyServiceProvider(), new RuntimeActivityInputMaterializer());
        var executable = Executable([Node("write-one", "one")], [], ["write-one"]);

        await Assert.ThrowsAsync<OperationCanceledException>(() => executor.ExecuteAsync(executable).AsTask());
    }

    [Fact]
    public async Task LiteralMemoryBlockReferenceUsesObjectConverterForTypedReads()
    {
        var converter = new RecordingObjectConverter();
        var executor = new SequentialWorkflowExecutor(_factory, new SingleServiceProvider(converter), new RuntimeActivityInputMaterializer());
        var executable = Executable(
            [Node("write-one", "one", readFromReference: true)],
            [],
            ["write-one"]);

        var result = await executor.ExecuteAsync(executable);

        Assert.Equal(WorkflowExecutionResultStatus.Completed, result.Status);
        Assert.Equal(typeof(string), converter.LastTargetType);
        Assert.Collection(_messages, message => Assert.Equal("one", message));
    }

    private static WorkflowExecutable Executable(
        IReadOnlyCollection<ExecutableNode> nodes,
        IReadOnlyCollection<ExecutableEdge> edges,
        IReadOnlyCollection<string> startNodeIds) =>
        new(
            identity: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            nodes: nodes,
            edges: edges,
            startNodeIds: startNodeIds,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UtcNow,
            publishedAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>());

    private static ExecutableNode Node(string id, string text, bool canExecute = true, bool readFromReference = false) =>
        new(
            executableNodeId: id,
            authoredActivityId: id,
            activityType: typeof(RecordingTextActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "Fake",
            descriptorPayload: JsonSerializer.SerializeToElement(new { id, canExecute, readFromReference }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Text"] = new(
                    inputName: "Text",
                    source: RuntimeInputBindingSource.Literal,
                    literalValue: JsonSerializer.SerializeToElement(text),
                    metadata: new Dictionary<string, string> { ["typeName"] = $"{typeof(string).FullName}, {typeof(string).Assembly.GetName().Name}" })
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

    private sealed class FakeActivityFactory(List<string?> messages) : IActivityFactory
    {
        public ValueTask<IActivity> Create(
            string descriptorType,
            JsonElement payload,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken = default)
        {
            var canExecute = payload.GetProperty("canExecute").GetBoolean();
            var readFromReference = payload.GetProperty("readFromReference").GetBoolean();
            var activity = new RecordingTextActivity(messages)
            {
                Text = (InputArgument<string>)inputs!["Text"],
                ShouldExecute = canExecute,
                ReadFromReference = readFromReference
            };

            return ValueTask.FromResult<IActivity>(activity);
        }
    }

    private sealed class CancelingActivityFactory : IActivityFactory
    {
        public ValueTask<IActivity> Create(
            string descriptorType,
            JsonElement payload,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActivity>(new CancelingActivity());
    }

    private sealed class RecordingTextActivity(List<string?> messages) : ActivityBase
    {
        public InputArgument<string> Text { get; set; } = null!;
        public bool ShouldExecute { get; set; } = true;
        public bool ReadFromReference { get; set; }

        protected override ValueTask<bool> CanExecuteAsync(IActivityExecutionContext context)
        {
            return ValueTask.FromResult(ShouldExecute);
        }

        protected override void Execute(IActivityExecutionContext context)
        {
            var expressionContext = context.ExpressionExecutionContext;
            var text = ReadFromReference
                ? Text.MemoryBlockReference().Get<string>(expressionContext.Memory, expressionContext)
                : context.Get(Text);

            messages.Add(text);
        }
    }

    private sealed class CancelingActivity : ActivityBase
    {
        protected override void Execute(IActivityExecutionContext context) =>
            throw new OperationCanceledException(context.CancellationToken);
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class SingleServiceProvider(object service) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType.IsInstanceOfType(service) ? service : null;
    }

    private sealed class RecordingObjectConverter : IObjectConverter
    {
        public Type? LastTargetType { get; private set; }

        public T? ConvertTo<T>(object? value, ObjectConverterOptions? converterOptions = null)
        {
            LastTargetType = typeof(T);
            return (T?)value;
        }

        public object? ConvertTo(object? value, Type targetType, ObjectConverterOptions? converterOptions = null)
        {
            LastTargetType = targetType;
            return value;
        }

        public Result TryConvertTo(object? value, Type targetType, ObjectConverterOptions? converterOptions = null)
        {
            LastTargetType = targetType;
            return new(true, value, null);
        }
    }
}
