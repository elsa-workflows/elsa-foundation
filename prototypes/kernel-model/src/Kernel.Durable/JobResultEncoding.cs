using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kernel.Durable;

internal static class JobResultEncoding
{
    // Reserve M and F for the projected and unprojected fields of the 16 MiB row.
    private const int Limit = 16 * 1024 * 1024 - 2 * 24 * 1024;
    internal const string Error = "result too large";
    private static readonly JsonSerializerOptions Options = new() { Converters = { new BoundedString() } };

    internal static string Value(object? value) => Encode(value);

    internal static string Payload(JobFinished result)
    {
        // The encoded result is a string in a second envelope and its escaping also counts against the cap.
        if ((long)(result.Result?.Length ?? 0) + (result.Error?.Length ?? 0) <= Limit)
        {
            try { return Encode(result); }
            catch (LimitExceeded) { }
        }
        return Json.Write(new JobFinished(result.Key, null, Error));
    }

    private static string Encode(object? value)
    {
        using var stream = new BoundedBuffer();
        try
        {
            JsonSerializer.Serialize(stream, value, value?.GetType() ?? typeof(object), Options);
        }
        catch (ArgumentException error) when (IsWriterSizeLimit(error))
        {
            // A DOM node or a custom converter can call the writer before the buffer sees the token.
            throw new LimitExceeded();
        }
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    private static bool IsWriterSizeLimit(ArgumentException error)
    {
        var origin = error.TargetSite;
        var owner = origin?.DeclaringType;
        // .NET 10 reports token limits through these helpers. Match the assembly and throw site, so the
        // classification is independent of locale and leaves unrelated JSON and activity errors intact.
        return owner is { FullName: "System.Text.Json.ThrowHelper" } && owner.Assembly == typeof(Utf8JsonWriter).Assembly &&
            origin!.Name is "ThrowArgumentException_ValueTooLarge" or "ThrowArgumentException_PropertyNameTooLarge" or
                "ThrowPropertyNameTooLargeArgumentException";
    }

    internal sealed class LimitExceeded : Exception;

    // Check scalar strings before the writer's own single-token limit. The buffer limits aggregate encoded bytes.
    private sealed class BoundedString : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => reader.GetString();

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WriteStringValue(WithinLimit(value));

        public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WritePropertyName(WithinLimit(value));

        private static string WithinLimit(string value) => value.Length <= Limit ? value : throw new LimitExceeded();
    }

    private sealed class BoundedBuffer : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            CheckSpace(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            CheckSpace(buffer.Length);
            base.Write(buffer);
        }

        private void CheckSpace(int count)
        {
            if (count > Limit - Position) throw new LimitExceeded();
        }
    }
}
