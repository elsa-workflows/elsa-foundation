using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;

namespace Elsa.Cli;

/// <summary>Captures supplied composition inputs once and detects edits before reviewed publication.</summary>
public sealed class CompositionInputSnapshot
{
    private readonly IReadOnlyDictionary<string, byte[]> _files;
    private readonly CompositionFileReader? _candidateReader;

    private CompositionInputSnapshot(IReadOnlyDictionary<string, byte[]> files, CompositionFileReader? candidateReader)
    {
        _files = files;
        _candidateReader = candidateReader;
    }

    public static CompositionInputSnapshot Open(IEnumerable<string> paths) => Capture(paths, candidateReader: null);

    /// <summary>Captures every supplied intent file with candidate-only byte and count limits.</summary>
    public static CompositionInputSnapshot OpenForCandidate(IEnumerable<string> paths, CompositionFileReader? reader = null) =>
        Capture(paths, reader ?? new CompositionFileReader());

    private static CompositionInputSnapshot Capture(IEnumerable<string> paths, CompositionFileReader? candidateReader)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var totalBytes = 0;
        foreach (var path in paths)
        {
            var fullPath = FullPath(path);
            if (files.ContainsKey(fullPath))
                continue;

            try
            {
                if (candidateReader is not null && files.Count >= CompositionFileReader.MaximumFiles)
                    throw CompositionFileReader.LimitExceeded();
                var bytes = Read(fullPath, candidateReader, totalBytes);
                if (candidateReader is not null)
                    totalBytes += bytes.Length;
                files.Add(fullPath, bytes);
            }
            catch (CliRefusal)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                throw Unreadable();
            }
        }

        return new CompositionInputSnapshot(files, candidateReader);
    }

    public string ReadText(string path)
    {
        var fullPath = FullPath(path);
        if (!_files.TryGetValue(fullPath, out var bytes))
            throw Unreadable();

        try
        {
            using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch (Exception exception) when (exception is DecoderFallbackException or ArgumentException)
        {
            throw CliRefusal.Usage("composition-input-invalid", "A supplied composition input is not valid encoded JSON text.");
        }
    }

    public void VerifyUnchanged()
    {
        var totalBytes = 0;
        foreach (var (path, snapshot) in _files)
        {
            try
            {
                var current = Read(path, _candidateReader, totalBytes);
                if (_candidateReader is not null)
                    totalBytes += current.Length;
                if (!snapshot.AsSpan().SequenceEqual(current))
                    throw Changed();
            }
            catch (CliRefusal)
            {
                throw Changed();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                throw Changed();
            }
        }
    }

    private static byte[] Read(string path, CompositionFileReader? candidateReader, int totalBytes)
    {
        if (candidateReader is not null)
            return candidateReader.Read(path, Math.Min(CompositionFileReader.MaximumFileBytes,
                CompositionFileReader.MaximumContextBytes - totalBytes));
        CompositionFileReader.EnsureRegularFile(path);
        var bytes = File.ReadAllBytes(path);
        CompositionFileReader.EnsureRegularFile(path);
        return bytes;
    }

    private static string FullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw Unreadable();
        }
    }

    private static CliRefusal Unreadable() =>
        CliRefusal.Resolution("composition-input-unreadable", "A supplied composition input could not be read as a regular local file.");

    private static CliRefusal Changed() =>
        CliRefusal.Resolution("composition-input-changed", "A supplied composition input changed after review.");
}

/// <summary>Admits the one explicit private environment overlay without consulting ambient configuration.</summary>
internal static class ExplicitEnvironmentInput
{
    internal const int MaximumBytes = 1_048_576;
    internal const int MaximumEntries = 1_024;
    internal const int MaximumKeyBytes = 1_024;
    internal const int MaximumValueBytes = 65_536;

    private const int JsonMaximumDepth = 64;
    private const string PolicyPrefix = "candidate-environment-";
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        MaxDepth = JsonMaximumDepth,
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow
    };
    private static readonly string[] UnsupportedServicePrefixes =
    [
        "MYSQLCONNSTR_",
        "SQLAZURECONNSTR_",
        "SQLCONNSTR_",
        "CUSTOMCONNSTR_",
        "POSTGRESQLCONNSTR_",
        "APIHUBCONNSTR_",
        "DOCDBCONNSTR_",
        "EVENTHUBCONNSTR_",
        "NOTIFICATIONHUBCONNSTR_",
        "REDISCACHECONNSTR_",
        "SERVICEBUSCONNSTR_"
    ];

    internal static IReadOnlyDictionary<string, string> Parse(ReadOnlyMemory<byte> document)
    {
        if (document.Length > MaximumBytes)
            throw TooLarge();

        try
        {
            var utf8 = document.Span;
            if (utf8.StartsWith("\uFEFF"u8))
            {
                utf8 = utf8[3..];
                if (utf8.StartsWith("\uFEFF"u8))
                    throw Invalid();
            }

            var json = StrictUtf8.GetString(utf8);
            if (json.StartsWith('\uFEFF'))
                throw Invalid();

            using var parsed = JsonDocument.Parse(json, JsonOptions);
            return ParseRoot(parsed.RootElement);
        }
        catch (CliRefusal)
        {
            throw;
        }
        catch (DecoderFallbackException)
        {
            throw Invalid();
        }
        catch (JsonException)
        {
            throw Invalid();
        }
        catch (EncoderFallbackException)
        {
            throw Invalid();
        }
    }

    private static IReadOnlyDictionary<string, string> ParseRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw Invalid();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var hasVersion = false;
        var hasEntries = false;
        var version = default(JsonElement);
        var entries = default(JsonElement);
        foreach (var property in root.EnumerateObject())
        {
            if (!seen.Add(property.Name))
                throw Invalid();

            switch (property.Name)
            {
                case "version" when !hasVersion:
                    hasVersion = true;
                    version = property.Value;
                    break;
                case "entries" when !hasEntries:
                    hasEntries = true;
                    entries = property.Value;
                    break;
                default:
                    throw Invalid();
            }
        }

        if (!hasVersion || !hasEntries || version.ValueKind != JsonValueKind.Number || version.GetRawText() != "1" ||
            entries.ValueKind != JsonValueKind.Array)
            throw Invalid();

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var count = 0;
        foreach (var entry in entries.EnumerateArray())
        {
            if (++count > MaximumEntries)
                throw TooLarge();
            ParseEntry(entry, values);
        }

        return new ReadOnlyDictionary<string, string>(values);
    }

    private static void ParseEntry(JsonElement entry, IDictionary<string, string> values)
    {
        if (entry.ValueKind != JsonValueKind.Object)
            throw Invalid();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var hasKey = false;
        var hasValue = false;
        var keyElement = default(JsonElement);
        var valueElement = default(JsonElement);
        foreach (var property in entry.EnumerateObject())
        {
            if (!seen.Add(property.Name))
                throw Invalid();

            switch (property.Name)
            {
                case "key" when !hasKey:
                    hasKey = true;
                    keyElement = property.Value;
                    break;
                case "value" when !hasValue:
                    hasValue = true;
                    valueElement = property.Value;
                    break;
                default:
                    throw Invalid();
            }
        }

        if (!hasKey || !hasValue || keyElement.ValueKind != JsonValueKind.String ||
            valueElement.ValueKind != JsonValueKind.String)
            throw Invalid();

        var key = keyElement.GetString() ?? throw Invalid();
        var value = valueElement.GetString() ?? throw Invalid();
        ValidateString(keyElement, key, key: true);
        ValidateString(valueElement, value, key: false);

        if (StrictUtf8.GetByteCount(key) > MaximumKeyBytes || StrictUtf8.GetByteCount(value) > MaximumValueBytes)
            throw TooLarge();

        if (UnsupportedServicePrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            throw PrefixUnsupported();

        var normalized = key.Replace("__", ":", StringComparison.Ordinal);
        if (StrictUtf8.GetByteCount(normalized) > MaximumKeyBytes)
            throw TooLarge();

        if (!values.TryAdd(normalized, value))
            throw KeyCollision();
    }

    private static void ValidateString(JsonElement element, string value, bool key)
    {
        var raw = element.GetRawText();
        for (var index = 1; index < raw.Length - 1; index++)
        {
            if (raw[index] != '\\')
                continue;

            if (raw[index + 1] != 'u')
            {
                index++;
                continue;
            }

            if (!TryReadUnicodeEscape(raw, index, out var codeUnit))
                throw Invalid();
            if (char.IsHighSurrogate(codeUnit))
            {
                if (!TryReadUnicodeEscape(raw, index + 6, out var low) || !char.IsLowSurrogate(low))
                    throw Invalid();
                index += 11;
            }
            else if (char.IsLowSurrogate(codeUnit))
            {
                throw Invalid();
            }
            else
            {
                index += 5;
            }
        }

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                    throw Invalid();
                index++;
                continue;
            }

            if (char.IsLowSurrogate(character) || key && char.IsControl(character) || key && character == '=' ||
                !key && character == '\0')
                throw Invalid();
        }
    }

    private static bool TryReadUnicodeEscape(string raw, int slash, out char codeUnit)
    {
        codeUnit = default;
        if (slash < 0 || slash + 5 >= raw.Length || raw[slash] != '\\' || raw[slash + 1] != 'u')
            return false;

        var value = 0;
        for (var index = slash + 2; index <= slash + 5; index++)
        {
            var digit = raw[index] switch
            {
                >= '0' and <= '9' => raw[index] - '0',
                >= 'a' and <= 'f' => raw[index] - 'a' + 10,
                >= 'A' and <= 'F' => raw[index] - 'A' + 10,
                _ => -1
            };
            if (digit < 0)
                return false;
            value = (value << 4) | digit;
        }

        codeUnit = (char)value;
        return true;
    }

    private static CliRefusal Invalid() =>
        CliRefusal.Usage(PolicyPrefix + "input-invalid", "The explicit environment input is invalid.");

    private static CliRefusal TooLarge() =>
        CliRefusal.Usage(PolicyPrefix + "input-too-large", "The explicit environment input exceeds the supported size limit.");

    private static CliRefusal KeyCollision() =>
        CliRefusal.Usage(PolicyPrefix + "key-collision", "The explicit environment input contains colliding keys.");

    private static CliRefusal PrefixUnsupported() =>
        CliRefusal.Usage(PolicyPrefix + "prefix-unsupported", "The explicit environment input contains an unsupported service prefix.");
}
