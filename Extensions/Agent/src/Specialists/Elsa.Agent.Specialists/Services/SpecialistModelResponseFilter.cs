using Microsoft.Extensions.AI;
using static Elsa.Agent.Specialists.Services.SpecialistModelRunner;

namespace Elsa.Agent.Specialists.Services;

internal static class SpecialistModelResponseFilter
{
    internal static ChatResponse VisibleResult(ChatResponse response)
    {
        if (response.FinishReason == ChatFinishReason.ContentFilter || response.Messages.SelectMany(x => x.Contents).Any(x => x is ErrorContent))
            throw Failure("specialist.result.refused", "The model refused to produce the specialist result.");
        if (response.FinishReason == ChatFinishReason.Length)
            throw Failure("specialist.result.truncated", "The model result was truncated before completion.");
        if (response.FinishReason is { } reason && reason != ChatFinishReason.Stop ||
            response.Messages.Any(x => x.Role != ChatRole.Assistant || x.Contents.Any(content => content is not (TextContent or TextReasoningContent))))
            throw Failure("specialist.result.content", "The model response is outside the specialist's visible JSON result profile.");
        if (!string.IsNullOrEmpty(response.ConversationId))
            throw Failure("specialist.session.provider", "Provider-managed conversation state is outside the specialist session profile.");

        // Never hand SDK raw representations, additional properties or private reasoning to MAF's
        // history provider. Only a validated business result can later become an accepted session turn.
        var messages = response.Messages.Select(message => new ChatMessage(ChatRole.Assistant,
            message.Contents.OfType<TextContent>().Select(content => (AIContent)new TextContent(content.Text)).ToList())).ToList();
        return new ChatResponse(messages)
        {
            FinishReason = response.FinishReason,
            ModelId = response.ModelId,
            Usage = response.Usage is { } usage ? new UsageDetails
            {
                InputTokenCount = usage.InputTokenCount,
                OutputTokenCount = usage.OutputTokenCount,
                TotalTokenCount = usage.TotalTokenCount
            } : null
        };
    }
}
