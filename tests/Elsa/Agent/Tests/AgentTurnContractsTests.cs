using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Services;

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
    public void Tool_registry_exposes_descriptors_and_finds_by_name_case_insensitively()
    {
        var registry = new DefaultAgentToolRegistry([new StubTool("read-workflow"), new StubTool("add-activity")]);

        Assert.Equal(2, registry.Descriptors.Count);
        Assert.NotNull(registry.Find("READ-WORKFLOW"));
        Assert.Null(registry.Find("missing"));
        Assert.Null(registry.Find(""));
    }

    [Fact]
    public void Tool_registry_deduplicates_tools_sharing_a_name()
    {
        var registry = new DefaultAgentToolRegistry([new StubTool("read-workflow"), new StubTool("read-workflow")]);

        Assert.Single(registry.Descriptors);
    }

    [Fact]
    public void Tool_registry_rejects_names_harnesses_cannot_accept()
    {
        // Dotted names are rejected by GitHub Copilot + Anthropic; fail fast at registration.
        var ex = Assert.Throws<InvalidOperationException>(() => new DefaultAgentToolRegistry([new StubTool("workflow.read_graph")]));

        Assert.Contains("workflow.read_graph", ex.Message);
        Assert.Contains("^[a-zA-Z0-9_-]+$", ex.Message);
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
            []);

        public Task<AgentResult<AgentToolResult>> InvokeAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
            => Task.FromResult(AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true, "ok")));
    }
}
