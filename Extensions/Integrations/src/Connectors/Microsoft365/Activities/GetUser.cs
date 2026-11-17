using Elsa.Activities.Http.Connections;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Integrations.Connectors.Microsoft365.Models;

namespace Elsa.Integrations.Connectors.Microsoft365.Activities;

[Activity(Icon = "user")]
[Version("1.0.0")]
public sealed class GetUser(
    IHttpClientFactory httpClientFactory,
    IHttpConnectionApplicator connectionApplicator)
    : Microsoft365Activity<GraphUserLookupResult>(httpClientFactory, connectionApplicator)
{
    [ActivityInput(Key = nameof(UserId), DisplayName = "User id", Tier = InputTier.Essential, Description = "User id or UPN. A 404 returns Found=false. Requires User.ReadBasic.All.")]
    public string UserId { get; set; } = "";

    protected override async ValueTask<ActivityTransition<GraphUserLookupResult>> ExecuteAsync(ActivityExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(UserId))
        {
            return ActivityTransition.Fault<GraphUserLookupResult>(
                new ActivityFault(Microsoft365ErrorCodes.InvalidInput, "Get User requires User id."));
        }

        try
        {
            var root = await CreateClient()
                .GetJsonAsync(
                    $"users/{Uri.EscapeDataString(UserId.Trim())}",
                    Connection,
                    context,
                    context.CancellationToken,
                    Microsoft365Scopes.UserReadBasicAll,
                    allowNotFound: true)
                .ConfigureAwait(false);

            if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
                return ActivityTransition.Complete(new GraphUserLookupResult { Found = false });

            return ActivityTransition.Complete(new GraphUserLookupResult
            {
                Found = true,
                User = GraphPayloads.ReadUser(root)
            });
        }
        catch (Microsoft365ApiException exception)
        {
            return GraphFault(exception);
        }
    }
}
