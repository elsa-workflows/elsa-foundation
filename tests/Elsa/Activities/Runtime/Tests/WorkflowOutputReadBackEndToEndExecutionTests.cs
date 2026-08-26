using System.Text.Json;
using Elsa.Activities.Runtime;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// End-to-end acceptance for #254 Seam R1: a workflow output assigned by the <c>SetOutput</c> leaf during a
/// run started through the real <see cref="WorkflowExecutionStartService"/> → dispatcher → scheduler-drain
/// flow must be readable back on the instance details view served by
/// <see cref="WorkflowInstanceDetailsService"/> (the service behind <c>GET instances/{id}</c>). Also pins
/// the default diagnostic snapshot contract: a payload the default <c>IRuntimePayloadCapturePolicy</c> does not
/// expose raw still surfaces as named diagnostic evidence — never silently absent.
/// </summary>
public sealed class WorkflowOutputReadBackEndToEndExecutionTests
{
    private const string OutputName = "Result";
    private const string OutputValue = "done";
    private readonly DateTimeOffset _now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteWorkflow_SetOutputValue_IsReadableOnInstanceDetails()
    {
        // A host policy that exposes non-sensitive payloads: the assigned output value reads back inline.
        await using var provider = BuildServiceProvider(new CaptureUnlessSensitivePolicy());

        var details = await RunSetOutputWorkflowAndReadBack(provider, "artifact-out-1");

        var output = Assert.Contains(OutputName, details.Outputs);
        Assert.False(output.IsRedacted);
        Assert.Null(output.RedactionReason);
        Assert.Equal(OutputValue, Assert.IsType<JsonElement>(output.Value).GetString());
    }

    [Fact]
    public async Task ExecuteWorkflow_DefaultPolicy_SurfacesOutputAsDiagnosticSnapshot_NotAbsent()
    {
        // Under the default capture policy the output must still appear as a bounded diagnostic snapshot,
        // not as raw payload and not silently dropped from the view.
        await using var provider = BuildServiceProvider(payloadCapturePolicy: null);

        var details = await RunSetOutputWorkflowAndReadBack(provider, "artifact-out-2");

        var output = Assert.Contains(OutputName, details.Outputs);
        Assert.False(output.IsRedacted);
        Assert.Null(output.RedactionReason);
        var snapshot = Assert.IsType<JsonElement>(output.Value);
        Assert.Equal("string", snapshot.GetProperty("kind").GetString());
        Assert.Equal(OutputValue, snapshot.GetProperty("preview").GetString());
    }

    [Fact]
    public async Task SensitiveMarkedOutput_SurfacesAsRedactedMarker_EvenUnderAPayloadCapturingPolicy()
    {
        // The IsSensitive durable-value marker must be honored at the read edge: even with a host policy that
        // exposes payloads, a sensitive-marked output reads back as a marker — not a value, and not absent —
        // while the plain output on the same instance still reads back inline.
        await using var provider = BuildServiceProvider(new CaptureUnlessSensitivePolicy());

        var workflowExecutionId = await RunSetOutputWorkflowAsync(provider, "artifact-out-3");
        await provider.GetRequiredService<IDurableValueStateStore>().SaveAsync(
            NewSensitiveOutputState(workflowExecutionId, "Secret", "hunter2"));

        var details = await ReadInstanceAsync(provider, workflowExecutionId);

        var plain = Assert.Contains(OutputName, details.Outputs);
        Assert.Equal(OutputValue, Assert.IsType<JsonElement>(plain.Value).GetString());
        var sensitive = Assert.Contains("Secret", details.Outputs);
        Assert.True(sensitive.IsRedacted);
        Assert.Null(sensitive.Value);
        Assert.False(string.IsNullOrWhiteSpace(sensitive.RedactionReason));
    }

    private async Task<Elsa.Workflows.Runtime.Api.Models.WorkflowInstanceDetailsView> RunSetOutputWorkflowAndReadBack(
        ServiceProvider provider,
        string artifactId)
    {
        var workflowExecutionId = await RunSetOutputWorkflowAsync(provider, artifactId);
        return await ReadInstanceAsync(provider, workflowExecutionId);
    }

    private async Task<string> RunSetOutputWorkflowAsync(ServiceProvider provider, string artifactId)
    {
        var executable = NewSetOutputExecutable(artifactId);
        await PublishedExecutableSeeder.SaveAsync(provider, executable);

        var startService = new WorkflowExecutionStartService(
            provider.GetRequiredService<IWorkflowStartDispatcher>(),
            provider.GetRequiredService<IWorkflowExecutableStore>());
        var view = await startService.ExecuteAsync(new ExecuteWorkflow(executable.Identity.ArtifactId), CancellationToken.None);

        var workflowState = await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(view.WorkflowExecutionId);
        Assert.Equal(WorkflowExecutionStatus.Completed, workflowState?.Status);
        return view.WorkflowExecutionId;
    }

    private static async Task<Elsa.Workflows.Runtime.Api.Models.WorkflowInstanceDetailsView> ReadInstanceAsync(
        ServiceProvider provider,
        string workflowExecutionId)
    {
        var readService = new WorkflowInstanceDetailsService(
            provider.GetRequiredService<IWorkflowExecutionStateStore>(),
            provider.GetRequiredService<IActivityExecutionInspectionStore>(),
            provider.GetRequiredService<IIncidentStateStore>(),
            provider.GetRequiredService<IDurableValueStateStore>(),
            provider.GetRequiredService<IRuntimePayloadCapturePolicy>(),
            new AllowAllActivityExecutionInspectionAuthorizationContext(),
            new Elsa.Workflows.Runtime.Api.Coalescing.RuntimeCheckpointCadenceInspector([]));
        var view = await readService.GetAsync(new GetWorkflowInstance(workflowExecutionId), CancellationToken.None);

        Assert.NotNull(view);
        return view!;
    }

    private DurableValueState NewSensitiveOutputState(string workflowExecutionId, string outputName, string value) =>
        new(
            durableValueId: $"durable-output:{outputName}",
            workflowExecutionId: workflowExecutionId,
            valueId: $"output:{outputName}",
            type: new RuntimeValueTypeDescriptor("clr", "System.String", null),
            lifecycle: DurableValueLifecycle.Instance,
            storage: DurableValueStorage.Inline,
            inlineValue: JsonSerializer.SerializeToElement(value),
            externalReference: null,
            sourceActivityExecutionId: null,
            capturedAt: _now,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RuntimeMetadataKeys.OutputName] = outputName,
                [RuntimeMetadataKeys.IsSensitive] = "true"
            });

    private static ServiceProvider BuildServiceProvider(IRuntimePayloadCapturePolicy? payloadCapturePolicy)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        // Register before the features: runtime core TryAdds the default policy, so a test-supplied policy
        // registered first wins — the same way a host opts in to exposing workflow-output payloads.
        if (payloadCapturePolicy is not null)
            services.AddSingleton(payloadCapturePolicy);

        new EventsFeature().ConfigureServices(services);
        new SerializationFeature().ConfigureServices(services);
        new ExpressionsFeature().ConfigureServices(services);
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        new ActivitiesRuntimeFeature().ConfigureServices(services);

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

    private WorkflowExecutable NewSetOutputExecutable(string artifactId)
    {
        var node = new ExecutableNode(
            executableNodeId: "node-set-output",
            authoredActivityId: "authored-node-set-output",
            activityType: "elsa.intrinsic.set-output",
            activityTypeVersion: "1.0.0",
            descriptorType: "intrinsic",
            descriptorPayload: JsonSerializer.SerializeToElement(new { kind = "SetOutput", schemaVersion = "1.0.0" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                [WorkflowIntrinsicInputKeys.Name] = Literal(WorkflowIntrinsicInputKeys.Name, OutputName),
                [WorkflowIntrinsicInputKeys.Value] = Literal(WorkflowIntrinsicInputKeys.Value, OutputValue)
            },
            metadata: new Dictionary<string, string>(),
            intrinsicKind: WorkflowIntrinsicKind.SetOutput);

        return new(
            identity: new WorkflowExecutableIdentity(artifactId, $"definition-{artifactId}", $"version-{artifactId}", "1.0.0", $"sha256:{artifactId}"),
            rootActivity: node,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: _now,
            compatibilityMetadata: new Dictionary<string, string>(),
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
    }

    private static RuntimeInputBinding Literal(string inputName, string value) =>
        new(
            inputKey: inputName,
            targetType: new Elsa.Primitives.Models.ValueTypeDescriptor("String"),
            effectivePolicy: ValueProtectionPolicy.InstanceInline,
            source: RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(
                new Elsa.Primitives.Models.ValueTypeDescriptor("String"),
                JsonSerializer.SerializeToElement(value),
                ValueProtectionPolicy.InstanceInline));

    /// <summary>The opt-in shape a host registers to expose workflow-output payloads: capture unless sensitive.</summary>
    private sealed class CaptureUnlessSensitivePolicy : IRuntimePayloadCapturePolicy
    {
        public RuntimePayloadCaptureDecision Decide(RuntimePayloadCaptureRequest request) =>
            request.IsSensitive
                ? new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.None, "Sensitive runtime payloads are excluded.")
                : new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.Payload, "Test policy captures non-sensitive payloads.");
    }
}
