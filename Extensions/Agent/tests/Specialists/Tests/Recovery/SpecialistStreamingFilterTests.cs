using Elsa.Agent.Specialists.Services;
using Microsoft.Extensions.AI;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Recovery;

public sealed class SpecialistStreamingFilterTests
{
    [Fact]
    public void Only_copied_visible_text_and_accounting_reach_MAF()
    {
        var filter = new SpecialistStreamingResponseFilter();
        var usage = new UsageDetails { InputTokenCount = 11, OutputTokenCount = 7, TotalTokenCount = 18, AdditionalCounts = new() { ["private"] = 99 } };
        var input = new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("private-reasoning"), new TextContent("{}") { RawRepresentation = "private-raw", AdditionalProperties = new() { ["private"] = "value" } }, new UsageContent(usage)])
        { RawRepresentation = "private-sdk", AdditionalProperties = new() { ["private"] = "value" }, MessageId = "private-provider-message", ResponseId = "private-provider-response", FinishReason = ChatFinishReason.Stop };
        var visible = filter.Filter(input);
        filter.Complete();
        Assert.Equal("{}", visible.Text);
        Assert.Null(visible.RawRepresentation);
        Assert.Null(visible.AdditionalProperties);
        Assert.Equal("specialist-result", visible.MessageId);
        Assert.Equal("specialist-response", visible.ResponseId);
        Assert.DoesNotContain(visible.Contents, c => c is TextReasoningContent);
        var text = Assert.IsType<TextContent>(visible.Contents[0]);
        Assert.Null(text.RawRepresentation);
        Assert.Null(text.AdditionalProperties);
        var copied = Assert.IsType<UsageContent>(visible.Contents[1]).Details;
        Assert.NotSame(usage, copied);
        Assert.Null(copied.AdditionalCounts);
        Assert.Equal(18, copied.TotalTokenCount);
    }

    [Theory]
    [InlineData("updates", "specialist.result.bounds")]
    [InlineData("bytes", "specialist.result.bounds")]
    [InlineData("late-text", "specialist.result.content")]
    [InlineData("role", "specialist.result.content")]
    [InlineData("tool", "specialist.result.content")]
    [InlineData("conversation", "specialist.session.provider")]
    [InlineData("continuation", "specialist.session.provider")]
    [InlineData("model-drift", "specialist.result.metadata")]
    [InlineData("model-bounds", "specialist.result.metadata")]
    [InlineData("usage-duplicate", "specialist.result.metadata")]
    [InlineData("usage-negative", "specialist.result.metadata")]
    public void Invalid_stream_evidence_is_refused_before_session_acceptance(string mode, string expected)
    {
        var filter = new SpecialistStreamingResponseFilter();
        var error = Assert.Throws<SpecialistTurnException>(() =>
        {
            switch (mode)
            {
                case "updates": for (var i = 0; i <= 16_384; i++) filter.Filter(new()); break;
                case "bytes": filter.Filter(new(ChatRole.Assistant, new string('x', 1_048_576))); filter.Filter(new(ChatRole.Assistant, "x")); break;
                case "late-text": filter.Filter(new() { FinishReason = ChatFinishReason.Stop }); filter.Filter(new(ChatRole.Assistant, "{}")); break;
                case "role": filter.Filter(new(ChatRole.User, "{}")); break;
                case "tool": filter.Filter(new(ChatRole.Assistant, [new FunctionCallContent("id", "tool")])); break;
                case "conversation": filter.Filter(new() { ConversationId = "provider-owned" }); break;
                case "continuation": filter.Filter(new() { ContinuationToken = ResponseContinuationToken.FromBytes(new byte[] { 1 }) }); break;
                case "model-drift": filter.Filter(new() { ModelId = "first" }); filter.Filter(new() { ModelId = "second" }); break;
                case "model-bounds": filter.Filter(new() { ModelId = new string('x', 513) }); break;
                case "usage-duplicate": filter.Filter(new(ChatRole.Assistant, [new UsageContent(new())])); filter.Filter(new(ChatRole.Assistant, [new UsageContent(new())])); break;
                case "usage-negative": filter.Filter(new(ChatRole.Assistant, [new UsageContent(new() { OutputTokenCount = -1 })])); break;
            }
        });
        Assert.Equal(expected, error.Code);
    }
}
