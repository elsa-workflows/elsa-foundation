using System.Text.Json;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Core.Services;
using Elsa.Weaver.Services;

namespace Elsa.Weaver.Tests;

public sealed class WeaverTurnContractsTests
{
    [Fact]
    public void Turn_context_for_message_exposes_latest_user_message()
    {
        var context = WeaverTurnContext.ForMessage("session-1", "Hello", []);

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
        var context = new WeaverTurnContext(
            "session-1",
            "provider-1",
            [
                new(WeaverRole.User, "first"),
                new(WeaverRole.Assistant, "answer"),
                new(WeaverRole.User, "second"),
                new(WeaverRole.Tool, "tool-output", "call-1", "read")
            ],
            [],
            [],
            [],
            0,
            8);

        Assert.Equal("second", context.LatestUserMessage?.Content);
    }

    [Theory]
    [InlineData(WeaverStreamEventKind.StepStarted)]
    [InlineData(WeaverStreamEventKind.ToolCallRequested)]
    [InlineData(WeaverStreamEventKind.ToolCallCompleted)]
    [InlineData(WeaverStreamEventKind.PlanUpdated)]
    [InlineData(WeaverStreamEventKind.TurnCancelled)]
    public void New_stream_event_kinds_round_trip_through_default_serialization(WeaverStreamEventKind kind)
    {
        // StreamSession serializes events with the default JsonSerializer; new enum members must survive that.
        var streamEvent = new WeaverStreamEvent(
            "evt-1",
            kind,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            null,
            new WeaverToolCallRequest("call-1", "read-workflow", "{}", WeaverRisk.ReadOnly, false));

        var json = JsonSerializer.Serialize(streamEvent);
        var roundTripped = JsonSerializer.Deserialize<WeaverStreamEvent>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(kind, roundTripped!.Kind);
        Assert.Equal("evt-1", roundTripped.Id);
    }

    [Fact]
    public async Task Tool_registry_exposes_descriptors_and_finds_by_name_case_insensitively()
    {
        var registry = new DefaultWeaverToolRegistry(new WeaverTestToolsPublisher(new StubTool("read-workflow"), new StubTool("add-activity")));

        Assert.Equal(2, (await registry.ListAsync()).Count);
        Assert.NotNull(await registry.FindAsync("READ-WORKFLOW"));
        Assert.Null(await registry.FindAsync("missing"));
        Assert.Null(await registry.FindAsync(""));
    }

    [Fact]
    public async Task Tool_registry_rejects_tools_sharing_a_name()
    {
        // Issue #414 item 6: duplicate names now fail fast (previously the first registration silently won).
        var ex = await Assert.ThrowsAsync<WeaverContributionException>(() =>
            new DefaultWeaverToolRegistry(new WeaverTestToolsPublisher(new StubTool("read-workflow"), new StubTool("read-workflow"))).ListAsync());

        Assert.Contains("read-workflow", ex.Message);
        Assert.Contains("Duplicate Weaver tool name", ex.Message);
    }

    [Fact]
    public async Task Tool_registry_rejects_names_harnesses_cannot_accept()
    {
        // Dotted names are rejected by GitHub Copilot + Anthropic; fail fast at registration.
        var ex = await Assert.ThrowsAsync<WeaverContributionException>(() => new DefaultWeaverToolRegistry(new WeaverTestToolsPublisher(new StubTool("workflow.read_graph"))).ListAsync());

        Assert.Contains("workflow.read_graph", ex.Message);
        Assert.Contains("^[a-zA-Z0-9_-]+$", ex.Message);
    }

    [Fact]
    public void Cancellation_and_unregister_are_bound_to_the_owning_session()
    {
        var registry = new WeaverTurnRegistry();
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
        var registry = new WeaverTurnRegistry();
        registry.Register("session-a", "turn-a");
        Assert.Throws<WeaverContributionException>(() => registry.Register("session-a", "another-turn"));
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

    private sealed class StubTool(string name) : IWeaverTool
    {
        public WeaverToolDescriptor Descriptor { get; } = new(
            name,
            name,
            "Stub tool.",
            WeaverToolMutability.ReadOnly,
            WeaverRisk.ReadOnly,
            "{}",
            [],
            []) { ModuleId = "test.module" };

        public Task<WeaverResult<WeaverToolPreparation>> PrepareAsync(WeaverToolInvocation invocation, CancellationToken cancellationToken = default)
            => Task.FromResult(WeaverResult<WeaverToolPreparation>.Success(WeaverTestToolHelpers.Preparation(invocation, Descriptor)));

        public Task<WeaverResult<WeaverToolResult>> ExecuteAsync(WeaverPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
            => Task.FromResult(WeaverResult<WeaverToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true, "ok")));
    }
}
