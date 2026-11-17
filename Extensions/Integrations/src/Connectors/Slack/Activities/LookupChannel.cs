using System.Text.Json.Nodes;
using Elsa.Activities.Http.Connections;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Integrations.Connectors.Slack.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Integrations.Connectors.Slack.Activities;

[Activity(Icon = "search")]
[Version("1.0.0")]
public sealed class LookupChannel(
    IHttpClientFactory httpClientFactory,
    IHttpConnectionApplicator connectionApplicator,
    ILoggerFactory loggerFactory)
    : SlackActivity<SlackChannel>(httpClientFactory, connectionApplicator, loggerFactory)
{
    [ActivityInput(Key = nameof(ChannelId), DisplayName = "Channel id", Tier = InputTier.Essential)]
    public string ChannelId { get; set; } = "";

    protected override async ValueTask<ActivityTransition<SlackChannel>> ExecuteAsync(ActivityExecutionContext context)
    {
        try
        {
            var body = new JsonObject { ["channel"] = ChannelId };
            var root = await CreateClient()
                .PostFormAsync(SlackApi.ConversationInfo, body, Connection, context, context.CancellationToken)
                .ConfigureAwait(false);
            var channel = root.TryGetProperty("channel", out var element)
                ? SlackJson.Deserialize<SlackChannel>(element) ?? new SlackChannel()
                : new SlackChannel();
            return ActivityTransition.Complete(channel);
        }
        catch (SlackApiException exception)
        {
            return SlackFault(exception);
        }
    }
}
