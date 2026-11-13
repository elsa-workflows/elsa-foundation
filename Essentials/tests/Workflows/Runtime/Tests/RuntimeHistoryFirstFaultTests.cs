using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.History;
using Elsa.Workflows.Runtime.Services.Incidents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Actual fault owners through internal capture/memory composition; not a full start/drain/provider journey.</summary>
public sealed class RuntimeHistoryFirstFaultTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch.AddDays(1);
    private static readonly WorkflowExecutableIdentity Executable = new("artifact", "definition", "version", "1", "hash");

    [Theory]
    [InlineData("enabled")]
    [InlineData("unknown")]
    [InlineData("disabled")]
    [InlineData("future")]
    [InlineData("absent")]
    public async Task Refused_first_root_records_a_fault_boundary_only_for_actual_known_captured_admission(string choice)
    {
        using var f = new Fixture();
        var policy = choice is "absent" or "disabled" ? null : choice == "future"
            ? new RuntimeHistoryCapturePolicy("runtime-history-v4", 4, true, ["WorkflowFaulted"]) : f.Policy;
        bool? enabled = choice is "unknown" or "absent" ? null : choice != "disabled";
        var envelope = Envelope(policy, enabled);
        await f.Faulter.FaultIfCheckpointRuleViolatedAsync("run", null, new RuntimeCheckpointCommitValidationException("refused"), envelope);
        if (choice != "enabled")
        {
            Assert.Null(await f.Executions.FindAsync("run"));
            Assert.Empty(f.Writer.ListCommits());
            Assert.Null(await f.Reader.FindHeadAsync("run"));
            return;
        }
        await f.Faulter.FaultIfCheckpointRuleViolatedAsync("run", null, new RuntimeCheckpointCommitValidationException("refused"), envelope);
        var commit = Assert.Single(f.Writer.ListCommits()).Commit;
        Assert.True(commit.Checkpoint.HistoryStartsAtAdmission);
        Assert.True(Assert.Single(commit.HistoryBatches!).StartsAtRunStart);
        var state = await f.Executions.FindAsync("run");
        Assert.Equal(WorkflowExecutionStatus.Faulted, state!.Status);
        Assert.True(state.HistoryCaptureEnabledAtAdmission);
        Assert.True(f.Policy.IsEquivalentTo(state.HistoryCapturePolicy));
        var page = await f.Reader.ReadPageAsync(new("run"));
        Assert.True(page.Head!.CapturedFromStart);
        Assert.False(page.Head.HasGaps);
        Assert.Equal(new[] { "WorkflowFaulted", "IncidentRecorded" }, page.Items.Select(e => e.Name));
        Assert.DoesNotContain(page.Items, e => e.Name is "WorkflowStarted" or "ActivityStarted" or "ActivityCompleted");
    }

    [Fact]
    public async Task A_captured_child_keeps_the_existing_dispatch_projection_gate_before_fault_synthesis()
    {
        using var f = new Fixture();
        await f.Faulter.FaultIfCheckpointRuleViolatedAsync("run", null, new RuntimeCheckpointCommitValidationException("refused"),
            Envelope(f.Policy, true, parent: "parent"));
        Assert.Null(await f.Executions.FindAsync("run"));
        Assert.Empty(f.Writer.ListCommits());
    }

    [Fact]
    public async Task A_fault_of_previously_accepted_state_does_not_claim_the_original_admission_boundary()
    {
        using var f = new Fixture();
        await f.Executions.SaveAsync(new WorkflowExecutionState("run", Executable, WorkflowExecutionStatus.Running, null,
            At.AddHours(-1), At.AddHours(-1), At.AddHours(-1), null, null, null, null, new Dictionary<string, string>())
            { HistoryCapturePolicy = f.Policy, HistoryCaptureEnabledAtAdmission = true });
        await f.Faulter.FaultIfCheckpointRuleViolatedAsync("run", null, new RuntimeCheckpointCommitValidationException("refused"), Envelope(f.Policy, true));
        var commit = Assert.Single(f.Writer.ListCommits()).Commit;
        Assert.Null(commit.Checkpoint.HistoryStartsAtAdmission);
        Assert.False(Assert.Single(commit.HistoryBatches!).StartsAtRunStart);
        Assert.False((await f.Reader.FindHeadAsync("run"))!.CapturedFromStart);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Poisoned_first_root_preserves_known_capture_boundary_and_legacy_absence(bool captured)
    {
        using var f = new Fixture();
        var poison = f.Services.GetRequiredService<IWorkflowSchedulerPoisonStore>();
        await poison.RecordAsync(new RuntimeSchedulerPoisonRecord("run", "work", WorkflowExecutionCommandKind.RunSchedulerWork, "handler",
            new RuntimeFaultInfo("System.InvalidOperationException", "private poison detail"), 1, RuntimeSchedulerPoisonDisposition.Poisoned, At, At, null));
        var observer = new PoisonedSchedulerWorkIncidentObserver(poison, f.Services.GetRequiredService<IIncidentStateStore>(),
            f.Services.GetRequiredService<RuntimeCheckpointCommitter>(), f.Clock, activityExecutionStateStore: null, inspectionAccumulator: null,
            activityExecutionInspectionStore: null, workflowExecutionStateStore: f.Executions, workflowDispatchStore: null);
        var result = new RuntimeSchedulerDrainResult("run", At, At,
            [new("work", "run", WorkflowExecutionCommandKind.RunSchedulerWork, RuntimeSchedulerWorkItemResultStatus.Faulted, "handler", At, At, "private poison detail")]);
        var envelope = Envelope(captured ? f.Policy : null, captured ? true : null);
        await observer.OnDrainedAsync(envelope, result);
        await observer.OnDrainedAsync(envelope, result);
        var commit = Assert.Single(f.Writer.ListCommits()).Commit;
        Assert.Equal(WorkflowExecutionStatus.Faulted, (await f.Executions.FindAsync("run"))!.Status);
        if (!captured)
        {
            Assert.Null(commit.Checkpoint.HistoryStartsAtAdmission);
            Assert.Null(commit.HistoryBatches);
            Assert.Null(await f.Reader.FindHeadAsync("run"));
            return;
        }
        Assert.True(commit.Checkpoint.HistoryStartsAtAdmission);
        Assert.True(Assert.Single(commit.HistoryBatches!).StartsAtRunStart);
        var page = await f.Reader.ReadPageAsync(new("run"));
        Assert.True(page.Head!.CapturedFromStart);
        Assert.Equal(new[] { "WorkflowFaulted", "IncidentRecorded" }, page.Items.Select(e => e.Name));
        Assert.All(page.Items, e => Assert.DoesNotContain("private poison detail", string.Join(" ", e.Metadata.Values)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unreadable_or_mismatched_start_cannot_supply_a_known_boundary(bool mismatch)
    {
        using var f = new Fixture();
        var envelope = mismatch ? Envelope(f.Policy, true, execution: "another-run") : Envelope(f.Policy, true, malformed: true);
        await f.Faulter.FaultIfCheckpointRuleViolatedAsync("run", null, new RuntimeCheckpointCommitValidationException("refused"), envelope);
        Assert.Empty(f.Writer.ListCommits());
        Assert.Null(await f.Reader.FindHeadAsync("run"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task First_fault_owners_keep_known_start_origin_without_inventing_a_workflow_started_transition(bool poison)
    {
        using var f = new Fixture();
        var origin = new RuntimeHistoryCausation("publication-event", "publisher", "source:send", "publisher-command", "publisher-work");
        var envelope = Envelope(f.Policy, true, historyCausation: origin);
        if (poison)
        {
            var store = f.Services.GetRequiredService<IWorkflowSchedulerPoisonStore>();
            await store.RecordAsync(new RuntimeSchedulerPoisonRecord("run", "work", WorkflowExecutionCommandKind.RunSchedulerWork, "handler",
                new RuntimeFaultInfo("System.InvalidOperationException", "controlled poison"), 1, RuntimeSchedulerPoisonDisposition.Poisoned, At, At, null));
            var observer = new PoisonedSchedulerWorkIncidentObserver(store, f.Services.GetRequiredService<IIncidentStateStore>(),
                f.Services.GetRequiredService<RuntimeCheckpointCommitter>(), f.Clock, activityExecutionStateStore: null, inspectionAccumulator: null,
                activityExecutionInspectionStore: null, workflowExecutionStateStore: f.Executions, workflowDispatchStore: null);
            await observer.OnDrainedAsync(envelope, new("run", At, At,
                [new("work", "run", WorkflowExecutionCommandKind.RunSchedulerWork, RuntimeSchedulerWorkItemResultStatus.Faulted, "handler", At, At, "controlled poison")]));
        }
        else await f.Faulter.FaultIfCheckpointRuleViolatedAsync("run", null, new RuntimeCheckpointCommitValidationException("controlled refusal"), envelope);
        var commit = Assert.Single(f.Writer.ListCommits()).Commit;
        Assert.Equal(origin, commit.Checkpoint.HistoryCausation); Assert.True(commit.Checkpoint.HistoryStartsAtAdmission);
        var page = await f.Reader.ReadPageAsync(new("run"));
        Assert.Equal(origin, Assert.Single(page.Items, e => e.Name == "WorkflowFaulted").Causation);
        Assert.DoesNotContain(page.Items, e => e.Name is "WorkflowStarted" or "ActivityStarted");
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Malformed_optional_start_diagnostics_cannot_replace_the_eligible_primary_fault(bool oversized)
    {
        using var f = new Fixture();
        var cause = new RuntimeHistoryCausation(CommandId: oversized ? new string('x', 1025) : " ");
        await f.Faulter.FaultIfCheckpointRuleViolatedAsync("run", null, new RuntimeCheckpointCommitValidationException("controlled primary"),
            Envelope(f.Policy, true, historyCausation: cause));
        var commit = Assert.Single(f.Writer.ListCommits()).Commit;
        Assert.Null(commit.Checkpoint.HistoryCausation);
        Assert.Equal(WorkflowExecutionStatus.Faulted, (await f.Executions.FindAsync("run"))!.Status);
        Assert.Single((await f.Reader.ReadPageAsync(new("run"))).Items, e => e.Name == "WorkflowFaulted");
    }

    private static WorkflowExecutionCommandEnvelope Envelope(RuntimeHistoryCapturePolicy? policy, bool? enabled,
        string? parent = null, string execution = "run", bool malformed = false, RuntimeHistoryCausation? historyCausation = null)
    {
        var payload = new WorkflowExecutionStartCommandPayload(Executable, Executable.ArtifactId, null, null, null, null,
            parentWorkflowExecutionId: parent) { HistoryCausation = historyCausation, HistoryCapturePolicy = policy, HistoryCaptureEnabledAtAdmission = enabled };
        var command = new WorkflowExecutionCommand("command", execution, WorkflowExecutionCommandKind.Start, At,
            malformed ? JsonSerializer.SerializeToElement("unreadable") : JsonSerializer.SerializeToElement(payload), new Dictionary<string, string>());
        return new("envelope", execution, command, "idempotency", WorkflowExecutionCommandDeliveryMode.AtLeastOnce, At);
    }
    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly IServiceScope _scope;
        internal IServiceProvider Services => _scope.ServiceProvider;
        internal FakeTimeProvider Clock { get; } = new(At);
        internal RuntimeHistoryCapturePolicy Policy { get; }
        internal InMemoryRuntimeCheckpointCommitStore Writer => Services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        internal IRuntimeHistoryStore Reader => Services.GetRequiredService<IRuntimeHistoryStore>();
        internal IWorkflowExecutionStateStore Executions => Services.GetRequiredService<IWorkflowExecutionStateStore>();
        internal CheckpointRuleViolationWorkflowFaulter Faulter { get; }
        internal Fixture()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<IPersistenceAccessContextAccessor>(new RuntimeHistoryTestAccess());
            services.AddSingleton<IWorkflowExecutableRootWriteLeaseManager>(PassThroughWorkflowExecutableRootWriteLeaseManager.Instance);
            services.AddWorkflowRuntime();
            services.Configure<RuntimeRecoveryContinuationOptions>(options => options.SigningKey = "first-fault-history-tests-signing-key-at-least-32-bytes");
            RuntimeHistoryRegistration.AddServices(services);
            _provider = services.BuildServiceProvider(validateScopes: true);
            _scope = _provider.CreateScope();
            Policy = RuntimeHistoryRegistration.ResolvePolicy(Services, "run")!;
            Faulter = new(Executions, Services.GetRequiredService<RuntimeCheckpointCommitter>(), Services.GetRequiredService<IRuntimeFaultCapturePolicy>(), Clock,
                logger: null, workflowDispatchStore: null);
        }
        public void Dispose() { _scope.Dispose(); _provider.Dispose(); }
    }
}
