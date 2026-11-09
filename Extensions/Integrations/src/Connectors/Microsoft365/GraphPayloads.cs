using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Integrations.Connectors.Microsoft365.Models;

namespace Elsa.Integrations.Connectors.Microsoft365;

internal static class GraphPayloads
{
    public static JsonArray Recipients(string? value, string inputName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        var trimmed = value.Trim();
        if (trimmed.StartsWith('['))
        {
            JsonNode? parsed;
            try
            {
                parsed = JsonNode.Parse(trimmed);
            }
            catch (JsonException)
            {
                throw new Microsoft365ApiException(
                    Microsoft365ErrorCodes.InvalidInput,
                    $"{inputName} must be a JSON array of email addresses.");
            }

            if (parsed is not JsonArray array)
            {
                throw new Microsoft365ApiException(
                    Microsoft365ErrorCodes.InvalidInput,
                    $"{inputName} must be a JSON array of email addresses.");
            }

            var recipients = new JsonArray();
            foreach (var node in array)
            {
                var address = node switch
                {
                    JsonValue scalar when scalar.TryGetValue<string>(out var text) => text,
                    JsonObject obj => NodeString(obj["address"]) ?? NodeString(obj["email"]),
                    _ => null
                };
                if (string.IsNullOrWhiteSpace(address))
                    continue;
                recipients.Add(Recipient(address));
            }

            return recipients;
        }

        var result = new JsonArray();
        foreach (var part in trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            result.Add(Recipient(part));
        return result;
    }

    public static JsonArray Attendees(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        var trimmed = value.Trim();
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(trimmed);
        }
        catch (JsonException)
        {
            throw new Microsoft365ApiException(
                Microsoft365ErrorCodes.InvalidInput,
                "Attendees must be a JSON array of email addresses or attendee objects.");
        }

        if (parsed is not JsonArray array)
        {
            throw new Microsoft365ApiException(
                Microsoft365ErrorCodes.InvalidInput,
                "Attendees must be a JSON array of email addresses or attendee objects.");
        }

        var attendees = new JsonArray();
        foreach (var node in array)
        {
            if (node is JsonValue scalar && scalar.TryGetValue<string>(out var email) && !string.IsNullOrWhiteSpace(email))
            {
                attendees.Add(new JsonObject
                {
                    ["emailAddress"] = new JsonObject { ["address"] = email },
                    ["type"] = "required"
                });
                continue;
            }

            if (node is JsonObject obj)
            {
                var address = NodeString(obj["address"]) ?? NodeString(obj["email"]);
                if (string.IsNullOrWhiteSpace(address))
                    continue;
                var attendee = new JsonObject
                {
                    ["emailAddress"] = new JsonObject
                    {
                        ["address"] = address,
                        ["name"] = NodeString(obj["name"])
                    },
                    ["type"] = NodeString(obj["type"]) ?? "required"
                };
                attendees.Add(attendee);
            }
        }

        return attendees;
    }

    public static string MailBodyType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Text";
        var trimmed = value.Trim();
        if (trimmed.Equals("Text", StringComparison.OrdinalIgnoreCase))
            return "Text";
        if (trimmed.Equals("HTML", StringComparison.OrdinalIgnoreCase))
            return "HTML";
        throw new Microsoft365ApiException(
            Microsoft365ErrorCodes.InvalidInput,
            "Body type must be Text or HTML.");
    }

    public static string TeamsContentType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "text";
        var trimmed = value.Trim();
        if (trimmed.Equals("text", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("html", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.ToLowerInvariant();
        }

        throw new Microsoft365ApiException(
            Microsoft365ErrorCodes.InvalidInput,
            "Content type must be text or html.");
    }

    public static string ConflictBehavior(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "rename";
        var trimmed = value.Trim();
        if (trimmed.Equals("rename", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("replace", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("fail", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.ToLowerInvariant();
        }

        throw new Microsoft365ApiException(
            Microsoft365ErrorCodes.InvalidInput,
            "Conflict behavior must be rename, replace, or fail.");
    }

    public static string ResolveTimeZone(string? timeZone) =>
        string.IsNullOrWhiteSpace(timeZone) ? "UTC" : timeZone.Trim();

    public static string FormatInZone(DateTimeOffset value, string? timeZone)
    {
        var zoneName = ResolveTimeZone(timeZone);
        return ClockInZone(value, zoneName).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
    }

    public static JsonObject DateTime(DateTimeOffset value, string? timeZone)
    {
        var zoneName = ResolveTimeZone(timeZone);
        return new JsonObject
        {
            ["dateTime"] = FormatInZone(value, zoneName),
            ["timeZone"] = zoneName
        };
    }

    public static JsonArray TeamsAttachments(string? attachments, string content, string contentType)
    {
        if (string.IsNullOrWhiteSpace(attachments))
            return [];

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(attachments);
        }
        catch (JsonException)
        {
            throw new Microsoft365ApiException(
                Microsoft365ErrorCodes.InvalidInput,
                "Attachments must be a JSON array.");
        }

        if (parsed is not JsonArray array)
        {
            throw new Microsoft365ApiException(
                Microsoft365ErrorCodes.InvalidInput,
                "Attachments must be a JSON array.");
        }

        if (array.Count == 0)
            return array;

        if (!contentType.Equals("html", StringComparison.OrdinalIgnoreCase))
        {
            throw new Microsoft365ApiException(
                Microsoft365ErrorCodes.InvalidInput,
                "Attachments require Content type html and a matching <attachment id=\"...\"></attachment> tag in Content for each attachment.");
        }

        foreach (var node in array)
        {
            var id = node is JsonObject obj ? NodeString(obj["id"]) : null;
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new Microsoft365ApiException(
                    Microsoft365ErrorCodes.InvalidInput,
                    "Each attachment must have an id.");
            }

            var tag = "<attachment id=\"" + id + "\"></attachment>";
            if (!content.Contains(tag, StringComparison.Ordinal))
            {
                throw new Microsoft365ApiException(
                    Microsoft365ErrorCodes.InvalidInput,
                    "Content must include " + tag + " for each attachment.");
            }
        }

        return array;
    }

    private static DateTime ClockInZone(DateTimeOffset value, string timeZone)
    {
        if (timeZone.Equals("UTC", StringComparison.OrdinalIgnoreCase)
            || timeZone.Equals("Etc/UTC", StringComparison.OrdinalIgnoreCase)
            || timeZone.Equals("Etc/GMT", StringComparison.OrdinalIgnoreCase))
        {
            return value.UtcDateTime;
        }

        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new Microsoft365ApiException(
                Microsoft365ErrorCodes.InvalidInput,
                "Time zone must be a Windows or IANA id.");
        }

        return TimeZoneInfo.ConvertTime(value, zone).DateTime;
    }

    public static GraphMessage ReadMessage(JsonElement element)
    {
        GraphRecipient? from = null;
        if (element.TryGetProperty("from", out var fromElement)
            && fromElement.ValueKind == JsonValueKind.Object
            && fromElement.TryGetProperty("emailAddress", out var email))
        {
            from = new GraphRecipient
            {
                Address = email.TryGetProperty("address", out var address) ? address.GetString() : null,
                Name = email.TryGetProperty("name", out var name) ? name.GetString() : null
            };
        }

        return new GraphMessage
        {
            Id = StringProp(element, "id"),
            Subject = StringProp(element, "subject"),
            BodyPreview = StringProp(element, "bodyPreview"),
            ReceivedDateTime = DateProp(element, "receivedDateTime"),
            IsRead = element.TryGetProperty("isRead", out var read) && read.ValueKind == JsonValueKind.True,
            From = from
        };
    }

    public static GraphEvent ReadEvent(JsonElement element)
    {
        var start = DateTimeProp(element, "start");
        var end = DateTimeProp(element, "end");
        return new GraphEvent
        {
            Id = StringProp(element, "id"),
            Subject = StringProp(element, "subject"),
            Start = start.Value,
            End = end.Value,
            TimeZone = start.TimeZone ?? end.TimeZone,
            Location = element.TryGetProperty("location", out var location)
                && location.ValueKind == JsonValueKind.Object
                && location.TryGetProperty("displayName", out var display)
                ? display.GetString()
                : null,
            WebLink = StringProp(element, "webLink")
        };
    }

    public static GraphUser ReadUser(JsonElement element) =>
        new()
        {
            Id = StringProp(element, "id"),
            DisplayName = StringProp(element, "displayName"),
            Mail = StringProp(element, "mail"),
            UserPrincipalName = StringProp(element, "userPrincipalName")
        };

    public static GraphDriveItem ReadDriveItem(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return new GraphDriveItem();

        return new GraphDriveItem
        {
            Id = StringProp(element, "id"),
            Name = StringProp(element, "name"),
            Size = element.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) ? bytes : null,
            WebUrl = StringProp(element, "webUrl")
        };
    }

    public static GraphChatMessage ReadChatMessage(JsonElement element) =>
        new()
        {
            Id = StringProp(element, "id"),
            WebUrl = StringProp(element, "webUrl")
        };

    private static string? NodeString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static JsonObject Recipient(string address) =>
        new()
        {
            ["emailAddress"] = new JsonObject { ["address"] = address }
        };

    private static string? StringProp(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? DateProp(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static (DateTimeOffset? Value, string? TimeZone) DateTimeProp(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var node) || node.ValueKind != JsonValueKind.Object)
            return (null, null);

        var timeZone = node.TryGetProperty("timeZone", out var zone) ? zone.GetString() : null;
        if (node.TryGetProperty("dateTime", out var date)
            && date.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(date.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return (parsed, timeZone);
        }

        return (null, timeZone);
    }
}
