using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Elsa.Canary.Fixtures;
using Elsa.Secrets.Workflows.Tests.Support;
using Elsa.Serialization.Core;
using Elsa.Workflows.ExecutionEvidence.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using static Elsa.Secrets.Workflows.Tests.Support.SecretsCanaryWorkflowHost;

namespace Elsa.Secrets.Workflows.Tests;

/// <summary>
/// The canary (spec 188, T081, FR-013, SC-004). A secret's value, planted through the real Secrets bridge, is consumed by
/// a canary activity in each run shape of FR-013 (success, a faulting exception, a returned fault, suspend and resume, a
/// throw on resume, a structural child-completion throw, and rotation, S1 to S4), and is then found zero times on each
/// of the eight surfaces, each read only after its precondition proved it holds the run (<see cref="CanarySurfaces"/>).
/// The injection scenarios of A15 (S5 to S10) each remove an upstream protection through a test-only seam
/// (<see cref="SecretsCanaryInjectionFeature"/>), so the protection under test is the only thing between a planted
/// value and a surface; each asserts its own positive control and leaves out the surface its injection planted. Every
/// scenario that runs a workflow runs once with runtime diagnostics at <c>DiagnosticSnapshot</c> and once at
/// <c>Payload</c>.
/// </summary>
public abstract class WorkflowSecretCanaryTests(CanaryHostFixture fixture, ITestOutputHelper output)
{
    protected SecretsCanaryWorkflowHost Host => fixture.Host;

    public static TheoryData<RuntimeDiagnosticsEvidenceLevel> Levels => new()
    {
        RuntimeDiagnosticsEvidenceLevel.DiagnosticSnapshot,
        RuntimeDiagnosticsEvidenceLevel.Payload
    };

    private static readonly CanarySurface[] AllSurfaces = Enum.GetValues<CanarySurface>();

    /// <summary>An imported artifact has no definition, so the stored definitions and the git export hold nothing of its run.</summary>
    private static readonly CanarySurface[] RunSurfaces = AllSurfaces.Except([CanarySurface.StoredDefinitions, CanarySurface.GitExport]).ToArray();

    // ---- FR-013 run shapes (S1 to S4) ----------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Levels))]
    public async Task S1_a_successful_run_and_a_run_after_rotation_leave_the_value_on_no_surface(RuntimeDiagnosticsEvidenceLevel level)
    {
        var scenario = await BeginAsync("s1", level);
        var rotated = NewValue("rotated");
        var publication = await PublishAsync(scenario, CanaryModes.Record);

        var first = await RunAsync(publication, Completed);
        await Host.RotateSecretAsync(scenario.ReferenceName, rotated);
        var second = await RunAsync(publication, Completed);

        Assert.Equal([scenario.Value], Host.Activations.For(first).Select(activation => activation.Primary));
        Assert.Equal([rotated], Host.Activations.For(second).Select(activation => activation.Primary));
        await AssertAbsentAsync(scenario, [Run(scenario, first, publication), Run(scenario, second, publication)], [scenario.Value, rotated], AllSurfaces);
        await AssertSnapshotWithholdsAsync(first, scenario, nameof(CanaryActivity.Primary));
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public async Task S2_an_exception_holding_the_value_is_recorded_masked_and_leaves_it_on_no_surface(RuntimeDiagnosticsEvidenceLevel level)
    {
        var scenario = await BeginAsync("s2", level);
        var publication = await PublishAsync(scenario, CanaryModes.Throw);

        var run = await RunAsync(publication, Faulted(scenario.NodeId));

        Assert.Equal([scenario.Value], Host.Activations.For(run).Select(activation => activation.Primary));
        await AssertAbsentAsync(scenario, [Run(scenario, run, publication)], [scenario.Value], AllSurfaces);
        await AssertFaultMaskedAsync(run, scenario);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public async Task S2b_a_returned_fault_holding_the_value_is_recorded_masked_and_leaves_it_on_no_surface(RuntimeDiagnosticsEvidenceLevel level)
    {
        var scenario = await BeginAsync("s2b", level);
        var publication = await PublishAsync(scenario, CanaryModes.Fault);

        var run = await RunAsync(publication, Faulted(scenario.NodeId));

        Assert.Equal([scenario.Value], Host.Activations.For(run).Select(activation => activation.Primary));
        await AssertAbsentAsync(scenario, [Run(scenario, run, publication)], [scenario.Value], AllSurfaces);
        var fault = await AssertFaultMaskedAsync(run, scenario);
        Assert.Equal(CanaryActivity.FaultCode, fault.Code);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public async Task S3_a_suspended_run_resumed_after_rotation_reads_the_new_value_and_leaves_neither_on_any_surface(RuntimeDiagnosticsEvidenceLevel level)
    {
        var scenario = await BeginAsync("s3", level);
        var rotated = NewValue("rotated");
        var publication = await PublishAsync(scenario, CanaryModes.Suspend);

        var run = await RunAsync(publication, Suspended(scenario.NodeId));
        await Host.RotateSecretAsync(scenario.ReferenceName, rotated);
        await Host.ResumeAsync(run);
        await WaitForAsync(run, Completed);

        Assert.Equal(
            [(CanaryRecorder.Execute, scenario.Value), (CanaryRecorder.Resume, rotated)],
            Host.Activations.For(run).Select(activation => (activation.Step, activation.Primary)));
        await AssertAbsentAsync(scenario, [Run(scenario, run, publication)], [scenario.Value, rotated], AllSurfaces);
        await AssertSnapshotWithholdsAsync(run, scenario, nameof(CanaryActivity.Primary));
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public async Task S3b_an_exception_holding_the_value_on_resume_is_recorded_masked_and_leaves_it_on_no_surface(RuntimeDiagnosticsEvidenceLevel level)
    {
        var scenario = await BeginAsync("s3b", level);
        var publication = await PublishAsync(scenario, CanaryModes.ThrowOnResume);

        var run = await RunAsync(publication, Suspended(scenario.NodeId));
        await Host.ResumeAsync(run);
        await WaitForAsync(run, Faulted(scenario.NodeId));

        Assert.Equal(
            [(CanaryRecorder.Execute, scenario.Value), (CanaryRecorder.Resume, scenario.Value)],
            Host.Activations.For(run).Select(activation => (activation.Step, activation.Primary)));
        await AssertAbsentAsync(scenario, [Run(scenario, run, publication)], [scenario.Value], AllSurfaces);
        await AssertFaultMaskedAsync(run, scenario);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public async Task S4_a_structural_child_completion_throwing_the_value_is_recorded_masked_and_leaves_it_on_no_run_surface(RuntimeDiagnosticsEvidenceLevel level)
    {
        var scenario = await BeginAsync("s4", level);
        var executable = CanaryArtifacts.Structural(scenario.ArtifactId, scenario.NodeId, scenario.ReferenceName, Host.Shell.ServiceProvider.GetRequiredService<IWellKnownTypeRegistry>());
        var sourceReferenceId = await Host.ImportAsync(executable);

        var run = await Host.ExecuteAsync(scenario.ArtifactId, sourceReferenceId);
        await WaitForAsync(run, Faulted(scenario.NodeId));

        Assert.Equal(
            [(CanaryRecorder.Execute, scenario.Value), (CanaryRecorder.ChildCompleted, scenario.Value)],
            Host.Activations.For(run).Select(activation => (activation.Step, activation.Primary)));
        await AssertAbsentAsync(scenario, [Imported(scenario, run, sourceReferenceId, scenario.ReferenceName, scenario.ReferenceName)], [scenario.Value], RunSurfaces);
        await AssertFaultMaskedAsync(run, scenario);
    }

    // ---- A15 injection scenarios (S5 to S10) ---------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Levels))]
    public async Task S5_a_literal_on_an_encryption_required_input_of_an_imported_artifact_is_withheld_and_refused_at_activation(RuntimeDiagnosticsEvidenceLevel level)
    {
        var scenario = await BeginAsync("s5", level);
        var planted = NewValue("imported");
        var sourceReferenceId = await Host.ImportAsync(CanaryArtifacts.EncryptionRequiredLiteral(scenario.ArtifactId, scenario.NodeId, planted, CanaryModes.Record));

        var (run, _) = await Host.StartAsync(scenario.ArtifactId, sourceReferenceId);
        // Settled once activation refused the marker, or once the commit backstop refused a value producer withholding let through.
        var state = await WaitForAsync(run, settled => Faulted(scenario.NodeId)(settled) || CommitRefused(run));

        // P3's own assertions: producer withholding recorded the marker, and activation refused it with VF-ACT-010 (the
        // commit backstop refuses a value producer withholding let through with VF-ACT-005 instead).
        var canary = state.Node(scenario.NodeId)!;
        Assert.NotNull(canary.InputSnapshot);
        var primary = canary.InputSnapshot.Values[nameof(CanaryActivity.Primary)];
        Assert.Equal(ValuePresence.Withheld, primary.Presence);
        Assert.Equal(WithheldValueKind.PolicyRequiresEncryption, primary.WithheldValue!.Kind);
        Assert.Contains("VF-ACT-010", canary.Fault?.Message, StringComparison.Ordinal);
        Assert.Empty(Host.Activations.For(run));
        AssertStartMode(run);
        // Positive control: the import planted the literal in the artifact it stored, and nowhere the scan reads.
        await AssertPlantedInArtifactsAsync(planted);
        await AssertAbsentAsync(
            scenario,
            [Imported(scenario, run, sourceReferenceId, referenceName: null, nameof(WithheldValueKind.PolicyRequiresEncryption))],
            [planted],
            RunSurfaces,
            excludedRuntimeTables: CanarySurfaces.ExecutableArtifactTables);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public async Task S6_a_present_value_requiring_encryption_is_refused_by_the_commit_backstop(RuntimeDiagnosticsEvidenceLevel level)
    {
        var scenario = await BeginAsync("s6", level);
        var planted = NewValue("replanted");
        var sourceReferenceId = await Host.ImportAsync(CanaryArtifacts.EncryptionRequiredLiteral(scenario.ArtifactId, scenario.NodeId, planted, CanaryModes.Record));
        Host.Injections.Replant(scenario.NodeId, nameof(CanaryActivity.Primary), planted, Policy(isSensitive: true, requiresEncryption: true));

        var (run, dispatch) = await Host.StartAsync(scenario.ArtifactId, sourceReferenceId);
        // Settled once the backstop refused the commit, or once a run the backstop let through completed.
        await WaitForAsync(run, settled => Completed(settled) || CommitRefused(run));

        // The refused commit left no inspection projection to read diagnostic snapshots or an activity view from.
        await AssertAbsentAsync(
            scenario,
            [Imported(scenario, run, sourceReferenceId, referenceName: null, scenario.NodeId)],
            [planted],
            [CanarySurface.RuntimeState, CanarySurface.ExecutionEvidence, CanarySurface.RuntimeTelemetry, CanarySurface.RuntimeLogs],
            excludedRuntimeTables: CanarySurfaces.ExecutableArtifactTables);
        // P4's own refusal: the commit that would have carried the planted value was refused, so the activity never ran,
        // and the import planted the value in the artifact it stored.
        Assert.True(CommitRefused(run));
        Assert.Equal("AcceptedButFaulted", dispatch);
        Assert.Empty(Host.Activations.For(run));
        Assert.Null((await Host.ReadRunAsync(run)).Activities.Single(activity => activity.Execution.ExecutableNodeId == scenario.NodeId).InputSnapshot);
        await AssertPlantedInArtifactsAsync(planted);
    }

    [Fact]
    public async Task S7_publish_refuses_a_secret_on_a_set_variable_intrinsic()
    {
        var reference = ReferenceName("s7", Suffix());
        var root = new JsonObject
        {
            ["nodeId"] = "s7-set",
            ["activityVersionId"] = "$intrinsic",
            ["inputs"] = new JsonArray(SecretReference(WorkflowIntrinsicInputKeys.Value, reference)),
            ["outputs"] = new JsonArray(),
            ["intrinsic"] = new JsonObject
            {
                ["kind"] = "set",
                ["valueType"] = new JsonObject { ["alias"] = "String", ["collectionKind"] = "single" },
                ["variable"] = new JsonObject { ["referenceKey"] = "held", ["declaringScopeId"] = "workflow" }
            }
        };
        var variables = new JsonArray(new JsonObject
        {
            ["referenceKey"] = "held",
            ["name"] = "Held",
            ["type"] = new JsonObject { ["alias"] = "String", ["collectionKind"] = "single" },
            ["storageDriverType"] = null,
            ["default"] = null
        });

        await AssertPublishRefusedAsync($"canary s7 set {reference}", root, variables);
    }

    [Fact]
    public async Task S7_publish_refuses_a_secret_on_a_checkpoint_participant()
    {
        var reference = ReferenceName("s7", Suffix());
        var root = new JsonObject
        {
            ["nodeId"] = "s7-checkpoint",
            ["activityVersionId"] = await Host.FindActivityVersionIdAsync(typeof(CanaryCheckpointActivity)),
            ["inputs"] = new JsonArray(SecretReference(nameof(CanaryCheckpointActivity.Text), reference)),
            ["outputs"] = new JsonArray()
        };

        await AssertPublishRefusedAsync($"canary s7 checkpoint {reference}", root);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public async Task S8_a_present_sensitive_input_value_reaches_no_diagnostic_snapshot(RuntimeDiagnosticsEvidenceLevel level)
    {
        var scenario = await BeginAsync("s8", level);
        var planted = NewValue("sensitive");
        var publication = await PublishAsync(scenario, CanaryModes.Record);
        Host.Injections.Replant(scenario.NodeId, nameof(CanaryActivity.Primary), planted, Policy(isSensitive: true, requiresEncryption: false));

        var run = await RunAsync(publication, Completed);

        // Positive control: the planted value is what the activity received, and what the committed snapshot holds.
        Assert.Equal([planted], Host.Activations.For(run).Select(activation => activation.Primary));
        await AssertSnapshotHoldsAsync(run, nameof(CanaryActivity.Primary), planted);
        await AssertAbsentAsync(
            scenario,
            [Run(scenario, run, publication) with { SubjectWithheld = false }],
            [planted],
            AllSurfaces.Except([CanarySurface.RuntimeState]));
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public async Task S8b_a_captured_sensitive_payload_is_hidden_by_the_inspector_views(RuntimeDiagnosticsEvidenceLevel level)
    {
        var scenario = await BeginAsync("s8b", level);
        var planted = NewValue("captured");
        var publication = await PublishAsync(scenario, CanaryModes.Record);
        Host.Injections.Replant(scenario.NodeId, nameof(CanaryActivity.Primary), planted, Policy(isSensitive: true, requiresEncryption: false));
        Host.Injections.CaptureEverything(scenario.NodeId);

        var run = await RunAsync(publication, Completed);

        // Positive control: the capture-everything policy captured the planted, sensitive input in full.
        Assert.Equal([planted], Host.Activations.For(run).Select(activation => activation.Primary));
        Assert.Contains((nameof(CanaryActivity.Primary), true), Host.Injections.CapturedEverything(scenario.NodeId));
        await AssertAbsentAsync(
            scenario,
            [Run(scenario, run, publication) with { SubjectWithheld = false }],
            [planted],
            AllSurfaces.Except([CanarySurface.RuntimeState, CanarySurface.DiagnosticSnapshots]),
            inspectorCaller: CanaryCaller.StructureOnly);
    }

    /// <summary>
    /// S8c, the scenario that reaches P8 (A15 names S8, which cannot: execution evidence records root variables, activity
    /// facts and incident messages, never an activity's inputs, so a value planted on an input never reaches the
    /// enricher's sensitive-value rule). An imported artifact declares a workflow variable sensitive with the planted
    /// value as its initial value, so the root variable frame holds a present, sensitive value, and the enricher's
    /// redaction is the one thing between it and the evidence. A published design cannot do this: the variable frame
    /// takes a variable's own policy, which publication derives from its storage alone, so an author's sensitivity on
    /// a value written into a variable (a Set Variable literal, a captured output) does not reach the frame.
    /// </summary>
    [Theory]
    [MemberData(nameof(Levels))]
    public async Task S8c_a_sensitive_workflow_variable_is_kept_out_of_evidence(RuntimeDiagnosticsEvidenceLevel level)
    {
        var scenario = await BeginAsync("s8c", level);
        var planted = NewValue("variable");
        var sourceReferenceId = await Host.ImportAsync(CanaryArtifacts.SensitiveVariable(scenario.ArtifactId, scenario.NodeId, HeldVariable, planted, CanaryModes.Record));

        var run = await Host.ExecuteAsync(scenario.ArtifactId, sourceReferenceId);
        var state = await WaitForAsync(run, Completed);

        // Positive control: the root variable frame holds the value, present and sensitive, where the import put it.
        var held = state.Workflow!.RootVariableFrame!.Values[HeldVariable];
        Assert.Equal(ValuePresence.Present, held.Presence);
        Assert.True(held.Policy.IsSensitive);
        Assert.Equal(planted, held.InlineValue!.Value.GetString());
        // The artifact and the variable frame, in the runtime state, hold the value the import planted.
        await AssertAbsentAsync(
            scenario,
            [Imported(scenario, run, sourceReferenceId, referenceName: null, scenario.NodeId) with { SubjectInput = nameof(CanaryActivity.Companion) }],
            [planted],
            RunSurfaces.Except([CanarySurface.RuntimeState]));
        // The evidence recorded the variable's write, withheld as sensitive.
        Assert.Contains(Host.EvidenceRecords(run), record => record.Name == HeldVariable && record.ValueDisposition == ExecutionEvidenceValueDisposition.Sensitive);
    }

    private const string HeldVariable = "held";

    [Theory]
    [MemberData(nameof(Levels))]
    public async Task S9_a_handler_exception_reaches_runtime_spans_as_its_type_only(RuntimeDiagnosticsEvidenceLevel level)
    {
        var scenario = await BeginAsync("s9", level);
        var planted = NewValue("handler");
        var publication = await PublishAsync(scenario, CanaryModes.Record);
        Host.Injections.FaultInvokeHandler(scenario.NodeId, planted);

        var run = await Host.ExecuteAsync(publication, "AcceptedButFaulted");

        // Positive control: the drainer recorded the escaped exception as a handler fault, with an error span of the run,
        // and its poison record holds the planted message. The handler fault's records are the surfaces the injection
        // planted: the poison record, and the incident the drainer raises with the message, which the inspection
        // projection, the evidence and the inspector views all show. That leaves the spans, the protection under test,
        // and the logs and the definition surfaces to scan.
        var failed = await WaitForSpanAsync(span => span.WorkflowExecutionId == run && span.Status == ActivityStatusCode.Error);
        AssertStartMode(run);
        var poison = await ReadDatabaseCellsAsync(Host.RuntimeDirectory, table => CanarySurfaces.PoisonTables.Contains(table));
        Assert.Contains(poison, cell => CanaryScanner.Find(cell.Bytes, planted, cell.Location).Count > 0);
        await AssertAbsentAsync(
            scenario,
            [Run(scenario, run, publication)],
            [planted],
            [CanarySurface.RuntimeTelemetry, CanarySurface.RuntimeLogs, CanarySurface.StoredDefinitions, CanarySurface.GitExport]);
        // The span names the exception's type, and nothing of its message.
        Assert.Equal(typeof(InvalidOperationException).FullName, failed.StatusDescription);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public async Task S10_a_secret_on_an_undeclared_input_under_a_lowered_policy_leaves_the_value_on_no_surface(RuntimeDiagnosticsEvidenceLevel level)
    {
        var scenario = await BeginAsync("s10", level);
        var publication = await Host.PublishCanaryAsync(
            $"canary {scenario.Name}",
            scenario.NodeId,
            SecretReference(nameof(CanaryActivity.Plain), scenario.ReferenceName),
            Literal(nameof(CanaryActivity.Companion), CanaryModes.Record));
        Host.Injections.LowerSecretPolicy(scenario.NodeId, nameof(CanaryActivity.Plain));

        var run = await RunAsync(publication, Completed);

        // Positive control: the activity received the value on Plain, whose pinned contract is not sensitive.
        Assert.Equal([scenario.Value], Host.Activations.For(run).Select(activation => activation.Plain));
        var executable = await Host.ReadExecutableAsync(publication.ArtifactId);
        Assert.False(executable.RootActivity.ActivityContract!.Inputs[nameof(CanaryActivity.Plain)].Policy.IsSensitive);
        await AssertAbsentAsync(scenario, [Run(scenario, run, publication, nameof(CanaryActivity.Plain))], [scenario.Value], AllSurfaces);
        // The committed snapshot holds the reference under the lowered policy, so no sensitive-value rule stands in front
        // of the value: only the withholding protections keep it out.
        var plain = await AssertSnapshotWithholdsAsync(run, scenario, nameof(CanaryActivity.Plain));
        Assert.False(plain.Policy.IsSensitive);
        Assert.False(plain.Policy.RequiresEncryption);
    }

    // ---- Scenario support ----------------------------------------------------------------------------------------

    /// <summary>One scenario's names and its secret: a reference and a value of its own, so a leak names the scenario it came from.</summary>
    protected sealed record CanaryScenario(string Name, RuntimeDiagnosticsEvidenceLevel Level, string NodeId, string ReferenceName, string Value)
    {
        public string ArtifactId => $"canary-{NodeId}";
    }

    /// <summary>The scenario labels, which name each scenario's node, secret and definition.</summary>
    public static readonly string[] ScenarioLabels = ["s1", "s2", "s2b", "s3", "s3b", "s4", "s5", "s6", "s7", "s8", "s8b", "s8c", "s9", "s10"];

    /// <summary>A scenario's secret reference name: a scenario label and a hexadecimal suffix, which no redactor matches (T078).</summary>
    public static string ReferenceName(string label, string suffix) => $"canary.{label}.{suffix}";

    private static string Suffix() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>Saves the diagnostics level and creates the scenario's secret.</summary>
    protected async Task<CanaryScenario> BeginAsync(string name, RuntimeDiagnosticsEvidenceLevel level)
    {
        Assert.Contains(name, ScenarioLabels);
        var suffix = Suffix();
        var scenario = new CanaryScenario($"{name}@{level}", level, $"{name}-{suffix}", ReferenceName(name, suffix), NewValue("primary"));
        await Host.SaveDiagnosticsLevelAsync(level);
        await Host.CreateSecretAsync(scenario.ReferenceName, scenario.Value);
        output.WriteLine($"{scenario.Name}: node {scenario.NodeId}, reference {scenario.ReferenceName}, mode {Host.Mode}");
        return scenario;
    }

    /// <summary>A canary value: a plain word with a distinguishing prefix, generated per run, never a credential shape.</summary>
    protected static string NewValue(string kind) => $"canary{kind}{Guid.NewGuid():N}";

    protected static ValueProtectionPolicy Policy(bool isSensitive, bool requiresEncryption) =>
        new(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive, requiresEncryption);

    /// <summary>Publishes a canary whose <c>Primary</c> input is bound to the scenario's secret, in <paramref name="mode"/>.</summary>
    protected Task<CanaryPublication> PublishAsync(CanaryScenario scenario, string mode) =>
        Host.PublishCanaryAsync(
            $"canary {scenario.Name}",
            scenario.NodeId,
            SecretReference(nameof(CanaryActivity.Primary), scenario.ReferenceName),
            Literal(nameof(CanaryActivity.Companion), mode));

    protected async Task<string> RunAsync(CanaryPublication publication, Func<RunState, bool> settled)
    {
        var run = await Host.ExecuteAsync(publication);
        await WaitForAsync(run, settled);
        AssertStartMode(run);
        return run;
    }

    /// <summary>
    /// The run's dispatch spans show the start mode the host selected: in discrete mode the canary's start and invoke
    /// stages are dispatches of their own; in fused mode the runtime ran them inside the schedule dispatch, so neither is.
    /// </summary>
    protected void AssertStartMode(string workflowExecutionId)
    {
        var dispatched = Host.Spans.Spans
            .Where(span => span.WorkflowExecutionId == workflowExecutionId && span.Name == WorkflowEngineTelemetry.DispatchSpanName)
            .Select(span => span.Tags.GetValueOrDefault(WorkflowEngineTelemetry.CommandKindTag))
            .ToArray();
        Assert.Contains(nameof(WorkflowExecutionCommandKind.ScheduleActivity), dispatched);
        if (Host.Mode == CanaryStartMode.Discrete)
            Assert.Contains(nameof(WorkflowExecutionCommandKind.InvokeActivity), dispatched);
        else
            Assert.DoesNotContain(nameof(WorkflowExecutionCommandKind.InvokeActivity), dispatched);
    }

    protected static CanaryRun Run(CanaryScenario scenario, string workflowExecutionId, CanaryPublication publication, string subjectInput = nameof(CanaryActivity.Primary)) =>
        new(scenario.Name, scenario.NodeId, workflowExecutionId, publication.ArtifactId, publication.SourceReferenceId, publication.DefinitionId, subjectInput, scenario.ReferenceName, scenario.ReferenceName);

    protected static CanaryRun Imported(CanaryScenario scenario, string workflowExecutionId, string sourceReferenceId, string? referenceName, string runtimeMarker) =>
        new(scenario.Name, scenario.NodeId, workflowExecutionId, scenario.ArtifactId, sourceReferenceId, DefinitionId: null, nameof(CanaryActivity.Primary), referenceName, runtimeMarker);

    /// <summary>A run's committed state, as the settle predicates read it.</summary>
    protected sealed record RunState(IReadOnlyCollection<ActivityExecutionState> Activities, WorkflowExecutionState? Workflow)
    {
        public ActivityExecutionState? Node(string nodeId) => Activities.SingleOrDefault(activity => activity.Execution.ExecutableNodeId == nodeId);
    }

    protected static bool Completed(RunState run) => run.Workflow?.Status == WorkflowExecutionStatus.Completed;

    protected static Func<RunState, bool> Faulted(string nodeId) => run => run.Node(nodeId)?.Fault is not null;

    protected static Func<RunState, bool> Suspended(string nodeId) => run => run.Node(nodeId)?.Status == ActivityExecutionStatus.Suspended;

    /// <summary>Polls the run's committed state until <paramref name="settled"/> holds; the drain may outlive the request that started it.</summary>
    protected async Task<RunState> WaitForAsync(string workflowExecutionId, Func<RunState, bool> settled)
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            var (activities, workflow) = await Host.ReadRunAsync(workflowExecutionId);
            var state = new RunState(activities, workflow);
            if (settled(state))
                return state;
            Assert.True(
                deadline.Elapsed < SettleTimeout,
                $"Run '{workflowExecutionId}' did not settle: workflow {workflow?.Status}, activities {string.Join(", ", activities.Select(activity => $"{activity.Execution.ExecutableNodeId}={activity.Status}"))}.");
            await Task.Delay(100);
        }
    }

    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Whether the commit backstop refused a commit of the run, as the log line that reports the refusal says.</summary>
    protected bool CommitRefused(string workflowExecutionId) =>
        Host.Logs.Lines.Any(line => line.Contains("VF-ACT-005", StringComparison.Ordinal) && line.Contains(workflowExecutionId, StringComparison.Ordinal));

    protected async Task<CanarySpan> WaitForSpanAsync(Func<CanarySpan, bool> match)
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            if (Host.Spans.Spans.FirstOrDefault(match) is { } span)
                return span;
            Assert.True(deadline.Elapsed < SettleTimeout, "No span matched in time.");
            await Task.Delay(100);
        }
    }

    /// <summary>The persisted input snapshot of the scenario's node holds <paramref name="inputKey"/> withheld as its reference, and no value (A03).</summary>
    protected async Task<ValueEnvelope> AssertSnapshotWithholdsAsync(string workflowExecutionId, CanaryScenario scenario, string inputKey)
    {
        var (activities, _) = await Host.ReadRunAsync(workflowExecutionId);
        var envelope = activities.Single(activity => activity.Execution.ExecutableNodeId == scenario.NodeId).InputSnapshot!.Values[inputKey];
        Assert.Equal(ValuePresence.Withheld, envelope.Presence);
        Assert.Equal(WithheldValueKind.SecretReference, envelope.WithheldValue!.Kind);
        Assert.Equal(scenario.ReferenceName, envelope.WithheldValue.Secret!.Name);
        return envelope;
    }

    /// <summary>The committed input snapshot holds <paramref name="value"/> for <paramref name="inputKey"/>, where an injection planted it.</summary>
    protected async Task AssertSnapshotHoldsAsync(string workflowExecutionId, string inputKey, string value)
    {
        var (activities, _) = await Host.ReadRunAsync(workflowExecutionId);
        var envelope = activities.Single(activity => activity.InputSnapshot is not null).InputSnapshot!.Values[inputKey];
        Assert.Equal(ValuePresence.Present, envelope.Presence);
        Assert.Equal(value, envelope.InlineValue!.Value.GetString());
    }

    /// <summary>The imported artifact holds the planted literal: the surface the import planted, which the runtime scan leaves out.</summary>
    protected async Task AssertPlantedInArtifactsAsync(string planted)
    {
        var artifacts = await ReadDatabaseCellsAsync(Host.RuntimeDirectory, table => CanarySurfaces.ExecutableArtifactTables.Contains(table));
        Assert.Contains(artifacts, cell => CanaryScanner.Find(cell.Bytes, planted, cell.Location).Count > 0);
    }

    /// <summary>The run's recorded fault, and its incident, carry the masking marker where the activity put the value.</summary>
    protected async Task<NormalizedActivityFault> AssertFaultMaskedAsync(string workflowExecutionId, CanaryScenario scenario)
    {
        var marker = $"[secret:{scenario.ReferenceName}]";
        var (activities, _) = await Host.ReadRunAsync(workflowExecutionId);
        var fault = Assert.IsType<NormalizedActivityFault>(activities.Single(activity => activity.Execution.ExecutableNodeId == scenario.NodeId).Fault);
        Assert.Contains(marker, fault.Message, StringComparison.Ordinal);
        Assert.Contains(await Host.ReadIncidentsAsync(workflowExecutionId), incident => incident.Message.Contains(marker, StringComparison.Ordinal));
        return fault;
    }

    /// <summary>
    /// Submits a definition whose root is <paramref name="root"/> (the Design API admits a secret reference anywhere),
    /// then asserts that publish refuses it with <c>VF-ACT-012</c> and writes no artifact.
    /// </summary>
    protected async Task AssertPublishRefusedAsync(string name, JsonObject root, JsonArray? variables = null)
    {
        var (_, versionId) = await Host.SubmitAsync(name, root, variables);

        using var response = await Host.TryPublishAsync(versionId);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("VF-ACT-012", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads each of <paramref name="surfaces"/>, asserting its precondition for every run first, and asserts that none of
    /// <paramref name="values"/> occurs on it in any form the scanner searches for (SC-004).
    /// </summary>
    protected async Task AssertAbsentAsync(
        CanaryScenario scenario,
        IReadOnlyCollection<CanaryRun> runs,
        IReadOnlyCollection<string> values,
        IEnumerable<CanarySurface> surfaces,
        IReadOnlySet<string>? excludedRuntimeTables = null,
        CanaryCaller inspectorCaller = CanaryCaller.Operator)
    {
        var options = new CanaryReadOptions(RuntimeDiagnosticsSettingsResolver.ToCaptureMode(scenario.Level), excludedRuntimeTables, inspectorCaller);
        var reader = new CanarySurfaces(Host);
        var leaks = new List<string>();
        var preconditions = new List<string>();
        foreach (var surface in surfaces)
        {
            var read = await reader.ReadAsync(surface, runs, options);
            preconditions.AddRange(read.FailedPreconditions);
            leaks.AddRange(values.SelectMany(value => read.Contents.SelectMany(content => CanaryScanner.Find(content.Bytes, value, content.Location)))
                .Select(hit => $"{surface}: {hit.Form} at {hit.Location} (offset {hit.Offset})"));
        }

        // Both are reported: a leak is never hidden behind a precondition that failed on another surface.
        Assert.True(
            leaks.Count == 0 && preconditions.Count == 0,
            $"{scenario.Name}: a canary value was found {leaks.Count} times, and {preconditions.Count} preconditions did not hold.\n" +
            $"Leaks:\n{string.Join('\n', leaks.Take(40))}\nFailed preconditions:\n{string.Join('\n', preconditions)}");
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CanaryHostCollection
{
    /// <summary>
    /// The canary hosts run alone: each is a full host with a process-wide span listener, so one at a time keeps their
    /// spans apart and the shared machine's load off their timings.
    /// </summary>
    public const string Name = "secrets-canary-host";
}

/// <summary>One canary host for every scenario of a test class.</summary>
public abstract class CanaryHostFixture(CanaryStartMode mode) : IAsyncLifetime
{
    public SecretsCanaryWorkflowHost Host { get; private set; } = null!;

    public async Task InitializeAsync() => Host = await StartAsync(mode);

    public async Task DisposeAsync() => await Host.DisposeAsync();
}

public sealed class DiscreteCanaryHostFixture() : CanaryHostFixture(CanaryStartMode.Discrete);

public sealed class FusedCanaryHostFixture() : CanaryHostFixture(CanaryStartMode.Fused);

/// <summary>The canary with each stage of a run its own dispatch and commit.</summary>
[Collection(CanaryHostCollection.Name)]
public sealed class WorkflowSecretCanaryDiscreteTests(DiscreteCanaryHostFixture fixture, ITestOutputHelper output)
    : WorkflowSecretCanaryTests(fixture, output), IClassFixture<DiscreteCanaryHostFixture>;

/// <summary>The canary with the canary activity's schedule, start and invoke stages fused into one dispatch.</summary>
[Collection(CanaryHostCollection.Name)]
public sealed class WorkflowSecretCanaryFusedTests(FusedCanaryHostFixture fixture, ITestOutputHelper output)
    : WorkflowSecretCanaryTests(fixture, output), IClassFixture<FusedCanaryHostFixture>;
