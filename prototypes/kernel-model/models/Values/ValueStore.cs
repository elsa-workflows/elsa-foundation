using System.Security.Cryptography;

namespace Kernel.Values;

public sealed record ValueRef(string Sha256, int Length)
{
    public static ValueRef For(byte[] bytes) => new(Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length);
    public void Verify(byte[] bytes)
    {
        if (bytes.Length != Length || For(bytes).Sha256 != Sha256)
            throw new InvalidDataException("Stored value length or SHA-256 did not match the reference.");
    }
}

public interface IValueStore
{
    Task<ValueRef> PutAsync(byte[] bytes, CancellationToken cancellationToken);
    Task<byte[]> ReadAsync(ValueRef reference, CancellationToken cancellationToken);
}

/// <summary>One synthetic namespace per directory. Publish by atomic rename only after a complete write and flush.</summary>
public sealed class DirectoryValues(string directory) : IValueStore
{
    public int Puts { get; private set; }
    public int Reads { get; private set; }

    private string FileFor(ValueRef reference)
    {
        if (reference.Sha256 is not { Length: 64 } key || key.Any(c => !char.IsAsciiHexDigitUpper(c)) || reference.Length < 0)
            throw new InvalidDataException("Malformed value reference.");
        return Path.Combine(directory, key);
    }

    public async Task<ValueRef> PutAsync(byte[] bytes, CancellationToken ct)
    {
        Puts++;
        var reference = ValueRef.For(bytes);
        var destination = FileFor(reference);
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            if (!File.Exists(destination))
            {
                await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await file.WriteAsync(bytes, ct);
                    file.Flush(flushToDisk: true);
                }
                try { File.Move(path, destination); }
                catch (IOException) when (File.Exists(destination)) { /* A second writer published the same digest. */ }
            }
            await ReadAsync(reference, ct);  // also validate an object which won a concurrent put
            return reference;
        }
        finally { File.Delete(path); }
    }

    public async Task<byte[]> ReadAsync(ValueRef reference, CancellationToken ct)
    {
        Reads++;
        var bytes = await File.ReadAllBytesAsync(FileFor(reference), ct);
        reference.Verify(bytes);
        return bytes;
    }
}
