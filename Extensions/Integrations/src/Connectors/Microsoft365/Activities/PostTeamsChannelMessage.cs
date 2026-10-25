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

    [ActivityInput(Key = nameof(ContentType), DisplayName = "Content type", Tier = InputTier.Optional, Description = "text or html. Default text. Attachments require html.")]
    public string? ContentType { get; set; }

    [ActivityInput(Key = nameof(Attachments), DisplayName = "Attachments", Tier = InputTier.Optional, Description = "Optional JSON array of Teams attachments. Requires Content type html and a matching <attachment id=\"...\"></attachment> tag in Content for each id.")]
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
            var contentType = GraphPayloads.TeamsContentType(ContentType);
            var body = new JsonObject
            {
                ["body"] = new JsonObject
                {
                    ["contentType"] = contentType,
                    ["content"] = Content
                }
            };
            if (!string.IsNullOrWhiteSpace(Attachments))
                body["attachments"] = GraphPayloads.TeamsAttachments(Attachments, Content, contentType);

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
}
