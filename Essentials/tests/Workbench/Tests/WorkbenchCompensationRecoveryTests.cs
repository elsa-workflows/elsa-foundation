using System.IO.Compression;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Workbench.Tests;

/// <summary>
/// Compensation journeys on a real Workbench process, published and run through REST. Spec 250 (M1): a later step is
/// rejected and the process is killed after a handler's external effect and before its completion commits; the
/// restarted process re-runs that handler with the same invocation id. Spec 251 (M2): a step's response is lost, its
/// handler exhausts its attempts and the scope waits until an operator retries it, and the process is killed after the
/// retry commits. Spec 258 (M3): an inner scope hands its reservations to the outer scope, a Confirm keeps the payment,
/// and the process is killed after the first handed-over handler's effect. In all, the participant applies each effect
/// once and the scope finishes Compensated. Spec 260 (M4): an operator retries, resolves and abandons over REST on runs
/// a Studio test run started, and the process is killed after a resolve's alteration checkpoint and before the decision
/// reaches the scope. Spec 261 (M5): a handler retry waits for its delay, and the process is killed during the wait.
/// </summary>
/// <remarks>
/// The participant keeps its state in files under the content root, which outlive the killed process. The recovery
/// sweep waits for the dead process's ownership lease, so the shell shortens that lease for this test. Runs in its own
/// non-parallel collection because sibling Workbench hosts starve the recovery poller.
/// </remarks>
[Collection(Name)]
public sealed class WorkbenchCompensationRecoveryTests
{
    public const string Name = nameof(WorkbenchCompensationRecoveryTests);

    private const string PackageId = "Elsa.Workbench.Tests.Reservations";
    private const string ParticipantType = PackageId + ".ReservationParticipant";
    private const string ScopeType = "Elsa.Activities.Compensation.Activities.CompensationScope";
    private const string ConfirmType = "Elsa.Activities.Compensation.Activities.Confirm";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(120);

    [Fact]
    public async Task A_killed_handler_runs_again_with_the_same_invocation_and_recovery_finishes_once()
    {
        await using var original = await WorkbenchProcess.StartAsync(WitnessShell(), PrepareContentRoot);
        await LoginAsync(original);
        var journal = Path.Combine(original.ContentRoot, "reservations");
        var published = await PublishAsync(original, journal);

        // Execute runs inline until the workflow yields, and the held handler never yields, so the request stays open
        // until the process is killed. The handler for "Reserve delivery" applies its effect, then holds.
        var execute = original.Client.PostAsJsonAsync($"/runtime/workflows/executables/{published.ArtifactId}/execute",
            new { sourceReferenceId = published.SourceReferenceId });
        await WaitForAsync(original, () => EffectLog(journal).Contains("delivery-cancel"),
            "the delivery cancellation effect to land before its completion commits", () => Describe(original, journal, execute));
        var executionId = await ExecutionIdAsync(original, published.ArtifactId);
        var killedCall = Assert.Single(Calls(journal, "delivery-cancel"));
        Assert.DoesNotContain("inventory-release", EffectLog(journal));

        await using var restarted = await original.CrashAndRestartAsync(directory =>
            File.WriteAllText(Path.Combine(directory, "reservations", "delivery-cancel.release"), "release"));
        await Assert.ThrowsAnyAsync<Exception>(() => execute);
        await LoginAsync(restarted);
        var completed = await WaitForStateAsync(restarted, executionId, instance => instance["instance"]?["status"]?.GetValue<string>() == "Completed");

        // Each effect landed once, handlers ran newest first, and the killed handler ran again with its invocation id.
        Assert.Equal(["inventory", "delivery", "delivery-cancel", "inventory-release"], EffectLog(journal));
        var calls = Calls(journal, "delivery-cancel");
        Assert.Equal(2, calls.Length);
        Assert.NotEqual(killedCall.ProcessId, calls[1].ProcessId);
        Assert.Equal(killedCall.InvocationId, calls[1].InvocationId);
        Assert.Single(Calls(journal, "inventory-release"));

        // Each handler undid the reservation its own forward step made, through the captured variable and through the
        // direct result reference alike.
        Assert.Equal([$"delivery:{Assert.Single(Calls(journal, "delivery")).InvocationId}"], Undone(journal, "delivery-cancel").Distinct());
        Assert.Equal([$"inventory:{Assert.Single(Calls(journal, "inventory")).InvocationId}"], Undone(journal, "inventory-release"));

        var scope = Activity(completed, "scope");
        Assert.Contains("Compensated", scope["outcomeNames"]!.AsArray().Select(outcome => outcome!.GetValue<string>()));
        var incident = Assert.Single(completed["incidents"]!.AsArray());
        Assert.Equal("confirm", incident!["executableNodeId"]!.GetValue<string>());
        Assert.Equal("Resolved", incident["status"]!.GetValue<string>());
        Assert.Equal("compensation", Activity(completed, "delivery-cancel")["provenance"]!["schedulingCause"]!.GetValue<string>());
        Assert.Single(completed["activities"]!.AsArray(), activity => activity?["executableNodeId"]?.GetValue<string>() == "delivery-cancel" &&
                                                                       activity["status"]?.GetValue<string>() == "Completed");
    }

    [Fact]
    public async Task An_operator_retry_resumes_a_recovery_that_waits_for_intervention_and_survives_a_kill()
    {
        await using var original = await WorkbenchProcess.StartAsync(WitnessShell(), PrepareContentRoot);
        await LoginAsync(original);
        var journal = Path.Combine(original.ContentRoot, "reservations");
        var reservations = new Reservations(await ActivityVersionAsync(original, ParticipantType), journal);

        // J1: the delivery effect lands and its response is lost, so the step is uncertain and recovery starts. J5: the
        // delivery cancellation fails on both attempts, so the scope waits for an operator; the third call succeeds.
        var published = await PublishScopeAsync(original, "Compensation intervention witness",
            [Input("faultAfterCompensation", false), Input("handlerMaxAttempts", 2)],
            Step("Reserve inventory", reservations.Participant("inventory", "inventory", captures: "inventoryReservation"),
                reservations.Participant("inventory-release", "inventory-release", undoes: VariableArgument("Undoes", "inventoryReservation"))),
            Step("Reserve delivery", reservations.Participant("delivery", "delivery", loseResponse: true),
                reservations.Participant("delivery-cancel", "delivery-cancel", hold: true, failTimes: 2)),
            Step("Confirm order", reservations.Participant("confirm", "confirm"), null));

        var execute = original.Client.PostAsJsonAsync($"/runtime/workflows/executables/{published.ArtifactId}/execute",
            new { sourceReferenceId = published.SourceReferenceId });
        await WaitForAsync(original, () => Calls(journal, "delivery-cancel").Length == 2,
            "both delivery cancellation attempts to fail", () => Describe(original, journal, execute));
        var executionId = await ExecutionIdAsync(original, published.ArtifactId);
        var waiting = await WaitForStateAsync(original, executionId, instance => WaitingIncident(instance) is not null);

        // The run yielded, so the execute request returned. It stays Running while the handler's incident blocks and waits; no compensation effect exists yet.
        using (var executed = await execute)
            Assert.True(executed.IsSuccessStatusCode, await executed.Content.ReadAsStringAsync());
        Assert.Equal("Running", waiting["instance"]!["status"]!.GetValue<string>());
        var incident = WaitingIncident(waiting)!;
        Assert.Equal("StructuralIntervention", incident["resolutionOutcome"]!["systemSource"]!.GetValue<string>());
        Assert.Equal(["inventory", "delivery"], EffectLog(journal));
        Assert.Empty(Calls(journal, "confirm"));
        var faulted = incident["activityExecutionId"]!.GetValue<string>();

        // The operator retries the faulted handler. The successor applies the cancellation and holds, and the process is
        // killed after the retry committed and before the successor completes (injection point 4).
        await SubmitRetryAsync(original, executionId, faulted, "retry-" + executionId);
        await WaitForAsync(original, () => EffectLog(journal).Contains("delivery-cancel"),
            "the retried delivery cancellation to apply its effect", () => Describe(journal));
        var retriedCall = Calls(journal, "delivery-cancel")[2];

        await using var restarted = await original.CrashAndRestartAsync(directory =>
            File.WriteAllText(Path.Combine(directory, "reservations", "delivery-cancel.release"), "release"));
        await LoginAsync(restarted);
        var completed = await WaitForStateAsync(restarted, executionId, instance => instance["instance"]?["status"]?.GetValue<string>() == "Completed");

        // Every handler call used one operation key, which targeted the uncertain delivery's key, and each effect landed
        // once: the cancellation, then the inventory release.
        Assert.Equal(["inventory", "delivery", "delivery-cancel", "inventory-release"], EffectLog(journal));
        var cancelCalls = Calls(journal, "delivery-cancel");
        Assert.Equal(4, cancelCalls.Length);
        Assert.NotEqual(retriedCall.ProcessId, cancelCalls[3].ProcessId);
        Assert.Single(cancelCalls.Select(call => call.OperationKey).Distinct());
        Assert.False(File.Exists(EffectMarkerPath(journal, Assert.Single(Calls(journal, "delivery")).OperationKey)));
        Assert.Single(Calls(journal, "inventory-release"));
        Assert.Empty(Calls(journal, "confirm"));

        var scope = Activity(completed, "scope");
        Assert.Contains("Compensated", scope["outcomeNames"]!.AsArray().Select(outcome => outcome!.GetValue<string>()));
        var retried = Assert.Single(completed["incidents"]!.AsArray(), item => item?["incidentId"]?.GetValue<string>() == incident["incidentId"]!.GetValue<string>());
        Assert.Equal("Resolved", retried!["status"]!.GetValue<string>());
        Assert.Equal("OperatorRetry", retried["resolutionOutcome"]!["actionKind"]!.GetValue<string>());
        Assert.Equal("OperatorRetryAlteration", retried["resolutionOutcome"]!["systemSource"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_nested_scope_s_handed_over_entries_are_undone_once_and_a_confirmed_step_is_kept_across_a_kill()
    {
        await using var original = await WorkbenchProcess.StartAsync(WitnessShell(), PrepareContentRoot);
        await LoginAsync(original);
        var journal = Path.Combine(original.ContentRoot, "reservations");
        var reservations = new Reservations(await ActivityVersionAsync(original, ParticipantType), journal);
        var inner = await ActivityVersionAsync(original, ScopeType);
        var confirm = await ActivityVersionAsync(original, ConfirmType);

        // Spec 258 N1 + C1: the payment is confirmed, so nothing can undo it; the inner scope completes and hands its two
        // reservations to the outer scope, which undoes them when the order is rejected.
        var published = await PublishScopeAsync(original, "Compensation nesting witness", [Input("faultAfterCompensation", false)],
            Step("Pay", reservations.Participant("payment", "payment"), reservations.Participant("payment-refund", "payment-refund")),
            Step("Confirm payment", Node("confirm-payment", confirm), null),
            Step("Reserve", Node("inner", inner, [Input("faultAfterCompensation", false)], ScopeStructure(
                Step("Reserve inventory", reservations.Participant("inventory", "inventory", captures: "inventoryReservation"),
                    reservations.Participant("inventory-release", "inventory-release", undoes: VariableArgument("Undoes", "inventoryReservation"))),
                Step("Reserve delivery", reservations.Participant("delivery", "delivery"),
                    reservations.Participant("delivery-cancel", "delivery-cancel", hold: true)))), null),
            Step("Confirm order", reservations.Participant("confirm", "confirm", reject: true), null));

        // The outer scope's first handler is the inner scope's delivery cancellation: it applies its effect and holds.
        var execute = original.Client.PostAsJsonAsync($"/runtime/workflows/executables/{published.ArtifactId}/execute",
            new { sourceReferenceId = published.SourceReferenceId });
        await WaitForAsync(original, () => EffectLog(journal).Contains("delivery-cancel"),
            "the handed-over delivery cancellation to apply its effect", () => Describe(original, journal, execute));
        var executionId = await ExecutionIdAsync(original, published.ArtifactId);

        await using var restarted = await original.CrashAndRestartAsync(directory =>
            File.WriteAllText(Path.Combine(directory, "reservations", "delivery-cancel.release"), "release"));
        await Assert.ThrowsAnyAsync<Exception>(() => execute);
        await LoginAsync(restarted);
        var completed = await WaitForStateAsync(restarted, executionId, instance => instance["instance"]?["status"]?.GetValue<string>() == "Completed");

        // One undo per unconfirmed effect, newest first, and none for the confirmed payment.
        Assert.Equal(["payment", "inventory", "delivery", "delivery-cancel", "inventory-release"], EffectLog(journal));
        Assert.Equal(2, Calls(journal, "delivery-cancel").Length);
        Assert.Single(Calls(journal, "inventory-release"));
        Assert.Empty(Calls(journal, "payment-refund"));
        Assert.Contains("Done", Activity(completed, "inner")["outcomeNames"]!.AsArray().Select(outcome => outcome!.GetValue<string>()));
        Assert.Contains("Compensated", Activity(completed, "scope")["outcomeNames"]!.AsArray().Select(outcome => outcome!.GetValue<string>()));
        Assert.Equal("compensation", Activity(completed, "delivery-cancel")["provenance"]!["schedulingCause"]!.GetValue<string>());
    }

    /// <summary>
    /// Spec 261 (M5): with a handler retry delay, a failed delivery cancellation waits before its second attempt, the run
    /// yields while it waits, and the process is killed during the wait. The restarted process runs the retry once, after
    /// its due time, with the same operation key, and the recovery finishes.
    /// </summary>
    [Fact]
    public async Task A_handler_retry_that_waits_survives_a_kill_and_runs_once_after_its_due_time()
    {
        await using var original = await WorkbenchProcess.StartAsync(WitnessShell(), PrepareContentRoot);
        await LoginAsync(original);
        var journal = Path.Combine(original.ContentRoot, "reservations");
        var reservations = new Reservations(await ActivityVersionAsync(original, ParticipantType), journal);
        var published = await PublishScopeAsync(original, "Compensation backoff witness",
            [Input("faultAfterCompensation", false), Input("handlerMaxAttempts", 3), Input("handlerRetryDelay", "00:00:30")],
            Step("Reserve inventory", reservations.Participant("inventory", "backoff-inventory"),
                reservations.Participant("inventory-release", "backoff-inventory-release")),
            Step("Reserve delivery", reservations.Participant("delivery", "backoff-delivery"),
                reservations.Participant("delivery-cancel", "backoff-delivery-cancel", failTimes: 1)),
            Step("Confirm order", reservations.Participant("confirm", "backoff-confirm", reject: true), null));

        using (var executed = await original.Client.PostAsJsonAsync($"/runtime/workflows/executables/{published.ArtifactId}/execute",
                   new { sourceReferenceId = published.SourceReferenceId }))
            Assert.True(executed.IsSuccessStatusCode, await executed.Content.ReadAsStringAsync());
        await WaitForAsync(original, () => Calls(journal, "backoff-delivery-cancel").Length == 1,
            "the first delivery cancellation attempt to fail", () => Describe(journal));
        var executionId = await ExecutionIdAsync(original, published.ArtifactId);

        // The run yielded while the retry waits: it is running, nothing blocks, and the journal shows the due time.
        var waitingRun = await WaitForStateAsync(original, executionId, instance =>
            instance["incidents"]!.AsArray().All(item => item?["isBlocking"]?.GetValue<bool>() != true));
        Assert.Equal("Running", waitingRun["instance"]!["status"]!.GetValue<string>());
        var waitingJournal = await AssertJournalAsync(original, waitingRun, "Compensating");
        var delivery = Assert.Single(waitingJournal["entries"]!.AsArray(), entry => entry?["step"]?.GetValue<string>() == "Reserve delivery")!;
        var scheduled = delivery["attempts"]!.AsArray()[^1]!;
        Assert.Equal("Running", scheduled["result"]!.GetValue<string>());
        var notBefore = scheduled["notBefore"]!.GetValue<DateTimeOffset>();
        Assert.True(notBefore > DateTimeOffset.UtcNow, $"The retry was due at {notBefore:O}, before the kill.");

        await using var restarted = await original.CrashAndRestartAsync(_ => { });
        await LoginAsync(restarted);
        if (DateTimeOffset.UtcNow < notBefore - TimeSpan.FromSeconds(2))
            Assert.Single(Calls(journal, "backoff-delivery-cancel"));
        var completed = await WaitForStateAsync(restarted, executionId, instance => instance["instance"]?["status"]?.GetValue<string>() == "Completed");

        Assert.True(DateTimeOffset.UtcNow >= notBefore);
        var cancelCalls = Calls(journal, "backoff-delivery-cancel");
        Assert.Equal(2, cancelCalls.Length);
        Assert.Single(cancelCalls.Select(call => call.OperationKey).Distinct());
        Assert.Single(Calls(journal, "backoff-inventory-release"));
        Assert.Contains("Compensated", Activity(completed, "scope")["outcomeNames"]!.AsArray().Select(outcome => outcome!.GetValue<string>()));
    }

    /// <summary>
    /// Spec 260 (M4), over REST against the real Workbench process, on runs a Studio test run started (execution
    /// authority <c>workflow-designer-test-run</c>, which only FR-406 lets an operator alter by id): O1 retries the failed
    /// handler; O4 abandons a recovery, which faults the run and keeps the journal; O2h resolves the failed handler as
    /// effect absent, and the process is killed after the alteration checkpoint committed and before the decision reached
    /// the scope (injection point 2). The restarted process applies the decision once.
    /// </summary>
    [Fact]
    public async Task Operator_actions_retry_resolve_and_abandon_over_rest_and_a_resolve_survives_a_kill_before_delivery()
    {
        await using var original = await WorkbenchProcess.StartAsync(WitnessShell(), PrepareContentRoot);
        await LoginAsync(original);
        var journal = Path.Combine(original.ContentRoot, "reservations");
        var reservations = new Reservations(await ActivityVersionAsync(original, ParticipantType), journal);

        // O1: the retried cancellation (third call) succeeds and the recovery finishes.
        var retried = await StartWaitingTestRunAsync(original, reservations, "o1");
        var retryRun = await WaitForStateAsync(original, retried, instance => WaitingIncident(instance) is not null);
        Assert.True(retryRun["permissions"]!["canManage"]!.GetValue<bool>());
        await AssertJournalAsync(original, retryRun, "AwaitingIntervention");
        await SubmitRetryAsync(original, retried, WaitingIncident(retryRun)!["activityExecutionId"]!.GetValue<string>(), "o1-retry");
        var completedRetry = await WaitForStateAsync(original, retried, instance => instance["instance"]?["status"]?.GetValue<string>() == "Completed");
        Assert.Contains("Compensated", Activity(completedRetry, "scope")["outcomeNames"]!.AsArray().Select(outcome => outcome!.GetValue<string>()));
        Assert.Equal(3, Calls(journal, "o1-delivery-cancel").Length);
        Assert.Single(Calls(journal, "o1-inventory-release"));

        // O4: abandon faults the scope with the coded fault, the run faults, and the journal stays readable.
        var abandoned = await StartWaitingTestRunAsync(original, reservations, "o4", failTimes: 10);
        var abandonRun = await WaitForStateAsync(original, abandoned, instance => WaitingIncident(instance) is not null);
        var abandonIncident = WaitingIncident(abandonRun)!;
        Assert.Contains("compensation.abandon", abandonIncident["resolutionOutcome"]!["metadata"]!["runtime.interventionDecisions"]!.GetValue<string>());
        await SubmitDecisionAsync(original, abandoned, abandonIncident["incidentId"]!.GetValue<string>(), "compensation.abandon", "o4-abandon");
        var faulted = await WaitForStateAsync(original, abandoned, instance => instance["instance"]?["status"]?.GetValue<string>() == "Faulted", stopOnTerminal: false);
        Assert.Equal("Faulted", Activity(faulted, "scope")["status"]!.GetValue<string>());
        var abandonedJournal = await AssertJournalAsync(original, faulted, "Abandoned");
        Assert.Equal(abandonIncident["incidentId"]!.GetValue<string>(), abandonedJournal["abandonment"]!["incidentId"]!.GetValue<string>());
        Assert.Empty(Calls(journal, "o4-inventory-release"));
        var decided = Assert.Single(faulted["incidents"]!.AsArray(), item => item?["incidentId"]?.GetValue<string>() == abandonIncident["incidentId"]!.GetValue<string>());
        Assert.Equal("OperatorDecision", decided!["resolutionOutcome"]!["actionKind"]!.GetValue<string>());
        Assert.Equal("OperatorDecisionAlteration", decided["resolutionOutcome"]!["systemSource"]!.GetValue<string>());

        // O2h: resolve the failed handler as effect absent; the delivery holds and the process is killed before it.
        var resolved = await StartWaitingTestRunAsync(original, reservations, "o2h", failTimes: 10);
        var resolveRun = await WaitForStateAsync(original, resolved, instance => WaitingIncident(instance) is not null);
        var resolveIncident = WaitingIncident(resolveRun)!["incidentId"]!.GetValue<string>();
        File.WriteAllText(Path.Combine(journal, "decision-delivery.hold"), "hold");
        await SubmitDecisionAsync(original, resolved, resolveIncident, "compensation.resolve.effect-absent", "o2h-resolve");
        await WaitForAsync(original, () => File.Exists(Path.Combine(journal, "decision-delivery.held")),
            "the decision delivery to hold after the alteration checkpoint", () => Describe(journal));
        var committed = await WaitForStateAsync(original, resolved, instance => instance["incidents"]!.AsArray()
            .Any(item => item?["incidentId"]?.GetValue<string>() == resolveIncident && item["status"]?.GetValue<string>() == "Resolved"));
        Assert.Equal("Running", committed["instance"]!["status"]!.GetValue<string>());
        Assert.Empty(Calls(journal, "o2h-inventory-release"));

        await using var restarted = await original.CrashAndRestartAsync(directory =>
            File.WriteAllText(Path.Combine(directory, "reservations", "decision-delivery.release"), "release"));
        await LoginAsync(restarted);
        var completed = await WaitForStateAsync(restarted, resolved, instance => instance["instance"]?["status"]?.GetValue<string>() == "Completed");

        Assert.Contains("Compensated", Activity(completed, "scope")["outcomeNames"]!.AsArray().Select(outcome => outcome!.GetValue<string>()));
        Assert.Single(Calls(journal, "o2h-inventory-release"));
        Assert.Equal(2, Calls(journal, "o2h-delivery-cancel").Length);
        // The operator said the delivery was undone elsewhere: its reservation is still present here.
        Assert.True(File.Exists(EffectMarkerPath(journal, Assert.Single(Calls(journal, "o2h-delivery")).OperationKey)));
        var outcome = Assert.Single(completed["incidents"]!.AsArray(), item => item?["incidentId"]?.GetValue<string>() == resolveIncident)!["resolutionOutcome"]!;
        Assert.Equal("OperatorDecision", outcome["actionKind"]!.GetValue<string>());
        Assert.Equal("compensation.resolve.effect-absent", outcome["metadata"]!["runtime.interventionDecision"]!.GetValue<string>());
        Assert.Equal("Witness reason", outcome["metadata"]!["runtime.alteration.reason"]!.GetValue<string>());
        Assert.Equal(1, Directory.EnumerateFiles(journal, "decision-delivery.held").Count());
    }

    // Publishes a scope whose delivery cancellation fails its two attempts, starts it as a Studio test run, and returns
    // the run id. With failTimes 2 the third call (an operator retry) succeeds.
    private static async Task<string> StartWaitingTestRunAsync(WorkbenchProcess host, Reservations reservations, string prefix, int failTimes = 2)
    {
        var versionId = await SubmitScopeAsync(host, $"Compensation operator witness {prefix}",
            [Input("faultAfterCompensation", false), Input("handlerMaxAttempts", 2)],
            Step("Reserve inventory", reservations.Participant("inventory", $"{prefix}-inventory"),
                reservations.Participant("inventory-release", $"{prefix}-inventory-release")),
            Step("Reserve delivery", reservations.Participant("delivery", $"{prefix}-delivery"),
                reservations.Participant("delivery-cancel", $"{prefix}-delivery-cancel", failTimes: failTimes)),
            Step("Confirm order", reservations.Participant("confirm", $"{prefix}-confirm", reject: true), null));
        var testRun = await PostAsync(host, $"/publishing/workflows/{versionId}/test-runs", new { });
        return testRun["workflowExecutionId"]?.GetValue<string>()
               ?? throw new InvalidOperationException($"The test run did not start a run: {testRun}\n{host.Output}");
    }

    private static async Task<string> SubmitScopeAsync(WorkbenchProcess host, string name, JsonNode[] scopeInputs, params JsonObject[] steps)
    {
        var scope = await ActivityVersionAsync(host, ScopeType);
        var root = Node("scope", scope, scopeInputs, ScopeStructure(steps));
        var definition = await PostAsync(host, "/design/workflows/definitions/submit", new
        {
            name, description = "Reservations an operator acts on",
            state = new { variables = Array.Empty<object>(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>(), rootActivity = root }
        });
        return definition["version"]!["id"]!.GetValue<string>();
    }

    private static async Task<JsonNode> AssertJournalAsync(WorkbenchProcess host, JsonNode instance, string phase)
    {
        var scopeExecutionId = Activity(instance, "scope")["activityExecutionId"]!.GetValue<string>();
        var executionId = instance["instance"]!["workflowExecutionId"]!.GetValue<string>();
        var view = await host.Client.GetFromJsonAsync<JsonNode>($"/runtime/workflows/instances/{executionId}/activity-executions/{scopeExecutionId}/state-view");
        Assert.True(view!["available"]!.GetValue<bool>(), view.ToJsonString());
        Assert.Equal("compensation.journal", view["viewKind"]!.GetValue<string>());
        Assert.Equal(phase, view["view"]!["phase"]!.GetValue<string>());
        return view["view"]!;
    }

    private static async Task SubmitDecisionAsync(WorkbenchProcess host, string executionId, string incidentId, string decision, string idempotencyKey)
    {
        var body = new
        {
            target = new { executionIds = new[] { executionId } },
            alterations = new[]
            {
                new { kind = "DecideIntervention", schemaVersion = 1, payload = new { incidentId, decision, reason = "Witness reason" } }
            }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/runtime/workflows/alteration-plans") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        using var response = await host.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Decision submission: {(int)response.StatusCode} {text}\n{host.Output}");
    }

    private static JsonNode? WaitingIncident(JsonNode instance) => instance["incidents"]?.AsArray().LastOrDefault(item =>
        item?["executableNodeId"]?.GetValue<string>() == "delivery-cancel" && item["isBlocking"]?.GetValue<bool>() == true &&
        item["resolutionOutcome"]?["actionKind"]?.GetValue<string>() == "WaitForIntervention");

    private static async Task SubmitRetryAsync(WorkbenchProcess host, string executionId, string activityExecutionId, string idempotencyKey)
    {
        var body = new
        {
            target = new { executionIds = new[] { executionId } },
            alterations = new[]
            {
                new { kind = "RetryFaultedActivity", schemaVersion = 1, payload = new { sourceActivityExecutionId = activityExecutionId, reason = "Delivery service restored" } }
            }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/runtime/workflows/alteration-plans") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        using var response = await host.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Retry submission: {(int)response.StatusCode} {text}\n{host.Output}");
    }

    // Mirrors ReservationParticipant's marker naming so the test can see whether an effect is still present.
    private static string EffectMarkerPath(string journal, string operationKey) => Path.Combine(journal,
        $"{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(operationKey)))[..24]}.effect");

    private static WorkbenchShell WitnessShell() => WorkbenchShell.Development with
    {
        Settings = new Dictionary<string, string>
        {
            ["Nuplane:Setup:Feeds:0:Directory:Watch"] = "false",
            ["Nuplane:Setup:PollInterval"] = "1.00:00:00"
        }
    };

    private static async Task<PublishedWorkflow> PublishAsync(WorkbenchProcess host, string journal)
    {
        var participant = await ActivityVersionAsync(host, ParticipantType);
        var reservations = new Reservations(participant, journal);
        // A handler gets the reservation id its forward step made in one of two ways: the step captures it into a
        // workflow variable that the handler reads, or the handler references the step's result directly.
        return await PublishScopeAsync(host, "Compensation recovery witness", [Input("faultAfterCompensation", false)],
            Step("Reserve inventory", reservations.Participant("inventory", "inventory", captures: "inventoryReservation"),
                reservations.Participant("inventory-release", "inventory-release", undoes: VariableArgument("Undoes", "inventoryReservation"))),
            Step("Reserve delivery", reservations.Participant("delivery", "delivery"),
                reservations.Participant("delivery-cancel", "delivery-cancel", hold: true, undoes: ActivityResultArgument("Undoes", "delivery", "Reservation"))),
            Step("Confirm order", reservations.Participant("confirm", "confirm", reject: true), null));
    }

    private static async Task<PublishedWorkflow> PublishScopeAsync(WorkbenchProcess host, string name, JsonNode[] scopeInputs, params JsonObject[] steps)
    {
        var scope = await ActivityVersionAsync(host, ScopeType);
        var root = Node("scope", scope, scopeInputs, ScopeStructure(steps));
        var definition = await PostAsync(host, "/design/workflows/definitions/submit", new
        {
            name, description = "Reservations undone after a rejected order",
            state = new { variables = new[] { Variable("inventoryReservation") }, inputs = Array.Empty<object>(), outputs = Array.Empty<object>(), rootActivity = root }
        });
        var published = await PostAsync(host, $"/publishing/workflows/{definition["version"]!["id"]!.GetValue<string>()}/publish", new { });
        return new(published["artifactId"]!.GetValue<string>(), published["sourceReferenceId"]!.GetValue<string>());
    }

    private static JsonObject ScopeStructure(params JsonObject[] steps) => new()
    {
        ["kind"] = "elsa.compensation-scope.structure", ["schemaVersion"] = "1.0.0",
        ["payload"] = new JsonObject { ["steps"] = new JsonArray(steps) }
    };

    private static JsonObject Step(string name, JsonObject activity, JsonObject? compensation) => new()
    {
        ["name"] = name, ["activity"] = activity, ["compensation"] = compensation
    };

    private sealed record Reservations(string ParticipantVersion, string Journal)
    {
        public JsonObject Participant(string nodeId, string key, bool reject = false, bool hold = false, string? captures = null,
            JsonObject? undoes = null, bool loseResponse = false, int failTimes = 0)
        {
            List<JsonNode> inputs = [Input("DirectoryPath", Journal), Input("Key", key), Input("Reject", reject), Input("HoldAfterEffect", hold)];
            if (undoes is not null) inputs.Add(undoes);
            if (loseResponse) inputs.Add(Input("LoseResponse", true));
            if (failTimes > 0) inputs.Add(Input("FailTimes", failTimes));
            var node = Node(nodeId, ParticipantVersion, [.. inputs]);
            if (captures is not null) node["outputs"] = new JsonArray(VariableArgument("Reservation", captures));
            return node;
        }
    }

    private static async Task<string> ExecutionIdAsync(WorkbenchProcess host, string artifactId)
    {
        var instances = await host.Client.GetFromJsonAsync<JsonArray>("/runtime/workflows/instances");
        var instance = Assert.Single(instances!, item => item?["artifactId"]?.GetValue<string>() == artifactId);
        return instance!["workflowExecutionId"]!.GetValue<string>();
    }

    private static object Variable(string referenceKey) =>
        new { referenceKey, name = referenceKey, type = new { alias = "String" }, storageDriverType = (string?)null, @default = (object?)null };

    private static JsonObject VariableArgument(string key, string variable) => new()
    {
        ["referenceKey"] = key,
        ["value"] = new JsonObject
        {
            ["expressionType"] = "Variable", ["value"] = new JsonObject { ["referenceKey"] = variable, ["declaringScopeId"] = "workflow" }
        }
    };

    private static JsonObject ActivityResultArgument(string key, string producerNodeId, string projectionKey) => new()
    {
        ["referenceKey"] = key,
        ["value"] = new JsonObject
        {
            ["expressionType"] = "ActivityResult",
            ["value"] = new JsonObject { ["producerNodeId"] = producerNodeId, ["projectionKey"] = projectionKey, ["producerScopeId"] = "scope" }
        }
    };

    private static JsonObject Node(string id, string version, JsonNode[]? inputs = null, JsonNode? structure = null) => new()
    {
        ["nodeId"] = id, ["activityVersionId"] = version, ["inputs"] = new JsonArray(inputs ?? []),
        ["outputs"] = new JsonArray(), ["structure"] = structure
    };

    private static JsonObject Input(string key, object value) => new()
    {
        ["referenceKey"] = key, ["value"] = new JsonObject { ["expressionType"] = "Literal", ["value"] = JsonSerializer.SerializeToNode(value) }
    };

    private static JsonNode Activity(JsonNode instance, string nodeId) => instance["activities"]!.AsArray()
        .Last(activity => activity?["executableNodeId"]?.GetValue<string>() == nodeId)!;

    private static string[] EffectLog(string journal)
    {
        var path = Path.Combine(journal, "effects.log");
        return File.Exists(path) ? File.ReadAllLines(path).Where(line => line.Length > 0).ToArray() : [];
    }

    private static ParticipantCall[] Calls(string journal, string key)
    {
        var path = Path.Combine(journal, key + ".calls");
        return File.Exists(path)
            ? File.ReadAllLines(path).Select(line => line.Split('|')).Where(parts => parts.Length >= 3)
                .Select(parts => new ParticipantCall(parts[0], parts[1], parts.Length > 3 ? parts[3] : "")).ToArray()
            : [];
    }

    private static string[] Undone(string journal, string key)
    {
        var path = Path.Combine(journal, key + ".undoes");
        return File.Exists(path) ? File.ReadAllLines(path).Where(line => line.Length > 0).ToArray() : [];
    }

    private static string Describe(WorkbenchProcess host, string journal, Task<HttpResponseMessage> execute)
    {
        if (!execute.IsCompletedSuccessfully)
            return $"{Describe(journal)}{Environment.NewLine}Execute: {execute.Status}";

        var response = execute.Result.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var executionId = JsonNode.Parse(response)?["workflowExecutionId"]?.GetValue<string>();
        var instance = executionId is null ? null : host.Client.GetStringAsync($"/runtime/workflows/instances/{executionId}").GetAwaiter().GetResult();
        return $"{Describe(journal)}{Environment.NewLine}Execute: {(int)execute.Result.StatusCode} {response}{Environment.NewLine}Instance: {instance}";
    }

    private static string Describe(string journal) => Directory.Exists(journal)
        ? string.Join(Environment.NewLine, Directory.EnumerateFiles(journal).OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => $"{Path.GetFileName(path)}: {File.ReadAllText(path).Trim()}"))
        : $"Participant journal is missing: {journal}";

    private static async Task LoginAsync(WorkbenchProcess host) =>
        _ = await PostAsync(host, "/_elsa/identity/login", new { username = "admin", password = "Password123!" });

    private static async Task<string> ActivityVersionAsync(WorkbenchProcess host, string type)
    {
        var catalog = await host.Client.GetFromJsonAsync<JsonNode>($"/design/activities/definitions?search={Uri.EscapeDataString(type)}");
        Assert.True(catalog?["items"]?.AsArray().Count > 0, $"Activity {type} was not reconciled: {catalog}\n{host.Output}");
        var match = Assert.Single(catalog!["items"]!.AsArray(), item => item?["definition"]?["activityTypeKey"]?.GetValue<string>() == type);
        return match!["definition"]!["headVersionId"]!.GetValue<string>();
    }

    private static async Task<JsonNode> PostAsync(WorkbenchProcess host, string path, object body)
    {
        using var response = await host.Client.PostAsJsonAsync(path, body);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path}: {(int)response.StatusCode} {text}\n{host.Output}");
        return JsonNode.Parse(text)!;
    }

    private static async Task<JsonNode> WaitForStateAsync(WorkbenchProcess host, string executionId, Func<JsonNode, bool> predicate, bool stopOnTerminal = true)
    {
        JsonNode? last = null;
        var deadline = DateTimeOffset.UtcNow + Patience;
        while (DateTimeOffset.UtcNow < deadline)
        {
            last = await host.Client.GetFromJsonAsync<JsonNode>($"/runtime/workflows/instances/{executionId}");
            if (last is not null && predicate(last)) return last;
            if (stopOnTerminal && last?["instance"]?["status"]?.GetValue<string>() is "Faulted" or "Cancelled") break;
            await Task.Delay(200);
        }
        throw new InvalidOperationException($"Workflow did not reach the expected state: {last}\n{host.Output}");
    }

    private static async Task WaitForAsync(WorkbenchProcess host, Func<bool> predicate, string expected, Func<string>? evidence = null)
    {
        var deadline = DateTimeOffset.UtcNow + Patience;
        while (!predicate())
        {
            if (DateTimeOffset.UtcNow >= deadline)
                Assert.Fail($"Timed out waiting for {expected}.\n{evidence?.Invoke()}\n{host.Output}");
            await Task.Delay(100);
        }
    }

    private static void PrepareContentRoot(string directory)
    {
        var path = Path.Combine(directory, "shells.json");
        var shells = JsonNode.Parse(File.ReadAllText(path))!;
        var features = shells["CShells"]!["Shells"]!["default"]!["Features"]!.AsObject();
        features["ReservationWitness"] = new JsonObject { ["HoldDecisionDeliveryDirectory"] = Path.Combine(directory, "reservations") };
        // A restarted process reclaims the killed process's execution once its ownership lease expires.
        features["WorkflowsRuntimeResumption"] = new JsonObject
        {
            ["SweepIntervalSeconds"] = 1,
            ["LeaseTimeoutMinutes"] = 0.1,
            ["HeartbeatTimeoutMinutes"] = 0.1
        };
        File.WriteAllText(path, shells.ToJsonString());

        var configuration = typeof(WorkbenchBuild).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var project = Path.Combine(WorkbenchBuild.RepositoryRoot, "Essentials/tests/Workbench/Fixtures/Reservations");
        var framework = Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        // Nuplane's acquisition cache can outlive the host's state directory, so each run packs a unique version.
        var packageVersion = "1.0.0-witness." + Guid.NewGuid().ToString("N");
        using var archive = ZipFile.Open(Path.Combine(directory, "packages", PackageId + "." + packageVersion + ".nupkg"), ZipArchiveMode.Create);
        AddText(archive, "[Content_Types].xml", """<?xml version="1.0"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="nuspec" ContentType="application/octet"/><Default Extension="dll" ContentType="application/octet"/><Default Extension="json" ContentType="application/octet"/></Types>""");
        AddText(archive, "_rels/.rels", $"""<?xml version="1.0"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Type="http://schemas.microsoft.com/packaging/2010/07/manifest" Target="/{PackageId}.nuspec" Id="R1"/></Relationships>""");
        AddText(archive, PackageId + ".nuspec", $"""<?xml version="1.0"?><package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>{PackageId}</id><version>{packageVersion}</version><authors>tests</authors><description>Owned process compensation witness.</description></metadata></package>""");
        archive.CreateEntryFromFile(Path.Combine(project, "bin", configuration, framework, PackageId + ".dll"), "lib/" + framework + "/" + PackageId + ".dll");
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(project, "elsa-package.json")))!;
        manifest["package"]!["version"] = packageVersion;
        AddText(archive, "elsa-package.json", manifest.ToJsonString());
    }

    private static void AddText(ZipArchive archive, string path, string value)
    {
        using var stream = archive.CreateEntry(path).Open();
        stream.Write(Encoding.UTF8.GetBytes(value));
    }

    private sealed record ParticipantCall(string ProcessId, string InvocationId, string OperationKey);

    private sealed record PublishedWorkflow(string ArtifactId, string SourceReferenceId);
}

[CollectionDefinition(WorkbenchCompensationRecoveryTests.Name, DisableParallelization = true)]
public sealed class WorkbenchCompensationRecoveryTestCollection;
