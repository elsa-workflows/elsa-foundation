using System.Text.Json;
using Elsa.Activities.Runtime.Services.StepState;
using Elsa.Activities.Runtime.Services.StepVariables;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests.VariableLifecycle;

public sealed class StepWorkflowStatePolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Constant_replacement_keeps_destination_policy_and_reencodes_external_storage(bool external)
    {
        var payloads = new Payloads();
        await using var harness = WorkflowExecutionHarness.Create().ConfigureServices(services => services.AddSingleton<IExternalPayloadStore>(payloads)).Build("unused");
        var policy = new ValueProtectionPolicy(DurableValueLifecycle.Audit, external ? DurableValueStorage.External : DurableValueStorage.Inline,
            isSensitive: true, retentionPolicy: "retain", metadata: external ? new Dictionary<string, string> { [ValuePolicyCombiner.StorageProfileMetadataKey] = "store" } : null);
        var old = external
            ? ValueEnvelope.External(new ValueTypeDescriptor("String"), new DurableValueExternalReference("store", "old", new Dictionary<string, string>()), policy)
            : ValueEnvelope.Inline(new ValueTypeDescriptor("String"), JsonSerializer.SerializeToElement("old"), policy);
        var session = Session(new Dictionary<string, ValueEnvelope> { ["Answer"] = old });
        session.SetOutput("Answer", JsonSerializer.SerializeToElement(new { Value = 42 }));
        Assert.Equal(0, payloads.Writes);
        var prepared = await Prepare(harness, session);
        var value = Assert.Single(prepared.StateValues).State;
        Assert.Equal("Any", value.Type.Id);
        Assert.Equal(DurableValueLifecycle.Audit, value.Lifecycle);
        Assert.Equal(external ? DurableValueStorage.External : DurableValueStorage.Inline, value.Storage);
        Assert.Equal("True", value.Metadata["runtime.isSensitive"]);
        Assert.Equal("retain", value.Metadata["runtime.retentionPolicy"]);
        if (external)
        {
            Assert.Equal(1, payloads.Writes);
            Assert.NotEqual("old", value.ExternalReference!.Locator);
            Assert.Equal("{\"Value\":42}", payloads.Last!.Payload.GetRawText());
        }
    }

    [Theory]
    [InlineData(DurableValueStorage.Custom, false)]
    [InlineData(DurableValueStorage.External, false)]
    [InlineData(DurableValueStorage.Inline, true)]
    public async Task Unavailable_custom_external_or_encrypted_encoding_refuses_the_evaluation(DurableValueStorage storage, bool encryption)
    {
        await using var harness = WorkflowExecutionHarness.Create().Build("unused");
        var policy = new ValueProtectionPolicy(DurableValueLifecycle.Instance, storage, requiresEncryption: encryption);
        var session = Session(new Dictionary<string, ValueEnvelope> { ["Answer"] = ValueEnvelope.Inline(new ValueTypeDescriptor("String"), JsonSerializer.SerializeToElement("old"), policy) });
        session.SetOutput("Answer", JsonSerializer.SerializeToElement(42));
        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => Prepare(harness, session));
        Assert.Matches("VF-(ACT|STATE)-", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Protected_read_before_or_after_public_identity_assignment_refuses_without_values(bool readAfter)
    {
        await using var harness = WorkflowExecutionHarness.Create().Build("unused");
        var session = ProtectedSession();
        if (!readAfter) session.TryGetOutput("Protected", out _);
        session.CorrelationId = "attempted";
        session.SetOutput("Safe", JsonSerializer.SerializeToElement(42));
        if (readAfter) session.TryGetOutput("Protected", out _);
        var failure = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => Prepare(harness, session));
        Assert.Contains("VF-STATE-003", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("attempted", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Protected_reads_cannot_be_declassified_by_a_constant_looking_output_name()
    {
        await using var harness = WorkflowExecutionHarness.Create().Build("unused");
        var session = ProtectedSession();
        session.SetOutput("Safe", JsonSerializer.SerializeToElement(42));
        session.TryGetOutput("Protected", out _);
        var failure = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => Prepare(harness, session));
        Assert.Contains("VF-STATE-003", failure.Message, StringComparison.Ordinal);
    }

    private static StepWorkflowState ProtectedSession()
    {
        var json = JsonSerializer.SerializeToElement("sentinel");
        return new StepWorkflowState(true, null, null,
            new Dictionary<string, ValueEnvelope> { ["Protected"] = ValueEnvelope.Inline(new ValueTypeDescriptor("String"), json,
                new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true)) },
            new Dictionary<string, JsonElement> { ["Protected"] = json });
    }

    private static StepWorkflowState Session(Dictionary<string, ValueEnvelope> outputs) => new(true, null, null, outputs, new Dictionary<string, JsonElement>());
    private static Task<StepVariableCommitResult> Prepare(WorkflowExecutionHarness harness, StepWorkflowState session)
    {
        var invocation = new ActivityExecutionState(new ActivityExecution("body", "workflow", "node", "node", "Test", "1"),
            ActivityExecutionStatus.Running, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, null, null, null, null, null,
            [], [], 0, 0, new Dictionary<string, string>());
        return StepStateCommit.PrepareAsync(session, StepVariableCommitResult.None, harness.Services.GetRequiredService<IWorkflowExecutionStateStore>(),
            harness.Services.GetRequiredService<IWorkflowStateChangePreparer>(), "workflow", invocation, DateTimeOffset.UnixEpoch, CancellationToken.None).AsTask();
    }

    private sealed class Payloads : IExternalPayloadStore
    {
        public int Writes { get; private set; }
        public ExternalPayloadWriteRequest? Last { get; private set; }
        public ValueTask<DurableValueExternalReference> WriteAsync(ExternalPayloadWriteRequest request, CancellationToken cancellationToken = default)
        { Last = request; return ValueTask.FromResult(new DurableValueExternalReference(request.StorageProfile, $"fresh-{++Writes}", new Dictionary<string, string>())); }
        public ValueTask<JsonElement> ReadAsync(DurableValueExternalReference reference, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A constant replacement must not read the old payload.");
    }
}
