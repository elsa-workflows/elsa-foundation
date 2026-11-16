using System.Text.Json.Nodes;
using Elsa.Activities.Http.Connections;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Integrations.Connectors.Slack.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Integrations.Connectors.Slack.Activities;

[Activity(Icon = "hash")]
[Version("1.0.0")]
public sealed class ListChannels(
    IHttpClientFactory httpClientFactory,
    IHttpConnectionApplicator connectionApplicator,
    ILoggerFactory loggerFactory)
    : SlackActivity<IReadOnlyList<SlackChannel>>(httpClientFactory, connectionApplicator, loggerFactory)
{
    [ActivityInput(Tier = InputTier.Optional, Description = "Comma-separated Slack conversation types. Default public_channel. Extra types need groups:read, im:read, or mpim:read.")]
    public string? Types { get; set; }

    [ActivityInput(Tier = InputTier.Optional)]
    public bool ExcludeArchived { get; set; } = true;

    [ActivityInput(Tier = InputTier.Optional, Description = "Page size (1-999). Default 100.")]
    public int? Limit { get; set; }

    [ActivityInput(Key = nameof(MaxPages), DisplayName = "Max pages", Tier = InputTier.Optional, Description = "How many cursor pages to follow. Default 10.")]
    public int? MaxPages { get; set; }

    protected override async ValueTask<ActivityTransition<IReadOnlyList<SlackChannel>>> ExecuteAsync(ActivityExecutionContext context)
    {
        try
        {
            var limit = Limit is > 0 and <= 999 ? Limit.Value : 100;
            var maxPages = MaxPages is > 0 ? Math.Min(MaxPages.Value, 100) : 10;
            var body = new JsonObject
            {
                ["types"] = string.IsNullOrWhiteSpace(Types) ? "public_channel" : Types,
                ["exclude_archived"] = ExcludeArchived,
                ["limit"] = limit
            };
            var pages = await CreateClient()
                .CollectPagesAsync(
                    SlackApi.ListConversations,
                    body,
                    "channels",
                    maxPages,
                    Connection,
                    context,
                    context.CancellationToken)
                .ConfigureAwait(false);
            var channels = pages
                .Select(item => SlackJson.Deserialize<SlackChannel>(item) ?? new SlackChannel())
                .ToArray();
            return ActivityTransition.Complete<IReadOnlyList<SlackChannel>>(channels);
        }
        catch (SlackApiException exception)
        {
            return SlackFault(exception);
        }
    }
}
