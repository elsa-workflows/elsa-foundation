using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Elsa.Etl.Runtime.Csv;

/// <summary>Reads bounded logical records from the supported UTF-8 CSV dialect.</summary>
/// <remarks>The reader owns its bounded input buffer, but leaves the source stream open.</remarks>
internal sealed class CsvRecordReader
{
    private readonly Stream _stream;
    private readonly int _maxRecordBytes;
    private readonly int _maxFields;
    private readonly int _bufferSize;

    public CsvRecordReader(Stream stream, int maxRecordBytes, int maxFields, int bufferSize = 4096)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new ArgumentException("The CSV source stream must be readable.", nameof(stream));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRecordBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFields);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize);

        _stream = stream;
        _maxRecordBytes = maxRecordBytes;
        _maxFields = maxFields;
        _bufferSize = bufferSize;
    }

    public IAsyncEnumerable<CsvRecord> ReadAsync(CancellationToken cancellationToken = default) => ReadCoreAsync(cancellationToken);

    private async IAsyncEnumerable<CsvRecord> ReadCoreAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var parser = new Parser(_stream, _maxRecordBytes, _maxFields, _bufferSize);
        await parser.InitializeAsync(cancellationToken);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = await parser.ReadNextAsync(cancellationToken);
            if (record is null)
                yield break;

            yield return record;
        }
    }

    private sealed class Parser
    {
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);
        private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

        private readonly Stream _stream;
        private readonly int _maxRecordBytes;
        private readonly int _maxFields;
        private readonly byte[] _buffer;
        private readonly byte[] _prefix = new byte[3];
        private readonly List<byte> _fieldBytes = [];
        private readonly List<string> _fields = [];
        private int _bufferPosition;
        private int _bufferLength;
        private int _prefixPosition;
        private int _prefixLength;
        private long _recordOrdinal;
        private int _recordBytes;
        private long _bytesConsumed;
        private long _recordStartByteOffset;
        private bool _insideQuotedField;
        private bool _afterClosingQuote;
        private bool _atFieldStart = true;
        private bool _recordHasBytes;

        public Parser(Stream stream, int maxRecordBytes, int maxFields, int bufferSize)
        {
            _stream = stream;
            _maxRecordBytes = maxRecordBytes;
            _maxFields = maxFields;
            _buffer = new byte[bufferSize];
        }

        public async ValueTask InitializeAsync(CancellationToken cancellationToken)
        {
            while (_prefixLength < _prefix.Length)
            {
                var next = await ReadFromStreamAsync(cancellationToken);
                if (next < 0)
                    break;

                _prefix[_prefixLength++] = (byte)next;
            }

            if (_prefixLength == Utf8Bom.Length && _prefix.AsSpan().SequenceEqual(Utf8Bom))
            {
                _prefixPosition = _prefixLength;
                _bytesConsumed = Utf8Bom.Length;
                _recordStartByteOffset = _bytesConsumed;
            }
        }

        public async ValueTask<CsvRecord?> ReadNextAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var next = await ReadByteAsync(cancellationToken);
                if (next < 0)
                    return FinishAtEndOfStream();

                var value = (byte)next;

                if (_insideQuotedField)
                {
                    if (value == (byte)'"')
                    {
                        CountRecordByte();
                        _insideQuotedField = false;
                        _afterClosingQuote = true;
                    }
                    else
                    {
                        CountRecordByte();
                        _fieldBytes.Add(value);
                        _recordHasBytes = true;
                    }

                    continue;
                }

                if (_afterClosingQuote)
                {
                    if (value == (byte)'"')
                    {
                        CountRecordByte();
                        _fieldBytes.Add(value);
                        _insideQuotedField = true;
                        _afterClosingQuote = false;
                        _recordHasBytes = true;
                        continue;
                    }

                    if (value == (byte)',')
                    {
                        CountRecordByte();
                        EnsureDelimiterFitsFieldLimit();
                        AddCurrentField();
                        _recordHasBytes = true;
                        continue;
                    }

                    if (value == (byte)'\n')
                        return FinishRecord();

                    if (value == (byte)'\r')
                    {
                        await ConsumeLineFeedAfterCarriageReturnAsync(cancellationToken);
                        return FinishRecord();
                    }

                    throw InvalidQuoting();
                }

                if (value == (byte)',')
                {
                    CountRecordByte();
                    EnsureDelimiterFitsFieldLimit();
                    AddCurrentField();
                    _recordHasBytes = true;
                    continue;
                }

                if (value == (byte)'\n')
                    return FinishRecord();

                if (value == (byte)'\r')
                {
                    await ConsumeLineFeedAfterCarriageReturnAsync(cancellationToken);
                    return FinishRecord();
                }

                if (value == (byte)'"')
                {
                    CountRecordByte();
                    if (!_atFieldStart)
                        throw InvalidQuoting();

                    _insideQuotedField = true;
                    _atFieldStart = false;
                    _recordHasBytes = true;
                    continue;
                }

                CountRecordByte();
                _fieldBytes.Add(value);
                _atFieldStart = false;
                _recordHasBytes = true;
            }
        }

        private async ValueTask<int> ReadByteAsync(CancellationToken cancellationToken)
        {
            int next;
            if (_prefixPosition < _prefixLength)
                next = _prefix[_prefixPosition++];
            else
                next = await ReadFromStreamAsync(cancellationToken);

            if (next >= 0)
                _bytesConsumed++;

            return next;
        }

        private async ValueTask<int> ReadFromStreamAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_bufferPosition >= _bufferLength)
            {
                int bytesRead;
                try
                {
                    bytesRead = await _stream.ReadAsync(_buffer.AsMemory(), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (IOException exception)
                {
                    throw SourceReadFailed(exception);
                }
                catch (Exception exception) when (exception is ObjectDisposedException or UnauthorizedAccessException or NotSupportedException)
                {
                    throw SourceReadFailed(exception);
                }

                _bufferPosition = 0;
                _bufferLength = bytesRead;
                if (bytesRead == 0)
                    return -1;
            }

            return _buffer[_bufferPosition++];
        }

        private async ValueTask ConsumeLineFeedAfterCarriageReturnAsync(CancellationToken cancellationToken)
        {
            var next = await ReadByteAsync(cancellationToken);
            if (next != (byte)'\n')
                throw InvalidLineEnding();
        }

        private void CountRecordByte()
        {
            if (_recordBytes >= _maxRecordBytes)
                throw new CsvReadException("csv.record-too-large", CurrentRecordOrdinal, "CSV logical record exceeds the configured byte limit.");

            _recordBytes++;
        }

        private void EnsureDelimiterFitsFieldLimit()
        {
            if (_fields.Count + 2 > _maxFields)
                throw new CsvReadException("csv.too-many-fields", CurrentRecordOrdinal, "CSV logical record exceeds the configured field limit.");
        }

        private void AddCurrentField()
        {
            if (_fields.Count >= _maxFields)
                throw new CsvReadException("csv.too-many-fields", CurrentRecordOrdinal, "CSV logical record exceeds the configured field limit.");

            try
            {
                _fields.Add(StrictUtf8.GetString(CollectionsMarshal.AsSpan(_fieldBytes)));
            }
            catch (DecoderFallbackException exception)
            {
                throw new CsvReadException("csv.invalid-utf8", CurrentRecordOrdinal, "CSV logical record contains invalid UTF-8.", exception);
            }

            _fieldBytes.Clear();
            _atFieldStart = true;
            _afterClosingQuote = false;
        }

        private CsvRecord FinishRecord()
        {
            if (_recordOrdinal > int.MaxValue)
                throw new CsvReadException("csv.record-ordinal-overflow", int.MaxValue, "CSV contains more logical records than the supported ordinal range.");

            AddCurrentField();
            var record = new CsvRecord((int)_recordOrdinal, _fields.ToArray(), _recordStartByteOffset, _bytesConsumed);
            _recordOrdinal++;
            _recordStartByteOffset = _bytesConsumed;
            _fields.Clear();
            _fieldBytes.Clear();
            _recordBytes = 0;
            _insideQuotedField = false;
            _afterClosingQuote = false;
            _atFieldStart = true;
            _recordHasBytes = false;
            return record;
        }

        private CsvRecord? FinishAtEndOfStream()
        {
            if (_insideQuotedField)
                throw InvalidQuoting();

            if (!_recordHasBytes && _fields.Count == 0 && _fieldBytes.Count == 0 && !_afterClosingQuote)
                return null;

            return FinishRecord();
        }

        private CsvReadException InvalidQuoting() => new("csv.invalid-quoting", CurrentRecordOrdinal, "CSV logical record has invalid quote framing.");

        private CsvReadException InvalidLineEnding() => new("csv.invalid-line-ending", CurrentRecordOrdinal, "CSV logical record has an invalid line ending.");

        private CsvReadException SourceReadFailed(Exception exception) => new(
            "csv.source-read-failed",
            CurrentRecordOrdinal,
            "CSV source could not be read.",
            exception);

        private int CurrentRecordOrdinal => (int)Math.Min(_recordOrdinal, int.MaxValue);
    }
}
