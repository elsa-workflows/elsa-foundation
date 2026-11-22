using System.Text;
using Elsa.Etl.Runtime.Csv;

namespace Elsa.Etl.Tests;

public sealed class CsvRecordReaderTests
{
    [Fact]
    public async Task Parses_bom_quotes_escaped_quotes_multiline_fields_and_line_endings()
    {
        const string csv = "\uFEFFname,comment\r\nAda,\"first line\r\nsecond \"\"line\"\"\"\nGrace,hello";
        var records = await ReadRecordsAsync(Encoding.UTF8.GetBytes(csv));

        Assert.Equal(3, records.Count);
        Assert.Equal(0, records[0].RecordOrdinal);
        Assert.Equal(new[] { "name", "comment" }, records[0].Fields);
        Assert.Equal(1, records[1].RecordOrdinal);
        Assert.Equal(new[] { "Ada", "first line\r\nsecond \"line\"" }, records[1].Fields);
        Assert.Equal(2, records[2].RecordOrdinal);
        Assert.Equal(new[] { "Grace", "hello" }, records[2].Fields);
    }

    [Fact]
    public async Task Empty_file_blank_record_and_trailing_delimiter_have_distinct_shapes()
    {
        Assert.Empty(await ReadRecordsAsync([]));

        var records = await ReadRecordsAsync(Encoding.UTF8.GetBytes("\nvalue,\n"));

        Assert.Equal(2, records.Count);
        Assert.Equal(new[] { "" }, records[0].Fields);
        Assert.Equal(new[] { "value", "" }, records[1].Fields);
    }

    [Fact]
    public async Task Enforces_raw_record_byte_limit_at_the_exact_boundary()
    {
        var accepted = await ReadRecordsAsync(Encoding.UTF8.GetBytes("\uFEFFé,\"x\"\r\n"), maxRecordBytes: 6);
        Assert.Equal(new[] { "é", "x" }, Assert.Single(accepted).Fields);

        var error = await Assert.ThrowsAsync<CsvReadException>(() =>
            ReadRecordsAsync(Encoding.UTF8.GetBytes("é\n"), maxRecordBytes: 1));

        Assert.Equal("csv.record-too-large", error.StableCode);
        Assert.Equal(0, error.RecordOrdinal);
        Assert.DoesNotContain("é", error.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Enforces_field_limit_before_accepting_an_additional_field()
    {
        var error = await Assert.ThrowsAsync<CsvReadException>(() =>
            ReadRecordsAsync(Encoding.UTF8.GetBytes("a,b\n"), maxFields: 1));

        Assert.Equal("csv.too-many-fields", error.StableCode);
        Assert.Equal(0, error.RecordOrdinal);
    }

    [Theory]
    [InlineData("a\"b\n")]
    [InlineData("\"a\"x\n")]
    [InlineData("\"unfinished")]
    public async Task Rejects_invalid_quote_framing_without_exposing_record_text(string csv)
    {
        var error = await Assert.ThrowsAsync<CsvReadException>(() => ReadRecordsAsync(Encoding.UTF8.GetBytes(csv)));

        Assert.Equal("csv.invalid-quoting", error.StableCode);
        Assert.Equal(0, error.RecordOrdinal);
        Assert.DoesNotContain(csv, error.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_bare_carriage_return_outside_a_quoted_field()
    {
        var error = await Assert.ThrowsAsync<CsvReadException>(() => ReadRecordsAsync(Encoding.UTF8.GetBytes("a\rb")));

        Assert.Equal("csv.invalid-line-ending", error.StableCode);
        Assert.Equal(0, error.RecordOrdinal);
    }

    [Fact]
    public async Task Rejects_invalid_utf8_even_when_multibyte_sequence_is_split_across_reads()
    {
        var stream = new ShortReadStream([0xC3, 0x28], maxChunkSize: 1);
        var error = await Assert.ThrowsAsync<CsvReadException>(() => ReadRecordsAsync(stream));

        Assert.Equal("csv.invalid-utf8", error.StableCode);
        Assert.Equal(0, error.RecordOrdinal);
        Assert.IsType<DecoderFallbackException>(error.InnerException);
    }

    [Fact]
    public async Task Accepts_valid_multibyte_utf8_across_short_reads_and_leaves_stream_open()
    {
        var stream = new ShortReadStream(Encoding.UTF8.GetBytes("café,雪\n"), maxChunkSize: 1);

        var record = Assert.Single(await ReadRecordsAsync(stream));

        Assert.Equal(new[] { "café", "雪" }, record.Fields);
        Assert.True(stream.ReadCount > 2);
        Assert.False(stream.WasDisposed);
    }

    [Fact]
    public async Task Tracks_exact_byte_offsets_across_bom_multibyte_quoted_newlines_crlf_and_eof()
    {
        const string csv = "\uFEFFid,note\r\n1,\"雪\nline\"\r\n2,tail";
        var bytes = Encoding.UTF8.GetBytes(csv);
        var records = await ReadRecordsAsync(new ShortReadStream(bytes, maxChunkSize: 1));
        long headerEnd = 3 + Encoding.UTF8.GetByteCount("id,note\r\n");
        var firstDataEnd = headerEnd + Encoding.UTF8.GetByteCount("1,\"雪\nline\"\r\n");

        Assert.Collection(records,
            header =>
            {
                Assert.Equal(0, header.RecordOrdinal);
                Assert.Equal(3, header.StartByteOffset);
                Assert.Equal(headerEnd, header.EndByteOffset);
            },
            firstData =>
            {
                Assert.Equal(1, firstData.RecordOrdinal);
                Assert.Equal(headerEnd, firstData.StartByteOffset);
                Assert.Equal(firstDataEnd, firstData.EndByteOffset);
                Assert.Equal(new[] { "1", "雪\nline" }, firstData.Fields);
            },
            finalData =>
            {
                Assert.Equal(2, finalData.RecordOrdinal);
                Assert.Equal(firstDataEnd, finalData.StartByteOffset);
                Assert.Equal(bytes.LongLength, finalData.EndByteOffset);
                Assert.Equal(new[] { "2", "tail" }, finalData.Fields);
            });
    }

    [Fact]
    public async Task Uses_only_the_configured_fixed_size_read_buffer()
    {
        var stream = new ShortReadStream(Encoding.UTF8.GetBytes("a,b\n"), maxChunkSize: 1);

        var records = await ReadRecordsAsync(stream, bufferSize: 2);

        Assert.Single(records);
        Assert.Equal(2, stream.LargestReadRequest);
    }

    [Fact]
    public async Task Wraps_source_read_failures_with_safe_classification_and_original_exception()
    {
        var sourceError = new IOException("private path and payload");
        var stream = new ShortReadStream([], maxChunkSize: 1, readFailure: sourceError);

        var error = await Assert.ThrowsAsync<CsvReadException>(() => ReadRecordsAsync(stream));

        Assert.Equal("csv.source-read-failed", error.StableCode);
        Assert.Same(sourceError, error.InnerException);
        Assert.DoesNotContain("private path", error.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Propagates_cancellation_and_keeps_stream_owned_by_caller()
    {
        using var cancellation = new CancellationTokenSource();
        var stream = new BlockingReadStream();
        var readTask = ReadRecordsAsync(stream, cancellationToken: cancellation.Token);
        await stream.ReadStarted;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => readTask);
        Assert.False(stream.WasDisposed);
    }

    [Fact]
    public async Task Reports_a_framing_error_after_prior_records_with_the_current_logical_ordinal()
    {
        await using var enumerator = new CsvRecordReader(new MemoryStream(Encoding.UTF8.GetBytes("good\n\"bad\"x")), 128, 8)
            .ReadAsync()
            .GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(0, enumerator.Current.RecordOrdinal);
        var error = await Assert.ThrowsAsync<CsvReadException>(async () => await enumerator.MoveNextAsync());

        Assert.Equal("csv.invalid-quoting", error.StableCode);
        Assert.Equal(1, error.RecordOrdinal);
        Assert.False(await enumerator.MoveNextAsync());
    }

    private static async Task<List<CsvRecord>> ReadRecordsAsync(
        byte[] bytes,
        int maxRecordBytes = 1024,
        int maxFields = 16,
        CancellationToken cancellationToken = default) =>
        await ReadRecordsAsync(new MemoryStream(bytes), maxRecordBytes, maxFields, cancellationToken);

    private static async Task<List<CsvRecord>> ReadRecordsAsync(
        Stream stream,
        int maxRecordBytes = 1024,
        int maxFields = 16,
        CancellationToken cancellationToken = default,
        int bufferSize = 4096)
    {
        var records = new List<CsvRecord>();
        await foreach (var record in new CsvRecordReader(stream, maxRecordBytes, maxFields, bufferSize)
                           .ReadAsync(cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            records.Add(record);
        }

        return records;
    }

    private sealed class ShortReadStream(
        byte[] bytes,
        int maxChunkSize,
        IOException? readFailure = null) : Stream
    {
        private int _position;

        public int ReadCount { get; private set; }
        public int LargestReadRequest { get; private set; }
        public bool WasDisposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => bytes.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            LargestReadRequest = Math.Max(LargestReadRequest, buffer.Length);
            if (readFailure is not null)
                return ValueTask.FromException<int>(readFailure);

            var count = Math.Min(Math.Min(buffer.Length, maxChunkSize), bytes.Length - _position);
            bytes.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return ValueTask.FromResult(count);
        }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class BlockingReadStream : Stream
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ReadStarted => _started.Task;
        public bool WasDisposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }
}
