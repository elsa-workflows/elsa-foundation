using System.Text;
using Elsa.Cli.Worker;

namespace Elsa.Cli;

/// <summary>Captures supplied composition inputs once and detects edits before reviewed publication.</summary>
public sealed class CompositionInputSnapshot : IDisposable
{
    private readonly IReadOnlyDictionary<string, byte[]> _files;
    private readonly CompositionFileReader? _candidateReader;
    private readonly object _lifecycleGate = new();
    private bool _disposed;

    private CompositionInputSnapshot(IReadOnlyDictionary<string, byte[]> files, CompositionFileReader? candidateReader)
    {
        _files = files;
        _candidateReader = candidateReader;
    }

    public static CompositionInputSnapshot Open(IEnumerable<string> paths) => Capture(paths, candidateReader: null, environmentInputPath: null);

    /// <summary>Captures every supplied intent file with candidate-only byte and count limits.</summary>
    public static CompositionInputSnapshot OpenForCandidate(IEnumerable<string> paths, CompositionFileReader? reader = null) =>
        Capture(paths, reader ?? new CompositionFileReader(), environmentInputPath: null);

    /// <summary>Captures candidate inputs and one explicit environment document without rereading that document for admission.</summary>
    internal static CompositionInputSnapshot OpenForCandidateWithEnvironmentInput(IEnumerable<string> paths,
        string environmentInputPath, CompositionFileReader? reader = null) =>
        Capture(paths, reader ?? new CompositionFileReader(), environmentInputPath);

    internal static string NormalizePath(string path) => FullPath(path);

    private static CompositionInputSnapshot Capture(IEnumerable<string> paths, CompositionFileReader? candidateReader,
        string? environmentInputPath)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var environmentFullPath = environmentInputPath is null ? null : FullPath(environmentInputPath);
        var totalBytes = 0;
        try
        {
            foreach (var path in paths)
            {
                var fullPath = FullPath(path);
                if (files.ContainsKey(fullPath))
                    continue;

                byte[]? bytes = null;
                var environmentReadCompleted = false;
                try
                {
                    if (candidateReader is not null && files.Count >= CompositionFileReader.MaximumFiles)
                        throw CompositionFileReader.LimitExceeded();
                    var isEnvironmentInput = candidateReader is not null && environmentFullPath == fullPath;
                    bytes = isEnvironmentInput
                        ? candidateReader!.Read(fullPath, CompositionFileReader.MaximumFileBytes)
                        : Read(fullPath, candidateReader, totalBytes);
                    environmentReadCompleted = isEnvironmentInput;
                    if (candidateReader is not null)
                    {
                        if (totalBytes > CompositionFileReader.MaximumContextBytes - bytes.Length)
                            throw CompositionFileReader.LimitExceeded();
                        totalBytes += bytes.Length;
                    }
                    files.Add(fullPath, bytes);
                    bytes = null;
                }
                catch (CliRefusal refusal) when (candidateReader is not null && environmentFullPath == fullPath &&
                                                 files.Count < CompositionFileReader.MaximumFiles &&
                                                 !environmentReadCompleted &&
                                                 refusal.Code == "candidate-capture-invalid" &&
                                                 totalBytes <= CompositionFileReader.MaximumContextBytes)
                {
                    throw CliRefusal.Usage("candidate-environment-input-too-large",
                        "The explicit environment input exceeds the supported size limit.");
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    throw Unreadable();
                }
                finally
                {
                    if (bytes is not null)
                        Array.Clear(bytes);
                }
            }
        }
        catch
        {
            ClearOwned(files);
            throw;
        }

        return new CompositionInputSnapshot(files, candidateReader);
    }

    public string ReadText(string path)
    {
        lock (_lifecycleGate)
        {
            ThrowIfDisposed();
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
    }

    /// <summary>Returns a defensive copy of one captured file for a single in-memory admission operation.</summary>
    internal byte[] ReadBytes(string path)
    {
        lock (_lifecycleGate)
        {
            ThrowIfDisposed();
            var fullPath = FullPath(path);
            if (!_files.TryGetValue(fullPath, out var bytes))
                throw Unreadable();
            return bytes.ToArray();
        }
    }

    public void VerifyUnchanged()
    {
        lock (_lifecycleGate)
        {
            ThrowIfDisposed();
            VerifyUnchangedCore();
        }
    }

    private void VerifyUnchangedCore()
    {
        var totalBytes = 0;
        foreach (var (path, snapshot) in _files)
        {
            byte[]? current = null;
            try
            {
                var observed = Read(path, _candidateReader, totalBytes);
                current = observed;
                if (_candidateReader is not null)
                    totalBytes += observed.Length;
                if (!snapshot.AsSpan().SequenceEqual(observed))
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
            finally
            {
                if (current is not null)
                    Array.Clear(current);
            }
        }
    }

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (_disposed)
                return;

            _disposed = true;
            ClearOwned(_files);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw CliRefusal.Usage("candidate-capture-invalid", "The captured candidate is no longer available.");
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

    private static void ClearOwned(IReadOnlyDictionary<string, byte[]> files)
    {
        foreach (var bytes in files.Values)
            Array.Clear(bytes);
    }
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
