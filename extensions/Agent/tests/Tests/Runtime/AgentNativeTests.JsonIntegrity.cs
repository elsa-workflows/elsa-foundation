using System.Text.Json;
using Elsa.Activities.Runtime.Services;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Journal;
using Elsa.Agent.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Tests.Runtime;

public sealed partial class AgentNativeTests
{
    private const string RawAgentPayload = """{ "escaped" : "\u0061\/\u003c", "number" : 12345678901234567890123456789012345, "fraction" : 1.0, "nested" : [ null, { "CaseKey" : "v" } ] }""";

    [Theory]
    [InlineData(false, false, null)]
    [InlineData(true, false, null)]
    [InlineData(false, true, null)]
    [InlineData(true, true, null)]
    [InlineData(false, true, AgentInvocationControls.LegacyProfile)]
    [InlineData(true, true, AgentInvocationControls.LegacyProfile)]
    [InlineData(false, true, AgentInvocationControls.Profile)]
    [InlineData(true, true, AgentInvocationControls.Profile)]
    public async Task Authenticated_journal_to_native_completion_preserves_raw_result_members(bool durable, bool allowContinuity, string? controlsProfile)
    {
        using var files = new JournalFiles();
        var factory = new FixtureFactory("{\"Payload\":" + RawAgentPayload + "}");
        await using var provider = await ServicesAsync(factory, durable ? files.ConnectionString : null);
        var executable = await ExecutableAsync(provider, outputName: "Payload", outputType: AgentInvocationControls.JsonType,
            allowContinuity: allowContinuity, invocationControls: controlsProfile is not null, controlsProfile: controlsProfile);
        var (actor, start) = await StartAsync(provider, executable);
        var claim = await provider.GetRequiredService<IBackgroundActivityWorkStore>()
            .ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        Assert.NotNull(claim);

        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim);
        await DrainAsync(actor);

        Assert.Equal(WorkflowExecutionStatus.Completed, (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run");
        var boundary = states.Single(state => state.Execution.ExecutableNodeId == executable.RootActivity.ExecutableNodeId);
        var completion = boundary.Completion!.Result.InlineValue!.Value;
        var (turn, request) = await NativeTurnAsync(provider, executable);
        Assert.Equal(AgentTurnStatus.Accepted, turn.Status);
        Assert.NotNull(turn.ProtectedResult);
        await using var freshScope = provider.CreateAsyncScope();
        var accepted = await freshScope.ServiceProvider.GetRequiredService<AgentTurnJournal>()
            .ResultAsync(turn.Handle, request.Identity, turn.ChildInvocationId!, CancellationToken.None);
        var originalAccepted = accepted.Result.GetRawText();
        Assert.Equal(RawAgentPayload, accepted.Result.GetProperty("Payload").GetRawText());
        Assert.Equal(RawAgentPayload, completion.GetProperty("Payload").GetRawText());
        var descriptor = AgentNativeBinding.Descriptor(executable.RootActivity);
        Assert.Equal(completion.GetRawText(), AgentNativeControls.Complete(descriptor, accepted).GetRawText());
        Assert.Equal(originalAccepted, accepted.Result.GetRawText());
        Assert.False(accepted.Result.TryGetProperty(AgentInvocationControls.OutputName, out _));
        Assert.Equal(allowContinuity, accepted.Session is not null);
        if (controlsProfile is null)
        {
            Assert.Equal(originalAccepted, completion.GetRawText());
            Assert.False(completion.TryGetProperty(AgentInvocationControls.OutputName, out _));
        }
        else
        {
            var conversation = completion.GetProperty(AgentInvocationControls.OutputName);
            Assert.Equal(accepted.Session!.ConversationId, conversation.GetProperty("conversationId").GetString());
            Assert.Equal(accepted.Session.Revision, conversation.GetProperty("revision").GetInt64());
            Assert.Equal(new[] { "conversationId", "revision" }, conversation.EnumerateObject().Select(property => property.Name));
        }
        await actor.EnqueueAsync(start);
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim);
        Assert.Equal(1, factory.Calls);
        var replay = await freshScope.ServiceProvider.GetRequiredService<AgentTurnJournal>()
            .ResultAsync(turn.Handle, request.Identity, turn.ChildInvocationId!, CancellationToken.None);
        Assert.Equal(originalAccepted, replay.Result.GetRawText());
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"root\"")]
    public void Enabled_completion_requires_an_object_result(string json) =>
        Assert.Throws<InvalidOperationException>(() => AgentNativeControls.Complete(ControlsDescriptor(), Accepted(json)));

    [Fact]
    public void Enabled_completion_rejects_duplicate_result_keys_without_choosing_a_value() =>
        Assert.Throws<ArgumentException>(() => AgentNativeControls.Complete(ControlsDescriptor(), Accepted("{\"Payload\":1,\"Payload\":2}")));

    [Fact]
    public void Enabled_completion_refuses_the_reserved_conversation_output()
    {
        var error = Assert.Throws<AgentTurnException>(() => AgentNativeControls.Complete(ControlsDescriptor(), Accepted("{\"Conversation\":null}")));
        Assert.Equal("agent.controls.invalid", error.Code);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[ 1.0, null, \"\\u0061\" ]")]
    public void Disabled_completion_passes_through_the_entire_result_without_requiring_a_session(string json)
    {
        var descriptor = ControlsDescriptor() with { InvocationControlsProfile = null, AllowContinuity = false };
        var accepted = Accepted(json) with { Session = null };

        Assert.Equal(json, AgentNativeControls.Complete(descriptor, accepted).GetRawText());
    }

    private static AgentRuntimeDescriptor ControlsDescriptor() => AgentJournalTests.Request().Descriptor with
    {
        AllowContinuity = true,
        InvocationControlsProfile = AgentInvocationControls.Profile
    };

    private static AgentTurnResult Accepted(string json) => new(Json(json), null, null, null, 1, 1,
        new AgentSessionReference(new string('a', 64), 1, "unused-direct-control-session"));
}
