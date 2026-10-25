using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Http.Connections;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Integrations.Connectors.Microsoft365.Models;

namespace Elsa.Integrations.Connectors.Microsoft365.Activities;

[Activity(Icon = "messages-square")]
[Version("1.0.0")]
[RefusesSecretBinding(nameof(Content), SecretBindingRefusalReason.EchoedToOutput)]
[RefusesSecretBinding(nameof(Attachments), SecretBindingRefusalReason.EchoedToOutput)]
public sealed class PostTeamsChannelMessage(
    IHttpClientFactory httpClientFactory,
    IHttpConnectionApplicator connectionApplicator)
    : Microsoft365Activity<GraphChatMessage>(httpClientFactory, connectionApplicator)
{
    [ActivityInput(Key = nameof(TeamId), DisplayName = "Team id", Tier = InputTier.Essential, Description = "Requires ChannelMessage.Send.")]
    public string TeamId { get; set; } = "";

    [ActivityInput(Key = nameof(ChannelId), DisplayName = "Channel id", Tier = InputTier.Essential)]
    public string ChannelId { get; set; } = "";

    [ActivityInput(Key = nameof(Content), DisplayName = "Content", Tier = InputTier.Essential)]
    public string Content { get; set; } = "";

    [ActivityInput(Key = nameof(ContentType), DisplayName = "Content type", Tier = InputTier.Optional, Description = "text or html. Default text.")]
    public string? ContentType { get; set; }

    [ActivityInput(Key = nameof(Attachments), DisplayName = "Attachments", Tier = InputTier.Optional, Description = "Optional JSON array of Teams message attachments.")]
    public string? Attachments { get; set; }

    protected override async ValueTask<ActivityTransition<GraphChatMessage>> ExecuteAsync(ActivityExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(TeamId) || string.IsNullOrWhiteSpace(ChannelId) || string.IsNullOrWhiteSpace(Content))
        {
            return ActivityTransition.Fault<GraphChatMessage>(
                new ActivityFault(Microsoft365ErrorCodes.InvalidInput, "Post Teams Channel Message requires Team id, Channel id and Content."));
        }

        try
        {
            var body = new JsonObject
            {
                ["body"] = new JsonObject
                {
                    ["contentType"] = string.IsNullOrWhiteSpace(ContentType) ? "text" : ContentType.Trim(),
                    ["content"] = Content
                }
            };
            if (!string.IsNullOrWhiteSpace(Attachments))
                body["attachments"] = ParseAttachments(Attachments);

            var root = await CreateClient()
                .PostJsonAsync(
                    $"teams/{Uri.EscapeDataString(TeamId.Trim())}/channels/{Uri.EscapeDataString(ChannelId.Trim())}/messages",
                    body,
                    Connection,
                    context,
                    context.CancellationToken,
                    Microsoft365Scopes.ChannelMessageSend)
                .ConfigureAwait(false);
            return ActivityTransition.Complete(GraphPayloads.ReadChatMessage(root));
        }
        catch (Microsoft365ApiException exception)
        {
            return GraphFault(exception);
        }
    }

    private static JsonArray ParseAttachments(string attachments)
    {
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(attachments);
        }
        catch (JsonException)
        {
            throw new Microsoft365ApiException(
                Microsoft365ErrorCodes.InvalidInput,
                "Attachments must be a JSON array.");
        }

        if (parsed is not JsonArray array)
        {
            throw new Microsoft365ApiException(
                Microsoft365ErrorCodes.InvalidInput,
                "Attachments must be a JSON array.");
        }

        return array;
    }
}
