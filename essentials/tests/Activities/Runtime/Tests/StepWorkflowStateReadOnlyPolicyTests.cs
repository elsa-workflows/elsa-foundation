using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Tests.VariableLifecycle;
using Elsa.Activities.Scripting;
using Elsa.Activities.Scripting.Activities;
using Elsa.Activities.Testing;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Primitives.Models;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class StepWorkflowStateReadOnlyPolicyTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Distinct_read_storage_profiles_need_combining_only_when_a_destination_is_written(bool native, bool write)
    {
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesScriptingFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddLogging(); services.AddMemoryCache();
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
                services.AddSingleton<IExternalPayloadStore>(new Payloads());
                new EventsFeature().ConfigureServices(services); new SerializationFeature().ConfigureServices(services);
                new ExpressionsFeature().ConfigureServices(services); new JavaScriptFeature().ConfigureServices(services);
                new JintFeature().ConfigureServices(services);
            }).Build("actexec-profiles");
        var node = native
            ? StepVariableTestGraph.Leaf("body", typeof(ProfiledStateReader), new Dictionary<string, RuntimeInputBinding> { ["Write"] = Literal("Write", "Boolean", write) })
            : new ExecutableNode("body", "body", typeof(RunJavaScript).FullName!, "1", "test", JsonSerializer.SerializeToElement(new { }),
                inputBindings: new Dictionary<string, RuntimeInputBinding> { ["script"] = Literal("script", "String",
                    "workflowState.getOutput('A'); workflowState.getOutput('B'); " + (write ? "workflowState.setOutput('New',1); " : "") + "return 1;"),
                    ["arguments"] = Literal("arguments", "JsonElement", new { }) }, metadata: new Dictionary<string, string>());
        var store = harness.Services.GetRequiredService<IDurableValueStateStore>();
        foreach (var name in new[] { "A", "B" })
            await store.SaveAsync(new DurableValueState("durable-" + name, harness.ExecutionId, "output:" + name,
                new RuntimeValueTypeDescriptor("Any", "Single", null), DurableValueLifecycle.Instance, DurableValueStorage.External,
                null, new DurableValueExternalReference("store-" + name, name, new Dictionary<string, string>()), null,
                DateTimeOffset.UnixEpoch, new Dictionary<string, string>()));
        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(node), allowPendingWorkOnTerminalCompletion: true, allowAcceptedButFaulted: true);
        Assert.Equal(write ? ActivityExecutionStatus.Faulted : ActivityExecutionStatus.Completed, run.State("body").Status);
        if (!write) run.AssertWorkflowCompleted();
        Assert.DoesNotContain(await store.ListAllDurableValueStatesAsync(harness.ExecutionId), value => value.ValueId == "output:New");
    }

    private static RuntimeInputBinding Literal(string key, string alias, object value)
    {
        var type = new ValueTypeDescriptor(alias);
        return new RuntimeInputBinding(key, type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));
    }

    private sealed class Payloads : IExternalPayloadStore
    {
        public ValueTask<JsonElement> ReadAsync(DurableValueExternalReference reference, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(JsonSerializer.SerializeToElement(42));
        public ValueTask<DurableValueExternalReference> WriteAsync(ExternalPayloadWriteRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Incompatible source profiles must refuse before encoding.");
    }
}

[AccessesWorkflowState]
public sealed class ProfiledStateReader : Activity<Incremented>
{
    [ActivityInput] public bool Write { get; set; }
    protected override ValueTask<ActivityTransition<Incremented>> ExecuteAsync(ActivityExecutionContext context)
    {
        Assert.True(context.WorkflowState.TryGetOutput("A", out _));
        Assert.True(context.WorkflowState.TryGetOutput("B", out _));
        if (Write) context.WorkflowState.SetOutput("New", JsonSerializer.SerializeToElement(1));
        return ValueTask.FromResult(ActivityTransition.Complete(new Incremented(1)));
    }
}
