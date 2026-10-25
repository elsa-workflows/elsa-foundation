using Elsa.Activities.Http.Connections;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Integrations.Connectors.Slack.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Integrations.Connectors.Slack.Activities;

[Activity(Icon = "pencil")]
[Version("1.0.0")]
[RefusesSecretBinding(nameof(Text), SecretBindingRefusalReason.EchoedToOutput)]
[RefusesSecretBinding(nameof(Blocks), SecretBindingRefusalReason.EchoedToOutput)]
public sealed class UpdateMessage(
    IHttpClientFactory httpClientFactory,
    IHttpConnectionApplicator connectionApplicator,
    ILoggerFactory loggerFactory)
    : SlackActivity<SlackMessageResult>(httpClientFactory, connectionApplicator, loggerFactory)
{
    [ActivityInput(Tier = InputTier.Essential)]
    public string Channel { get; set; } = "";

    [ActivityInput(Key = nameof(Ts), DisplayName = "Message ts", Tier = InputTier.Essential, Description = "Timestamp of the message to update.")]
    public string Ts { get; set; } = "";

    [ActivityInput(Tier = InputTier.Essential)]
    public string Text { get; set; } = "";

    [ActivityInput(Tier = InputTier.Optional, Description = "Slack Block Kit JSON array.")]
    public string? Blocks { get; set; }

    protected override async ValueTask<ActivityTransition<SlackMessageResult>> ExecuteAsync(ActivityExecutionContext context)
    {
        try
        {
            var body = SlackMessageBody.Create(Channel, Text, Blocks, threadTs: null, replyBroadcast: null, unfurlLinks: null, unfurlMedia: null);
            body["ts"] = Ts;
            var root = await CreateClient()
                .PostJsonAsync(SlackApi.UpdateMessage, body, Connection, context, context.CancellationToken)
                .ConfigureAwait(false);
            return ActivityTransition.Complete(SlackMessageBody.ReadResult(root));
        }
        catch (SlackApiException exception)
        {
            return SlackFault(exception);
        }
    }
}
