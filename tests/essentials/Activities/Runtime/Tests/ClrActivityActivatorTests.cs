using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Primitives.Activation;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class ClrActivityActivatorTests : IAsyncDisposable
{
    private const string WorkflowExecutionId = "wfexec-1";
    private const string Partition = "tenant-a";
    private const string ReferenceName = SecretResolutionTestSupport.ReferenceName;
    private const string ResolverSentinel = "resolver-detail-sentinel";
    private const string CleanupMessage = "Cancellation cleanup failed.";
    private static readonly ValueTypeDescriptor StringType = new("String");
    private static readonly ValueTypeDescriptor Int32Type = new("Int32");

    private readonly ServiceProvider _root = Services().BuildServiceProvider();

    // Secret resolution: the instance runs under partition 'tenant-a' and records no tenant of its own.
    private readonly FakeRuntimeSecretResolver _resolver = new();
    private readonly CountingPartitionAccessor _partition = new(Partition);
    private readonly SingleInstanceStateStore _instances = new(Instance(tenantId: null));
    private readonly RecordingConversionExecutor _conversions = new();

    public ClrActivityActivatorTests() => ScopedDependency.Reset();

    public ValueTask DisposeAsync() => _root.DisposeAsync();

    [Fact]
    public async Task Each_attempt_gets_a_fresh_hydrated_activity_and_scoped_service()
    {
        var (activator, contract) = Activator(_root);

        await using var first = await activator.ActivateAsync(Request(contract, "attempt-1", "hello"));
        await using var second = await activator.ActivateAsync(Request(contract, "attempt-2", "hello"));
        var firstActivity = Assert.IsType<ServiceBearingActivity>(first.Activity);
        var secondActivity = Assert.IsType<ServiceBearingActivity>(second.Activity);

        Assert.NotSame(firstActivity, secondActivity);
        Assert.NotSame(firstActivity.Dependency, secondActivity.Dependency);
        Assert.Equal("hello", firstActivity.Message);
        Assert.Equal("hello", secondActivity.Message);

        await first.DisposeAsync();
        await second.DisposeAsync();
        Assert.Equal(2, ScopedDependency.DisposeCount);
    }

    [Fact]
    public async Task Hydration_failure_disposes_the_attempt_scope()
    {
        var (activator, contract) = Activator(_root);
        var snapshot = new ActivityInputSnapshot(
            "invocation-1",
            contract.SchemaFingerprint,
            "bindings",
            new Dictionary<string, ValueEnvelope>(),
            DateTimeOffset.UtcNow);
        var request = new ActivityActivationRequest(
            WorkflowExecutionId,
            contract,
            snapshot,
            new ActivityAttempt("attempt-1", "invocation-1", 1, ActivityAttemptReason.Initial, DateTimeOffset.UtcNow),
            Descriptor: RuntimeDescriptor(contract));

        await Assert.ThrowsAsync<InvalidOperationException>(() => activator.ActivateAsync(request).AsTask());
        Assert.Equal(1, ScopedDependency.DisposeCount);
    }

    [Fact]
    public async Task A_value_withheld_because_its_policy_requires_encryption_is_refused_before_the_activity_is_created()
    {
        // Such a value cannot be recovered, so hydrating would have to invent one; a composed resolver changes nothing
        // (spec 188, T008, T018).
        var exception = await Assert.ThrowsAsync<WithheldValueException>(() =>
            SecretActivator().ActivateAsync(WithheldRequest(new WithheldValue(WithheldValueKind.PolicyRequiresEncryption))).AsTask());

        Assert.Equal("VF-ACT-010: Activity input 'message' was withheld and is not resolved in this host.", exception.Message);
        Assert.Equal(0, ScopedDependency.DisposeCount);
        Assert.Empty(_resolver.Requests);
    }

    [Fact]
    public async Task A_secret_reference_in_a_host_without_a_resolver_is_refused_as_a_missing_capability()
    {
        // A missing resolver is a composition fault, which parks the activity, not a resolution failure (T031). The
        // runtime composes the activator's secret input collaborator without a resolver.
        var activator = NoResolverActivator();

        var exception = await Assert.ThrowsAsync<RuntimeSecretResolverNotFoundException>(() =>
            activator.ActivateAsync(WithheldRequest(Withheld())).AsTask());

        Assert.Equal("message", exception.InputKey);
        Assert.Equal(0, ScopedDependency.DisposeCount);
        Assert.Equal(0, _instances.Reads);
    }

    [Fact]
    public async Task An_unrecoverable_withheld_input_is_refused_before_a_missing_resolver_is_reported()
    {
        // Composing a resolver would not repair this activation, so it must fault rather than park.
        var activator = NoResolverActivator();
        var contract = Contract(typeof(TwoInputActivity), "first", "second");

        var exception = await Assert.ThrowsAsync<WithheldValueException>(() => activator.ActivateAsync(Request(contract, new Dictionary<string, ValueEnvelope>
        {
            ["first"] = SecretResolutionTestSupport.Withheld(),
            ["second"] = ValueEnvelope.Withheld(StringType, new WithheldValue(WithheldValueKind.PolicyRequiresEncryption), SecretBindingTestSupport.SecretPolicy)
        })).AsTask());

        Assert.Equal("VF-ACT-010: Activity input 'second' was withheld and is not resolved in this host.", exception.Message);
    }

    [Fact]
    public async Task A_withheld_input_is_refused_for_a_strategy_that_does_not_hydrate_inputs()
    {
        // A strategy that skips hydration (graph activation) never reads the input, so only the activator's own
        // refusal stops a withheld value from passing silently, even with a resolver composed (spec 188, T008).
        var strategy = new NonHydratingStrategy();
        var activator = new ActivityActivator([strategy], new ActivityInputHydrator(), ResolvingSecretInputResolver(_partition));
        var contract = Contract(typeof(ServiceBearingActivity), "message");

        var exception = await Assert.ThrowsAsync<WithheldValueException>(() => activator.ActivateAsync(WithheldRequest(
            Withheld(),
            contract,
            new RuntimeActivityDescriptor(NonHydratingStrategy.Key, RuntimeActivityDescriptor.InitialSchemaVersion, contract.DescriptorPayload))).AsTask());

        Assert.Equal("VF-ACT-010: Activity input 'message' was withheld and is not resolved in this host.", exception.Message);
        Assert.Equal(0, strategy.Activations);
        Assert.Empty(_resolver.Requests);
    }

    [Fact]
    public async Task A_withheld_secret_is_resolved_for_the_partition_converted_with_its_plan_and_hydrated()
    {
        var request = WithheldRequest(Withheld());

        await using var lease = await SecretActivator().ActivateAsync(request);

        var resolution = Assert.Single(_resolver.Requests);
        Assert.Equal(Partition, resolution.TenantId);
        Assert.Equal(SecretResolutionTestSupport.Reference(), resolution.Reference);
        var conversion = Assert.Single(_conversions.Conversions);
        Assert.Same(SecretResolutionTestSupport.TextPlan, conversion.Plan);
        Assert.Equal(ValuePresence.Present, conversion.Source.Presence);
        Assert.Equal(FakeRuntimeSecretResolver.ValueOf(ReferenceName), conversion.Source.InlineValue!.Value.GetString());
        Assert.Equal(FakeRuntimeSecretResolver.ValueOf(ReferenceName), Assert.IsType<ServiceBearingActivity>(lease.Activity).Message);
    }

    [Fact]
    public async Task Activation_writes_nothing_back_to_the_request_snapshot()
    {
        var request = WithheldRequest(Withheld());

        await using var lease = await SecretActivator().ActivateAsync(request);

        Assert.Equal(FakeRuntimeSecretResolver.ValueOf(ReferenceName), Assert.IsType<ServiceBearingActivity>(lease.Activity).Message);
        var envelope = Assert.Single(request.Inputs.Values).Value;
        Assert.Equal(ValuePresence.Withheld, envelope.Presence);
        Assert.Null(envelope.InlineValue);
        Assert.DoesNotContain(FakeRuntimeSecretResolver.ValueOf(ReferenceName), JsonSerializer.Serialize(request.Inputs), StringComparison.Ordinal);
    }

    public static TheoryData<PersistenceAccessContext> ContextsWithoutOnePartition =>
    [
        PersistenceAccessContext.Global,
        PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("maintenance"))
    ];

    [Theory]
    [MemberData(nameof(ContextsWithoutOnePartition))]
    public async Task A_context_without_one_partition_refuses_before_the_resolver_is_called(PersistenceAccessContext context)
    {
        // The runtime's own partition accessor, bound to the context, is what refuses: a double here would prove only itself.
        await using var runtime = new ServiceCollection().AddWorkflowRuntime().BuildServiceProvider();
        await using var scope = runtime.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(context);
        var activator = SecretActivator(scope.ServiceProvider.GetRequiredService<IWorkflowExecutionPartitionAccessor>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => activator.ActivateAsync(WithheldRequest(Withheld())).AsTask());

        Assert.Empty(_resolver.Requests);
        Assert.Equal(0, _instances.Reads);
        Assert.Equal(1, ScopedDependency.DisposeCount);
    }

    [Fact]
    public async Task An_instance_whose_tenant_differs_from_the_partition_refuses_with_TenantMismatch_before_the_resolver_is_called()
    {
        _instances.Instance = Instance(tenantId: "tenant-b");

        var exception = await Assert.ThrowsAsync<RuntimeSecretResolutionException>(() =>
            SecretActivator().ActivateAsync(WithheldRequest(Withheld())).AsTask());

        Assert.Equal(RuntimeSecretResolutionException.TenantMismatch, exception.FailureCode);
        Assert.False(exception.IsRetryable);
        Assert.Equal($"Secret '{ReferenceName}' could not be resolved (TenantMismatch).", exception.Message);
        Assert.Empty(_resolver.Requests);
        Assert.Equal(1, ScopedDependency.DisposeCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Partition)]
    public async Task An_instance_without_a_differing_tenant_resolves_under_the_partition(string? instanceTenantId)
    {
        _instances.Instance = Instance(instanceTenantId);

        await using var lease = await SecretActivator().ActivateAsync(WithheldRequest(Withheld()));

        Assert.Equal(Partition, Assert.Single(_resolver.Requests).TenantId);
        Assert.Equal(1, _instances.Reads);
    }

    [Fact]
    public async Task An_instance_the_partition_cannot_read_refuses_before_the_resolver_is_called()
    {
        _instances.Instance = null;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SecretActivator().ActivateAsync(WithheldRequest(Withheld())).AsTask());

        Assert.Contains($"Workflow execution '{WorkflowExecutionId}' is not found", exception.Message, StringComparison.Ordinal);
        Assert.Empty(_resolver.Requests);
    }

    [Fact]
    public async Task A_snapshot_without_withheld_inputs_never_calls_the_resolver_or_reads_the_partition_or_the_instance()
    {
        await using var lease = await SecretActivator().ActivateAsync(Request(Contract(typeof(ServiceBearingActivity), "message"), "attempt-1", "hello"));

        Assert.Equal("hello", Assert.IsType<ServiceBearingActivity>(lease.Activity).Message);
        Assert.Empty(_resolver.Requests);
        Assert.Equal(0, _partition.Reads);
        Assert.Equal(0, _instances.Reads);
    }

    [Fact]
    public async Task Two_secret_inputs_resolve_independently()
    {
        var contract = Contract(typeof(TwoInputActivity), "first", "second");

        await using var lease = await SecretActivator().ActivateAsync(Request(contract, new Dictionary<string, ValueEnvelope>
        {
            ["first"] = SecretResolutionTestSupport.Withheld("payments.api-key"),
            ["second"] = SecretResolutionTestSupport.Withheld("payments.webhook-key")
        }));

        var activity = Assert.IsType<TwoInputActivity>(lease.Activity);
        Assert.Equal(FakeRuntimeSecretResolver.ValueOf("payments.api-key"), activity.First);
        Assert.Equal(FakeRuntimeSecretResolver.ValueOf("payments.webhook-key"), activity.Second);
        Assert.Equal(["payments.api-key", "payments.webhook-key"], _resolver.Requests.Select(request => request.Reference.Name));
        Assert.Equal(1, _instances.Reads);
    }

    [Theory]
    [InlineData("NotFound", false)]
    [InlineData("Revoked", false)]
    [InlineData("StoreUnavailable", true)]
    public async Task A_failed_resolution_faults_with_the_reference_name_code_and_retryable_flag_and_no_value(string failureCode, bool isRetryable)
    {
        _resolver.Respond = (_, _) => RuntimeSecretResolution.Failure(failureCode, isRetryable);

        var exception = await Assert.ThrowsAsync<RuntimeSecretResolutionException>(() =>
            SecretActivator().ActivateAsync(WithheldRequest(Withheld())).AsTask());

        Assert.Equal(ReferenceName, exception.ReferenceName);
        Assert.Equal(failureCode, exception.FailureCode);
        Assert.Equal(isRetryable, exception.IsRetryable);
        Assert.Equal($"Secret '{ReferenceName}' could not be resolved ({failureCode}).", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Equal(1, ScopedDependency.DisposeCount);
    }

    [Fact]
    public async Task A_conversion_failure_reports_ConversionFailed_and_drops_the_conversion_message()
    {
        // Publish pins no plan from text that can fail on a string, so only a double reaches this branch (T031).
        const string conversionDetail = "rejected the resolved text";
        _conversions.Failure = plan => new RuntimeValueConversionException(plan, conversionDetail);

        var exception = await Assert.ThrowsAsync<RuntimeSecretResolutionException>(() =>
            SecretActivator().ActivateAsync(WithheldRequest(Withheld())).AsTask());

        Assert.Equal(RuntimeSecretResolutionException.ConversionFailed, exception.FailureCode);
        Assert.False(exception.IsRetryable);
        Assert.Equal($"Secret '{ReferenceName}' could not be resolved (ConversionFailed).", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Equal(1, ScopedDependency.DisposeCount);
    }

    [Fact]
    public async Task A_secret_withheld_without_a_conversion_plan_reports_ConversionFailed()
    {
        var exception = await Assert.ThrowsAsync<RuntimeSecretResolutionException>(() =>
            SecretActivator().ActivateAsync(WithheldRequest(WithheldValue.SecretReference(SecretResolutionTestSupport.Reference(), conversionPlan: null))).AsTask());

        Assert.Equal(RuntimeSecretResolutionException.ConversionFailed, exception.FailureCode);
        Assert.Empty(_conversions.Conversions);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_canceled_resolution_is_not_reported_as_a_resolution_failure(bool resolverThrows)
    {
        // Whether the resolver honors the token or answers with a failure once it is canceled, the activation reports
        // the cancellation, which no handler records as a fault.
        using var cancellation = new CancellationTokenSource();
        var resolverToken = CancellationToken.None;
        _resolver.Respond = (_, token) =>
        {
            resolverToken = token;
            cancellation.Cancel();
            if (resolverThrows)
                token.ThrowIfCancellationRequested();
            return RuntimeSecretResolution.Failure("StoreUnavailable", isRetryable: true);
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SecretActivator().ActivateAsync(WithheldRequest(Withheld()), cancellation.Token).AsTask());

        Assert.Single(_resolver.Requests);
        Assert.True(resolverToken.IsCancellationRequested, "The resolver must receive the activation's cancellation token.");
        Assert.Equal(1, ScopedDependency.DisposeCount);
    }

    public static TheoryData<Exception> ResolverThrows =>
    [
        new InvalidOperationException(ResolverSentinel, new InvalidOperationException(ResolverSentinel)),
        // A cancellation-typed exception from the resolver's own store or HTTP timeout is not the activation's cancellation.
        new TaskCanceledException(ResolverSentinel, new InvalidOperationException(ResolverSentinel))
    ];

    [Theory]
    [MemberData(nameof(ResolverThrows))]
    public async Task A_resolver_that_throws_while_the_activation_is_live_reports_ResolverFailed_without_what_it_threw(Exception thrown)
    {
        // A resolver may only throw a cancellation of the activation; anything else may carry the value or store detail.
        _resolver.Respond = (_, _) => throw thrown;

        var exception = await Assert.ThrowsAsync<RuntimeSecretResolutionException>(() =>
            SecretActivator().ActivateAsync(WithheldRequest(Withheld())).AsTask());

        Assert.Equal(RuntimeSecretResolutionException.ResolverFailed, exception.FailureCode);
        Assert.False(exception.IsRetryable);
        Assert.Equal($"Secret '{ReferenceName}' could not be resolved (ResolverFailed).", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(ResolverSentinel, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(thrown.GetType().Name, exception.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, ScopedDependency.DisposeCount);
    }

    [Fact]
    public async Task A_resolver_that_returns_null_reports_ResolverFailed()
    {
        _resolver.Respond = (_, _) => null!;

        var exception = await Assert.ThrowsAsync<RuntimeSecretResolutionException>(() =>
            SecretActivator().ActivateAsync(WithheldRequest(Withheld())).AsTask());

        Assert.Equal(ReferenceName, exception.ReferenceName);
        Assert.Equal(RuntimeSecretResolutionException.ResolverFailed, exception.FailureCode);
        Assert.False(exception.IsRetryable);
        Assert.Equal(1, ScopedDependency.DisposeCount);
    }

    [Fact]
    public async Task A_cancellation_typed_conversion_failure_while_the_activation_is_live_reports_ConversionFailed()
    {
        _conversions.Failure = _ => new OperationCanceledException(ResolverSentinel);

        var exception = await Assert.ThrowsAsync<RuntimeSecretResolutionException>(() =>
            SecretActivator().ActivateAsync(WithheldRequest(Withheld())).AsTask());

        Assert.Equal(RuntimeSecretResolutionException.ConversionFailed, exception.FailureCode);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(ResolverSentinel, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_fatal_exception_from_the_resolver_propagates_instead_of_being_mapped()
    {
        var fatal = new OutOfMemoryException(ResolverSentinel);
        _resolver.Respond = (_, _) => throw fatal;

        var thrown = await Assert.ThrowsAsync<OutOfMemoryException>(() =>
            SecretActivator().ActivateAsync(WithheldRequest(Withheld())).AsTask());

        Assert.Same(fatal, thrown);
    }

    [Fact]
    public async Task A_fatal_exception_from_the_conversion_propagates_instead_of_being_mapped()
    {
        var fatal = new OutOfMemoryException(ResolverSentinel);
        _conversions.Failure = _ => fatal;

        var thrown = await Assert.ThrowsAsync<OutOfMemoryException>(() =>
            SecretActivator().ActivateAsync(WithheldRequest(Withheld())).AsTask());

        Assert.Same(fatal, thrown);
    }

    [Fact]
    public async Task A_classified_resolution_failure_keeps_its_classification_when_lease_disposal_also_fails()
    {
        _resolver.Respond = (_, _) => RuntimeSecretResolution.Failure("StoreUnavailable", isRetryable: true);
        var activator = new ActivityActivator([new FailingDisposalStrategy()], new ActivityInputHydrator(), ResolvingSecretInputResolver(_partition));

        var exception = await Assert.ThrowsAnyAsync<AggregateException>(() => activator.ActivateAsync(WithheldRequest(Withheld())).AsTask());

        // Nothing ran: the activation failed, and the message says so.
        Assert.StartsWith("Activity activation and activation disposal both failed.", exception.Message, StringComparison.Ordinal);
        // The fault recorder reads retryability and the code through the classification contract (T032).
        var classification = Assert.IsAssignableFrom<IRuntimeFaultClassification>(exception);
        Assert.True(classification.IsRetryable);
        Assert.Equal("StoreUnavailable", classification.FailureCode);
        Assert.Collection(
            exception.InnerExceptions,
            inner => Assert.IsType<RuntimeSecretResolutionException>(inner),
            inner => Assert.Equal("Scope disposal failed.", inner.Message));
    }

    [Fact]
    public async Task A_canceled_resolution_stays_a_cancellation_when_lease_disposal_also_fails()
    {
        // A handler recognizes the activation's cancellation only as an OperationCanceledException for its token; a
        // combined cleanup failure would be recorded as a fault (contracts/runtime-secret-resolution.md).
        using var cancellation = SecretResolutionTestSupport.CancelOnNextResolution(_resolver);

        var exception = await CanceledActivationWithFailingDisposalAsync(cancellation.Token);

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal("Activity activation was canceled, and disposing its activation lease failed.", exception.Message);
        Assert.IsNotAssignableFrom<IRuntimeFaultClassification>(exception);
        Assert.IsAssignableFrom<OperationCanceledException>(exception.InnerException);
    }

    [Fact]
    public async Task A_failure_that_is_not_the_activations_cancellation_stays_a_fault_when_the_token_is_canceled_during_lease_disposal()
    {
        // Whether a failure is the activation's cancellation is decided when it is caught, before the lease is disposed:
        // a store's own operation-canceled failure while the token is live stays a failure, even when the token is
        // canceled while the lease is disposed.
        using var cancellation = new CancellationTokenSource();
        var contract = Contract(typeof(ServiceBearingActivity), "message");
        var activator = new ActivityActivator(
            [new FailingDisposalStrategy(onDispose: cancellation.Cancel)],
            new ActivityInputHydrator(),
            SecretResolutionTestSupport.NoResolverSecretInputResolver(),
            new FixedExternalPayloadStore("unused", new OperationCanceledException("The store timed out.")));

        var exception = await Assert.ThrowsAnyAsync<AggregateException>(() =>
            activator.ActivateAsync(ExternalInputRequest(contract), cancellation.Token).AsTask());

        Assert.True(cancellation.IsCancellationRequested);
        Assert.StartsWith("Activity activation and activation disposal both failed.", exception.Message, StringComparison.Ordinal);
        Assert.IsAssignableFrom<IRuntimeFaultClassification>(exception);
        Assert.Collection(
            exception.InnerExceptions,
            inner => Assert.Equal("The store timed out.", inner.Message),
            inner => Assert.Equal("Scope disposal failed.", inner.Message));
    }

    [Fact]
    public async Task Cancellation_cleanup_keeps_the_carried_disposal_failure_and_the_leases_own()
    {
        using var cancellation = SecretResolutionTestSupport.CancelOnNextResolution(_resolver);
        var carried = await CanceledActivationWithFailingDisposalAsync(cancellation.Token);

        var failure = await DisposeAfterCancellationAsync(new ActivityActivationLease(new ThrowingDisposableActivity()), carried);

        Assert.StartsWith(CleanupMessage, Assert.IsType<AggregateException>(failure).Message, StringComparison.Ordinal);
        Assert.Collection(
            ((AggregateException)failure).InnerExceptions,
            inner => Assert.Same(carried, inner),
            inner => Assert.Equal("Activity disposal failed.", inner.Message),
            inner => Assert.Equal("Scope disposal failed.", inner.Message));
    }

    [Fact]
    public async Task Cancellation_cleanup_reports_the_leases_disposal_failure_with_the_cancellation()
    {
        var cancellation = new OperationCanceledException();

        var failure = await DisposeAfterCancellationAsync(new ActivityActivationLease(new ThrowingDisposableActivity()), cancellation);

        Assert.Collection(
            Assert.IsType<AggregateException>(failure).InnerExceptions,
            inner => Assert.Same(cancellation, inner),
            inner => Assert.Equal("Activity disposal failed.", inner.Message));
    }

    [Fact]
    public async Task Cancellation_cleanup_reports_nothing_when_no_disposal_failed()
    {
        Assert.Null(await DisposeAfterCancellationAsync(new ActivityActivationLease(new OptionalInitializerActivity()), new OperationCanceledException()));
    }

    [Fact]
    public async Task ActivationLease_WhenActivityAndScopeDisposalFail_AttemptsBothAndAggregatesFailures()
    {
        var activity = new ThrowingDisposableActivity();
        var scope = new ThrowingAsyncDisposableScope();
        var lease = new ActivityActivationLease(activity, scope);

        var exception = await Assert.ThrowsAsync<AggregateException>(() => lease.DisposeAsync().AsTask());

        Assert.True(activity.DisposeAttempted);
        Assert.True(scope.DisposeAttempted);
        Assert.Collection(
            exception.InnerExceptions,
            inner => Assert.Equal("Activity disposal failed.", inner.Message),
            inner => Assert.Equal("Scope disposal failed.", inner.Message));
    }

    [Fact]
    public void Hydrator_rejects_a_second_write_to_the_same_activity_instance()
    {
        var dependency = new ScopedDependency();
        var activity = new ServiceBearingActivity(dependency);
        var contract = Contract(typeof(ServiceBearingActivity), "message");
        var snapshot = Snapshot(contract, "hello");
        var hydrator = new ActivityInputHydrator();

        hydrator.Hydrate(activity, contract, snapshot);

        Assert.Throws<InvalidOperationException>(() => hydrator.Hydrate(activity, contract, snapshot));
        dependency.Dispose();
    }

    [Fact]
    public void Hydrator_replays_pinned_optional_absence_across_changed_property_initializers()
    {
        var contract = new ActivityContract(
            "test/optional-initializer",
            "1.0.0",
            "test",
            JsonSerializer.SerializeToElement(new { }),
            [new ActivityInputContract("message", "Message", StringType, false, true, false, null, ActivityValuePolicy.Default)],
            new ActivityResultContract(new ValueTypeDescriptor("Unit"), false, ActivityValuePolicy.Default, []),
            ["Done"],
            new ActivityActivationRequirement("test", "test/optional-initializer"));
        var snapshot = new ActivityInputSnapshot(
            "invocation-1",
            contract.SchemaFingerprint,
            "bindings",
            new Dictionary<string, ValueEnvelope>
            {
                ["message"] = ValueEnvelope.Absent(StringType, ValueProtectionPolicy.InstanceInline)
            },
            DateTimeOffset.UtcNow);

        OptionalInitializerActivity.Initializer = "version-one-initializer";
        var firstAttempt = new OptionalInitializerActivity();
        OptionalInitializerActivity.Initializer = "version-two-initializer";
        var retriedAfterDeployment = new OptionalInitializerActivity();
        var hydrator = new ActivityInputHydrator();
        hydrator.Hydrate(firstAttempt, contract, snapshot);
        hydrator.Hydrate(retriedAfterDeployment, contract, snapshot);

        Assert.Null(firstAttempt.Message);
        Assert.Null(retriedAfterDeployment.Message);
    }

    [Fact]
    public void Hydrator_accepts_explicit_null_for_a_required_nullable_reference_input()
    {
        var contract = new ActivityContract(
            "test/required-nullable",
            "1.0.0",
            "test",
            JsonSerializer.SerializeToElement(new { }),
            [new ActivityInputContract("message", "Message", StringType, true, true, false, null, ActivityValuePolicy.Default)],
            new ActivityResultContract(new ValueTypeDescriptor("Unit"), false, ActivityValuePolicy.Default, []),
            ["Done"],
            new ActivityActivationRequirement("test", "test/required-nullable"));
        var snapshot = new ActivityInputSnapshot(
            "invocation-1",
            contract.SchemaFingerprint,
            "bindings",
            new Dictionary<string, ValueEnvelope>
            {
                ["message"] = ValueEnvelope.Null(StringType, ValueProtectionPolicy.InstanceInline)
            },
            DateTimeOffset.UtcNow);
        var activity = new RequiredNullableActivity();

        new ActivityInputHydrator().Hydrate(activity, contract, snapshot);

        Assert.Null(activity.Message);
    }

    [Fact]
    public void Hydrator_honors_pinned_non_nullable_contract_when_the_current_clr_property_is_nullable()
    {
        var contract = new ActivityContract(
            "test/pinned-non-nullable",
            "1.0.0",
            "test",
            JsonSerializer.SerializeToElement(new { }),
            [new ActivityInputContract("message", "Message", StringType, false, false, false, null, ActivityValuePolicy.Default)],
            new ActivityResultContract(new ValueTypeDescriptor("Unit"), false, ActivityValuePolicy.Default, []),
            ["Done"],
            new ActivityActivationRequirement("test", "test/pinned-non-nullable"));
        var snapshot = new ActivityInputSnapshot(
            "invocation-1",
            contract.SchemaFingerprint,
            "bindings",
            new Dictionary<string, ValueEnvelope>
            {
                ["message"] = ValueEnvelope.Null(StringType, ValueProtectionPolicy.InstanceInline)
            },
            DateTimeOffset.UtcNow);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ActivityInputHydrator().Hydrate(new RequiredNullableActivity(), contract, snapshot));

        Assert.Contains("does not accept null", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Hydrator_uses_inherited_input_key_to_assign_hidden_derived_property()
    {
        var contract = new ActivityContract(
            "test/hidden-input",
            "1.0.0",
            "test",
            JsonSerializer.SerializeToElement(new { }),
            [new ActivityInputContract("inheritedKey", "Value", StringType, false, false, false, null, ActivityValuePolicy.Default)],
            new ActivityResultContract(new ValueTypeDescriptor("Unit"), false, ActivityValuePolicy.Default, []),
            ["Done"],
            new ActivityActivationRequirement("test", "test/hidden-input"));
        var snapshot = new ActivityInputSnapshot(
            "invocation-1",
            contract.SchemaFingerprint,
            "bindings",
            new Dictionary<string, ValueEnvelope>
            {
                ["inheritedKey"] = ValueEnvelope.Inline(
                    StringType,
                    JsonSerializer.SerializeToElement("hydrated"),
                    ValueProtectionPolicy.InstanceInline)
            },
            DateTimeOffset.UtcNow);
        var activity = new HiddenInputActivity();

        new ActivityInputHydrator().Hydrate(activity, contract, snapshot);

        Assert.Equal("hydrated", activity.Value);
        Assert.Null(((HiddenInputActivityBase)activity).Value);
    }

    [Fact]
    public void Hydrator_rejects_absence_for_an_optional_non_nullable_value_input()
    {
        var contract = new ActivityContract(
            "test/optional-int32",
            "1.0.0",
            "test",
            JsonSerializer.SerializeToElement(new { }),
            [new ActivityInputContract("value", "Value", Int32Type, false, false, false, null, ActivityValuePolicy.Default)],
            new ActivityResultContract(new ValueTypeDescriptor("Unit"), false, ActivityValuePolicy.Default, []),
            ["Done"],
            new ActivityActivationRequirement("test", "test/optional-int32"));
        var snapshot = new ActivityInputSnapshot(
            "invocation-1",
            contract.SchemaFingerprint,
            "bindings",
            new Dictionary<string, ValueEnvelope>
            {
                ["value"] = ValueEnvelope.Absent(Int32Type, ValueProtectionPolicy.InstanceInline)
            },
            DateTimeOffset.UtcNow);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ActivityInputHydrator().Hydrate(new OptionalInt32Activity(), contract, snapshot));

        Assert.Contains("does not accept absence", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Hydrator_rejects_absence_and_explicit_null_for_an_optional_non_nullable_reference_input(bool absent)
    {
        var contract = new ActivityContract(
            "test/optional-non-nullable-reference",
            "1.0.0",
            "test",
            JsonSerializer.SerializeToElement(new { }),
            [new ActivityInputContract("message", "Message", StringType, false, false, false, null, ActivityValuePolicy.Default)],
            new ActivityResultContract(new ValueTypeDescriptor("Unit"), false, ActivityValuePolicy.Default, []),
            ["Done"],
            new ActivityActivationRequirement("test", "test/optional-non-nullable-reference"));
        var envelope = absent
            ? ValueEnvelope.Absent(StringType, ValueProtectionPolicy.InstanceInline)
            : ValueEnvelope.Null(StringType, ValueProtectionPolicy.InstanceInline);
        var snapshot = new ActivityInputSnapshot(
            "invocation-1",
            contract.SchemaFingerprint,
            "bindings",
            new Dictionary<string, ValueEnvelope> { ["message"] = envelope },
            DateTimeOffset.UtcNow);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ActivityInputHydrator().Hydrate(new OptionalNonNullableReferenceActivity(), contract, snapshot));

        Assert.Contains(absent ? "does not accept absence" : "does not accept null", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task External_input_is_dereferenced_only_for_activation()
    {
        var store = new FixedExternalPayloadStore("from-external-store");
        var (activator, contract) = Activator(_root, store);
        var request = ExternalInputRequest(contract);
        var persistedSnapshot = request.Inputs;

        await using var lease = await activator.ActivateAsync(request);

        Assert.Equal("from-external-store", Assert.IsType<ServiceBearingActivity>(lease.Activity).Message);
        Assert.NotNull(persistedSnapshot.Values["message"].ExternalReference);
        Assert.Null(persistedSnapshot.Values["message"].InlineValue);
        Assert.Equal("payloads/message", Assert.Single(store.Reads).Locator);
    }

    private static readonly JsonPayloadSerializer Serializer = new(new JsonPayloadConverterRegistry());

    /// <summary>A request whose single input, <c>message</c>, is stored externally.</summary>
    private static ActivityActivationRequest ExternalInputRequest(ActivityContract contract)
    {
        var policy = new ValueProtectionPolicy(
            DurableValueLifecycle.Instance,
            DurableValueStorage.External,
            isSensitive: true,
            requiresEncryption: true,
            redactionMode: "Full",
            retentionPolicy: "P30D");
        return Request(contract, new Dictionary<string, ValueEnvelope>
        {
            ["message"] = ValueEnvelope.External(
                StringType,
                new DurableValueExternalReference("encrypted", "payloads/message", new Dictionary<string, string>()),
                policy)
        });
    }

    /// <summary>
    /// Activates a withheld secret whose resolution <paramref name="cancellationToken"/>'s source cancels (see
    /// <see cref="SecretResolutionTestSupport.CancelOnNextResolution"/>) through a lease that fails to dispose, and
    /// returns the cancellation the activator throws.
    /// </summary>
    private async Task<OperationCanceledException> CanceledActivationWithFailingDisposalAsync(CancellationToken cancellationToken)
    {
        var activator = new ActivityActivator([new FailingDisposalStrategy()], new ActivityInputHydrator(), ResolvingSecretInputResolver(_partition));
        return await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            activator.ActivateAsync(WithheldRequest(Withheld()), cancellationToken).AsTask());
    }

    /// <summary>
    /// The handlers' cancellation cleanup, <c>ActivityActivationLeaseDisposer.DisposeAfterCancellationAsync</c>. It is
    /// internal to the runtime, and constitution section 2.23.3 allows no InternalsVisibleTo, so it is invoked by name.
    /// </summary>
    private static ValueTask<Exception?> DisposeAfterCancellationAsync(ActivityActivationLease lease, OperationCanceledException cancellation) =>
        (ValueTask<Exception?>)typeof(ActivityActivator).Assembly
            .GetType("Elsa.Activities.Runtime.Services.ActivityActivationLeaseDisposer", throwOnError: true)!
            .GetMethod("DisposeAfterCancellationAsync", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [lease, cancellation, CleanupMessage])!;

    private static IServiceCollection Services() =>
        new ServiceCollection().AddScoped<ScopedDependency>();

    private static (IActivityActivator Activator, ActivityContract Contract) Activator(
        IServiceProvider services,
        IExternalPayloadStore? externalPayloadStore = null) =>
        (new ActivityActivator([ClrStrategy(services)], new ActivityInputHydrator(), SecretResolutionTestSupport.NoResolverSecretInputResolver(), externalPayloadStore),
            Contract(typeof(ServiceBearingActivity), "message"));

    private ActivityActivator SecretActivator(IWorkflowExecutionPartitionAccessor? partitionAccessor = null) =>
        new([ClrStrategy(_root)], new ActivityInputHydrator(), ResolvingSecretInputResolver(partitionAccessor ?? _partition));

    /// <summary>An activator whose secret input collaborator is composed as a host without a resolver composes it.</summary>
    private ActivityActivator NoResolverActivator() =>
        new([ClrStrategy(_root)], new ActivityInputHydrator(), SecretResolutionTestSupport.NoResolverSecretInputResolver(_partition, _instances, _conversions));

    /// <summary>A secret input collaborator that resolves through <see cref="_resolver"/> under <paramref name="partitionAccessor"/>.</summary>
    private ActivitySecretInputResolver ResolvingSecretInputResolver(IWorkflowExecutionPartitionAccessor partitionAccessor) =>
        new(partitionAccessor, _instances, _conversions, _resolver);

    private static ClrActivityActivator ClrStrategy(IServiceProvider services)
    {
        var registry = new WellKnownTypeRegistry();
        foreach (var activityType in new[] { typeof(ServiceBearingActivity), typeof(TwoInputActivity) })
            registry.RegisterType(activityType, activityType.FullName!);
        return new ClrActivityActivator(services.GetRequiredService<IServiceScopeFactory>(), registry, Serializer);
    }

    private static ActivityContract Contract(Type activityType, params string[] inputKeys) =>
        new(
            activityType.FullName!,
            "1.0.0",
            typeof(ClrActivityDescriptor).FullName!,
            Serializer.SerializeToElement(new ClrActivityDescriptor(activityType.FullName!)),
            inputKeys.Select(key => new ActivityInputContract(key, key, StringType, true, false, false, null, ActivityValuePolicy.Default)),
            new ActivityResultContract(new ValueTypeDescriptor("Unit"), false, ActivityValuePolicy.Default, []),
            ["Done"],
            new ActivityActivationRequirement(typeof(ClrActivityDescriptor).FullName!, "constructor-injection"));

    private static ActivityActivationRequest Request(ActivityContract contract, string attemptId, string message) =>
        new(
            WorkflowExecutionId,
            contract,
            Snapshot(contract, message),
            new ActivityAttempt(attemptId, "invocation-1", attemptId == "attempt-1" ? 1 : 2, ActivityAttemptReason.Initial, DateTimeOffset.UtcNow),
            Descriptor: RuntimeDescriptor(contract));

    private static ActivityActivationRequest Request(ActivityContract contract, IReadOnlyDictionary<string, ValueEnvelope> values) =>
        new(
            WorkflowExecutionId,
            contract,
            new ActivityInputSnapshot("invocation-1", contract.SchemaFingerprint, "bindings", values, DateTimeOffset.UtcNow),
            new ActivityAttempt("attempt-1", "invocation-1", 1, ActivityAttemptReason.Initial, DateTimeOffset.UtcNow),
            Descriptor: RuntimeDescriptor(contract));

    /// <summary>A request whose single input, <c>message</c>, holds <paramref name="withheld"/>.</summary>
    private static ActivityActivationRequest WithheldRequest(
        WithheldValue withheld,
        ActivityContract? contract = null,
        RuntimeActivityDescriptor? descriptor = null)
    {
        contract ??= Contract(typeof(ServiceBearingActivity), "message");
        var request = Request(contract, new Dictionary<string, ValueEnvelope>
        {
            ["message"] = ValueEnvelope.Withheld(StringType, withheld, SecretBindingTestSupport.SecretPolicy)
        });
        return descriptor is null ? request : request with { Descriptor = descriptor };
    }

    private static WithheldValue Withheld() =>
        WithheldValue.SecretReference(SecretResolutionTestSupport.Reference(), SecretResolutionTestSupport.TextPlan);

    private static WorkflowExecutionState Instance(string? tenantId) =>
        new(
            WorkflowExecutionId,
            new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            WorkflowExecutionStatus.Running,
            null,
            DateTimeOffset.UnixEpoch,
            null,
            null,
            null,
            null,
            null,
            tenantId,
            new Dictionary<string, string>());

    private static RuntimeActivityDescriptor RuntimeDescriptor(ActivityContract contract) =>
        new(
            WellKnownRuntimeActivityConsumers.ClrActivity,
            RuntimeActivityDescriptor.InitialSchemaVersion,
            contract.DescriptorPayload);

    private static ActivityInputSnapshot Snapshot(ActivityContract contract, string message) =>
        new(
            "invocation-1",
            contract.SchemaFingerprint,
            "bindings",
            new Dictionary<string, ValueEnvelope>
            {
                ["message"] = ValueEnvelope.Inline(
                    StringType,
                    JsonSerializer.SerializeToElement(message),
                    ValueProtectionPolicy.InstanceInline)
            },
            DateTimeOffset.UtcNow);

    /// <summary>Records every conversion and delegates to the real executor, or throws the configured failure.</summary>
    private sealed class RecordingConversionExecutor : IRuntimeValueConversionExecutor
    {
        private readonly RuntimeValueConversionExecutor _inner = new();

        public List<(ValueEnvelope Source, ValueConversionPlan Plan)> Conversions { get; } = [];

        public Func<ValueConversionPlan, Exception>? Failure { get; set; }

        public ValueEnvelope Convert(ValueEnvelope source, ValueConversionPlan plan)
        {
            Conversions.Add((source, plan));
            return Failure is null ? _inner.Convert(source, plan) : throw Failure(plan);
        }
    }

    /// <summary>A hydrating CLR strategy whose lease fails to dispose its scope, after running <paramref name="onDispose"/>.</summary>
    private sealed class FailingDisposalStrategy(Action? onDispose = null) : IActivityActivationStrategy
    {
        public string ConsumerKey => WellKnownRuntimeActivityConsumers.ClrActivity;

        public IReadOnlyCollection<string> SupportedSchemaVersions { get; } = [RuntimeActivityDescriptor.InitialSchemaVersion];

        public bool RequiresInputHydration => true;

        public ValueTask<ActivityActivationLease> ActivateAsync(
            ActivityActivationStrategyRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityActivationLease(new OptionalInitializerActivity(), new ThrowingAsyncDisposableScope(onDispose)));
    }

    private sealed class NonHydratingStrategy : IActivityActivationStrategy
    {
        public const string Key = "test.non-hydrating";

        public int Activations { get; private set; }

        public string ConsumerKey => Key;

        public IReadOnlyCollection<string> SupportedSchemaVersions { get; } = [RuntimeActivityDescriptor.InitialSchemaVersion];

        public bool RequiresInputHydration => false;

        public ValueTask<ActivityActivationLease> ActivateAsync(
            ActivityActivationStrategyRequest request,
            CancellationToken cancellationToken = default)
        {
            Activations++;
            return ValueTask.FromResult(new ActivityActivationLease(new OptionalInitializerActivity()));
        }
    }

    private sealed class ServiceBearingActivity(ScopedDependency dependency) : Activity
    {
        public ScopedDependency Dependency { get; } = dependency;

        [ActivityInput(Key = "message")]
        public string Message { get; set; } = null!;

        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }

    private sealed class TwoInputActivity : Activity
    {
        [ActivityInput(Key = "first")]
        public string First { get; set; } = null!;

        [ActivityInput(Key = "second")]
        public string Second { get; set; } = null!;

        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }

    private sealed class OptionalInitializerActivity : Activity
    {
        public static string Initializer { get; set; } = "initializer";

        [ActivityInput(Key = "message")]
        public string? Message { get; set; } = Initializer;

        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }

    private sealed class RequiredNullableActivity : Activity
    {
        [ActivityInput(Key = "message")]
        public string? Message { get; set; } = "initializer";

        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }

    private sealed class OptionalInt32Activity : Activity
    {
        [ActivityInput(Key = "value")]
        public int Value { get; set; } = 42;

        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }

    private sealed class OptionalNonNullableReferenceActivity : Activity
    {
        [ActivityInput(Key = "message")]
        public string Message { get; set; } = "initializer";

        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }

    private abstract class HiddenInputActivityBase : Activity
    {
        [ActivityInput(Key = "inheritedKey")]
        public string? Value { get; set; }

        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }

    private sealed class HiddenInputActivity : HiddenInputActivityBase
    {
        public new string Value { get; set; } = string.Empty;
    }

    private sealed class ThrowingDisposableActivity : Activity, IDisposable
    {
        public bool DisposeAttempted { get; private set; }

        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));

        public void Dispose()
        {
            DisposeAttempted = true;
            throw new InvalidOperationException("Activity disposal failed.");
        }
    }

    private sealed class ThrowingAsyncDisposableScope(Action? onDispose = null) : IAsyncDisposable
    {
        public bool DisposeAttempted { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeAttempted = true;
            onDispose?.Invoke();
            return ValueTask.FromException(new InvalidOperationException("Scope disposal failed."));
        }
    }

    private sealed class ScopedDependency : IDisposable
    {
        private static int _disposeCount;
        public static int DisposeCount => _disposeCount;
        public static void Reset() => _disposeCount = 0;
        public void Dispose() => Interlocked.Increment(ref _disposeCount);
    }

    /// <summary>Reads <paramref name="value"/> for every reference, or throws <paramref name="failure"/> when one is given.</summary>
    private sealed class FixedExternalPayloadStore(string value, Exception? failure = null) : IExternalPayloadStore
    {
        public List<DurableValueExternalReference> Reads { get; } = [];

        public ValueTask<DurableValueExternalReference> WriteAsync(
            ExternalPayloadWriteRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<JsonElement> ReadAsync(
            DurableValueExternalReference reference,
            CancellationToken cancellationToken = default)
        {
            Reads.Add(reference);
            return failure is null ? ValueTask.FromResult(JsonSerializer.SerializeToElement(value)) : ValueTask.FromException<JsonElement>(failure);
        }
    }
}
