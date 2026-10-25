using System.Text.Json.Nodes;
using Elsa.Activities.Http.Connections;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Integrations.Connectors.Microsoft365.Models;

namespace Elsa.Integrations.Connectors.Microsoft365.Activities;

[Activity(Icon = "calendar-plus")]
[Version("1.0.0")]
[RefusesSecretBinding(nameof(Subject), SecretBindingRefusalReason.EchoedToOutput)]
[RefusesSecretBinding(nameof(Body), SecretBindingRefusalReason.EchoedToOutput)]
public sealed class CreateEvent(
    IHttpClientFactory httpClientFactory,
    IHttpConnectionApplicator connectionApplicator)
    : Microsoft365Activity<GraphEvent>(httpClientFactory, connectionApplicator)
{
    [ActivityInput(Key = nameof(Subject), DisplayName = "Subject", Tier = InputTier.Essential, Description = "Requires Calendars.ReadWrite.")]
    public string Subject { get; set; } = "";

    [ActivityInput(Key = nameof(Start), DisplayName = "Start", Tier = InputTier.Essential)]
    public DateTimeOffset Start { get; set; }

    [ActivityInput(Key = nameof(End), DisplayName = "End", Tier = InputTier.Essential)]
    public DateTimeOffset End { get; set; }

    [ActivityInput(Key = nameof(TimeZone), DisplayName = "Time zone", Tier = InputTier.Optional, Description = "Windows or IANA time zone. Default UTC.")]
    public string? TimeZone { get; set; }

    [ActivityInput(Key = nameof(Body), DisplayName = "Body", Tier = InputTier.Optional)]
    public string? Body { get; set; }

    [ActivityInput(Key = nameof(Location), DisplayName = "Location", Tier = InputTier.Optional)]
    public string? Location { get; set; }

    [ActivityInput(Key = nameof(Attendees), DisplayName = "Attendees", Tier = InputTier.Optional, Description = "JSON array of email addresses or {address, name, type} objects.")]
    public string? Attendees { get; set; }

    [ActivityInput(Key = nameof(UserId), DisplayName = "User id", Tier = InputTier.Optional, Description = "User id or UPN for a shared calendar the signed-in user can access. Default /me.")]
    public string? UserId { get; set; }

    protected override async ValueTask<ActivityTransition<GraphEvent>> ExecuteAsync(ActivityExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(Subject) || Start == default || End == default)
        {
            return ActivityTransition.Fault<GraphEvent>(
                new ActivityFault(Microsoft365ErrorCodes.InvalidInput, "Create Event requires Subject, Start and End."));
        }

        try
        {
            var timeZone = string.IsNullOrWhiteSpace(TimeZone) ? "UTC" : TimeZone.Trim();
            var body = new JsonObject
            {
                ["subject"] = Subject,
                ["start"] = GraphPayloads.DateTime(Start, timeZone),
                ["end"] = GraphPayloads.DateTime(End, timeZone)
            };
            if (!string.IsNullOrWhiteSpace(Body))
            {
                body["body"] = new JsonObject
                {
                    ["contentType"] = "Text",
                    ["content"] = Body
                };
            }

            if (!string.IsNullOrWhiteSpace(Location))
                body["location"] = new JsonObject { ["displayName"] = Location };

            var attendees = GraphPayloads.Attendees(Attendees);
            if (attendees.Count > 0)
                body["attendees"] = attendees;

            var root = await CreateClient()
                .PostJsonAsync(
                    $"{Microsoft365Paths.UserRoot(UserId)}/events",
                    body,
                    Connection,
                    context,
                    context.CancellationToken,
                    Microsoft365Scopes.CalendarsReadWrite)
                .ConfigureAwait(false);
            return ActivityTransition.Complete(GraphPayloads.ReadEvent(root));
        }
        catch (Microsoft365ApiException exception)
        {
            return GraphFault(exception);
        }
    }
}
