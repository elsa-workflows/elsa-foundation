using Elsa.Activities.Http.Connections;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Integrations.Connectors.Microsoft365.Models;

namespace Elsa.Integrations.Connectors.Microsoft365.Activities;

[Activity(Icon = "calendar")]
[Version("1.0.0")]
public sealed class ListEvents(
    IHttpClientFactory httpClientFactory,
    IHttpConnectionApplicator connectionApplicator)
    : Microsoft365Activity<IReadOnlyList<GraphEvent>>(httpClientFactory, connectionApplicator)
{
    [ActivityInput(Key = nameof(StartDateTime), DisplayName = "Start date time", Tier = InputTier.Essential, Description = "calendarView start, converted to UTC. Requires Calendars.Read.")]
    public DateTimeOffset StartDateTime { get; set; }

    [ActivityInput(Key = nameof(EndDateTime), DisplayName = "End date time", Tier = InputTier.Essential, Description = "calendarView end, converted to UTC.")]
    public DateTimeOffset EndDateTime { get; set; }

    [ActivityInput(Key = nameof(Top), DisplayName = "Top", Tier = InputTier.Optional, Description = "Page size (1-999). Default 50.")]
    public int? Top { get; set; }

    [ActivityInput(Key = nameof(MaxItems), DisplayName = "Max items", Tier = InputTier.Optional, Description = "How many events to collect across @odata.nextLink pages. Default 100.")]
    public int? MaxItems { get; set; }

    [ActivityInput(Key = nameof(UserId), DisplayName = "User id", Tier = InputTier.Optional, Description = "User id or UPN for a shared calendar the signed-in user can access. Default /me.")]
    public string? UserId { get; set; }

    protected override async ValueTask<ActivityTransition<IReadOnlyList<GraphEvent>>> ExecuteAsync(ActivityExecutionContext context)
    {
        if (StartDateTime == default || EndDateTime == default)
        {
            return ActivityTransition.Fault<IReadOnlyList<GraphEvent>>(
                new ActivityFault(Microsoft365ErrorCodes.InvalidInput, "List Events requires Start date time and End date time."));
        }

        try
        {
            var top = Top is > 0 and <= Microsoft365Api.MaxPageSize ? Top.Value : Microsoft365Api.DefaultPageSize;
            var maxItems = MaxItems is > 0
                ? Math.Min(MaxItems.Value, Microsoft365Api.AbsoluteMaxItems)
                : Microsoft365Api.DefaultMaxItems;
            var start = Uri.EscapeDataString(GraphPayloads.FormatInZone(StartDateTime, "UTC"));
            var end = Uri.EscapeDataString(GraphPayloads.FormatInZone(EndDateTime, "UTC"));
            var path = $"{Microsoft365Paths.UserRoot(UserId)}/calendarView?startDateTime={start}&endDateTime={end}&$top={top}";

            var pages = await CreateClient()
                .CollectPagesAsync(
                    path,
                    maxItems,
                    Connection,
                    context,
                    context.CancellationToken,
                    Microsoft365Scopes.CalendarsRead)
                .ConfigureAwait(false);
            return ActivityTransition.Complete<IReadOnlyList<GraphEvent>>(
                pages.Select(GraphPayloads.ReadEvent).ToArray());
        }
        catch (Microsoft365ApiException exception)
        {
            return GraphFault(exception);
        }
    }
}
