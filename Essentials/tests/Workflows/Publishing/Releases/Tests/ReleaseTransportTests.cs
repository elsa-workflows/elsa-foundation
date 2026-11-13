using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using Elsa.Workflows.Publishing.Releases.Core;
using Xunit;

namespace Elsa.Workflows.Publishing.Releases.Tests;

public sealed class ReleaseTransportTests
{
    private readonly ReleaseBundleCodec _codec = new();

    [Fact]
    public async Task Zip_round_trip_preserves_original_blob_and_manifest_bytes_and_attachments()
    {
        var fixture = new ReleaseBundleFixture();
        var input = fixture.Zip(("attachments/report.json", "{\"failed\":true}"u8.ToArray(), 0));
        var captured = await _codec.ReadZipAsync(new MemoryStream(input));
        Assert.Equal(fixture.Validate().Digest, captured.Digest);
        Assert.Equal(fixture.Bytes, captured.GetManifestBytes());
        foreach (var (digest, bytes) in fixture.Blobs) Assert.Equal(bytes.ToArray(), captured.GetBlobBytes(digest));
        Assert.Equal(new[] { "attachments/report.json" }, captured.AttachmentPaths);
        var attachment = captured.GetAttachmentBytes("attachments/report.json"); attachment[0] = 0;
        Assert.Equal("{\"failed\":true}"u8.ToArray(), captured.GetAttachmentBytes("attachments/report.json"));
        using var output = new MemoryStream();
        await _codec.WriteZipAsync(captured, output);
        var reopened = await _codec.ReadZipAsync(new MemoryStream(output.ToArray()));
        Assert.Equal(captured.Digest, reopened.Digest);
        Assert.Equal(fixture.Bytes, reopened.GetManifestBytes());
        Assert.Equal(captured.GetAttachmentBytes("attachments/report.json"), reopened.GetAttachmentBytes("attachments/report.json"));
    }

    [Fact]
    public void Expanded_parts_validate_without_recompression_and_snapshot_opaque_attachments()
    {
        var fixture = new ReleaseBundleFixture(); var bytes = new byte[] { 255, 0, 10 };
        var attachments = new Dictionary<string, ReadOnlyMemory<byte>> { ["attachments/report.bin"] = bytes };
        IReleaseBundleCodec codec = _codec;
        var captured = codec.ValidateParts(fixture.Bytes, fixture.Blobs, attachments, new() { MaxArchiveBytes = 1 });
        Assert.Equal(codec.Validate(fixture.Bytes, fixture.Blobs, null).Digest, captured.Digest);
        bytes[0] = 0; attachments.Clear();
        Assert.Equal(new[] { "attachments/report.bin" }, captured.AttachmentPaths);
        var returned = captured.GetAttachmentBytes("attachments/report.bin"); returned[0] = 0;
        Assert.Equal(new byte[] { 255, 0, 10 }, captured.GetAttachmentBytes("attachments/report.bin"));
    }

    [Theory]
    [InlineData("../escape", "unsafe_transport_path")]
    [InlineData("manifest.json", "invalid_attachment_path")]
    [InlineData("blobs/0000000000000000000000000000000000000000000000000000000000000000.json", "invalid_attachment_path")]
    [InlineData("attachments/", "invalid_attachment_path")]
    public void Expanded_attachment_paths_are_checked_without_archive_encoding(string path, string expected)
    {
        var fixture = new ReleaseBundleFixture();
        Assert.Equal(expected, Assert.Throws<ReleaseBundleFormatException>(() => _codec.ValidateParts(fixture.Bytes, fixture.Blobs,
            new Dictionary<string, ReadOnlyMemory<byte>> { [path] = "opaque"u8.ToArray() }, null)).Code);
    }

    [Fact]
    public void Expanded_parts_check_attachment_count_bytes_collisions_and_blob_hash()
    {
        var fixture = new ReleaseBundleFixture();
        var attachments = new Dictionary<string, ReadOnlyMemory<byte>> { ["attachments/A"] = new byte[128] };
        Assert.Equal("entry_limit_exceeded", Assert.Throws<ReleaseBundleFormatException>(() => _codec.ValidateParts(fixture.Bytes, fixture.Blobs, attachments, new() { MaxEntries = 3 })).Code);
        Assert.Equal("byte_limit_exceeded", Assert.Throws<ReleaseBundleFormatException>(() => _codec.ValidateParts(fixture.Bytes, fixture.Blobs, attachments, new() { MaxBlobBytes = 127 })).Code);
        var total = fixture.Bytes.Length + fixture.Blobs.Sum(x => x.Value.Length) + 127;
        Assert.Equal("byte_limit_exceeded", Assert.Throws<ReleaseBundleFormatException>(() => _codec.ValidateParts(fixture.Bytes, fixture.Blobs, attachments, new() { MaxExpandedBytes = total })).Code);
        attachments.Add("attachments/a", new byte[1]);
        Assert.Equal("duplicate_transport_path", Assert.Throws<ReleaseBundleFormatException>(() => _codec.ValidateParts(fixture.Bytes, fixture.Blobs, attachments, null)).Code);
        attachments.Clear();
        var key = fixture.Blobs.Keys.First(); var tampered = fixture.Blobs[key].ToArray(); tampered[0] = (byte)'['; fixture.Blobs[key] = tampered;
        Assert.Equal("blob_digest_mismatch", Assert.Throws<ReleaseBundleFormatException>(() => _codec.ValidateParts(fixture.Bytes, fixture.Blobs, attachments, null)).Code);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("C:/absolute")]
    [InlineData("blobs\\bad.json")]
    [InlineData("attachments//empty")]
    [InlineData("attachments/./dot")]
    [InlineData("attachments/../parent")]
    [InlineData("attachments/trailing.")]
    public async Task Archive_paths_cannot_escape_or_be_reinterpreted(string path)
    {
        var fixture = new ReleaseBundleFixture();
        var failure = await Assert.ThrowsAsync<ReleaseBundleFormatException>(() => _codec.ReadZipAsync(new MemoryStream(fixture.Zip((path, "{}"u8.ToArray(), 0)))).AsTask());
        Assert.Equal("unsafe_transport_path", failure.Code);
        Assert.DoesNotContain(path, failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("manifest.json", "manifest.json")]
    [InlineData("attachments/A.json", "attachments/a.json")]
    public async Task Duplicate_or_case_colliding_archive_entries_refuse(string first, string second)
    {
        var fixture = new ReleaseBundleFixture();
        var entries = first == "manifest.json"
            ? new[] { (first, fixture.Bytes, 0), (second, fixture.Bytes, 0) }
            : new[] { ("manifest.json", fixture.Bytes, 0), (first, "{}"u8.ToArray(), 0), (second, "{}"u8.ToArray(), 0) };
        var failure = await Assert.ThrowsAsync<ReleaseBundleFormatException>(() => _codec.ReadZipAsync(new MemoryStream(ReleaseBundleFixture.Archive(entries))).AsTask());
        Assert.Equal("duplicate_transport_path", failure.Code);
    }

    [Fact]
    public async Task Unix_symlink_and_device_entries_refuse_before_content_is_read()
    {
        foreach (var mode in new[] { 0xa1ff, 0x21ff })
        {
            var fixture = new ReleaseBundleFixture();
            var bytes = fixture.Zip(("attachments/link", "/outside"u8.ToArray(), unchecked(mode << 16)));
            Assert.Equal("archive_link_or_device", (await Assert.ThrowsAsync<ReleaseBundleFormatException>(() => _codec.ReadZipAsync(new MemoryStream(bytes)).AsTask())).Code);
        }
    }

    [Fact]
    public async Task Encrypted_flags_and_link_bearing_unix_extra_metadata_refuse()
    {
        var fixture = new ReleaseBundleFixture();
        var encrypted = fixture.Zip();
        foreach (var at in Headers(encrypted, 0x04034b50)) encrypted[at + 6] |= 1;
        foreach (var at in Headers(encrypted, 0x02014b50)) encrypted[at + 8] |= 1;
        Assert.Equal("encrypted_archive", (await Assert.ThrowsAsync<ReleaseBundleFormatException>(() => _codec.ReadZipAsync(new MemoryStream(encrypted)).AsTask())).Code);

        var original = fixture.Zip();
        var central = Headers(original, 0x02014b50).First();
        // Insert one empty Unix extra into the central record; its existence alone can carry link semantics.
        var insert = central + 46 + BinaryPrimitives.ReadUInt16LittleEndian(original.AsSpan(central + 28));
        var withUnix = new byte[original.Length + 4];
        original.AsSpan(0, insert).CopyTo(withUnix);
        withUnix[insert] = 0x0d; withUnix[insert + 1] = 0;
        original.AsSpan(insert).CopyTo(withUnix.AsSpan(insert + 4));
        BinaryPrimitives.WriteUInt16LittleEndian(withUnix.AsSpan(central + 30), 4);
        var end = Headers(withUnix, 0x06054b50).Last();
        var size = BinaryPrimitives.ReadUInt32LittleEndian(withUnix.AsSpan(end + 12));
        BinaryPrimitives.WriteUInt32LittleEndian(withUnix.AsSpan(end + 12), size + 4);
        Assert.Equal("unsupported_archive_metadata", (await Assert.ThrowsAsync<ReleaseBundleFormatException>(() => _codec.ReadZipAsync(new MemoryStream(withUnix)).AsTask())).Code);
    }

    [Fact]
    public async Task Decompression_bounds_do_not_trust_advertised_uncompressed_lengths()
    {
        var fixture = new ReleaseBundleFixture(1);
        var large = Encoding.UTF8.GetBytes("{\"value\":\"" + new string('a', 4096) + "\"}");
        var digest = ReleaseManifest.Hash(large); fixture.Blobs.Clear(); fixture.Blobs.Add(digest, large);
        fixture.Manifest["members"]![0]!["payloadDigest"] = digest; fixture.Manifest["members"]![0]!["byteLength"] = large.Length;
        var bytes = fixture.Zip();
        var blobCentral = Headers(bytes, 0x02014b50).Last();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(blobCentral + 24), 1);
        var failure = await Assert.ThrowsAsync<ReleaseBundleFormatException>(() => _codec.ReadZipAsync(new MemoryStream(bytes), new() { MaxBlobBytes = 128 }).AsTask());
        // The runtime decoder may truncate to its advertised length; the manifest's independent byte limit
        // still refuses the dishonest archive instead of accepting the truncated content.
        Assert.Equal("invalid_member_bounds", failure.Code);
    }

    [Fact]
    public async Task Archive_count_input_bytes_total_expansion_and_cancellation_are_bounded()
    {
        var fixture = new ReleaseBundleFixture(); var bytes = fixture.Zip();
        Assert.Equal("entry_limit_exceeded", (await Assert.ThrowsAsync<ReleaseBundleFormatException>(() => _codec.ReadZipAsync(new MemoryStream(bytes), new() { MaxEntries = 1 }).AsTask())).Code);
        Assert.Equal("byte_limit_exceeded", (await Assert.ThrowsAsync<ReleaseBundleFormatException>(() => _codec.ReadZipAsync(new MemoryStream(bytes), new() { MaxArchiveBytes = 20 }).AsTask())).Code);
        Assert.Equal("byte_limit_exceeded", (await Assert.ThrowsAsync<ReleaseBundleFormatException>(() => _codec.ReadZipAsync(new MemoryStream(bytes), new() { MaxExpandedBytes = fixture.Bytes.Length }).AsTask())).Code);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _codec.ReadZipAsync(new MemoryStream(bytes), cancellationToken: new(true)).AsTask());
        Assert.Equal("invalid_archive", (await Assert.ThrowsAsync<ReleaseBundleFormatException>(() => _codec.ReadZipAsync(new MemoryStream(bytes[..^5])).AsTask())).Code);
    }

    [Fact]
    public async Task Zip_writer_enforces_raw_archive_limit_and_keeps_caller_stream_open()
    {
        var fixture = new ReleaseBundleFixture();
        using var output = new MemoryStream();
        var failure = await Assert.ThrowsAsync<ReleaseBundleFormatException>(() => _codec.WriteZipAsync(fixture.Validate(), output, new() { MaxArchiveBytes = 20 }).AsTask());
        Assert.Equal("byte_limit_exceeded", failure.Code);
        Assert.True(output.CanWrite);
        Assert.True(output.Length <= 20);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("unsupported")]
    public async Task Input_stream_failures_are_sanitized(string kind)
    {
        using var input = new ThrowingTransportStream(TransportFailure(kind));
        var failure = await Assert.ThrowsAsync<ReleaseBundleFormatException>(() => _codec.ReadZipAsync(input).AsTask());
        Assert.Equal("archive_read_failed", failure.Code);
        Assert.Null(failure.InnerException);
        Assert.DoesNotContain("private-token", failure.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("io", false)]
    [InlineData("access", false)]
    [InlineData("unsupported", false)]
    [InlineData("io", true)]
    [InlineData("access", true)]
    [InlineData("unsupported", true)]
    public async Task Output_stream_failures_including_archive_finalization_are_sanitized(string kind, bool duringFinalization)
    {
        using var output = new ThrowingTransportStream(TransportFailure(kind), duringFinalization);
        var failure = await Assert.ThrowsAsync<ReleaseBundleFormatException>(() => _codec.WriteZipAsync(new ReleaseBundleFixture().Validate(), output).AsTask());
        Assert.Equal("archive_write_failed", failure.Code);
        Assert.Null(failure.InnerException);
        Assert.DoesNotContain("private-token", failure.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stream_cancellation_retains_its_exception_and_token(bool writing)
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var expected = new OperationCanceledException(cancellation.Token);
        using var stream = new ThrowingTransportStream(expected);
        var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writing
            ? _codec.WriteZipAsync(new ReleaseBundleFixture().Validate(), stream).AsTask()
            : _codec.ReadZipAsync(stream).AsTask());
        Assert.Same(expected, failure);
        Assert.Equal(cancellation.Token, failure.CancellationToken);
    }

    [Fact]
    public async Task Directory_round_trip_is_bounded_and_rejects_symbolic_and_hard_links()
    {
        var directory = Path.Combine(Path.GetTempPath(), "elsa-release-transport-" + Guid.NewGuid().ToString("N"));
        var outside = directory + "-outside";
        Directory.CreateDirectory(directory); Directory.CreateDirectory(outside);
        try
        {
            var fixture = new ReleaseBundleFixture(); fixture.WriteDirectory(directory);
            if (!(OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64) &&
                !(OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64))
            {
                Assert.Equal("directory_platform_unsupported", (await Assert.ThrowsAsync<ReleaseBundleFormatException>(() => _codec.ReadDirectoryAsync(directory).AsTask())).Code);
                return;
            }
            Assert.Equal(fixture.Validate().Digest, (await _codec.ReadDirectoryAsync(directory)).Digest);
            Assert.Equal("entry_limit_exceeded", (await Assert.ThrowsAsync<ReleaseBundleFormatException>(() => _codec.ReadDirectoryAsync(directory, new() { MaxEntries = 1 }).AsTask())).Code);
            var attachment = Path.Combine(directory, "attachments");
            Directory.CreateSymbolicLink(attachment, outside);
            Assert.Equal("unsafe_directory_entry", (await Assert.ThrowsAsync<ReleaseBundleFormatException>(() => _codec.ReadDirectoryAsync(directory).AsTask())).Code);
            Directory.Delete(attachment);
            var manifest = Path.Combine(directory, "manifest.json");
            var otherLink = Path.Combine(outside, "hard-link");
            Assert.Equal(0, Link(manifest, otherLink));
            Assert.Equal("unsafe_directory_entry", (await Assert.ThrowsAsync<ReleaseBundleFormatException>(() => _codec.ReadDirectoryAsync(directory).AsTask())).Code);
        }
        finally { Directory.Delete(directory, recursive: true); Directory.Delete(outside, recursive: true); }
    }

    private static IEnumerable<int> Headers(byte[] bytes, uint signature)
    {
        for (var i = 0; i <= bytes.Length - 4; i++)
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i, 4)) == signature) yield return i;
    }

    private static Exception TransportFailure(string kind) => kind switch
    {
        "io" => new IOException("https://private-token@example.invalid/archive"),
        "access" => new UnauthorizedAccessException("/private-token/archive"),
        "unsupported" => new NotSupportedException("private-token transport"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private sealed class ThrowingTransportStream(Exception failure, bool duringFinalization = false) : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromException<int>(failure);
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Check(buffer); base.Write(buffer);
        }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Check(buffer.Span); base.Write(buffer.Span); return ValueTask.CompletedTask;
        }
        private void Check(ReadOnlySpan<byte> buffer)
        {
            if (!duringFinalization || buffer.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(buffer) == 0x02014b50) throw failure;
        }
    }
    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link([MarshalAs(UnmanagedType.LPUTF8Str)] string existing, [MarshalAs(UnmanagedType.LPUTF8Str)] string newPath);
}
