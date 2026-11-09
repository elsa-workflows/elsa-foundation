using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Integrations.Connectors.Slack.Models;

namespace Elsa.Integrations.Connectors.Slack.Activities;

internal static class SlackMessageBody
{
    public static JsonObject Create(
        string channel,
        string? text,
        string? blocks,
        string? threadTs,
        bool? replyBroadcast,
        bool? unfurlLinks,
        bool? unfurlMedia)
    {
        var body = new JsonObject
        {
            ["channel"] = channel
        };
        if (!string.IsNullOrWhiteSpace(text))
            body["text"] = text;
        if (!string.IsNullOrWhiteSpace(blocks))
            body["blocks"] = ParseBlocks(blocks);
        if (!string.IsNullOrWhiteSpace(threadTs))
            body["thread_ts"] = threadTs;
        if (replyBroadcast is { } broadcast)
            body["reply_broadcast"] = broadcast;
        if (unfurlLinks is { } links)
            body["unfurl_links"] = links;
        if (unfurlMedia is { } media)
            body["unfurl_media"] = media;
        return body;
    }

    private static JsonArray ParseBlocks(string blocks)
    {
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(blocks);
        }
        catch (JsonException)
        {
            throw new SlackApiException(SlackErrorCodes.InvalidInput, "Blocks must be a JSON array.");
        }

        if (parsed is not JsonArray array)
            throw new SlackApiException(SlackErrorCodes.InvalidInput, "Blocks must be a JSON array.");

        return array;
    }

    public static SlackMessageResult ReadResult(JsonElement root) =>
        new()
        {
            Channel = root.TryGetProperty("channel", out var channel) ? channel.GetString() : null,
            Ts = root.TryGetProperty("ts", out var ts) ? ts.GetString() : null
        };
}
