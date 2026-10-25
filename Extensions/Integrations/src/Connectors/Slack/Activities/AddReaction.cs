using System.Text.Json.Nodes;
using Elsa.Activities.Http.Connections;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Integrations.Connectors.Slack.Activities;

[Activity(Icon = "smile")]
[Version("1.0.0")]
public sealed class AddReaction(
    IHttpClientFactory httpClientFactory,
    IHttpConnectionApplicator connectionApplicator,
    ILoggerFactory loggerFactory)
    : SlackActivity(httpClientFactory, connectionApplicator, loggerFactory)
{
    [ActivityInput(Tier = InputTier.Essential)]
    public string Channel { get; set; } = "";

    [ActivityInput(Tier = InputTier.Essential)]
    public string Ts { get; set; } = "";

    [ActivityInput(Key = nameof(EmojiName), DisplayName = "Emoji name", Tier = InputTier.Essential, Description = "Emoji name without colons. Requires reactions:write.")]
    public string EmojiName { get; set; } = "";

    protected override async ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        try
        {
            var body = new JsonObject
            {
                ["channel"] = Channel,
                ["timestamp"] = Ts,
                ["name"] = EmojiName.Trim().Trim(':')
            };
            await CreateClient()
                .PostJsonAsync(SlackApi.AddReaction, body, Connection, context, context.CancellationToken)
                .ConfigureAwait(false);
            return ActivityTransition.Complete(ActivityUnit.Value);
        }
        catch (SlackApiException exception)
        {
            return SlackFault(exception);
        }
    }
}
