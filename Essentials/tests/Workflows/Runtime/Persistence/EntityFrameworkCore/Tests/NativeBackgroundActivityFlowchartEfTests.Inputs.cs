using System.Text;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Spec 1230 US3 (C10) on a worker: a secret-bound input resolves at worker activation and its raw value is never
/// stored (T014); a supported JSON (<c>Any</c>) input travels to the worker and runs (T015).
/// </summary>
public sealed partial class NativeBackgroundActivityFlowchartEfTests
{
    private const string SecretReferenceName = "payments.api-key";
    private const string SecretValue = "s3cr3t-background-value";

    [Theory]
    [InlineData(RuntimeProvider.InMemory)]
    [InlineData(RuntimeProvider.EntityFrameworkCore)]
    public async Task A_secret_input_resolves_on_the_worker_and_is_never_stored(RuntimeProvider provider)
    {
        var executionId = ExecutionIdPrefix + Guid.NewGuid().ToString("N");
        var observed = new DependentStepObservations();
        await using var harness = await StartHarnessAsync(executionId, new BackgroundBodyBarrier(), new FinalStepCounter(), provider: provider,
            configure: services =>
            {
                services.AddSingleton(observed);
                services.AddSingleton<IRuntimeSecretResolver>(new FixedSecretResolver(SecretReferenceName, SecretValue));
            });
        var contract = ClrActivityContractTestBuilder.BuildContract(typeof(SecretReadingBackgroundActivity));
        var input = contract.Inputs[nameof(SecretReadingBackgroundActivity.Token)];
        var text = ValueConversionPlan.Identity(input.Type, ValueRepresentation.TextValue);
        var secretPolicy = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true, requiresEncryption: true);
        var node = BackgroundNodeWith(typeof(SecretReadingBackgroundActivity), new RuntimeInputBinding(input.Key, input.Type, secretPolicy,
            RuntimeInputBindingSource.SecretRead, conversionPlan: text, secret: new RuntimeSecretReference(SecretReferenceName, "text")));
        var reference = await PublishAsync(harness, NewBackgroundThenFinalExecutable(node));
        await CopyPublicationIntoPartitionAsync(harness, reference);

        await harness.StartPublishedAsync(reference, executionId);
        var claimed = Assert.Single(await ClaimWorkersAsync(harness, executionId, 1));
        await harness.Services.GetRequiredService<BackgroundActivityWorker>()
            .ExecuteClaimAsync(new PersistenceScope(WorkflowExecutionHarness.Partition.Value), claimed.Claim)
            .WaitAsync(TimeSpan.FromSeconds(10));
        await DrivePartitionAsync(harness, executionId);

        // Resolved at worker activation: the body saw the secret value.
        Assert.Equal<string?>([SecretValue], observed.Values);
        var workflow = await WorkflowStateAsync(harness, executionId);
        Assert.Equal(WorkflowExecutionStatus.Completed, workflow!.Status);
        Assert.Equal($"length:{SecretValue.Length}", workflow.RootVariableFrame!.Values[LeftVariableKey].InlineValue!.Value.GetString());

        // The work record carries only the withheld reference, and no stored record holds the raw value.
        await using (var scope = await CreatePartitionScopeAsync(harness))
        {
            var work = await scope.ServiceProvider.GetRequiredService<IBackgroundActivityWorkStore>().FindAsync(executionId, claimed.Request.WorkId);
            var token = work!.Request.InputSnapshot.Values[input.Key];
            Assert.Equal(ValuePresence.Withheld, token.Presence);
            Assert.Null(token.InlineValue);
            Assert.DoesNotContain(SecretValue, JsonSerializer.Serialize(work), StringComparison.Ordinal);
            var queued = await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>()
                .ListAsync(new RuntimeSchedulerWorkQuery(executionId, limit: 100));
            Assert.DoesNotContain(SecretValue, JsonSerializer.Serialize(queued.Items), StringComparison.Ordinal);
            var incidents = await scope.ServiceProvider.GetRequiredService<IIncidentStateStore>().ListAsync(executionId);
            Assert.DoesNotContain(SecretValue, JsonSerializer.Serialize(incidents), StringComparison.Ordinal);
        }

        Assert.DoesNotContain(SecretValue, JsonSerializer.Serialize(await ActivityStatesAsync(harness, executionId)), StringComparison.Ordinal);
        Assert.DoesNotContain(SecretValue, JsonSerializer.Serialize(workflow), StringComparison.Ordinal);
        if (provider == RuntimeProvider.EntityFrameworkCore)
        {
            foreach (var file in new[] { _databasePath, $"{_databasePath}-wal" }.Where(File.Exists))
            {
                await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.Latin1);
                Assert.DoesNotContain(SecretValue, await reader.ReadToEndAsync(), StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [InlineData(RuntimeProvider.InMemory)]
    [InlineData(RuntimeProvider.EntityFrameworkCore)]
    public async Task A_json_input_travels_to_the_worker_and_runs(RuntimeProvider provider)
    {
        var executionId = ExecutionIdPrefix + Guid.NewGuid().ToString("N");
        await using var harness = await StartHarnessAsync(executionId, new BackgroundBodyBarrier(), new FinalStepCounter(), provider: provider);
        var contract = ClrActivityContractTestBuilder.BuildContract(typeof(JsonInputBackgroundActivity));
        var input = contract.Inputs[nameof(JsonInputBackgroundActivity.Document)];
        var document = JsonSerializer.SerializeToElement(new { name = "ada", tags = new[] { "a", "b" } });
        var node = BackgroundNodeWith(typeof(JsonInputBackgroundActivity), new RuntimeInputBinding(input.Key, input.Type, ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal, literal: ValueEnvelope.Inline(input.Type, document, ValueProtectionPolicy.InstanceInline)));
        var reference = await PublishAsync(harness, NewBackgroundThenFinalExecutable(node));
        await CopyPublicationIntoPartitionAsync(harness, reference);

        await harness.StartPublishedAsync(reference, executionId);
        var claimed = Assert.Single(await ClaimWorkersAsync(harness, executionId, 1));
        await harness.Services.GetRequiredService<BackgroundActivityWorker>()
            .ExecuteClaimAsync(new PersistenceScope(WorkflowExecutionHarness.Partition.Value), claimed.Claim)
            .WaitAsync(TimeSpan.FromSeconds(10));
        await DrivePartitionAsync(harness, executionId);

        var workflow = await WorkflowStateAsync(harness, executionId);
        Assert.Equal(WorkflowExecutionStatus.Completed, workflow!.Status);
        Assert.Equal("ada:2", workflow.RootVariableFrame!.Values[LeftVariableKey].InlineValue!.Value.GetString());
    }

    // A background leaf on LeftNodeId whose result value is captured into LeftVariableKey, with one input binding.
    private static ExecutableNode BackgroundNodeWith(Type activityType, RuntimeInputBinding binding) =>
        BackgroundNode(LeftNodeId, activityType, ClrActivityContractTestBuilder.BuildContract(activityType),
            new Dictionary<string, RuntimeInputBinding> { [binding.InputName] = binding }, LeftVariableKey, "left");
}

public sealed class FixedSecretResolver(string referenceName, string value) : IRuntimeSecretResolver
{
    public ValueTask<RuntimeSecretResolution> ResolveAsync(RuntimeSecretResolutionRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(request.Reference.Name == referenceName
            ? RuntimeSecretResolution.Success(value)
            : RuntimeSecretResolution.Failure("secret.not-found", isRetryable: false));
}

[BackgroundExecutionSafe]
public sealed class SecretReadingBackgroundActivity(DependentStepObservations observations) : Activity<BackgroundLeafResult>
{
    [ActivityInput]
    public string? Token { get; set; }

    protected override ValueTask<ActivityTransition<BackgroundLeafResult>> ExecuteAsync(ActivityExecutionContext context)
    {
        observations.Record(Token);
        return ValueTask.FromResult(ActivityTransition.Complete(new BackgroundLeafResult($"length:{Token?.Length}")));
    }
}

[BackgroundExecutionSafe]
public sealed class JsonInputBackgroundActivity : Activity<BackgroundLeafResult>
{
    [ActivityInput]
    public JsonElement? Document { get; set; }

    protected override ValueTask<ActivityTransition<BackgroundLeafResult>> ExecuteAsync(ActivityExecutionContext context)
    {
        var document = Document ?? throw new InvalidOperationException("The JSON input did not reach the worker.");
        return ValueTask.FromResult(ActivityTransition.Complete(
            new BackgroundLeafResult($"{document.GetProperty("name").GetString()}:{document.GetProperty("tags").GetArrayLength()}")));
    }
}
