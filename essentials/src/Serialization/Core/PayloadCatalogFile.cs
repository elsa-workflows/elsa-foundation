using System.Buffers;
using System.Text.Json;

namespace Elsa.Serialization.Core;

/// <summary>
/// Reads and writes a JSON array of catalog models on disk through <see cref="IPayloadSerializer"/>.
/// Domain readers and writers keep their own exception types and logging; this helper has no logger.
/// <para>
/// The malformed-array reason still says "reconciliation models" because both current callers are
/// reconcilers and that is the message they emitted before this helper existed; changing it here would
/// alter an operator-visible diagnostic in what is otherwise a behaviour-preserving extraction. Give the
/// reason its own parameter when a caller that is not a reconciler needs it.
/// </para>
/// </summary>
public static class PayloadCatalogFile
{
    public static TModel[] ReadArray<TModel>(
        string path,
        IPayloadSerializer serializer,
        Func<string, string, Exception?, Exception> exceptionFactory)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(exceptionFactory);

        if (string.IsNullOrWhiteSpace(path))
            throw exceptionFactory(path ?? string.Empty, "no file path was configured.", null);

        if (!File.Exists(path))
            throw exceptionFactory(path, "the file does not exist.", null);

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw exceptionFactory(path, "the file could not be read.", exception);
        }

        TModel[]? models;
        try
        {
            models = serializer.Deserialize<TModel[]>(json);
        }
        catch (JsonException exception)
        {
            throw exceptionFactory(path, "the file is not a valid JSON array of reconciliation models.", exception);
        }

        if (models is null)
            throw exceptionFactory(path, "the file deserialized to null.", null);

        return models;
    }

    /// <summary>
    /// Writes <paramref name="models"/> to <paramref name="path"/> as a top-level JSON array that
    /// <see cref="ReadArray{TModel}"/> reads back. The array is serialized with <paramref name="serializer"/>'s
    /// options, indented, with <c>\n</c> line endings and a final newline, as UTF-8 without a byte order mark, so
    /// the same models give the same bytes on every platform; embedded opaque JSON is written verbatim. The parent
    /// directory is created when it is missing. IO faults are not wrapped.
    /// </summary>
    public static void WriteArray<TModel>(string path, IReadOnlyList<TModel> models, IPayloadSerializer serializer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(serializer);

        // The serializer's own options, written through an indented writer: the options stay what the serializer
        // carries (no WriteIndented deviation from the baseline, ADR 0091), and embedded opaque JSON (a raw
        // JsonElement such as an activity descriptor) still goes through the serializer's raw converter verbatim,
        // so read(write(x)) serializes exactly as x does.
        var options = serializer.GetOptions();
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = options.Encoder }))
            JsonSerializer.Serialize(writer, models.ToArray(), typeof(TModel[]), options);
        buffer.Write("\n"u8);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllBytes(path, buffer.WrittenSpan.ToArray());
    }
}
