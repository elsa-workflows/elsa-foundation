using Elsa.Activities.Http.Connections;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Integrations.Connectors.Slack.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Integrations.Connectors.Slack.Activities;

[Activity(Icon = "messages-square")]
[Version("1.0.0")]
[RefusesSecretBinding(nameof(Text), SecretBindingRefusalReason.EchoedToOutput)]
[RefusesSecretBinding(nameof(Blocks), SecretBindingRefusalReason.EchoedToOutput)]
public sealed class ReplyInThread(
    IHttpClientFactory httpClientFactory,
    IHttpConnectionApplicator connectionApplicator,
    ILoggerFactory loggerFactory)
    : SlackActivity<SlackMessageResult>(httpClientFactory, connectionApplicator, loggerFactory)
{
    [ActivityInput(Tier = InputTier.Essential)]
    public string Channel { get; set; } = "";

    [ActivityInput(Key = nameof(ThreadTs), DisplayName = "Thread ts", Tier = InputTier.Essential)]
    public string ThreadTs { get; set; } = "";

    [ActivityInput(Tier = InputTier.Essential)]
    public string Text { get; set; } = "";

    [ActivityInput(Tier = InputTier.Optional, Description = "Slack Block Kit JSON array.")]
    public string? Blocks { get; set; }

    [ActivityInput(Tier = InputTier.Optional)]
    public bool? ReplyBroadcast { get; set; }

    protected override async ValueTask<ActivityTransition<SlackMessageResult>> ExecuteAsync(ActivityExecutionContext context)
    {
        try
        {
            var body = SlackMessageBody.Create(Channel, Text, Blocks, ThreadTs, ReplyBroadcast, unfurlLinks: null, unfurlMedia: null);
            var root = await CreateClient()
                .PostJsonAsync(SlackApi.PostMessage, body, Connection, context, context.CancellationToken)
                .ConfigureAwait(false);
            return ActivityTransition.Complete(SlackMessageBody.ReadResult(root));
        }
        catch (SlackApiException exception)
        {
            return SlackFault(exception);
        }
    }
}
