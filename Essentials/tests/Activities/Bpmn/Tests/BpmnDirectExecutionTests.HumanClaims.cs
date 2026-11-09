using System.Text.Json;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnDirectExecutionTests
{
    [Fact]
    public void Claim_required_material_needs_its_explicit_format_and_complete_claim_inventory()
    {
        var process = JsonSerializer.SerializeToElement(ClaimProcess(timer: false, unclaimedFollowUp: false), Web);
        Assert.Throws<ArgumentException>(() => new BpmnModelDefinition(6, process, ["review-work"], null, null, null, null));
        Assert.Throws<ArgumentException>(() => new BpmnModelDefinition(6, process, ["review-work"], null, null, null, []));
        Assert.Throws<ArgumentException>(() => new BpmnModelDefinition(6, process, ["review-work"], null, null, null, ["unknown-work"]));
        Assert.Throws<ArgumentException>(() => new BpmnModelDefinition(1, process, ["review-work"], null, null, null, ["review-work"]));
        Assert.Throws<ArgumentException>(() => new BpmnModelDefinition(6, process, ["review-work"], null, [], null, ["review-work"]));
        var definition = new BpmnModelDefinition(6, process, ["review-work"], null, null, null, ["review-work"]);
        var json = JsonSerializer.SerializeToElement(definition, Web);
        Assert.Equal("review-work", Assert.Single(json.GetProperty("claimBindings").EnumerateArray()).GetString());
        // Earlier formats keep their exact definition JSON.
        Assert.False(JsonSerializer.SerializeToElement(new BpmnModelDefinition(1, process, ["review-work"]), Web).TryGetProperty("claimBindings", out _));
    }

    [Theory]
    [InlineData(BpmnModelProtocol.Profile, 6)]
    [InlineData(BpmnModelProtocol.ClaimsProfile, 1)]
    public async Task Claim_profile_and_format_cannot_be_mixed_with_unclaimed_material(string profile, int format)
    {
        await using var host = await OpenAsync(); await using var scope = await ScopeAsync(host);
        var process = JsonSerializer.SerializeToElement(ClaimProcess(timer: false, unclaimedFollowUp: false), Web);
        var definition = format == 6 ? new BpmnModelDefinition(6, process, ["review-work"], null, null, null, ["review-work"])
            : new BpmnModelDefinition(1, process, ["review-work"]);
        var material = new RuntimeModelMaterial(1, new("mixed", "mixed", "mixed", "1.0.0", "pending"), new(BpmnModelProtocol.Key, BpmnModelProtocol.Version),
            profile, JsonSerializer.SerializeToElement(definition, Web), new(1, []), [], [], []);
        await Assert.ThrowsAsync<BpmnModelValidationException>(() => scope.ServiceProvider.GetRequiredService<BpmnDirectExecutionDriver>()
            .ValidateMaterialAsync(material).AsTask());
    }

    [Fact]
    public async Task The_first_claim_owns_the_work_and_only_its_exact_claimant_can_complete_it()
    {
        await using var host = await OpenAsync(); await using var scope = await ScopeAsync(host);
        var material = await SaveAsync(scope, ClaimMaterial());
        await SendAsync(host, material, "start", "start", new { });
        var reader = scope.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>();
        var started = (await reader.FindAsync("direct-execution"))!;
        var document = BpmnDirectExecutionDriver.Read(started);
        Assert.Equal(15, document.FormatVersion); Assert.Empty(document.Claims!);
        var work = Assert.Single(document.Work);

        await SendAsync(host, material, "legacy-complete", "complete-human", new { work.InvocationId, work.AttemptId });
        await AssertRejectedAsync(reader, "legacy-complete", "claim-required", started.Revision);
        await SendAsync(host, material, "unclaimed-complete", "complete-claimed-human",
            new { work.InvocationId, work.AttemptId, claimId = "bpmn-claim:none", claimantId = "alice" });
        await AssertRejectedAsync(reader, "unclaimed-complete", "not-claimed", started.Revision);

        await SendAsync(host, material, "claim-alice", "claim-human", new { work.InvocationId, work.AttemptId, claimantId = "alice" });
        var claimed = (await reader.FindAsync("direct-execution"))!;
        var receipt = (await reader.FindInputAsync("direct-execution", "claim-alice"))!;
        Assert.Equal("Accepted", receipt.Result.GetProperty("status").GetString());
        var claimId = receipt.Result.GetProperty("claimId").GetString()!;
        Assert.Equal(started.Revision + 1, claimed.Revision);
        Assert.Equal(JsonSerializer.Serialize(started.Authority, Web), JsonSerializer.Serialize(claimed.Authority, Web));
        var owner = Assert.Single(BpmnDirectExecutionDriver.Read(claimed).Claims!);
        Assert.Equal((claimId, "alice", work.InvocationId, work.AttemptId), (owner.ClaimId, owner.ClaimantId, owner.InvocationId, owner.AttemptId));

        await SendAsync(host, material, "claim-bob", "claim-human", new { work.InvocationId, work.AttemptId, claimantId = "bob" });
        await AssertRejectedAsync(reader, "claim-bob", "already-claimed", claimed.Revision);
        await SendAsync(host, material, "bob-completes", "complete-claimed-human", new { work.InvocationId, work.AttemptId, claimId, claimantId = "bob" });
        await AssertRejectedAsync(reader, "bob-completes", "foreign-claim", claimed.Revision);
        await SendAsync(host, material, "stale-claim", "complete-claimed-human", new { work.InvocationId, work.AttemptId, claimId = claimId + "-old", claimantId = "alice" });
        await AssertRejectedAsync(reader, "stale-claim", "claim-not-current", claimed.Revision);
        await SendAsync(host, material, "stale-attempt", "complete-claimed-human", new { work.InvocationId, attemptId = "attempt:other", claimId, claimantId = "alice" });
        await AssertRejectedAsync(reader, "stale-attempt", "obsolete-human-work", claimed.Revision);
        Assert.Equal(JsonSerializer.Serialize(claimed, Web), JsonSerializer.Serialize(await reader.FindAsync("direct-execution"), Web));

        await SendAsync(host, material, "alice-completes", "complete-claimed-human", new { work.InvocationId, work.AttemptId, claimId, claimantId = "alice" });
        var completed = (await reader.FindAsync("direct-execution"))!;
        Assert.Equal(RuntimeModelLifecycle.TerminalSettled, completed.Lifecycle);
        Assert.Equal("Completed", completed.TerminalResult!.Value.GetProperty("status").GetString());
        // Exact replay is a no-op; the same identity with changed content is refused.
        await SendAsync(host, material, "alice-completes", "complete-claimed-human", new { work.InvocationId, work.AttemptId, claimId, claimantId = "alice" });
        Assert.Equal(JsonSerializer.Serialize(completed, Web), JsonSerializer.Serialize(await reader.FindAsync("direct-execution"), Web));
        await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(() =>
            SendAsync(host, material, "claim-alice", "claim-human", new { work.InvocationId, work.AttemptId, claimantId = "bob" }));
        await SendAsync(host, material, "late-claim", "claim-human", new { work.InvocationId, work.AttemptId, claimantId = "carol" });
        await AssertRejectedAsync(reader, "late-claim", "execution-terminal", completed.Revision);
        await AssertNativeEmptyAsync(scope.ServiceProvider);
    }

    [Fact]
    public async Task A_claim_survives_host_restart_and_admission_off_without_reassignment()
    {
        RuntimeExecutionMaterial material; string claimId; BpmnDirectExecutionDriver.Work work;
        await using (var host = await OpenAsync())
        {
            await using var scope = await ScopeAsync(host);
            material = await SaveAsync(scope, ClaimMaterial());
            await SendAsync(host, material, "start", "start", new { });
            var reader = scope.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>();
            work = Assert.Single(BpmnDirectExecutionDriver.Read((await reader.FindAsync("direct-execution"))!).Work);
            await SendAsync(host, material, "claim-alice", "claim-human", new { work.InvocationId, work.AttemptId, claimantId = "alice" });
            claimId = (await reader.FindInputAsync("direct-execution", "claim-alice"))!.Result.GetProperty("claimId").GetString()!;
        }
        // New admission is off after restart; accepted claim-required work remains claimable and completable.
        await using (var host = await OpenAsync(admitNewExecutions: false))
        {
            await using var scope = await ScopeAsync(host);
            var reader = scope.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>();
            var recovered = (await reader.FindAsync("direct-execution"))!;
            Assert.Equal(claimId, Assert.Single(BpmnDirectExecutionDriver.Read(recovered).Claims!).ClaimId);
            await SendAsync(host, material, "claim-bob", "claim-human", new { work.InvocationId, work.AttemptId, claimantId = "bob" });
            await AssertRejectedAsync(reader, "claim-bob", "already-claimed", recovered.Revision);
            await SendAsync(host, material, "alice-completes", "complete-claimed-human", new { work.InvocationId, work.AttemptId, claimId, claimantId = "alice" });
            Assert.Equal(RuntimeModelLifecycle.TerminalSettled, (await reader.FindAsync("direct-execution"))!.Lifecycle);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Timer_interruption_and_claimed_completion_keep_the_first_winner(bool timerFirst)
    {
        var clock = new DurationClock(DateTimeOffset.UtcNow);
        await using var host = await OpenAsync(configure: services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock)));
        await using var scope = await ScopeAsync(host);
        var material = await SaveAsync(scope, ClaimMaterial(timer: true));
        await SendAsync(host, material, "start", "start", new { });
        var reader = scope.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>();
        var document = BpmnDirectExecutionDriver.Read((await reader.FindAsync("direct-execution"))!);
        Assert.Equal(15, document.FormatVersion);
        var human = Assert.Single(document.Work, item => item.Timer is null); var timer = Assert.Single(document.Work, item => item.Timer is not null).Timer!;
        await SendAsync(host, material, "claim-alice", "claim-human", new { human.InvocationId, human.AttemptId, claimantId = "alice" });
        var claimId = (await reader.FindInputAsync("direct-execution", "claim-alice"))!.Result.GetProperty("claimId").GetString()!;
        var complete = new { human.InvocationId, human.AttemptId, claimId, claimantId = "alice" };
        clock.Advance(TimeSpan.FromSeconds(5));
        if (timerFirst) await SendAsync(host, material, timer.InputId, "timer-elapsed", timer);
        else await SendAsync(host, material, "complete", "complete-claimed-human", complete);
        var won = (await reader.FindAsync("direct-execution"))!;
        Assert.Equal(RuntimeModelLifecycle.TerminalSettled, won.Lifecycle);
        var state = BpmnDirectExecutionDriver.Read(won);
        Assert.Contains(state.State.Diagnostics, diagnostic => diagnostic.ElementId == (timerFirst ? "timed-out" : "done"));
        // The interrupted owner's claim stays as obsolete history; it never resurrects work.
        Assert.Equal(claimId, Assert.Single(state.Claims!).ClaimId);
        Assert.Empty(state.Work);
        if (timerFirst) await SendAsync(host, material, "complete", "complete-claimed-human", complete);
        else await SendAsync(host, material, timer.InputId, "timer-elapsed", timer);
        var late = (await reader.FindAsync("direct-execution"))!;
        Assert.Equal(JsonSerializer.Serialize(won, Web), JsonSerializer.Serialize(late, Web));
        Assert.Equal("Rejected", (await reader.FindInputAsync("direct-execution", timerFirst ? "complete" : timer.InputId))!.Result.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_and_claiming_keep_the_first_winner(bool cancelFirst)
    {
        await using var host = await OpenAsync(); await using var scope = await ScopeAsync(host);
        var material = await SaveAsync(scope, ClaimMaterial());
        await SendAsync(host, material, "start", "start", new { });
        var reader = scope.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>();
        var work = Assert.Single(BpmnDirectExecutionDriver.Read((await reader.FindAsync("direct-execution"))!).Work);
        if (cancelFirst)
        {
            await SendAsync(host, material, "cancel", "cancel", new { reason = "operator" });
            var cancelled = (await reader.FindAsync("direct-execution"))!;
            await SendAsync(host, material, "claim-alice", "claim-human", new { work.InvocationId, work.AttemptId, claimantId = "alice" });
            await AssertRejectedAsync(reader, "claim-alice", "execution-terminal", cancelled.Revision);
            Assert.Empty(BpmnDirectExecutionDriver.Read((await reader.FindAsync("direct-execution"))!).Claims!);
            return;
        }
        await SendAsync(host, material, "claim-alice", "claim-human", new { work.InvocationId, work.AttemptId, claimantId = "alice" });
        var claimId = (await reader.FindInputAsync("direct-execution", "claim-alice"))!.Result.GetProperty("claimId").GetString()!;
        await SendAsync(host, material, "cancel", "cancel", new { reason = "operator" });
        var terminal = (await reader.FindAsync("direct-execution"))!;
        Assert.Equal("Cancelled", terminal.TerminalResult!.Value.GetProperty("status").GetString());
        await SendAsync(host, material, "complete", "complete-claimed-human", new { work.InvocationId, work.AttemptId, claimId, claimantId = "alice" });
        await AssertRejectedAsync(reader, "complete", "execution-terminal", terminal.Revision);
    }

    [Fact]
    public async Task Unclaimed_work_in_claim_material_keeps_its_original_completion()
    {
        await using var host = await OpenAsync(); await using var scope = await ScopeAsync(host);
        var material = await SaveAsync(scope, ClaimMaterial(unclaimedFollowUp: true));
        await SendAsync(host, material, "start", "start", new { });
        var reader = scope.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>();
        var review = Assert.Single(BpmnDirectExecutionDriver.Read((await reader.FindAsync("direct-execution"))!).Work);
        await SendAsync(host, material, "claim", "claim-human", new { review.InvocationId, review.AttemptId, claimantId = "alice" });
        var claimId = (await reader.FindInputAsync("direct-execution", "claim"))!.Result.GetProperty("claimId").GetString()!;
        await SendAsync(host, material, "review", "complete-claimed-human", new { review.InvocationId, review.AttemptId, claimId, claimantId = "alice" });
        var approve = Assert.Single(BpmnDirectExecutionDriver.Read((await reader.FindAsync("direct-execution"))!).Work);
        Assert.Equal("approve-work", approve.BindingId);
        var waiting = (await reader.FindAsync("direct-execution"))!;
        await SendAsync(host, material, "claim-unclaimed", "claim-human", new { approve.InvocationId, approve.AttemptId, claimantId = "bob" });
        await AssertRejectedAsync(reader, "claim-unclaimed", "claim-not-required", waiting.Revision);
        await SendAsync(host, material, "approve", "complete-human", new { approve.InvocationId, approve.AttemptId });
        Assert.Equal(RuntimeModelLifecycle.TerminalSettled, (await reader.FindAsync("direct-execution"))!.Lifecycle);
    }

    [Fact]
    public async Task Claim_inputs_are_refused_for_unclaimed_material_and_frames()
    {
        await using var host = await OpenAsync(); await using var scope = await ScopeAsync(host);
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>();
        var legacy = Material(); await store.SaveModelAsync(legacy);
        var material = (await store.FindMaterialAsync(legacy.Identity.ArtifactId))!;
        await SendAsync(host, material, "start", "start", new { });
        var reader = scope.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>();
        var state = (await reader.FindAsync("direct-execution"))!;
        var work = Assert.Single(BpmnDirectExecutionDriver.Read(state).Work);
        await Assert.ThrowsAsync<BpmnModelValidationException>(() =>
            SendAsync(host, material, "claim", "claim-human", new { work.InvocationId, work.AttemptId, claimantId = "alice" }));
        Assert.Null(await reader.FindInputAsync("direct-execution", "claim"));
        Assert.Equal(JsonSerializer.Serialize(state, Web), JsonSerializer.Serialize(await reader.FindAsync("direct-execution"), Web));
        // Neither an unclaimed frame carrying claims nor a claim frame without them is readable.
        var document = BpmnDirectExecutionDriver.Read(state);
        foreach (var invalid in new[] { document with { Claims = [] }, document with { FormatVersion = 15 } })
            Assert.Throws<BpmnModelValidationException>(() => BpmnDirectExecutionDriver.Read(new RuntimeModelExecutionState(state.FormatVersion, state.Scope,
                state.ExecutionId, state.TenantId, state.Protocol, state.Executable, state.MaterialIncarnationId, state.Revision, state.Lifecycle,
                state.Authority, state.CreatedAt, state.UpdatedAt, JsonSerializer.SerializeToElement(invalid, Web), state.TerminalResult)));
    }

    [Fact]
    public async Task The_runtime_captures_the_authenticated_claimant_and_projects_the_current_owner()
    {
        await using var host = await OpenAsync(); await using var scope = await ScopeAsync(host);
        var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
        var definition = ClaimMaterial().Definition.Deserialize<BpmnModelDefinition>(Web)!;
        var published = await runtime.PublishAsync(definition, "publish", new("publisher", null));
        Assert.Equal(BpmnModelProtocol.ClaimsProfile, published.Profile);
        var started = await runtime.StartAsync(published.SourceReferenceId, "start", null, new("publisher", null));
        var item = Assert.Single((await runtime.ListWorkAsync(started.ExecutionId, new(10), new("alice", null))).Items);
        Assert.True(item.ClaimRequired); Assert.Null(item.Claim);
        var claimed = await runtime.ClaimHumanAsync(started.ExecutionId, "claim", item.InvocationId, item.AttemptId, new("alice", null));
        var claimId = claimed.Result!.Value.GetProperty("claimId").GetString()!;
        var owned = Assert.Single((await runtime.ListWorkAsync(started.ExecutionId, new(10), new("bob", null))).Items);
        Assert.Equal((claimId, "alice"), (owned.Claim!.ClaimId, owned.Claim.ClaimantId));
        var inspected = Assert.Single((await runtime.InspectAsync(started.ExecutionId, new("bob", null))).Work);
        Assert.True(inspected.ClaimRequired); Assert.Equal(claimId, inspected.Claim!.ClaimId);
        // A different authenticated caller cannot reuse the owner's claim, whatever it sends.
        var foreign = await runtime.CompleteClaimedHumanAsync(started.ExecutionId, "bob", item.InvocationId, item.AttemptId, claimId, null, new("bob", null));
        Assert.Equal("foreign-claim", foreign.Result!.Value.GetProperty("code").GetString());
        var done = await runtime.CompleteClaimedHumanAsync(started.ExecutionId, "alice", item.InvocationId, item.AttemptId, claimId, null, new("alice", null));
        Assert.Equal("TerminalSettled", done.Result!.Value.GetProperty("lifecycle").GetString());
        Assert.True((await runtime.CompleteClaimedHumanAsync(started.ExecutionId, "alice", item.InvocationId, item.AttemptId, claimId, null, new("alice", null))).Replay);
        await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(() =>
            runtime.ClaimHumanAsync(started.ExecutionId, "claim", item.InvocationId, item.AttemptId, new("bob", null)).AsTask());
        // Unclaimed profiles keep their original work JSON.
        var legacyWork = JsonSerializer.SerializeToElement(new BpmnHumanWork("e", "b", "t", null, "i", "a"), Web);
        Assert.False(legacyWork.TryGetProperty("claimRequired", out _)); Assert.False(legacyWork.TryGetProperty("claim", out _));
    }

    [Fact]
    public async Task Concurrent_claims_from_two_callers_produce_exactly_one_owner()
    {
        await using var host = await OpenAsync();
        string executionId; BpmnHumanWork item;
        await using (var scope = await ScopeAsync(host))
        {
            var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
            var published = await runtime.PublishAsync(ClaimMaterial().Definition.Deserialize<BpmnModelDefinition>(Web)!, "publish", new("publisher", null));
            executionId = (await runtime.StartAsync(published.SourceReferenceId, "start", null, new("publisher", null))).ExecutionId;
            item = Assert.Single((await runtime.ListWorkAsync(executionId, new(10), new("alice", null))).Items);
        }
        async Task<BpmnModelCommandOutcome> ClaimAsync(string caller)
        {
            await using var scope = await ScopeAsync(host);
            return await scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>()
                .ClaimHumanAsync(executionId, "claim-" + caller, item.InvocationId, item.AttemptId, new(caller, null));
        }
        var outcomes = await Task.WhenAll(ClaimAsync("alice"), ClaimAsync("bob"));
        var statuses = outcomes.Select(outcome => outcome.Result!.Value.GetProperty("status").GetString()).Order().ToArray();
        Assert.Equal(["Accepted", "Rejected"], statuses);
        Assert.Equal("already-claimed", Assert.Single(outcomes, outcome => outcome.Result!.Value.GetProperty("status").GetString() == "Rejected")
            .Result!.Value.GetProperty("code").GetString());
        await using var read = await ScopeAsync(host);
        var winner = Assert.Single(outcomes, outcome => outcome.Result!.Value.GetProperty("status").GetString() == "Accepted").InputId["claim-".Length..];
        var state = (await read.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>().FindAsync(executionId))!;
        Assert.Equal(winner, Assert.Single(BpmnDirectExecutionDriver.Read(state).Claims!).ClaimantId);
    }

    private static async Task<RuntimeExecutionMaterial> SaveAsync(PersistenceOperationScope scope, RuntimeModelMaterial definition)
    {
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>();
        await store.SaveModelAsync(definition);
        return (await store.FindMaterialAsync(definition.Identity.ArtifactId))!;
    }

    private static async Task AssertRejectedAsync(IRuntimeModelExecutionReader reader, string inputId, string code, long revision)
    {
        var receipt = (await reader.FindInputAsync("direct-execution", inputId))!;
        Assert.Equal("Rejected", receipt.Result.GetProperty("status").GetString());
        Assert.Equal(code, receipt.Result.GetProperty("code").GetString());
        Assert.Equal(revision, receipt.AcceptedRevision);
        Assert.Equal(revision, (await reader.FindAsync("direct-execution"))!.Revision);
    }

    private static BpmnProcessDefinition ClaimProcess(bool timer, bool unclaimedFollowUp)
    {
        var elements = new List<BpmnElement> { new("start", BpmnElementTypes.StartEvent), new("review", BpmnElementTypes.UserTask, bindingRef: "review-work"), new("done", BpmnElementTypes.EndEvent) };
        var flows = new List<BpmnSequenceFlow> { new("start-review", "start", "review") };
        if (unclaimedFollowUp)
        {
            elements.Add(new("approve", BpmnElementTypes.UserTask, bindingRef: "approve-work"));
            flows.AddRange([new("review-approve", "review", "approve"), new("approve-done", "approve", "done")]);
        }
        else flows.Add(new("review-done", "review", "done"));
        if (timer)
        {
            elements.AddRange([new("timeout", BpmnElementTypes.BoundaryEvent, bindingRef: "timeout-work", attachedToRef: "review", cancelActivity: true,
                eventDefinitions: [new(BpmnEventDefinitionTypes.Timer, new Dictionary<string, string> { [BpmnEventDefinitionProperties.Interval] = "PT5S" })]),
                new("timed-out", BpmnElementTypes.EndEvent)]);
            flows.Add(new("timeout-end", "timeout", "timed-out"));
        }
        return new("claimed-human", Elements: elements, SequenceFlows: flows);
    }

    private static RuntimeModelMaterial ClaimMaterial(bool timer = false, bool unclaimedFollowUp = false)
    {
        var definition = new BpmnModelDefinition(6, JsonSerializer.SerializeToElement(ClaimProcess(timer, unclaimedFollowUp), Web),
            unclaimedFollowUp ? ["approve-work", "review-work"] : ["review-work"], null, timer ? ["timeout-work"] : null, null, ["review-work"]);
        var pin = new WorkflowExecutableIdentity("claimed-artifact", "claimed-definition", "claimed-version", "1.0.0", "pending");
        var material = new RuntimeModelMaterial(1, pin, new(BpmnModelProtocol.Key, BpmnModelProtocol.Version), BpmnModelProtocol.ClaimsProfile,
            JsonSerializer.SerializeToElement(definition, Web), new(1, []), [], [], []);
        return new(1, pin with { ArtifactHash = material.ComputeContentHash() }, material.Protocol, material.Profile, material.Definition, material.InputContract, [], [], []);
    }
}
