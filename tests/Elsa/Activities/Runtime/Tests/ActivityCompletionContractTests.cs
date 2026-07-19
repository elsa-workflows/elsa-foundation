using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class ActivityCompletionContractTests
{
    private static readonly ValueTypeDescriptor ResultType = new("Payments.ChargeResult");
    private static readonly ValueTypeDescriptor StringType = new("String");

    [Fact]
    public void Completion_commits_one_result_outcome_and_read_only_projections()
    {
        var contract = Contract();
        var attempt = Attempt();

        var projected = new ActivityCompletionProjector().Project(
            "invocation-1",
            attempt,
            contract,
            ActivityTransition.Complete(new ChargeResult("receipt-1", true), "Charged"),
            DateTimeOffset.UtcNow);

        Assert.Equal("Charged", projected.Completion.OutcomeKey);
        Assert.Equal(ResultType, projected.Completion.Result.Type);
        Assert.Equal("receipt-1", projected.Projections["receipt-id"].InlineValue!.Value.GetString());
        Assert.True(projected.Projections["approved"].InlineValue!.Value.GetBoolean());
    }

    [Fact]
    public void Projection_failure_rejects_the_whole_completion()
    {
        var contract = Contract(requiredProjectionPath: "missing");

        var exception = Assert.Throws<InvalidOperationException>(() => new ActivityCompletionProjector().Project(
            "invocation-1",
            Attempt(),
            contract,
            ActivityTransition.Complete(new ChargeResult("receipt-1", true), "Charged"),
            DateTimeOffset.UtcNow));

        Assert.Contains("VF-ACT-006", exception.Message);
    }

    [Fact]
    public void Opaque_projection_key_with_dot_prefers_exact_result_property()
    {
        var contract = new ActivityContract(
            "Graph.Boundary",
            "1.0.0",
            "elsa.graph-activity",
            System.Text.Json.JsonSerializer.SerializeToElement(new { }),
            [],
            new ActivityResultContract(
                new ValueTypeDescriptor("Object"),
                true,
                ActivityValuePolicy.Default,
                [new ActivityResultProjectionContract("order.total", "order.total", StringType, true, ActivityValuePolicy.Default)]),
            ["Done"],
            new ActivityActivationRequirement("elsa.graph-activity", "elsa.graph-activity"));

        var projected = new ActivityCompletionProjector().Project(
            "invocation-1",
            Attempt(),
            contract,
            ActivityTransition.Complete<IReadOnlyDictionary<string, object?>>(
                new Dictionary<string, object?> { ["order.total"] = "42" },
                "Done"),
            DateTimeOffset.UtcNow);

        Assert.Equal("42", projected.Projections["order.total"].InlineValue!.Value.GetString());
    }

    [Fact]
    public void Undeclared_outcome_and_nonpersistable_result_are_rejected()
    {
        var projector = new ActivityCompletionProjector();
        Assert.Throws<InvalidOperationException>(() => projector.Project(
            "invocation-1", Attempt(), Contract(), ActivityTransition.Complete(new ChargeResult("r", true), "Unknown"), DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => projector.Project(
            "invocation-1", Attempt(), Contract(resultPolicy: ActivityValuePolicy.Default with { IsPersistable = false }), ActivityTransition.Complete(new ChargeResult("r", true), "Charged"), DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task External_result_is_stored_and_projection_views_preserve_policy_without_downgrade()
    {
        var store = new RecordingExternalPayloadStore();
        var policy = new ActivityValuePolicy(
            true,
            true,
            true,
            "Full",
            ActivityValueLifecycle.Result,
            ActivityValueStorage.External,
            "encrypted-payloads",
            "P30D");

        var projected = await new ActivityCompletionProjector(store).ProjectAsync(
            "workflow-1",
            "invocation-1",
            Attempt(),
            Contract(resultPolicy: policy),
            ActivityTransition.Complete(new ChargeResult("receipt-1", true), "Charged"),
            DateTimeOffset.UtcNow);

        Assert.Null(projected.Completion.Result.InlineValue);
        Assert.Equal(DurableValueStorage.External, projected.Completion.Result.Policy.Storage);
        Assert.All(projected.Projections.Values, envelope =>
        {
            Assert.NotNull(envelope.InlineValue);
            Assert.Null(envelope.ExternalReference);
            Assert.Equal("P30D", envelope.Policy.RetentionPolicy);
            Assert.True(envelope.Policy.IsSensitive);
            Assert.True(envelope.Policy.RequiresEncryption);
            Assert.Equal("Full", envelope.Policy.RedactionMode);
        });
        Assert.Single(store.Writes);
        Assert.All(store.Writes, write => Assert.Equal("encrypted-payloads", write.StorageProfile));
    }

    [Fact]
    public async Task Incompatible_projection_retention_is_rejected_before_external_write()
    {
        var store = new RecordingExternalPayloadStore();
        var resultPolicy = new ActivityValuePolicy(
            true, true, true, "Full", ActivityValueLifecycle.Result,
            ActivityValueStorage.External, "encrypted-payloads", "P30D");
        var projectionPolicy = resultPolicy with { RetentionPolicy = "P7D" };
        var contract = Contract(resultPolicy: resultPolicy, projectionPolicy: projectionPolicy);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => new ActivityCompletionProjector(store)
            .ProjectAsync(
                "workflow-1", "invocation-1", Attempt(), contract,
                ActivityTransition.Complete(new ChargeResult("receipt-1", true), "Charged"),
                DateTimeOffset.UtcNow).AsTask());

        Assert.Contains("VF-ACT-005", exception.Message);
        Assert.Empty(store.Writes);
    }

    [Fact]
    public async Task StrongerProjectionPolicyIsFoldedIntoAtomicResultBeforePersistence()
    {
        var store = new RecordingExternalPayloadStore();
        var projectionPolicy = new ActivityValuePolicy(
            IsPersistable: true,
            IsSensitive: true,
            RequiresEncryption: true,
            RedactionMode: "Full",
            Lifecycle: ActivityValueLifecycle.Audit,
            Storage: ActivityValueStorage.External,
            StorageProfile: "audit-payloads",
            RetentionPolicy: "P30D");

        var projected = await new ActivityCompletionProjector(store).ProjectAsync(
            "workflow-1",
            "invocation-1",
            Attempt(),
            Contract(projectionPolicy: projectionPolicy),
            ActivityTransition.Complete(new ChargeResult("receipt-1", true), "Charged"),
            DateTimeOffset.UtcNow);

        Assert.Equal(DurableValueLifecycle.Audit, projected.Completion.Result.Policy.Lifecycle);
        Assert.Equal(DurableValueStorage.External, projected.Completion.Result.Policy.Storage);
        Assert.True(projected.Completion.Result.Policy.IsSensitive);
        Assert.True(projected.Completion.Result.Policy.RequiresEncryption);
        Assert.Equal("Full", projected.Completion.Result.Policy.RedactionMode);
        Assert.Equal("P30D", projected.Completion.Result.Policy.RetentionPolicy);
        Assert.Equal("audit-payloads", Assert.Single(store.Writes).StorageProfile);
    }

    private static ActivityContract Contract(
        string requiredProjectionPath = "receiptId",
        ActivityValuePolicy? resultPolicy = null,
        ActivityValuePolicy? projectionPolicy = null) =>
        new(
            "Payments.Charge",
            "1.0.0",
            "clr",
            System.Text.Json.JsonSerializer.SerializeToElement(new { typeAlias = "Payments.Charge" }),
            [],
            new ActivityResultContract(
                ResultType,
                true,
                resultPolicy ?? ActivityValuePolicy.Default,
                [
                    new ActivityResultProjectionContract("receipt-id", requiredProjectionPath, StringType, true, projectionPolicy ?? ActivityValuePolicy.Default),
                    new ActivityResultProjectionContract("approved", "approved", new ValueTypeDescriptor("Boolean"), true, ActivityValuePolicy.Default)
                ]),
            ["Charged", "Declined"],
            new ActivityActivationRequirement("clr", "Payments.Charge"));

    private static ActivityAttempt Attempt() =>
        new("attempt-1", "invocation-1", 1, ActivityAttemptReason.Initial, DateTimeOffset.UtcNow);

    private sealed record ChargeResult(string ReceiptId, bool Approved);

    private sealed class RecordingExternalPayloadStore : IExternalPayloadStore
    {
        public List<ExternalPayloadWriteRequest> Writes { get; } = [];

        public ValueTask<DurableValueExternalReference> WriteAsync(
            ExternalPayloadWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            Writes.Add(request);
            return ValueTask.FromResult(new DurableValueExternalReference(
                request.StorageProfile,
                $"payloads/{request.OwnerKey}",
                new Dictionary<string, string>()));
        }

        public ValueTask<System.Text.Json.JsonElement> ReadAsync(
            DurableValueExternalReference reference,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
