using System.Security.Cryptography;
using System.Text;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Elsa.Etl.Runtime.Output;

/// <summary>Publishes immutable chunks on a host-approved filesystem with atomic create-only publication.</summary>
/// <remarks>Only the accepted manifest makes a physical chunk authoritative.</remarks>
internal sealed class FileChunkStore : IFileChunkPublisher
{
    private readonly string _extension;

    public FileChunkStore(string extension = ".jsonl")
    {
        // Only feature-owned formats; never an authored path or suffix.
        if (extension is not (".jsonl" or ".csv" or ".index" or ".json" or ".page"))
            throw new ArgumentException("Unsupported ETL chunk format.", nameof(extension));
        _extension = extension;
    }

    internal string KeyFor(string effectIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effectIdentity);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(effectIdentity))).ToLowerInvariant() + _extension;
    }

    /// <summary>Reads one exact immutable chunk only after checking its finite physical bound.</summary>
    /// <remarks>Only a missing file under an existing root is reported as absent. An unavailable root is an I/O failure.</remarks>
    internal async ValueTask<byte[]?> ReadAsync(string outputRoot, string effectIdentity, long maxBytes,
        long? expectedByteLength = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(effectIdentity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        if (expectedByteLength is < 0 || expectedByteLength is { } expectedLength && expectedLength > maxBytes)
            throw new EtlEffectException("etl.output-read-conflict", "A published ETL object has an invalid size.");
        cancellationToken.ThrowIfCancellationRequested();

        var path = Path.Combine(outputRoot, KeyFor(effectIdentity));
        FileStream file;
        try
        {
            file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new EtlEffectException("etl.output-read-failed", "The ETL object could not be read.", exception);
        }

        await using (file)
        {
            try
            {
                var length = file.Length;
                if (length > maxBytes || length > int.MaxValue || expectedByteLength is { } expected && length != expected)
                    throw new EtlEffectException("etl.output-read-conflict", "A published ETL object has an invalid size.");

                var bytes = new byte[(int)length];
                await file.ReadExactlyAsync(bytes, cancellationToken);
                var extra = new byte[1];
                if (await file.ReadAsync(extra, cancellationToken) != 0)
                    throw new EtlEffectException("etl.output-read-conflict", "A published ETL object exceeds its declared size.");
                return bytes;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (EtlEffectException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new EtlEffectException("etl.output-read-failed", "The ETL object could not be read.", exception);
            }
        }
    }

    public async ValueTask<EtlChunk> PublishAsync(
        string outputRoot,
        string effectIdentity,
        long maxBytes,
        Func<Stream, CancellationToken, ValueTask> write,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(effectIdentity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        ArgumentNullException.ThrowIfNull(write);
        cancellationToken.ThrowIfCancellationRequested();

        // An effect identity is data, never an authored path. Staging and final files share a
        // directory so the host's declared create-only publication cannot cross filesystems.
        var key = KeyFor(effectIdentity);
        var finalPath = Path.Combine(outputRoot, key);
        var stagingPath = Path.Combine(outputRoot, $".{key}.{Guid.NewGuid():N}.staging");
        try
        {
            Directory.CreateDirectory(outputRoot);
            await using (var file = new FileStream(stagingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                using var bounded = new BoundedWriteStream(file, maxBytes);
                await write(bounded, cancellationToken);
                await file.FlushAsync(cancellationToken);
                file.Flush(flushToDisk: true);
            }

            var candidate = await ReadDescriptorAsync(stagingPath, key, maxBytes, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                PublishCreateOnly(stagingPath, finalPath);
            }
            catch (IOException) when (File.Exists(finalPath))
            {
                // A competing attempt may have published the same effect. Never overwrite it,
                // and compare real bytes, not a stale sidecar or the filename alone.
                var existing = await ReadDescriptorAsync(finalPath, key, maxBytes, cancellationToken);
                if (existing != candidate)
                    throw new EtlEffectException("etl.effect-conflict", "A published ETL effect has conflicting content.");
            }

            return candidate;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new EtlEffectException("etl.output-write-failed", "The ETL output could not be published.", exception);
        }
        finally
        {
            // The writer has unwound before cleanup. A leftover from another attempt is never
            // removed here; staging is not visible as an accepted or complete chunk.
            try { File.Delete(stagingPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private static void PublishCreateOnly(string stagingPath, string finalPath)
    {
        if (OperatingSystem.IsWindows())
        {
            File.Move(stagingPath, finalPath, overwrite: false);
            return;
        }
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
        {
            // File.Move(false) on Unix may check destination existence then call replacing
            // rename. link() creates the complete inode's name atomically or refuses it.
            // No copy fallback: unsupported filesystems cannot provide this binding contract.
            if (Link(stagingPath, finalPath) != 0)
                throw new IOException("Atomic ETL file publication failed.", new Win32Exception(Marshal.GetLastPInvokeError()));
            return;
        }
        throw new EtlEffectException("etl.output-publication-unsupported", "Atomic ETL publication is unavailable on this platform.");
    }

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link([MarshalAs(UnmanagedType.LPUTF8Str)] string existingPath, [MarshalAs(UnmanagedType.LPUTF8Str)] string newPath);

    private static async ValueTask<EtlChunk> ReadDescriptorAsync(string path, string key, long maxBytes, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > maxBytes)
            throw new EtlEffectException("etl.effect-conflict", "A published ETL effect exceeds its permitted size.");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[4096];
        long length = 0;
        while (true)
        {
            var count = await stream.ReadAsync(buffer, ct);
            if (count == 0)
                break;
            if (count > maxBytes - length)
                throw new EtlEffectException("etl.effect-conflict", "A published ETL effect exceeds its permitted size.");
            hash.AppendData(buffer.AsSpan(0, count));
            length += count;
        }

        return new EtlChunk(key, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), length);
    }
}
