using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Services;
using Elsa.Agent.Services;

namespace Elsa.Agent.Tests;

public sealed class AgentTurnContractsTests
{
    [Fact]
    public void Turn_context_for_message_exposes_latest_user_message()
    {
        var context = AgentTurnContext.ForMessage("session-1", "Hello", []);

        Assert.Equal("session-1", context.SessionId);
        Assert.Equal(0, context.StepIndex);
        Assert.Equal(1, context.MaxSteps);
        Assert.Equal("Hello", context.LatestUserMessage?.Content);
        Assert.Empty(context.PendingToolResults);
        Assert.Empty(context.AvailableTools);
    }

    [Fact]
    public void Latest_user_message_skips_assistant_and_tool_turns()
    {
        var context = new AgentTurnContext(
            "session-1",
            "provider-1",
            [
                new(AgentRole.User, "first"),
                new(AgentRole.Assistant, "answer"),
                new(AgentRole.User, "second"),
                new(AgentRole.Tool, "tool-output", "call-1", "read")
            ],
            [],
            [],
            [],
            0,
            8);

        Assert.Equal("second", context.LatestUserMessage?.Content);
    }

    [Theory]
    [InlineData(AgentStreamEventKind.StepStarted)]
    [InlineData(AgentStreamEventKind.ToolCallRequested)]
    [InlineData(AgentStreamEventKind.ToolCallCompleted)]
    [InlineData(AgentStreamEventKind.PlanUpdated)]
    [InlineData(AgentStreamEventKind.TurnCancelled)]
    public void New_stream_event_kinds_round_trip_through_default_serialization(AgentStreamEventKind kind)
    {
        // StreamSession serializes events with the default JsonSerializer; new enum members must survive that.
        var streamEvent = new AgentStreamEvent(
            "evt-1",
            kind,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            null,
            new AgentToolCallRequest("call-1", "read-workflow", "{}", AgentRisk.ReadOnly, false));

        var json = JsonSerializer.Serialize(streamEvent);
        var roundTripped = JsonSerializer.Deserialize<AgentStreamEvent>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(kind, roundTripped!.Kind);
        Assert.Equal("evt-1", roundTripped.Id);
    }

    [Fact]
    public async Task Tool_registry_exposes_descriptors_and_finds_by_name_case_insensitively()
    {
        var registry = new DefaultAgentToolRegistry(new AgentTestToolsPublisher(new StubTool("read-workflow"), new StubTool("add-activity")));

        Assert.Equal(2, (await registry.ListAsync()).Count);
        Assert.NotNull(await registry.FindAsync("READ-WORKFLOW"));
        Assert.Null(await registry.FindAsync("missing"));
        Assert.Null(await registry.FindAsync(""));
    }

    [Fact]
    public async Task Tool_registry_rejects_tools_sharing_a_name()
    {
        // Issue #414 item 6: duplicate names now fail fast (previously the first registration silently won).
        var ex = await Assert.ThrowsAsync<AgentContributionException>(() =>
            new DefaultAgentToolRegistry(new AgentTestToolsPublisher(new StubTool("read-workflow"), new StubTool("read-workflow"))).ListAsync());

        Assert.Contains("read-workflow", ex.Message);
        Assert.Contains("Duplicate agent tool name", ex.Message);
    }

    [Fact]
    public async Task Tool_registry_rejects_names_harnesses_cannot_accept()
    {
        // Dotted names are rejected by GitHub Copilot + Anthropic; fail fast at registration.
        var ex = await Assert.ThrowsAsync<AgentContributionException>(() => new DefaultAgentToolRegistry(new AgentTestToolsPublisher(new StubTool("workflow.read_graph"))).ListAsync());

        Assert.Contains("workflow.read_graph", ex.Message);
        Assert.Contains("^[a-zA-Z0-9_-]+$", ex.Message);
    }

    [Fact]
    public void Cancellation_and_unregister_are_bound_to_the_owning_session()
    {
        var registry = new AgentTurnRegistry();
        var token = registry.Register("session-a", "turn-a");
        try
        {
            Assert.True(registry.IsActive("session-a"));
            Assert.False(registry.Cancel("session-b", "turn-a"));
            registry.Unregister("session-b", "turn-a");
            Assert.False(token.IsCancellationRequested);
            Assert.True(registry.IsActive("session-a"));
            Assert.True(registry.Cancel("session-a", "turn-a"));
            Assert.True(token.IsCancellationRequested);
        }
        finally { registry.Unregister("session-a", "turn-a"); }
        Assert.False(registry.IsActive("session-a"));
    }

    [Fact]
    public void A_session_has_one_foreground_turn_and_a_stale_turn_cannot_cancel_its_successor()
    {
        var registry = new AgentTurnRegistry();
        registry.Register("session-a", "turn-a");
        Assert.Throws<AgentContributionException>(() => registry.Register("session-a", "another-turn"));
        registry.Unregister("session-a", "turn-a");
        var successor = registry.Register("session-a", "turn-b");
        try
        {
            Assert.False(registry.Cancel("session-a", "turn-a"));
            Assert.False(successor.IsCancellationRequested);
            Assert.True(registry.IsActive("session-a"));
        }
        finally { registry.Unregister("session-a", "turn-b"); }
    }

    private sealed class StubTool(string name) : IAgentTool
    {
        public AgentToolDescriptor Descriptor { get; } = new(
            name,
            name,
            "Stub tool.",
            AgentToolMutability.ReadOnly,
            AgentRisk.ReadOnly,
            "{}",
            [],
            []) { ModuleId = "test.module" };

        public Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
            => Task.FromResult(AgentResult<AgentToolPreparation>.Success(AgentTestToolHelpers.Preparation(invocation, Descriptor)));

        public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
            => Task.FromResult(AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true, "ok")));
    }
}
