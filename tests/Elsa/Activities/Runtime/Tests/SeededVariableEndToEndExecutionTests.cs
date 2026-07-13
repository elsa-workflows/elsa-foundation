using System.Text.Json;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.JavaScript.PreProcessors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// End-to-end guard for Seam C (#254): a workflow whose only activity binds an input to the JavaScript
/// expression <c>variables.greeting</c> must resolve that input to the workflow's authored variable
/// default when started through the real <see cref="ExecuteWorkflowRequestHandler"/> → dispatcher →
/// scheduler-drain flow. This proves the source end is wired: the handler projects the authored default
/// off the compiled executable, the start checkpoint seeds it as durable runtime state, and the invoke
/// handler projects it back so the materializer resolves <c>variables.*</c> in production — not merely in
/// a hand-constructed resolution context.
/// </summary>
public sealed class SeededVariableEndToEndExecutionTests
{
    private const string StringTypeName = "System.String";
    private readonly DateTimeOffset _now = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteWorkflow_ResolvesAuthoredVariableDefault_ThroughStartDispatchAndDrain()
    {
        var probe = new SeededInputProbe();
        await using var provider = BuildServiceProvider(probe);
        var executable = NewExecutableWithGreetingVariable();
        await PublishedExecutableSeeder.SaveAsync(provider, executable);

        var handler = new ExecuteWorkflowRequestHandler(
            provider.GetRequiredService<IWorkflowStartDispatcher>(),
            provider.GetRequiredService<IWorkflowExecutableStore>());

        var view = await handler.Handle(new ExecuteWorkflow(executable.Identity.ArtifactId), CancellationToken.None);

        var workflowState = await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(view.WorkflowExecutionId);
        Assert.Equal(WorkflowExecutionStatus.Completed, workflowState?.Status);
        Assert.Equal("Hello", probe.LastValue);
    }

    [Fact]
    public async Task ExecuteWorkflow_ReadOnlyActivity_DoesNotWriteBackTheUnchangedWorkflowVariable()
    {
        // Write-amplification guard (#286): the probe reads `variables.greeting` but never mutates it, so the
        // dirty-tracked write-back must emit nothing — only the single start-time seed durable value for the
        // variable should exist after the run, not a per-activity rewrite.
        var probe = new SeededInputProbe();
        await using var provider = BuildServiceProvider(probe);
        var executable = NewExecutableWithGreetingVariable();
        await PublishedExecutableSeeder.SaveAsync(provider, executable);

        var handler = new ExecuteWorkflowRequestHandler(
            provider.GetRequiredService<IWorkflowStartDispatcher>(),
            provider.GetRequiredService<IWorkflowExecutableStore>());

        var view = await handler.Handle(new ExecuteWorkflow(executable.Identity.ArtifactId), CancellationToken.None);

        var durableValues = await provider.GetRequiredService<IDurableValueStateStore>().ListAsync(view.WorkflowExecutionId);
        var variableValues = durableValues.Where(value => value.Metadata.ContainsKey(RuntimeMetadataKeys.VariableName)).ToList();
        var seed = Assert.Single(variableValues); // start seed only; no read-only write-back
        Assert.Equal("greeting", seed.Metadata[RuntimeMetadataKeys.VariableName]);
        Assert.Equal("Hello", seed.InlineValue!.Value.GetString());
    }

    [Fact]
    public async Task ExecuteWorkflow_ResolvesCallerSuppliedInput_ThroughStartDispatchAndDrain()
    {
        // #286 Part B: a caller-supplied workflow input threaded from the ExecuteWorkflow request resolves
        // `input.name` through the real handler → dispatcher → drain flow (inputs carry no artifact default,
        // so this is purely caller-supplied).
        var probe = new SeededInputProbe();
        await using var provider = BuildServiceProvider(probe);
        var executable = NewExecutableWithInputBinding();
        await PublishedExecutableSeeder.SaveAsync(provider, executable);

        var handler = new ExecuteWorkflowRequestHandler(
            provider.GetRequiredService<IWorkflowStartDispatcher>(),
            provider.GetRequiredService<IWorkflowExecutableStore>());

        var request = new ExecuteWorkflow(
            executable.Identity.ArtifactId,
            new Dictionary<string, JsonElement> { ["name"] = JsonSerializer.SerializeToElement("Ada") });
        var view = await handler.Handle(request, CancellationToken.None);

        var workflowState = await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(view.WorkflowExecutionId);
        Assert.Equal(WorkflowExecutionStatus.Completed, workflowState?.Status);
        Assert.Equal("Ada", probe.LastValue);
    }

    private ServiceProvider BuildServiceProvider(SeededInputProbe probe)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(probe);
        services.AddSingleton<IActivityConstructor, SeededInputProbeConstructor>();
        new EventsFeature().ConfigureServices(services);
        new SerializationFeature().ConfigureServices(services);
        new ExpressionsFeature().ConfigureServices(services);
        new JavaScriptFeature().ConfigureServices(services);
        new JintFeature().ConfigureServices(services);
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        new ActivitiesRuntimeFeature().ConfigureServices(services);

        // The materialization accessors pre-processor surfaces variables/inputs/outputs to the engine. It is
        // self-contained, so it is registered directly rather than via JavaScriptWorkflowsRuntimeFeature whose
        // other pre-processors require a live execution context.
        services.AddScoped<IScriptPreProcessor, MaterializationAccessorsPreProcessor>();

        var provider = services.BuildServiceProvider();
        RunStartupTasks(provider);
        return provider;
    }

    private static void RunStartupTasks(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        foreach (var task in scope.ServiceProvider.GetServices<Elsa.Tasks.Core.IStartupTask>())
            task.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private WorkflowExecutable NewExecutableWithGreetingVariable()
    {
        // The compiled root structure carries the workflow's authored variables in the generic `variables`
        // shape (the same shape Sequence/Flowchart emit), here a single `greeting` defaulting to "Hello".
        var structurePayload = JsonSerializer.SerializeToElement(
            new
            {
                variables = new[]
                {
                    new VariableDefinition(
                        ReferenceKey: "var-greeting",
                        Name: "greeting",
                        Type: new Elsa.Primitives.Models.TypeReference("String"),
                        StorageDriverType: null,
                        Default: new ArgumentValue("Hello", "Literal"))
                }
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var node = new ExecutableNode(
            executableNodeId: "node-start",
            authoredActivityId: "authored-node-start",
            activityType: "test/seeded-probe",
            activityTypeVersion: "1.0.0",
            descriptorType: SeededInputProbeConstructor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new SeededInputProbeDescriptor("Message")),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Message"] = new(
                    inputName: "Message",
                    source: RuntimeInputBindingSource.Expression,
                    expression: new RuntimeExpressionBinding("JavaScript", "variables.greeting", new RuntimeValueTypeDescriptor("clr", StringTypeName, null)),
                    metadata: new Dictionary<string, string>
                    {
                        [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = StringTypeName,
                        ["referenceKey"] = "Message"
                    })
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            structure: new ExecutableActivityStructure("test.structure", "1.0.0", structurePayload));

        return new(
            identity: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            rootActivity: node,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: _now,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    private WorkflowExecutable NewExecutableWithInputBinding()
    {
        // No authored variables: the activity binds its Message input to the JS `input.name` accessor, which
        // resolves only from caller-supplied inputs (#286 Part B).
        var node = new ExecutableNode(
            executableNodeId: "node-start",
            authoredActivityId: "authored-node-start",
            activityType: "test/seeded-probe",
            activityTypeVersion: "1.0.0",
            descriptorType: SeededInputProbeConstructor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new SeededInputProbeDescriptor("Message")),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Message"] = new(
                    inputName: "Message",
                    source: RuntimeInputBindingSource.Expression,
                    expression: new RuntimeExpressionBinding("JavaScript", "input.name", new RuntimeValueTypeDescriptor("clr", StringTypeName, null)),
                    metadata: new Dictionary<string, string>
                    {
                        [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = StringTypeName,
                        ["referenceKey"] = "Message"
                    })
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

        return new(
            identity: new WorkflowExecutableIdentity("artifact-input", "definition-input", "version-input", "1.0.0", "sha256:test-input"),
            rootActivity: node,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: _now,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    private sealed record SeededInputProbeDescriptor(string Label);

    private sealed class SeededInputProbe
    {
        public string? LastValue { get; private set; }
        public void Record(string? value) => LastValue = value;
    }

    private sealed class SeededInputProbeConstructor(SeededInputProbe probe) : IActivityConstructor<SeededInputProbeDescriptor>
    {
        public static string DescriptorTypeKey => typeof(SeededInputProbeDescriptor).FullName!;
        public string DescriptorType => DescriptorTypeKey;

        public ValueTask<IActivity> Construct(JsonElement payload, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken)
        {
            var descriptor = payload.Deserialize<SeededInputProbeDescriptor>()
                             ?? throw new InvalidOperationException("Seeded probe descriptor resolved to null.");
            return Construct(descriptor, inputs, outputs, cancellationToken);
        }

        public ValueTask<IActivity> Construct(SeededInputProbeDescriptor descriptor, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken)
        {
            if (inputs is null || !inputs.TryGetValue("Message", out var argument))
                throw new InvalidOperationException("Seeded probe expected a materialized 'Message' input argument.");

            return new(new SeededInputProbeActivity(probe, (InputArgument<string>)argument));
        }
    }

    private sealed class SeededInputProbeActivity(SeededInputProbe probe, InputArgument<string> message) : CodeActivity("test/seeded-probe")
    {
        protected override void Execute(IActivityExecutionContext context) => probe.Record(context.Get(message));
    }
}
