using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;

namespace Elsa.Serialization.Core;

/// <summary>Explicit-offset temporal conversions for API contracts and user-type conversions.</summary>
public static class StrictDateTimeConverters
{
    /// <summary>Adds temporal converters. API mode writes engine instants in UTC, including engine-owned unspecified values.</summary>
    public static void Configure(JsonSerializerOptions options, bool apiMode = false)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Converters.Add(new InstantConverter(apiMode));
        options.Converters.Add(new OffsetConverter(apiMode));
        options.Converters.Add(new DateConverter());
        options.Converters.Add(new TimeConverter());
        options.Converters.Add(new DurationConverter());
    }

    internal static bool HasOffset(string text)
    {
        var time = text.IndexOf('T');
        return time >= 0 && (text.EndsWith('Z') || text.AsSpan(time).IndexOfAny('+', '-') >= 0);
    }

    private static DateTimeOffset ReadInstant(ref Utf8JsonReader reader, Type target)
    {
        var text = ReadText(ref reader, target);
        if (text is null || !HasOffset(text))
            throw new JsonConversionException("json-datetime-offset-missing", $"{target.Name} requires Z or an explicit offset.");
        if (!reader.TryGetDateTimeOffset(out var value))
            throw new JsonException($"Invalid ISO 8601 {target.Name}.");
        return value;
    }

    private static string ReadText(ref Utf8JsonReader reader, Type target) => reader.TokenType == JsonTokenType.String
        ? reader.GetString()!
        : throw new JsonException($"{target.Name} requires a JSON string.");

    private sealed class InstantConverter(bool apiMode) : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => ReadInstant(ref reader, type).UtcDateTime;
        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            if (value.Kind == DateTimeKind.Unspecified && !apiMode)
                throw new JsonConversionException("json-datetime-offset-missing", "An Unspecified DateTime cannot establish an instant; send Z or an explicit offset.");
            writer.WriteStringValue(value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime());
        }
    }

    private sealed class OffsetConverter(bool apiMode) : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => ReadInstant(ref reader, type);
        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(apiMode ? value.ToUniversalTime() : value);
    }

    private sealed class DateConverter : JsonConverter<DateOnly>
    {
        public override DateOnly Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var text = ReadText(ref reader, type);
            if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return date;
            if (!HasOffset(text) && reader.TryGetDateTime(out var localDate)) return DateOnly.FromDateTime(localDate);
            return DateOnly.FromDateTime(ReadInstant(ref reader, type).UtcDateTime);
        }
        public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private sealed class TimeConverter : JsonConverter<TimeOnly>
    {
        public override TimeOnly Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var text = ReadText(ref reader, type);
            if (text.IndexOf('T') is var separator && separator >= 0)
            {
                text = text[(separator + 1)..];
                var offset = text.IndexOfAny(['Z', '+', '-']);
                if (offset >= 0) text = text[..offset];
            }
            if (TimeOnly.TryParseExact(text, ["HH:mm", "HH:mm:ss", "HH:mm:ss.FFFFFFF"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                return time;
            throw new JsonException("Invalid ISO 8601 time.");
        }
        public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture));
    }

    private sealed class DurationConverter : JsonConverter<TimeSpan>
    {
        public override TimeSpan Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var text = ReadText(ref reader, type);
            try
            {
                if (text.StartsWith('P') || text.StartsWith("-P", StringComparison.Ordinal))
                {
                    var datePart = text.Split('T')[0];
                    if (datePart.Contains('Y') || datePart.Contains('M'))
                        throw new JsonException("A calendar month or year is not a fixed duration.");
                    return XmlConvert.ToTimeSpan(text);
                }
                if (TimeSpan.TryParseExact(text, "c", CultureInfo.InvariantCulture, out var value)) return value;
            }
            catch (Exception exception) when (exception is FormatException or OverflowException) { throw new JsonException("Invalid ISO 8601 duration.", exception); }
            throw new JsonException("Invalid duration.");
        }
        public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options) => writer.WriteStringValue(XmlConvert.ToString(value));
    }
}
