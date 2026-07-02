using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Workflows.Runtime.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeWorkflowExecutionContextTests
{
    [Fact]
    public void ExposesWorkflowIdentityFromPinnedExecutableState()
    {
        var context = new WorkflowExecutionContext(NewState());

        Assert.Equal("definition-1", context.WorkflowDefinitionId);
        Assert.Equal("version-7", context.WorkflowDefinitionVersionId);
        Assert.Equal(7, context.WorkflowDefinitionVersion);
        Assert.Equal("wfexec-1", context.InstanceId);
        Assert.Equal("order-123", context.CorrelationId);
    }

    [Fact]
    public void AllowsRuntimeCorrelationNameAndVariablesToBeUpdated()
    {
        var context = new WorkflowExecutionContext(
            NewState(),
            variables: new Dictionary<string, object?> { ["status"] = "pending" },
            name: "initial");

        context.CorrelationId = "order-456";
        context.Name = "updated";
        context.SetVariable("status", "approved");
        context.SetVariable("createdByScript", 42);

        Assert.Equal("order-456", context.CorrelationId);
        Assert.Equal("updated", context.Name);
        Assert.Equal("approved", context.GetVariable("status"));
        Assert.Equal(42, context.GetVariable("createdByScript"));
        Assert.Equal(["createdByScript", "status"], context.GetVariables().Select(variable => variable.Name).Order());
    }

    [Fact]
    public void ResolvesWorkflowInputsAndActiveActivityOutputs()
    {
        var input = new InputArgument<string>(new TestMemoryBlockReference("input:customer"));
        var context = new WorkflowExecutionContext(
            NewState(),
            workflowInputs: new Dictionary<string, InputArgument> { ["customer"] = input },
            workflowInputValues: new Dictionary<string, object?> { ["customer"] = "northwind" },
            activityOutputs:
            [
                new WorkflowExecutionContextActivityOutput("actexec-fetch", "Fetch Customer", "customerId", "customer-1"),
                new WorkflowExecutionContextActivityOutput("actexec-score", "Score Customer", "score", 93)
            ],
            lastActivityResult: "done");

        Assert.Equal("northwind", context.GetInput("customer"));
        Assert.Same(input, context.GetWorkflowInputs()["customer"]);
        Assert.Equal("customer-1", context.GetOutput("customerId", "actexec-fetch"));
        Assert.Equal("customer-1", context.GetOutput("actexec-fetch", "customerId"));
        Assert.Equal(93, context.GetOutput("score", "Score Customer"));
        Assert.Equal("done", context.GetLastActivityResult());
    }

    [Fact]
    public void MissingRuntimeValuesFailDeterministically()
    {
        var context = new WorkflowExecutionContext(NewState());

        Assert.Contains("Workflow input 'missing'", Assert.Throws<InvalidOperationException>(() => context.GetInput("missing")).Message);
        Assert.Contains("Workflow variable 'missing'", Assert.Throws<InvalidOperationException>(() => context.GetVariable("missing")).Message);
        Assert.Contains("Activity output 'missing'", Assert.Throws<InvalidOperationException>(() => context.GetOutput("missing")).Message);
    }

    [Fact]
    public void RejectsNonNumericDefinitionVersionMetadata()
    {
        var state = NewState(systemMetadata: new Dictionary<string, string> { [WorkflowExecutionContext.DefinitionVersionMetadataKey] = "draft" });
        var context = new WorkflowExecutionContext(state);

        Assert.Contains("must be numeric", Assert.Throws<InvalidOperationException>(() => context.WorkflowDefinitionVersion).Message);
    }

    [Fact]
    public void ResolvesActivityContextForExpressionOnlyWhenMapped()
    {
        var expressionContext = new TestExpressionExecutionContext();
        var activityContext = new TestActivityExecutionContext(expressionContext);
        var context = new WorkflowExecutionContext(
            NewState(),
            activityContexts: new Dictionary<IExpressionExecutionContext, IActivityExecutionContext>
            {
                [expressionContext] = activityContext
            });

        Assert.Same(activityContext, context.GetActivityContextForExpression(expressionContext));
        Assert.Contains("not associated", Assert.Throws<InvalidOperationException>(() => context.GetActivityContextForExpression(new TestExpressionExecutionContext())).Message);
    }

    private static WorkflowExecutionState NewState(IReadOnlyDictionary<string, string>? systemMetadata = null) =>
        new(
            WorkflowExecutionId: "wfexec-1",
            PinnedExecutable: new WorkflowExecutableIdentity(
                ArtifactId: "artifact-1",
                DefinitionId: "definition-1",
                DefinitionVersionId: "version-7",
                ArtifactVersion: "7.0.0",
                ArtifactHash: "sha256:test"),
            Status: WorkflowExecutionStatus.Running,
            SubStatus: null,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            UpdatedAt: null,
            CompletedAt: null,
            CorrelationId: "order-123",
            ParentWorkflowExecutionId: null,
            TenantId: null,
            SystemMetadata: systemMetadata ?? new Dictionary<string, string>());

    private sealed class TestMemoryBlockReference(string id) : IMemoryBlockReference
    {
        public string Id { get; set; } = id;
        public IMemoryBlock Declare() => new TestMemoryBlock();
        public T? Get<T>(IMemoryRegister memoryRegister, IExpressionExecutionContext context) => default;
        public T? Get<T>(IExpressionExecutionContext context) => default;
    }

    private sealed class TestMemoryBlock : IMemoryBlock
    {
        public object? Value { get; set; }
        public object? Metadata { get; set; }
    }

    private sealed class TestExpressionExecutionContext : IExpressionExecutionContext
    {
        public IMemoryRegister Memory { get; } = new TestMemoryRegister();
        public IExpressionExecutionContext? ParentContext { get; set; }
        public CancellationToken CancellationToken => CancellationToken.None;
        public bool IsContainedWithinCompositeActivity() => false;
        public bool TryGetActivityInput(string key, out object? value) => Missing(out value);
        public bool TryGetWorkflowInput(string key, out object? value) => Missing(out value);
        public object? GetVariableValueOrDefault(string variableName) => null;
        public string GetCorrelationId() => string.Empty;
        public string GetWorkflowDefinitionId() => string.Empty;
        public string GetWorkflowDefinitionVersionId() => string.Empty;
        public int GetWorkflowDefinitionVersion() => 0;
        public string GetWorkflowInstanceId() => string.Empty;
        public object? GetRequiredService(Type type) => null;
        public IMemoryBlock GetBlock(IMemoryBlockReference blockReference) => Memory.Declare(blockReference);
        public bool TryGetBlock(IMemoryBlockReference blockReference, out IMemoryBlock block) => Memory.TryGetBlock(blockReference.Id, out block);
        public T? Get<T>(IMemoryBlockReference blockReference) => default;
        public void Set(IMemoryBlockReference blockReference, object? value, Action<IMemoryBlock>? configure = null) => Memory.Declare(blockReference).Value = value;
        public IVariable? GetVariable(string name, bool localScopeOnly = false) => null;
        public IVariable SetVariable<T>(string name, T? value, Action<IMemoryBlock>? configure = null) => throw new NotSupportedException();
        public IEnumerable<IVariable> EnumerateVariablesInScope() => [];
        private static bool Missing(out object? value)
        {
            value = null;
            return false;
        }
    }

    private sealed class TestMemoryRegister : IMemoryRegister
    {
        public IDictionary<string, IMemoryBlock> Blocks { get; } = new Dictionary<string, IMemoryBlock>();
    }

    private sealed class TestActivityExecutionContext(IExpressionExecutionContext expressionExecutionContext) : IActivityExecutionContext
    {
        public IExpressionExecutionContext ExpressionExecutionContext { get; } = expressionExecutionContext;
        public IActivity Activity => throw new NotSupportedException();
        public IActivityExecutionContext ParentActivityExecutionContext => null!;
        public CancellationToken CancellationToken => CancellationToken.None;
        public TService GetRequiredService<TService>() where TService : notnull => throw new NotSupportedException();
        public T? Get<T>(InputArgument<T>? input) => default;
        public void Set<T>(OutputArgument<T>? output, T? value, string? outputName = null) { }
        public IAsyncEnumerable<ActivityOutputs> GetActivityOutputs() => AsyncEnumerable.Empty<ActivityOutputs>();
        public void SetOutcomes(string[] outcomes) { }
        public IEnumerable<string> GetOutcomes() => [];
        public void CreateBookmark(ActivityBookmarkRequest request) { }
        public IReadOnlyCollection<ActivityBookmarkRequest> GetBookmarkRequests() => [];
    }
}
