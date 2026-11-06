using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Dispatch;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Incidents;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Trusted-gate component tests; the policy double is not a qualified scenario/provider implementation.</summary>
public sealed class RuntimeTestEffectGateTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("identity")]
    [InlineData("outside")]
    [InlineData("expanded")]
    public async Task Invalid_or_outside_context_never_reaches_the_policy_owner(string change)
    {
        var f = await Fixture.CreateAsync();
        var context = change switch
        {
            "missing" => null,
            "identity" => new RuntimeTestContextState("different", f.Entry.ContextFingerprint, [], [], ["selected"]),
            "expanded" => new RuntimeTestContextState(f.Entry.ContextId, f.Entry.ContextFingerprint, [], [], ["selected", "outside"]),
            _ => f.Header.TestContext
        };
        await f.Workflows.SaveAsync(f.Header with { TestContext = context });
        var invocation = f.Invocation;
        if (change == "outside")
        {
            await f.Activities.SaveAsync(f.Activity with { Execution = f.Activity.Execution with { ExecutableNodeId = "outside" } });
            invocation = invocation with { ExecutableNodeId = "outside" };
        }
        var error = await Assert.ThrowsAsync<RuntimeTestEffectUnavailableException>(() =>
            f.Gate().BeginAsync(f.Request with { Invocation = invocation }).AsTask());
        Assert.Equal("runtime.test-effects.owned-context-mismatch", error.Code);
        Assert.Equal(0, f.Policy.Begins);
    }

    [Fact]
    public async Task Same_node_id_with_changed_bindings_is_refused_before_input_evaluation()
    {
        var f = await Fixture.CreateAsync();
        var altered = new ExecutableNode("selected", "selected", "test", "1", f.Node.Descriptor,
            new Dictionary<string, RuntimeInputBinding>
            {
                ["injected"] = new("injected", new("System.Int32"), ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.Expression, expression: new("opaque", "doIO()"))
            }, new Dictionary<string, string>(), activityContract: f.Node.ActivityContract);
        var error = await Assert.ThrowsAsync<RuntimeTestEffectUnavailableException>(() => f.Gate().ValidateNodeAsync(
            new("workflow", "activity", f.Executable.Identity, altered)).AsTask());
        Assert.Equal("runtime.test-effects.node-mismatch", error.Code);
        Assert.Equal(0, f.Policy.Nodes);
        await f.Gate().ValidateNodeAsync(new("workflow", "activity", f.Executable.Identity, f.Node));
        Assert.Equal(1, f.Policy.Nodes);
    }

    [Fact]
    public async Task Wrong_activation_contract_cannot_borrow_a_qualified_nodes_constructor_permission()
    {
        var f = await Fixture.CreateAsync();
        var wrong = Contract("different-constructor");
        var error = await Assert.ThrowsAsync<RuntimeTestEffectUnavailableException>(() => f.Gate().ValidateActivityAsync(
            new("workflow", "activity", "attempt", wrong, f.Node.Descriptor)).AsTask());
        Assert.Equal("runtime.test-effects.activation-mismatch", error.Code);
        Assert.Equal(0, f.Policy.Activations);
    }

    [Fact]
    public async Task Unqualified_activation_is_refused_before_the_real_activator_enters_any_constructor_strategy()
    {
        var f = await Fixture.CreateAsync();
        f.Policy.ActivationRefused = true;
        var strategy = new ConstructorCounter();
        var activator = new ActivityActivator(f.Gate(), [strategy], new ActivityInputHydrator(),
            new ActivitySecretInputResolver(null!, null!, null!, null!));
        var snapshot = new ActivityInputSnapshot("activity", f.Node.ActivityContract!.SchemaFingerprint, "bindings",
            new Dictionary<string, ValueEnvelope>(), DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<RuntimeTestEffectUnavailableException>(() => activator.ActivateAsync(
            new("workflow", "activity", f.Node.ActivityContract, snapshot, Assert.Single(f.Activity.Attempts!), Descriptor: f.Node.Descriptor)).AsTask());
        Assert.Equal(0, strategy.Constructors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Missing_or_duplicate_retained_policy_never_selects_ordinary_effects(int count)
    {
        var f = await Fixture.CreateAsync();
        var error = await Assert.ThrowsAsync<RuntimeTestEffectUnavailableException>(() =>
            f.Gate(Enumerable.Repeat<IRuntimeTestEffectPolicySource>(f.Policy, count)).BeginAsync(f.Request).AsTask());
        Assert.Equal("runtime.test-effects.policy-unavailable", error.Code);
        Assert.Equal(0, f.Policy.Begins);
    }

    [Fact]
    public async Task Policy_cannot_downgrade_a_testing_invocation_to_ordinary_or_reuse_an_uncertain_operation()
    {
        var f = await Fixture.CreateAsync();
        f.Policy.Downgrade = true;
        var error = await Assert.ThrowsAsync<RuntimeTestEffectUnavailableException>(() => f.Gate().BeginAsync(f.Request).AsTask());
        Assert.Equal("runtime.test-effects.admission-mismatch", error.Code);
        f.Policy.Downgrade = false;
        f.Policy.CanExecute = false;
        var prior = await f.Gate().BeginAsync(f.Request);
        Assert.False(prior.CanExecute);
        error = await Assert.ThrowsAsync<RuntimeTestEffectUnavailableException>(() => f.Gate().RevalidateAsync(prior).AsTask());
        Assert.Equal("runtime.test-effects.replay-unavailable", error.Code);
    }

    [Fact]
    public async Task Closure_and_revocation_stop_new_work_but_preserve_exact_previously_admitted_settlement()
    {
        var f = await Fixture.CreateAsync();
        var admission = await f.Gate().BeginAsync(f.Request);
        await f.Scopes.CloseAsync(new(f.Entry.Scope.ScopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, DateTimeOffset.UtcNow));
        f.Policy.Revoked = true;
        await f.Workflows.SaveAsync(f.Header with { Status = WorkflowExecutionStatus.Cancelled, TestContext = f.Header.TestContext!.Close() });
        await Assert.ThrowsAsync<RuntimeTestEffectUnavailableException>(() => f.Gate().RevalidateAsync(admission).AsTask());
        await f.Gate().CompleteAsync(new(admission, RuntimeTestEffectDisposition.Unknown));
        Assert.Equal(1, f.Policy.Completions);
    }

    [Fact]
    public async Task Stale_attempt_cannot_admit()
    {
        var f = await Fixture.CreateAsync();
        var error = await Assert.ThrowsAsync<RuntimeTestEffectUnavailableException>(() =>
            f.Gate().BeginAsync(f.Request with { Invocation = f.Invocation with { AttemptId = "old-attempt" } }).AsTask());
        Assert.Equal("runtime.test-effects.invocation-mismatch", error.Code);
        Assert.Equal(0, f.Policy.Begins);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Provider_failure_retains_original_cause_without_exposing_it_through_fault_projection(bool returnsValue, bool asynchronous)
    {
        var f = await Fixture.CreateAsync();
        var privateValue = $"private-provider-{Guid.NewGuid():N}";
        var cause = new IOException(privateValue);
        f.Policy.ProviderFailure = cause;
        f.Policy.AsynchronousFailure = asynchronous;

        var error = returnsValue
            ? await Assert.ThrowsAsync<RuntimeTestEffectUnavailableException>(() => f.Gate().BeginAsync(f.Request).AsTask())
            : await Assert.ThrowsAsync<RuntimeTestEffectUnavailableException>(() => f.Gate().ValidateNodeAsync(
                new("workflow", "activity", f.Executable.Identity, f.Node)).AsTask());

        Assert.Equal("runtime.test-effects.infrastructure-failed", error.Code);
        Assert.Same(cause, error.InnerException);
        Assert.DoesNotContain(privateValue, error.Message, StringComparison.Ordinal);

        // The normal runtime records both captures. Provider text need not be a registered activity secret.
        // Masking therefore cannot be the only protection for the private diagnostic cause.
        foreach (var captureStackTrace in new[] { false, true })
        foreach (var projectedException in new Exception[] { error, new SecretMaskedException(error, text => text) })
        {
            var policy = new DefaultRuntimeFaultCapturePolicy(Options.Create(new RuntimeFaultCaptureOptions { CaptureStackTrace = captureStackTrace }));
            var outer = policy.Capture(projectedException);
            var inner = policy.CaptureInner(projectedException);
            Assert.Equal(typeof(RuntimeTestEffectUnavailableException).FullName, outer.ExceptionType);
            Assert.Null(inner);
            Assert.DoesNotContain(privateValue, JsonSerializer.Serialize(new { outer, inner }), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Ordinary_invocations_do_not_require_a_testing_policy_or_change_their_secret_reference()
    {
        var f = await Fixture.CreateAsync();
        await f.Workflows.SaveAsync(f.Header with { TestEntry = null, TestContext = null, TestScope = null, RunKind = WorkflowRunKind.PublishedRun });
        var gate = f.Gate([]);
        var result = await gate.BeginAsync(f.Request);
        Assert.Equal(RuntimeTestEffectMode.Ordinary, result.Mode);
        Assert.True(result.CanExecute);
        var original = new RuntimeSecretReference("ordinary-reference");
        Assert.Equal(original, await gate.MapSecretAsync(f.Invocation, "value", original));
        await gate.RevalidateAsync(result);
        await gate.CompleteAsync(new(result, RuntimeTestEffectDisposition.Completed));
        Assert.Equal(0, f.Policy.Begins);
    }

    private static ActivityContract Contract(string type) => new(type, "1", "test-consumer", JsonSerializer.SerializeToElement(new { }), [],
        new(new("object"), true, ActivityValuePolicy.Default with { Lifecycle = ActivityValueLifecycle.Result }, []), ["Done"], new("test-consumer", type));

    private sealed class Fixture
    {
        public InMemoryWorkflowExecutionStateStore Workflows { get; } = new();
        public InMemoryActivityExecutionStateStore Activities { get; } = new();
        public InMemoryWorkflowExecutableStore Executables { get; } = new();
        public InMemoryWorkflowTestScopeStore Scopes { get; } = new();
        public required WorkflowExecutionState Header { get; init; }
        public required ActivityExecutionState Activity { get; init; }
        public required WorkflowExecutable Executable { get; init; }
        public required ExecutableNode Node { get; init; }
        public required WorkflowTestEntry Entry { get; init; }
        public required Policy Policy { get; init; }
        public RuntimeTestInvocation Invocation => new("workflow", "activity", "attempt", "selected");
        public RuntimeTestEffectRequest Request => new(Invocation, "test.http", "logical-http-operation", "request-digest");
        public RuntimeTestEffectGate Gate(IEnumerable<IRuntimeTestEffectPolicySource>? policies = null) =>
            new(Workflows, Activities, Executables, Scopes, [Policy], policies ?? [Policy], TimeProvider.System);

        public static async Task<Fixture> CreateAsync()
        {
            var now = DateTimeOffset.UtcNow;
            var node = new ExecutableNode("selected", "selected", "test", "1", new RuntimeActivityDescriptor("test-consumer", "1", JsonSerializer.SerializeToElement(new { })),
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), activityContract: Contract("test"));
            var root = new ExecutableNode("root", "root", "test", "1", "test-root", JsonSerializer.SerializeToElement(new { }),
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), childSlots: [new("children", [node, WorkflowExecutionHarness.NewProbeNode("outside")])]);
            var executable = WorkflowExecutionHarness.NewExecutable(root);
            var scope = new WorkflowTestScope("effects-scope", now.AddMinutes(10), null, new(WorkflowExecutionPartition.DefaultValue));
            var entry = new WorkflowTestEntry("effect-context", "context-fingerprint", executable.Identity, "selected", scope, new("tests", "tests"));
            var header = new WorkflowExecutionState("workflow", executable.Identity, WorkflowExecutionStatus.Running, null, now, now, now, null, null, null, null, new Dictionary<string, string>())
            { TestEntry = entry, TestContext = new(entry.ContextId, entry.ContextFingerprint, [], [], ["selected"]), TestScope = scope,
                RunKind = WorkflowRunKind.TestRun, Authority = entry.Authority, Partition = scope.Partition };
            var activity = new ActivityExecutionState(new("activity", "workflow", "selected", "selected", "test", "1"), ActivityExecutionStatus.Running,
                null, 1, now, now, null, null, null, null, null, ActivitySchedulingProvenance.Empty, null, [], [], 0, 0,
                new Dictionary<string, string>(), Attempts: [new("attempt", "activity", 1, ActivityAttemptReason.Initial, now)]);
            var f = new Fixture { Header = header, Activity = activity, Executable = executable, Node = node, Entry = entry, Policy = new(entry) };
            await f.Executables.SaveAsync(executable);
            await f.Workflows.SaveAsync(header);
            await f.Activities.SaveAsync(activity);
            await f.Scopes.CreateAsync(scope, now);
            return f;
        }
    }

    private sealed class Policy(WorkflowTestEntry entry) : IRuntimeTestEffectPolicySource, IWorkflowTestEntryContextResolver
    {
        public int Nodes, Activations, Begins, Completions;
        public bool Downgrade, Revoked, ActivationRefused, CanExecute = true, AsynchronousFailure;
        public Exception? ProviderFailure;
        public ValueTask<WorkflowTestEntry?> FindAsync(string contextId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowTestEntry?>(!Revoked && contextId == entry.ContextId ? entry : null);
        public ValueTask ValidateNodeAsync(WorkflowTestEntry accepted, RuntimeTestNodeRequest request, CancellationToken cancellationToken = default)
        {
            Nodes++;
            if (ProviderFailure is { } failure)
            {
                if (AsynchronousFailure) return ValueTask.FromException(failure);
                throw failure;
            }
            return ValueTask.CompletedTask;
        }
        public ValueTask ValidateActivityAsync(WorkflowTestEntry accepted, RuntimeTestInvocation invocation, CancellationToken cancellationToken = default)
        {
            Activations++;
            if (ActivationRefused) throw new RuntimeTestEffectUnavailableException("test-only.activation-unqualified", "The activity has no qualified profile.");
            return ValueTask.CompletedTask;
        }
        public ValueTask<RuntimeSecretReference> MapSecretAsync(WorkflowTestEntry accepted, RuntimeTestInvocation invocation, string inputKey,
            RuntimeSecretReference reference, CancellationToken cancellationToken = default) => ValueTask.FromResult(new RuntimeSecretReference("mapped-test-reference"));
        public ValueTask<RuntimeTestEffectAdmission> BeginAsync(WorkflowTestEntry accepted, RuntimeTestEffectRequest request, CancellationToken cancellationToken = default)
        {
            Begins++;
            if (ProviderFailure is { } failure)
            {
                if (AsynchronousFailure) return ValueTask.FromException<RuntimeTestEffectAdmission>(failure);
                throw failure;
            }
            return ValueTask.FromResult(new RuntimeTestEffectAdmission(request, Downgrade ? RuntimeTestEffectMode.Ordinary : RuntimeTestEffectMode.Substitute,
                "test-only-profile", JsonSerializer.SerializeToElement(new { }), accepted.ContextId, accepted.ContextFingerprint, "receipt", 0, CanExecute));
        }
        public ValueTask RevalidateAsync(WorkflowTestEntry accepted, RuntimeTestEffectAdmission admission, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask CompleteAsync(WorkflowTestEntry accepted, RuntimeTestEffectCompletion completion, CancellationToken cancellationToken = default)
        { Completions++; return ValueTask.CompletedTask; }
    }

    private sealed class ConstructorCounter : IActivityActivationStrategy
    {
        public int Constructors;
        public string ConsumerKey => "test-consumer";
        public IReadOnlyCollection<string> SupportedSchemaVersions => ["1"];
        public bool RequiresInputHydration => true;
        public ValueTask<ActivityActivationLease> ActivateAsync(ActivityActivationStrategyRequest request, CancellationToken cancellationToken = default)
        { Constructors++; throw new InvalidOperationException("A constructor strategy ran without permission."); }
    }
}
