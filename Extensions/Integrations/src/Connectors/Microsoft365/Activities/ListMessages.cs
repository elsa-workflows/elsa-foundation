using Elsa.Activities.Http.Connections;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Integrations.Connectors.Microsoft365.Models;

namespace Elsa.Integrations.Connectors.Microsoft365.Activities;

[Activity(Icon = "inbox")]
[Version("1.0.0")]
public sealed class ListMessages(
    IHttpClientFactory httpClientFactory,
    IHttpConnectionApplicator connectionApplicator)
    : Microsoft365Activity<IReadOnlyList<GraphMessage>>(httpClientFactory, connectionApplicator)
{
    [ActivityInput(Key = nameof(Folder), DisplayName = "Folder", Tier = InputTier.Optional, Description = "Mail folder id or well-known name. Default inbox. Requires Mail.Read.")]
    public string? Folder { get; set; }

    [ActivityInput(Key = nameof(Filter), DisplayName = "Filter", Tier = InputTier.Optional, Description = "OData $filter.")]
    public string? Filter { get; set; }

    [ActivityInput(Key = nameof(Top), DisplayName = "Top", Tier = InputTier.Optional, Description = "Page size (1-999). Default 50.")]
    public int? Top { get; set; }

    [ActivityInput(Key = nameof(MaxItems), DisplayName = "Max items", Tier = InputTier.Optional, Description = "How many messages to collect across @odata.nextLink pages. Default 100.")]
    public int? MaxItems { get; set; }

    [ActivityInput(Key = nameof(UserId), DisplayName = "User id", Tier = InputTier.Optional, Description = "User id or UPN for a shared mailbox the signed-in user can access. Default /me.")]
    public string? UserId { get; set; }

    protected override async ValueTask<ActivityTransition<IReadOnlyList<GraphMessage>>> ExecuteAsync(ActivityExecutionContext context)
    {
        try
        {
            var folder = string.IsNullOrWhiteSpace(Folder) ? "inbox" : Folder.Trim();
            var top = Top is > 0 and <= Microsoft365Api.MaxPageSize ? Top.Value : Microsoft365Api.DefaultPageSize;
            var maxItems = MaxItems is > 0
                ? Math.Min(MaxItems.Value, Microsoft365Api.AbsoluteMaxItems)
                : Microsoft365Api.DefaultMaxItems;
            var path = $"{Microsoft365Paths.UserRoot(UserId)}/mailFolders/{Uri.EscapeDataString(folder)}/messages?$top={top}";
            if (!string.IsNullOrWhiteSpace(Filter))
                path += $"&$filter={Uri.EscapeDataString(Filter.Trim())}";

            var pages = await CreateClient()
                .CollectPagesAsync(
                    path,
                    maxItems,
                    Connection,
                    context,
                    context.CancellationToken,
                    Microsoft365Scopes.MailRead)
                .ConfigureAwait(false);
            return ActivityTransition.Complete<IReadOnlyList<GraphMessage>>(
                pages.Select(GraphPayloads.ReadMessage).ToArray());
        }
        catch (Microsoft365ApiException exception)
        {
            return GraphFault(exception);
        }
    }
}
