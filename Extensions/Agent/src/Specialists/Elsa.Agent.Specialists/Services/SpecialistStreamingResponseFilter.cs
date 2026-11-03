using System.Text;
using Elsa.Agent.Specialists.JsonSchema;
using Microsoft.Extensions.AI;
using static Elsa.Agent.Specialists.Services.SpecialistModelRunner;

namespace Elsa.Agent.Specialists.Services;

// Per-response state only. Provider content is never retained; MAF receives bounded visible text
// and copied final accounting. No partial result or history candidate is accepted by this filter.
internal sealed class SpecialistStreamingResponseFilter
{
    private int updates, bytes;
    private bool stopped, usageSeen;
    public UsageDetails? Usage { get; private set; }
    public string? ModelId { get; private set; }

    public ChatResponseUpdate Filter(ChatResponseUpdate update)
    {
        if (++updates > 16_384)
            throw Interrupted("specialist.result.bounds", "The model stream exceeds the bounded update profile.");
        if (!string.IsNullOrEmpty(update.ConversationId) || update.ContinuationToken is not null)
            throw Interrupted("specialist.session.provider", "Provider-managed conversation state is outside the specialist session profile.");
        if (update.FinishReason == ChatFinishReason.ContentFilter || update.Contents.Any(c => c is ErrorContent))
            throw Failure("specialist.result.refused", "The model refused to produce the specialist result.");
        if (update.FinishReason == ChatFinishReason.Length)
            throw Failure("specialist.result.truncated", "The model result was truncated before completion.");
        if (update.FinishReason?.Value == "specialist.incomplete")
            throw Interrupted("specialist.result.incomplete", "The model stream has no complete supported terminal response.");
        if (update.FinishReason is { } reason && reason != ChatFinishReason.Stop || update.Role is { } role && role != ChatRole.Assistant ||
            update.Contents.Any(c => c is not (TextContent or TextReasoningContent or UsageContent)))
            throw Interrupted("specialist.result.content", "The model stream is outside the specialist's visible JSON result profile.");
        if (update.ModelId is { } model)
        {
            if (string.IsNullOrWhiteSpace(model) || model.Length > 512 || model.Any(char.IsControl) || ModelId is not null && ModelId != model)
                throw Interrupted("specialist.result.metadata", "The model stream returned inconsistent model metadata.");
            ModelId = model;
        }
        var contents = new List<AIContent>();
        foreach (var content in update.Contents)
        {
            if (content is TextContent text)
            {
                if (stopped && text.Text.Length != 0)
                    throw Interrupted("specialist.result.content", "Visible model output continued after the terminal update.");
                var size = Encoding.UTF8.GetByteCount(text.Text);
                if (size > SpecialistSchemaValidator.MaximumValueBytes - bytes)
                    throw Interrupted("specialist.result.bounds", "The model result exceeds the bounded JSON profile.");
                bytes += size;
                if (text.Text.Length != 0) contents.Add(new TextContent(text.Text));
            }
            else if (content is UsageContent usage)
            {
                if (usageSeen || usage.Details.InputTokenCount is < 0 || usage.Details.OutputTokenCount is < 0 || usage.Details.TotalTokenCount is < 0)
                    throw Interrupted("specialist.result.metadata", "The model stream returned invalid or duplicate usage metadata.");
                usageSeen = true;
                Usage = new UsageDetails { InputTokenCount = usage.Details.InputTokenCount, OutputTokenCount = usage.Details.OutputTokenCount, TotalTokenCount = usage.Details.TotalTokenCount };
                contents.Add(new UsageContent(Usage));
            }
        }
        stopped |= update.FinishReason == ChatFinishReason.Stop;
        return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant, Contents = contents, FinishReason = update.FinishReason, ModelId = ModelId,
            MessageId = "specialist-result", ResponseId = "specialist-response"
        };
    }

    private static SpecialistTurnException Interrupted(string code, string message) => new(code, message) { OutcomeUnknown = true };

    public void Complete()
    {
        if (!stopped) throw Interrupted("specialist.result.incomplete", "The model stream ended without a successful terminal update.");
    }
}
