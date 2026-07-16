using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Scripting.Activities;
using Elsa.Activities.Testing;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Serialization.SystemText;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.JavaScript.PreProcessors;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Scripting.Tests;

/// <summary>
/// End-to-end execution coverage for <see cref="RunJavaScript"/> using the <b>real</b> shared Jint evaluator
/// (not a mock): the activity runs an authored script through the same <see cref="IJavaScriptEvaluator"/> the
/// JavaScript expression path uses, so these tests prove (a) the script's return value surfaces as the activity
/// result, (b) an empty script auto-completes without a result, and (c) the shared Jint sandbox (DS-9) bounds the
/// script when it exceeds the configured statement limit — the run faults instead of hanging.
/// </summary>
public sealed class RunJavaScriptExecutionTests
{
    private const string ObjectTypeName = "System.Object";
    private const string StringTypeName = "System.String";
    private const string ResultValueId = "runjs-result";

    [Fact]
    public async Task RunJavaScript_EvaluatesScript_AndSurfacesReturnValueAsResult()
    {
        // A literal script `40 + 2` runs through the real Jint evaluator; its completion value is captured as the
        // activity's durable Result output.
        await using var harness = NewHarness("actexec-runjs");

        var run = await harness.RunAsync(NewExecutable("40 + 2"));

        run.AssertCompleted("node-runjs");
        run.AssertWorkflowCompleted();

        var result = await FindResultAsync(harness);
        Assert.NotNull(result);
        Assert.Equal(42d, result!.InlineValue!.Value.GetDouble());
    }

    [Fact]
    public async Task RunJavaScript_WithWhitespaceScript_CompletesWithoutResult()
    {
        // A whitespace-only script authors no work: the activity auto-completes with no evaluation and no result.
        await using var harness = NewHarness("actexec-runjs");

        var run = await harness.RunAsync(NewExecutable("   "));

        run.AssertCompleted("node-runjs");
        run.AssertWorkflowCompleted();

        var result = await FindResultAsync(harness);
        Assert.Null(result);
    }

    [Fact]
    public async Task RunJavaScript_ExceedingTheStatementLimit_FaultsTheRunViaTheSharedSandbox()
    {
        // DS-9 on the activity path: the shared Jint engine factory enforces the configured statement-count limit
        // for every evaluation, including this activity. An unbounded loop trips that limit, so the evaluation
        // aborts and the activity faults rather than hanging the run — proving the sandbox applies here.
        await using var harness = NewHarness(
            "actexec-runjs",
            configureJint: services => services.Configure<Elsa.Expressions.JavaScript.Jint.Options.FeatureOptions>(
                options => options.MaxStatements = 10_000));

        var run = await harness.RunAsync(NewExecutable("while (true) { }"));

        var state = run.State("node-runjs");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Faulted, state!.Status);
    }

    private static async Task<DurableValueState?> FindResultAsync(WorkflowExecutionHarness harness) =>
        await harness.Services.GetRequiredService<IDurableValueStateStore>()
            .FindAsync(WorkflowExecutionHarness.WorkflowExecutionId, $"durable-{ResultValueId}");

    private static WorkflowExecutable NewExecutable(string script)
    {
        var node = new ExecutableNode(
            executableNodeId: "node-runjs",
            authoredActivityId: "authored-runjs",
            activityType: typeof(RunJavaScript).FullName!,
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor(RunJavaScriptConstructor.ConsumerKeyValue, RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new RunJavaScriptDescriptor())),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Script"] = LiteralString("Script", script)
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>
            {
                ["Result"] = new RuntimeOutputCapture(
                    outputName: "Result",
                    valueId: ResultValueId,
                    type: new RuntimeValueTypeDescriptor("clr", ObjectTypeName, null),
                    lifecycle: DurableValueLifecycle.Instance,
                    storage: DurableValueStorage.Inline,
                    captureOnSuccessfulCompletion: true)
            },
            metadata: new Dictionary<string, string>());

        return WorkflowExecutionHarness.NewExecutable(node);
    }

    private static RuntimeInputBinding LiteralString(string inputName, string value) =>
        new(
            inputName: inputName,
            source: RuntimeInputBindingSource.Literal,
            literalValue: JsonSerializer.SerializeToElement(value),
            expression: null,
            metadata: new Dictionary<string, string>
            {
                [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = StringTypeName,
                ["referenceKey"] = inputName
            });

    private static WorkflowExecutionHarness NewHarness(
        string activityExecutionId,
        Action<IServiceCollection>? configureJint = null)
    {
        var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesScriptingFeature().ConfigureServices(services))
            .WithConstructor<RunJavaScriptConstructor>()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddMemoryCache();
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
                new EventsFeature().ConfigureServices(services);
                new SerializationFeature().ConfigureServices(services);
                new ExpressionsFeature().ConfigureServices(services);
                new JavaScriptFeature().ConfigureServices(services);
                new JintFeature().ConfigureServices(services);
                // Surfaces variables/inputs/outputs to the engine at materialization time (self-contained).
                services.AddScoped<IScriptPreProcessor, MaterializationAccessorsPreProcessor>();
                configureJint?.Invoke(services);
            })
            .Build(activityExecutionId);

        RunStartupTasks(harness.Services);
        return harness;
    }

    private static void RunStartupTasks(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        foreach (var task in scope.ServiceProvider.GetServices<IStartupTask>())
            task.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private sealed record RunJavaScriptDescriptor;

    private sealed class RunJavaScriptConstructor : IActivityConstructor<RunJavaScriptDescriptor>
    {
        public static string ConsumerKeyValue => typeof(RunJavaScriptDescriptor).FullName!;
        public string ConsumerKey => ConsumerKeyValue;

        public ValueTask<IActivity> Construct(
            JsonElement payload,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken) =>
            Construct(payload.Deserialize<RunJavaScriptDescriptor>() ?? new RunJavaScriptDescriptor(), inputs, outputs, cancellationToken);

        public ValueTask<IActivity> Construct(
            RunJavaScriptDescriptor descriptor,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken)
        {
            if (inputs is null || !inputs.TryGetValue("Script", out var scriptInput))
                throw new InvalidOperationException("RunJavaScript expected a materialized 'Script' input argument.");

            var activity = new RunJavaScript { Script = (InputArgument<string>)scriptInput };

            if (outputs is not null && outputs.TryGetValue("Result", out var resultOutput))
                activity.Result = (OutputArgument<object?>)resultOutput;

            return new(activity);
        }
    }
}
