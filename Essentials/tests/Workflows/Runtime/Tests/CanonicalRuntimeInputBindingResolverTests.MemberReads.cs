using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// A member read (#4078) of an activity result whose producer completion is stored externally, through a named result
/// projection: the projection is applied with the shared external rule, then the member is selected exactly, and the
/// projection's protection is kept.
/// </summary>
public sealed partial class CanonicalRuntimeInputBindingResolverTests
{
    private static readonly JsonElement ExternalCustomerResult = JsonSerializer.SerializeToElement(new
    {
        customer = new { address = new { street = "Main" } }
    });

    private static readonly ActivityValuePolicy SensitiveExternalProjection = new(
        IsPersistable: true,
        IsSensitive: true,
        RequiresEncryption: false,
        RedactionMode: "Full",
        Lifecycle: ActivityValueLifecycle.Instance,
        Storage: ActivityValueStorage.External,
        StorageProfile: "external",
        RetentionPolicy: null);

    private static readonly Resolvers.RuntimeInputBindingResolver MemberSourceResolver = new();

    [Fact]
    public async Task A_member_of_an_externally_stored_result_projection_is_selected_and_keeps_the_projection_policy()
    {
        var store = new ExternalResultStore();
        var snapshot = await MaterializeExternalResultMemberAsync("customer", store);

        var value = snapshot.Values["customer-id"];
        Assert.True(value.Policy.IsSensitive);
        Assert.Equal("Full", value.Policy.RedactionMode);
        Assert.Equal("String", value.Type.Alias);
        var written = Assert.Single(store.Writes);
        Assert.Equal("Main", written.Payload.GetString());
        Assert.Equal(written.StorageProfile, value.ExternalReference!.StorageProfile);
    }

    [Fact]
    public async Task A_member_of_an_externally_stored_result_whose_projection_path_is_missing_is_refused_with_VF_ACT_004()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MaterializeExternalResultMemberAsync("nowhere", new ExternalResultStore()).AsTask());

        Assert.StartsWith("VF-ACT-004: ", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'nowhere'", exception.Message, StringComparison.Ordinal);
        Assert.IsNotType<RuntimeReadSelectionException>(exception);
    }

    [Fact]
    public async Task A_member_of_an_externally_stored_result_without_the_producer_contract_is_refused_with_VF_ACT_004()
    {
        // A replaced resolver may hand on the external result without consulting the executable; the projection rule
        // then has no producer contract to read the path from and refuses with a code, not an uncoded failure.
        var producer = ExternallyCompletedProducer();
        var consumer = RunningConsumer(producer.InvocationId);
        var materializer = new RuntimeActivityInputMaterializer(new ExternalResultPassThroughResolver(), new TestTypeRegistry(), new ExternalResultStore());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => materializer.MaterializeSnapshotAsync(
                NewConsumerNode(ExternalResultMemberBinding()), consumer.InvocationId,
                NewContext(consumer: consumer, runtimeView: [producer, consumer]), DateTimeOffset.UnixEpoch)
            .AsTask());

        Assert.StartsWith("VF-ACT-004: ", exception.Message, StringComparison.Ordinal);
        Assert.Contains("producer node 'producer'", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A whole-value read of an externally stored result through a projection whose path is <c>$</c> reads the whole
    /// stored payload, as the same projection of an inline result does.
    /// </summary>
    [Fact]
    public async Task A_whole_value_read_of_an_external_result_through_a_dollar_projection_reads_the_whole_payload()
    {
        var store = new ExternalResultStore();
        var producer = ExternallyCompletedProducer();
        var consumer = RunningConsumer(producer.InvocationId);
        var any = new ValueTypeDescriptor("Elsa.Any");
        var binding = new RuntimeInputBinding("customer-id", any, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.ActivityResult,
            activityResult: new RuntimeActivityResultReference("producer", "customer-id", producerScopeId: null));
        var context = NewContext(consumer: consumer, runtimeView: [producer, consumer],
            executable: NewProducerExecutable(SensitiveExternalProjection, any, "$"));

        await new RuntimeActivityInputMaterializer(MemberSourceResolver, new TestTypeRegistry(), store)
            .MaterializeSnapshotAsync(NewConsumerNode(binding, inputType: any), consumer.InvocationId, context, DateTimeOffset.UnixEpoch);

        var written = Assert.Single(store.Writes);
        Assert.True(JsonElement.DeepEquals(ExternalCustomerResult, written.Payload));
    }

    private static ValueTask<ActivityInputSnapshot> MaterializeExternalResultMemberAsync(string projectionPath, ExternalResultStore store)
    {
        var producer = ExternallyCompletedProducer();
        var consumer = RunningConsumer(producer.InvocationId);
        var context = NewContext(
            consumer: consumer,
            runtimeView: [producer, consumer],
            executable: NewProducerExecutable(SensitiveExternalProjection, new ValueTypeDescriptor("Elsa.Any"), projectionPath));
        return new RuntimeActivityInputMaterializer(MemberSourceResolver, new TestTypeRegistry(), store)
            .MaterializeSnapshotAsync(NewConsumerNode(ExternalResultMemberBinding()), consumer.InvocationId, context, DateTimeOffset.UnixEpoch);
    }

    private static RuntimeInputBinding ExternalResultMemberBinding() => new(
        "customer-id",
        StringType,
        ValueProtectionPolicy.InstanceInline,
        RuntimeInputBindingSource.ActivityResultMember,
        new RuntimeReadSelection(1, ["address", "street"], RuntimeReadSelectionMode.DeferredFit),
        activityResult: new RuntimeActivityResultReference("producer", "customer-id", producerScopeId: null));

    private static ActivityExecutionState ExternallyCompletedProducer() => CompletedProducer(ValueEnvelope.External(
        new ValueTypeDescriptor("test/result"),
        new DurableValueExternalReference("external", ExternalResultStore.ResultLocator, new Dictionary<string, string>()),
        ValuePolicyCombiner.ToProtectionPolicy(SensitiveExternalProjection)));

    private sealed class ExternalResultStore : IExternalPayloadStore
    {
        public const string ResultLocator = "results/producer-1";
        public List<ExternalPayloadWriteRequest> Writes { get; } = [];

        public ValueTask<DurableValueExternalReference> WriteAsync(ExternalPayloadWriteRequest request, CancellationToken cancellationToken = default)
        {
            Writes.Add(request with { Payload = request.Payload.Clone() });
            return ValueTask.FromResult(new DurableValueExternalReference(request.StorageProfile, $"payloads/{request.OwnerKey}", new Dictionary<string, string>()));
        }

        public ValueTask<JsonElement> ReadAsync(DurableValueExternalReference reference, CancellationToken cancellationToken = default) =>
            reference.Locator == ResultLocator
                ? ValueTask.FromResult(ExternalCustomerResult.Clone())
                : throw new InvalidOperationException($"No payload at '{reference.Locator}'.");
    }

    // Hands on the producer's external result envelope as the projected source, without reading the executable.
    private sealed class ExternalResultPassThroughResolver : IRuntimeInputBindingResolver
    {
        public RuntimeResolvedInput Resolve(RuntimeInputBinding binding, RuntimeInputBindingResolutionContext context) =>
            new(binding.InputName, binding.Source, null)
            {
                Envelope = context.RuntimeView.Single(state => state.InvocationId == "producer-1").Completion!.Result.Retype(binding.TargetType)
            };
    }
}
