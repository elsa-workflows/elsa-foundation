using System.Text.Json.Nodes;
using Elsa.Activities.Http.Connections;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Integrations.Connectors.Slack.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Integrations.Connectors.Slack.Activities;

[Activity(Icon = "user")]
[Version("1.0.0")]
public sealed class LookupUserByEmail(
    IHttpClientFactory httpClientFactory,
    IHttpConnectionApplicator connectionApplicator,
    ILoggerFactory loggerFactory)
    : SlackActivity<SlackUserLookupResult>(httpClientFactory, connectionApplicator, loggerFactory)
{
    [ActivityInput(Tier = InputTier.Essential, Description = "Requires users:read and users:read.email.")]
    public string Email { get; set; } = "";

    protected override async ValueTask<ActivityTransition<SlackUserLookupResult>> ExecuteAsync(ActivityExecutionContext context)
    {
        try
        {
            var body = new JsonObject { ["email"] = Email };
            var root = await CreateClient()
                .PostJsonAsync(
                    SlackApi.LookupUserByEmail,
                    body,
                    Connection,
                    context,
                    context.CancellationToken,
                    allowedErrors: new HashSet<string>(StringComparer.Ordinal) { SlackErrorCodes.UsersNotFound })
                .ConfigureAwait(false);

            if (root.TryGetProperty("error", out var error)
                && error.ValueKind == System.Text.Json.JsonValueKind.String
                && string.Equals(error.GetString(), SlackErrorCodes.UsersNotFound, StringComparison.Ordinal))
            {
                return ActivityTransition.Complete(new SlackUserLookupResult { Found = false });
            }

            var user = root.TryGetProperty("user", out var element)
                ? SlackJson.Deserialize<SlackUser>(element)
                : null;
            return ActivityTransition.Complete(new SlackUserLookupResult { Found = user is not null, User = user });
        }
        catch (SlackApiException exception)
        {
            return SlackFault(exception);
        }
    }
}
