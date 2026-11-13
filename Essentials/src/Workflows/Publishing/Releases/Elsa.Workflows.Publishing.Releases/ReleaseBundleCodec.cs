using System.IO.Compression;
using System.Text.Json;
using Elsa.Workflows.Publishing.Releases.Core;
using static Elsa.Workflows.Publishing.Releases.ReleaseManifest;

namespace Elsa.Workflows.Publishing.Releases;

internal sealed class ReleaseBundleCodec : IReleaseBundleCodec
{
    public IReleaseBundleTransport Validate(ReadOnlyMemory<byte> manifestUtf8,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> blobs, ReleaseBundleLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(blobs);
        return Capture(manifestUtf8, blobs, new Dictionary<string, ReadOnlyMemory<byte>>(), Limits(limits));
    }

    public async ValueTask<IReleaseBundleTransport> ReadZipAsync(Stream input, ReleaseBundleLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var bounds = Limits(limits);
        try
        {
            var archiveBytes = await ReadBoundedAsync(input, bounds.MaxArchiveBytes, cancellationToken);
            // Check headers the high-level archive API does not expose: encryption and link-bearing Unix extras.
            var names = ReleaseZipMetadata.Validate(archiveBytes, bounds.MaxEntries);
            using var archive = new ZipArchive(new MemoryStream(archiveBytes, writable: false), ZipArchiveMode.Read);
            if (archive.Entries.Count != names.Count) throw Refuse("invalid_archive");
            var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long expanded = 0;
            for (var i = 0; i < archive.Entries.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = archive.Entries[i];
                if (entry.FullName != names[i]) throw Refuse("ambiguous_archive_name");
                var (path, directoryEntry) = ReleaseTransportPaths.Validate(entry.FullName);
                if (!seen.Add(path)) throw Refuse("duplicate_transport_path");
                if (directoryEntry)
                {
                    if (entry.Length != 0) throw Refuse("invalid_directory_entry");
                    continue;
                }
                var maximum = path == "manifest.json" ? bounds.MaxManifestBytes : bounds.MaxBlobBytes;
                if (entry.Length > maximum || entry.Length > bounds.MaxExpandedBytes - expanded) throw Refuse("byte_limit_exceeded");
                await using var stream = entry.Open();
                var bytes = await ReadBoundedAsync(stream, (int)Math.Min(maximum, bounds.MaxExpandedBytes - expanded), cancellationToken);
                if (bytes.LongLength != entry.Length) throw Refuse("entry_length_mismatch");
                expanded += bytes.LongLength;
                entries.Add(path, bytes);
            }
            return FromEntries(entries, bounds);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException)
        { throw Refuse("invalid_archive"); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        { throw Refuse("archive_read_failed"); }
    }

    public async ValueTask<IReleaseBundleTransport> ReadDirectoryAsync(string directory, ReleaseBundleLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var bounds = Limits(limits);
        try
        {
            using var reader = ReleaseDirectoryReader.Open(directory);
            var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Stack<string>(); pending.Push(directory);
            long expanded = 0;
            var count = 0;
            while (pending.TryPop(out var current))
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(current))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++count > bounds.MaxEntries) throw Refuse("entry_limit_exceeded");
                    var attributes = File.GetAttributes(path);
                    if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0) throw Refuse("unsafe_directory_entry");
                    var isDirectory = (attributes & FileAttributes.Directory) != 0;
                    var relative = Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/');
                    var (name, _) = ReleaseTransportPaths.Validate(relative + (isDirectory ? "/" : ""));
                    if (!seen.Add(name)) throw Refuse("duplicate_transport_path");
                    if (isDirectory) { pending.Push(path); continue; }
                    await using var stream = reader.OpenRegularFile(name);
                    var maximum = name == "manifest.json" ? bounds.MaxManifestBytes : bounds.MaxBlobBytes;
                    var bytes = await ReadBoundedAsync(stream, (int)Math.Min(maximum, bounds.MaxExpandedBytes - expanded), cancellationToken);
                    expanded += bytes.LongLength;
                    entries.Add(name, bytes);
                }
            }
            return FromEntries(entries, bounds);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        { throw Refuse("directory_read_failed"); }
    }

    public async ValueTask WriteZipAsync(IReleaseBundleTransport bundle, Stream output, ReleaseBundleLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundle); ArgumentNullException.ThrowIfNull(output);
        var bounds = Limits(limits);
        var manifest = bundle.GetManifestBytes();
        if (manifest.Length > bounds.MaxManifestBytes) throw Refuse("byte_limit_exceeded");
        var (_, members) = Parse(manifest, bounds);
        var blobs = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        foreach (var member in members)
            if (!blobs.ContainsKey(member.PayloadDigest)) blobs.Add(member.PayloadDigest, bundle.GetBlobBytes(member.PayloadDigest));
        var attachments = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        foreach (var path in bundle.AttachmentPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attachments.Count >= bounds.MaxEntries) throw Refuse("entry_limit_exceeded");
            if (!attachments.TryAdd(path, bundle.GetAttachmentBytes(path))) throw Refuse("duplicate_transport_path");
        }
        var captured = Capture(manifest, blobs, attachments, bounds);
        if (captured.Digest != bundle.Digest) throw Refuse("digest_mismatch");
        try
        {
            using var boundedOutput = new BoundedArchiveOutput(output, bounds.MaxArchiveBytes);
            using var archive = new ZipArchive(boundedOutput, ZipArchiveMode.Create, leaveOpen: true);
            await WriteAsync("manifest.json", captured.GetManifestBytes());
            foreach (var digest in members.Select(x => x.PayloadDigest).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
                await WriteAsync("blobs/" + digest[7..] + ".json", captured.GetBlobBytes(digest));
            foreach (var path in captured.AttachmentPaths) await WriteAsync(path, captured.GetAttachmentBytes(path));

            async ValueTask WriteAsync(string path, byte[] bytes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
                entry.LastWriteTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                await using var stream = entry.Open();
                await stream.WriteAsync(bytes, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        { throw Refuse("archive_write_failed"); }
    }

    private static IReleaseBundleTransport FromEntries(Dictionary<string, byte[]> entries, ReleaseBundleLimits bounds)
    {
        if (!entries.TryGetValue("manifest.json", out var manifest)) throw Refuse("missing_manifest");
        var blobs = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        var attachments = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        foreach (var (path, bytes) in entries)
        {
            if (path == "manifest.json") continue;
            if (path.StartsWith("blobs/", StringComparison.Ordinal)) blobs.Add("sha256:" + path[6..^5], bytes);
            else attachments.Add(path, bytes);
        }
        return Capture(manifest, blobs, attachments, bounds);
    }

    private static IReleaseBundleTransport Capture(ReadOnlyMemory<byte> manifestInput,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> blobInput, IReadOnlyDictionary<string, ReadOnlyMemory<byte>> attachmentInput,
        ReleaseBundleLimits bounds)
    {
        if (manifestInput.Length > bounds.MaxManifestBytes || manifestInput.Length > bounds.MaxExpandedBytes) throw Refuse("byte_limit_exceeded");
        var manifestBytes = manifestInput.ToArray();
        var (manifest, members) = Parse(manifestBytes, bounds);
        var required = members.Select(x => x.PayloadDigest).ToHashSet(StringComparer.Ordinal);
        var blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long expanded = manifestBytes.LongLength;
        if (1L + blobInput.Count + attachmentInput.Count > bounds.MaxEntries) throw Refuse("entry_limit_exceeded");
        foreach (var (digest, input) in blobInput)
        {
            CheckDigest(digest);
            if (!required.Contains(digest)) throw Refuse("unlisted_blob");
            if (input.Length > bounds.MaxBlobBytes || input.Length > bounds.MaxExpandedBytes - expanded) throw Refuse("byte_limit_exceeded");
            var bytes = input.ToArray();
            expanded += bytes.LongLength;
            if (Hash(bytes) != digest) throw Refuse("blob_digest_mismatch");
            if (bytes.Length == 0 || bytes[^1] is 10 or 13) throw Refuse("invalid_blob_encoding");
            CheckJson(bytes, bounds.MaxJsonDepth);
            if (!blobs.TryAdd(digest, bytes)) throw Refuse("duplicate_blob");
        }
        foreach (var member in members)
        {
            if (!blobs.TryGetValue(member.PayloadDigest, out var bytes)) throw Refuse("missing_blob");
            if (bytes.LongLength != member.ByteLength) throw Refuse("blob_length_mismatch");
        }
        var attachments = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, input) in attachmentInput)
        {
            var (name, directory) = ReleaseTransportPaths.Validate(path);
            if (directory || !name.StartsWith("attachments/", StringComparison.Ordinal)) throw Refuse("invalid_attachment_path");
            if (!paths.Add(name)) throw Refuse("duplicate_transport_path");
            if (input.Length > bounds.MaxBlobBytes || input.Length > bounds.MaxExpandedBytes - expanded) throw Refuse("byte_limit_exceeded");
            var bytes = input.ToArray(); expanded += bytes.LongLength;
            attachments.Add(name, bytes);
        }
        return new CapturedTransport(HashManifest(manifest), manifest, members, manifestBytes, blobs, attachments);
    }

    private static ReleaseBundleLimits Limits(ReleaseBundleLimits? limits)
    {
        var value = limits ?? new();
        if (value.MaxManifestBytes <= 0 || value.MaxBlobBytes <= 0 || value.MaxExpandedBytes <= 0 || value.MaxArchiveBytes <= 0 ||
            value.MaxEntries <= 0 || value.MaxMembers <= 0 || value.MaxEdges <= 0 || value.MaxJsonDepth is <= 0 or > 256 || value.MaxDependencyDepth is <= 0 or > 256)
            throw new ArgumentOutOfRangeException(nameof(limits));
        return value;
    }

    private static async ValueTask<byte[]> ReadBoundedAsync(Stream input, int maximum, CancellationToken token)
    {
        if (maximum < 0) throw Refuse("byte_limit_exceeded");
        using var output = new MemoryStream();
        var buffer = new byte[Math.Min(16 * 1024, Math.Max(1, maximum))];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, maximum - output.Length + 1)), token);
            if (read == 0) return output.ToArray();
            if (read > maximum - output.Length) throw Refuse("byte_limit_exceeded");
            output.Write(buffer, 0, read);
        }
    }

    private sealed class CapturedTransport(string digest, JsonElement manifest, ReleaseBundleMember[] members, byte[] manifestBytes,
        Dictionary<string, byte[]> blobs, Dictionary<string, byte[]> attachments) : IReleaseBundleTransport
    {
        public string Digest { get; } = digest;
        public JsonElement Manifest { get; } = manifest;
        public IReadOnlyList<ReleaseBundleMember> Members { get; } = Array.AsReadOnly(members);
        public IReadOnlyList<string> AttachmentPaths { get; } = Array.AsReadOnly(attachments.Keys.Order(StringComparer.Ordinal).ToArray());
        public byte[] GetManifestBytes() => (byte[])manifestBytes.Clone();
        public byte[] GetBlobBytes(string payloadDigest) => (byte[])blobs[payloadDigest].Clone();
        public byte[] GetAttachmentBytes(string path) => (byte[])attachments[path].Clone();
    }

    private sealed class BoundedArchiveOutput(Stream output, int maximum) : Stream
    {
        private long _written;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => output.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => output.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => output.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Ensure(buffer.Length); output.Write(buffer); _written += buffer.Length;
        }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Ensure(buffer.Length); await output.WriteAsync(buffer, cancellationToken); _written += buffer.Length;
        }
        private void Ensure(int count) { if (count > maximum - _written) throw Refuse("byte_limit_exceeded"); }
    }
}
