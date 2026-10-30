using System.Text.Json;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnDirectExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Duration_and_human_decisions_keep_the_first_winner_and_refuse_late_progression(bool timerFirst)
    {
        var clock = new DurationClock(DateTimeOffset.UtcNow);
        var definition = TimedHumanMaterial(); RuntimeExecutionMaterial material;
        BpmnDirectExecutionDriver.Work human; BpmnDurationTimer timer;
        await using (var host = await OpenAsync(configure: services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock))))
        {
            await using var scope = await ScopeAsync(host);
            var store = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>();
            await store.SaveModelAsync(definition); material = (await store.FindMaterialAsync(definition.Identity.ArtifactId))!;
            await SendAsync(host, material, "start", "start", new { });
            var state = (await scope.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>().FindAsync("direct-execution"))!;
            var document = BpmnDirectExecutionDriver.Read(state);
            Assert.Equal(11, document.FormatVersion); Assert.Equal(2, document.Work.Count);
            human = Assert.Single(document.Work, work => work.Timer is null); timer = Assert.Single(document.Work, work => work.Timer is not null).Timer!;
            Assert.Equal(clock.GetUtcNow().AddSeconds(5), timer.DueAt);
            var listed = await scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>().ListWorkAsync("direct-execution", new(10), new("operator", null));
            Assert.Equal(human.InvocationId, Assert.Single(listed.Items).InvocationId);
            await AssertNativeEmptyAsync(scope.ServiceProvider);
        }
        // Provider restart and a controlled unit clock prove decisions; real host/process qualification is separate.
        await using (var host = await OpenAsync(configure: services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock))))
        {
            await using var scope = await ScopeAsync(host);
            var reader = scope.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>();
            if (timerFirst)
            {
                clock.Advance(TimeSpan.FromSeconds(5));
                await SendAsync(host, material, timer.InputId, "timer-elapsed", timer);
            }
            else await SendAsync(host, material, "human-first", "complete-human", new { human.InvocationId, human.AttemptId });
            var won = (await reader.FindAsync("direct-execution"))!;
            Assert.Equal(2, won.Revision); Assert.Equal(RuntimeModelLifecycle.TerminalSettled, won.Lifecycle);
            var state = BpmnDirectExecutionDriver.Read(won).State;
            Assert.Contains(state.Diagnostics, diagnostic => diagnostic.ElementId == (timerFirst ? "timed-out" : "done"));
            Assert.DoesNotContain(state.Diagnostics, diagnostic => diagnostic.ElementId == (timerFirst ? "done" : "timed-out"));
            clock.Advance(TimeSpan.FromSeconds(5));
            if (timerFirst) await SendAsync(host, material, "late-human", "complete-human", new { human.InvocationId, human.AttemptId });
            else await SendAsync(host, material, timer.InputId, "timer-elapsed", timer);
            var late = (await reader.FindAsync("direct-execution"))!;
            Assert.Equal(JsonSerializer.Serialize(won, Web), JsonSerializer.Serialize(late, Web));
            var receipt = (await reader.FindInputAsync("direct-execution", timerFirst ? "late-human" : timer.InputId))!;
            Assert.Equal("Rejected", receipt.Result.GetProperty("status").GetString()); Assert.Equal(2, receipt.AcceptedRevision);
            await AssertNativeEmptyAsync(scope.ServiceProvider);
        }
    }

    [Fact]
    public async Task An_actual_retained_timer_source_cannot_advance_the_model_before_its_deadline()
    {
        var clock = new DurationClock(DateTimeOffset.UtcNow);
        await using var host = await OpenAsync(configure: services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock)));
        await using var scope = await ScopeAsync(host);
        var definition = TimedHumanMaterial(); var store = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>();
        await store.SaveModelAsync(definition); var material = (await store.FindMaterialAsync(definition.Identity.ArtifactId))!;
        await SendAsync(host, material, "start", "start", new { });
        var reader = scope.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>(); var before = (await reader.FindAsync("direct-execution"))!;
        var timer = Assert.Single(BpmnDirectExecutionDriver.Read(before).Work, work => work.Timer is not null).Timer!;
        await Assert.ThrowsAsync<Elsa.Workflows.Runtime.Core.Exceptions.RuntimePostCommitDeliveryException>(() => SendAsync(host, material, timer.InputId, "timer-elapsed", timer));
        Assert.Null(await reader.FindInputAsync("direct-execution", timer.InputId));
        Assert.Equal(JsonSerializer.Serialize(before, Web), JsonSerializer.Serialize(await reader.FindAsync("direct-execution"), Web));
    }

    [Theory]
    [InlineData("PT0S")]
    [InlineData("-PT1S")]
    [InlineData("not-a-duration")]
    public async Task Unrepresentable_or_nonpositive_duration_bindings_refuse_before_material_admission(string duration)
    {
        await using var host = await OpenAsync(); await using var scope = await ScopeAsync(host);
        await Assert.ThrowsAsync<BpmnModelValidationException>(() => scope.ServiceProvider.GetRequiredService<BpmnDirectExecutionDriver>()
            .ValidateMaterialAsync(TimedHumanMaterial(duration)).AsTask());
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("attempt")]
    [InlineData("deadline")]
    public async Task A_timer_input_cannot_replace_its_committed_scope_attempt_or_deadline(string changed)
    {
        var clock = new DurationClock(DateTimeOffset.UtcNow);
        await using var host = await OpenAsync(configure: services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock)));
        await using var scope = await ScopeAsync(host);
        var definition = TimedHumanMaterial(); var store = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>();
        await store.SaveModelAsync(definition); var material = (await store.FindMaterialAsync(definition.Identity.ArtifactId))!;
        await SendAsync(host, material, "start", "start", new { });
        var reader = scope.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>(); var before = (await reader.FindAsync("direct-execution"))!;
        var timer = Assert.Single(BpmnDirectExecutionDriver.Read(before).Work, work => work.Timer is not null).Timer!;
        var invalid = changed switch { "scope" => timer with { Scope = new("foreign") }, "attempt" => timer with { AttemptId = "foreign" },
            _ => timer with { DueAt = timer.DueAt.AddSeconds(1) } };
        clock.Advance(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<BpmnModelValidationException>(() => SendAsync(host, material, invalid.InputId, "timer-elapsed", invalid));
        Assert.Null(await reader.FindInputAsync("direct-execution", invalid.InputId));
        Assert.Equal(JsonSerializer.Serialize(before, Web), JsonSerializer.Serialize(await reader.FindAsync("direct-execution"), Web));
    }

    [Fact]
    public async Task A_duration_listener_cannot_be_completed_as_human_work_to_bypass_its_deadline()
    {
        await using var host = await OpenAsync(); await using var scope = await ScopeAsync(host);
        var definition = TimedHumanMaterial("PT1H"); var store = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>();
        await store.SaveModelAsync(definition); var material = (await store.FindMaterialAsync(definition.Identity.ArtifactId))!;
        await SendAsync(host, material, "start", "start", new { });
        var reader = scope.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>(); var before = (await reader.FindAsync("direct-execution"))!;
        var timer = Assert.Single(BpmnDirectExecutionDriver.Read(before).Work, work => work.Timer is not null).Timer!;
        await SendAsync(host, material, "human-cannot-fire-timer", "complete-human", new { timer.InvocationId, timer.AttemptId });
        var receipt = (await reader.FindInputAsync("direct-execution", "human-cannot-fire-timer"))!;
        Assert.Equal("not-human-work", receipt.Result.GetProperty("code").GetString());
        Assert.Equal(JsonSerializer.Serialize(before, Web), JsonSerializer.Serialize(await reader.FindAsync("direct-execution"), Web));
    }

    private static RuntimeModelMaterial TimedHumanMaterial(string duration = "PT5S", bool emptyServiceInventory = false)
    {
        var process = new BpmnProcessDefinition("timed-human", Elements:
            [new("start", BpmnElementTypes.StartEvent), new("review", BpmnElementTypes.UserTask, bindingRef: "review-work"),
                new("timeout", BpmnElementTypes.BoundaryEvent, bindingRef: "timeout-work", attachedToRef: "review", cancelActivity: true,
                    eventDefinitions: [new(BpmnEventDefinitionTypes.Timer, new Dictionary<string,string> { [BpmnEventDefinitionProperties.Interval] = duration })]),
                new("done", BpmnElementTypes.EndEvent), new("timed-out", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("start-review", "start", "review"), new("review-done", "review", "done"), new("timeout-end", "timeout", "timed-out")]);
        var definition = new BpmnModelDefinition(4, JsonSerializer.SerializeToElement(process, Web), ["review-work"], emptyServiceInventory ? [] : null, ["timeout-work"]);
        var pin = new WorkflowExecutableIdentity("timed-artifact", "timed-definition", "timed-version", "1.0.0", "pending");
        var material = new RuntimeModelMaterial(1, pin, new(BpmnModelProtocol.Key, BpmnModelProtocol.Version), BpmnModelProtocol.NativeWorkProfile,
            JsonSerializer.SerializeToElement(definition, Web), new(1, []), [], [], []);
        return new(1, pin with { ArtifactHash = material.ComputeContentHash() }, material.Protocol, material.Profile, material.Definition, material.InputContract, [], [], []);
    }

    private sealed class DurationClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan duration) => current += duration;
    }
}
