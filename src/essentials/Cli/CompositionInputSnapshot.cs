using System.Text;
using Elsa.Cli.Worker;

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

/// <summary>Frontend adapter for the shared EF-free raw admission contract.</summary>
internal static class ExplicitEnvironmentInput
{
    internal static IReadOnlyDictionary<string, string> Parse(ReadOnlyMemory<byte> document)
    {
        try { return WorkerContract.ParseEnvironmentInputDocument(document); }
        catch (WorkerRefusal refusal)
        { throw CliRefusal.Usage(refusal.Code, refusal.Message); }
    }
}
