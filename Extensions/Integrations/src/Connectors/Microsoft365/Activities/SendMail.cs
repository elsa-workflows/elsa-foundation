using System.Text.Json.Nodes;
using Elsa.Activities.Http.Connections;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Integrations.Connectors.Microsoft365.Activities;

[Activity(Icon = "send")]
[Version("1.0.0")]
[RefusesSecretBinding(nameof(Subject), SecretBindingRefusalReason.EchoedToOutput)]
[RefusesSecretBinding(nameof(Body), SecretBindingRefusalReason.EchoedToOutput)]
public sealed class SendMail(
    IHttpClientFactory httpClientFactory,
    IHttpConnectionApplicator connectionApplicator)
    : Microsoft365Activity(httpClientFactory, connectionApplicator)
{
    [ActivityInput(Key = nameof(To), DisplayName = "To", Tier = InputTier.Essential, Description = "Comma-separated addresses, or a JSON array. Requires Mail.Send.")]
    public string To { get; set; } = "";

    [ActivityInput(Key = nameof(Cc), DisplayName = "Cc", Tier = InputTier.Optional, Description = "Comma-separated addresses, or a JSON array.")]
    public string? Cc { get; set; }

    [ActivityInput(Key = nameof(Bcc), DisplayName = "Bcc", Tier = InputTier.Optional, Description = "Comma-separated addresses, or a JSON array.")]
    public string? Bcc { get; set; }

    [ActivityInput(Key = nameof(Subject), DisplayName = "Subject", Tier = InputTier.Essential)]
    public string Subject { get; set; } = "";

    [ActivityInput(Key = nameof(Body), DisplayName = "Body", Tier = InputTier.Essential)]
    public string Body { get; set; } = "";

    [ActivityInput(Key = nameof(BodyType), DisplayName = "Body type", Tier = InputTier.Optional, Description = "Text or HTML. Default Text.")]
    public string? BodyType { get; set; }

    [ActivityInput(Key = nameof(SaveToSentItems), DisplayName = "Save to sent items", Tier = InputTier.Optional)]
    public bool SaveToSentItems { get; set; } = true;

    [ActivityInput(Key = nameof(UserId), DisplayName = "User id", Tier = InputTier.Optional, Description = "User id or UPN for a shared mailbox the signed-in user can access. Default /me.")]
    public string? UserId { get; set; }

    protected override async ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        try
        {
            var to = GraphPayloads.Recipients(To, "To");
            if (to.Count == 0)
            {
                return ActivityTransition.Fault<ActivityUnit>(
                    new ActivityFault(Microsoft365ErrorCodes.InvalidInput, "Send Mail requires at least one To recipient."));
            }

            var message = new JsonObject
            {
                ["subject"] = Subject,
                ["body"] = new JsonObject
                {
                    ["contentType"] = string.IsNullOrWhiteSpace(BodyType) ? "Text" : BodyType.Trim(),
                    ["content"] = Body ?? ""
                },
                ["toRecipients"] = to
            };
            var cc = GraphPayloads.Recipients(Cc, "Cc");
            if (cc.Count > 0)
                message["ccRecipients"] = cc;
            var bcc = GraphPayloads.Recipients(Bcc, "Bcc");
            if (bcc.Count > 0)
                message["bccRecipients"] = bcc;

            var body = new JsonObject
            {
                ["message"] = message,
                ["saveToSentItems"] = SaveToSentItems
            };

            await CreateClient()
                .PostNoContentAsync(
                    $"{Microsoft365Paths.UserRoot(UserId)}/sendMail",
                    body,
                    Connection,
                    context,
                    context.CancellationToken,
                    Microsoft365Scopes.MailSend)
                .ConfigureAwait(false);
            return ActivityTransition.Complete(ActivityUnit.Value);
        }
        catch (Microsoft365ApiException exception)
        {
            return GraphFault(exception);
        }
    }
}
