using Elsa.Agent.Specialists.Services;
using Microsoft.Extensions.AI;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Providers;

public sealed class SpecialistResponseFilterTests
{
    [Fact]
    public void Visible_history_is_rebuilt_without_reasoning_or_provider_objects_at_every_level()
    {
        var hidden = new object();
        var text = new TextContent("{\"headline\":\"Fixture\"}") { RawRepresentation = hidden, AdditionalProperties = new() { ["private"] = hidden } };
        var message = new ChatMessage(ChatRole.Assistant, [new TextReasoningContent("private reasoning"), text])
        {
            RawRepresentation = hidden, AdditionalProperties = new() { ["private"] = hidden }
        };
        var response = new ChatResponse(message)
        {
            FinishReason = ChatFinishReason.Stop, RawRepresentation = hidden, AdditionalProperties = new() { ["private"] = hidden },
            ResponseId = "private-response", ModelId = "observed-model", Usage = new() { InputTokenCount = 11, OutputTokenCount = 7, TotalTokenCount = 18 }
        };
        var visible = SpecialistModelResponseFilter.VisibleResult(response);
        var visibleMessage = Assert.Single(visible.Messages);
        var visibleText = Assert.IsType<TextContent>(Assert.Single(visibleMessage.Contents));
        Assert.Equal(text.Text, visibleText.Text);
        Assert.NotSame(text, visibleText);
        Assert.Null(visibleText.RawRepresentation);
        Assert.Null(visibleText.AdditionalProperties);
        Assert.Null(visibleMessage.RawRepresentation);
        Assert.Null(visibleMessage.AdditionalProperties);
        Assert.Null(visible.RawRepresentation);
        Assert.Null(visible.AdditionalProperties);
        Assert.Null(visible.ResponseId);
        Assert.Null(visible.ConversationId);
        Assert.NotSame(response.Usage, visible.Usage);
        Assert.Equal(18, visible.Usage!.TotalTokenCount);
        Assert.Equal("observed-model", visible.ModelId);
    }

    [Theory]
    [InlineData("server-conversation", "stop", "specialist.session.provider")]
    [InlineData(null, "tool_calls", "specialist.result.content")]
    [InlineData(null, "unknown", "specialist.result.content")]
    public void Unsupported_server_state_and_finish_profiles_are_explicit_refusals(string? conversation, string reason, string code)
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "{}"))
        {
            ConversationId = conversation, FinishReason = new ChatFinishReason(reason)
        };
        Assert.Equal(code, Assert.Throws<SpecialistTurnException>(() => SpecialistModelResponseFilter.VisibleResult(response)).Code);
    }
}
