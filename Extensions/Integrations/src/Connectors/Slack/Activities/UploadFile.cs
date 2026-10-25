using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Http.Connections;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Integrations.Connectors.Slack.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Integrations.Connectors.Slack.Activities;

[Activity(Icon = "upload")]
[Version("1.0.0")]
[RefusesSecretBinding(nameof(Content), SecretBindingRefusalReason.EchoedToOutput)]
[RefusesSecretBinding(nameof(InitialComment), SecretBindingRefusalReason.EchoedToOutput)]
public sealed class UploadFile(
    IHttpClientFactory httpClientFactory,
    IHttpConnectionApplicator connectionApplicator,
    ILoggerFactory loggerFactory)
    : SlackActivity<SlackFileUploadResult>(httpClientFactory, connectionApplicator, loggerFactory)
{
    [ActivityInput(Tier = InputTier.Optional, Description = "Channel to share the file into.")]
    public string? Channel { get; set; }

    [ActivityInput(Tier = InputTier.Essential)]
    public string Filename { get; set; } = "";

    [ActivityInput(Tier = InputTier.Essential, Description = "File bytes. The upload POST to Slack's returned URL does not send the connection Authorization header.")]
    public byte[]? Content { get; set; }

    [ActivityInput(Tier = InputTier.Optional)]
    public string? Title { get; set; }

    [ActivityInput(Key = nameof(InitialComment), DisplayName = "Initial comment", Tier = InputTier.Optional)]
    public string? InitialComment { get; set; }

    [ActivityInput(Key = nameof(ThreadTs), DisplayName = "Thread ts", Tier = InputTier.Optional)]
    public string? ThreadTs { get; set; }

    protected override async ValueTask<ActivityTransition<SlackFileUploadResult>> ExecuteAsync(ActivityExecutionContext context)
    {
        if (Content is null)
        {
            return ActivityTransition.Fault<SlackFileUploadResult>(
                new ActivityFault("invalid_input", "Upload File requires Content bytes."));
        }

        try
        {
            var client = CreateClient();
            var start = new JsonObject
            {
                ["filename"] = Filename,
                ["length"] = Content.Length
            };
            var started = await client
                .PostJsonAsync(SlackApi.GetUploadUrlExternal, start, Connection, context, context.CancellationToken)
                .ConfigureAwait(false);

            var uploadUrl = started.TryGetProperty("upload_url", out var urlElement) ? urlElement.GetString() : null;
            var fileId = started.TryGetProperty("file_id", out var idElement) ? idElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(uploadUrl) || string.IsNullOrWhiteSpace(fileId)
                || !Uri.TryCreate(uploadUrl, UriKind.Absolute, out var uploadUri)
                || !SlackApiClient.IsAllowedUploadUri(uploadUri))
            {
                return ActivityTransition.Fault<SlackFileUploadResult>(
                    new ActivityFault(SlackErrorCodes.InvalidResponse, "Slack did not return an upload URL and file id."));
            }

            var uploaded = await client.PostBytesWithoutAuthAsync(uploadUri, Content, context.CancellationToken)
                .ConfigureAwait(false);
            if (uploaded is not HttpStatusCode.OK)
            {
                return ActivityTransition.Fault<SlackFileUploadResult>(
                    new ActivityFault(SlackErrorCodes.HttpError, $"Slack file host returned HTTP {(int)uploaded}."));
            }

            var files = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = fileId,
                    ["title"] = string.IsNullOrWhiteSpace(Title) ? Filename : Title
                }
            };
            var complete = new JsonObject { ["files"] = files };
            if (!string.IsNullOrWhiteSpace(Channel))
                complete["channel_id"] = Channel;
            if (!string.IsNullOrWhiteSpace(InitialComment))
                complete["initial_comment"] = InitialComment;
            if (!string.IsNullOrWhiteSpace(ThreadTs))
                complete["thread_ts"] = ThreadTs;

            var finished = await client
                .PostJsonAsync(SlackApi.CompleteUploadExternal, complete, Connection, context, context.CancellationToken)
                .ConfigureAwait(false);

            var ids = ReadFileIds(finished, fileId);
            return ActivityTransition.Complete(new SlackFileUploadResult { FileIds = ids });
        }
        catch (SlackApiException exception)
        {
            return SlackFault(exception);
        }
    }

    private static IReadOnlyList<string> ReadFileIds(JsonElement root, string fallback)
    {
        if (root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
        {
            var ids = files.EnumerateArray()
                .Select(file => file.TryGetProperty("id", out var id) ? id.GetString() : null)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .ToArray();
            if (ids.Length > 0)
                return ids;
        }

        return [fallback];
    }
}
