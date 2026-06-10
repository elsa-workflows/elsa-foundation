using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
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
        _executor = new SequentialWorkflowExecutor(_factory, new EmptyServiceProvider());
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

    private static ExecutableNode Node(string id, string text) =>
        new(
            executableNodeId: id,
            authoredActivityId: id,
            activityType: typeof(RecordingTextActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "Fake",
            descriptorPayload: JsonSerializer.SerializeToElement(new { id }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Text"] = new(
                    inputName: "Text",
                    source: RuntimeInputBindingSource.Literal,
                    literalValue: JsonSerializer.SerializeToElement(text),
                    metadata: new Dictionary<string, string> { ["typeName"] = typeof(string).AssemblyQualifiedName! })
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
            var activity = new RecordingTextActivity(messages)
            {
                Text = (InputArgument<string>)inputs!["Text"]
            };

            return ValueTask.FromResult<IActivity>(activity);
        }
    }

    private sealed class RecordingTextActivity(List<string?> messages) : ActivityBase
    {
        public InputArgument<string> Text { get; set; } = null!;

        protected override void Execute(IActivityExecutionContext context)
        {
            messages.Add(context.Get(Text));
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
