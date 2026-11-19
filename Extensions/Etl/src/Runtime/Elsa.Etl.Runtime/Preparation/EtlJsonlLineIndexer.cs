using Elsa.Etl.Core;

namespace Elsa.Etl.Runtime.Preparation;

/// <summary>Writes the record index of a JSON Lines snapshot by scanning bytes, never buffering a line.</summary>
/// <remarks>
/// Entry 0 is a sentinel <c>(0, 0, bomLength)</c> so ordinals, record counts and partition plans match the CSV index.
/// A record spans one line up to and including its LF, or to EOF for an unterminated last line. Lines made only of
/// space, tab and CR are not records, so consecutive spans can leave gaps but never overlap.
/// </remarks>
internal static class EtlJsonlLineIndexer
{
    private const int BufferSize = 4096;
    private static ReadOnlySpan<byte> ByteOrderMark => [0xEF, 0xBB, 0xBF];

    /// <summary>Indexes <paramref name="input"/> into <paramref name="destination"/> and returns the record count.</summary>
    /// <param name="maxRecords">The largest record count allowed; routed record ordinals are 32-bit.</param>
    public static async ValueTask<long> WriteIndexAsync(Stream input, Stream destination, long maxRecords,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];
        var entry = new byte[EtlConnectorCsvSupport.IndexEntrySize];

        // Read up to three bytes before deciding whether the input starts with a byte order mark.
        var prefixLength = 0;
        while (prefixLength < ByteOrderMark.Length)
        {
            var count = await input.ReadAsync(buffer.AsMemory(prefixLength, ByteOrderMark.Length - prefixLength), cancellationToken);
            if (count == 0)
                break;
            prefixLength += count;
        }

        var bomLength = prefixLength == ByteOrderMark.Length && buffer.AsSpan(0, prefixLength).SequenceEqual(ByteOrderMark)
            ? ByteOrderMark.Length
            : 0;
        EtlConnectorCsvSupport.WriteIndexEntry(entry, 0, 0, bomLength);
        await destination.WriteAsync(entry, cancellationToken);

        var scanner = new Scanner(bomLength);
        var pending = prefixLength - bomLength;
        var offset = bomLength;
        while (true)
        {
            for (var index = offset; index < offset + pending; index++)
            {
                if (scanner.Accept(buffer[index]) is { } record)
                    await WriteRecordAsync(record.Start, record.End);
            }

            var count = await input.ReadAsync(buffer, cancellationToken);
            if (count == 0)
                break;
            offset = 0;
            pending = count;
        }

        if (scanner.Finish() is { } last)
            await WriteRecordAsync(last.Start, last.End);
        return scanner.RecordCount;

        async ValueTask WriteRecordAsync(long start, long end)
        {
            if (scanner.RecordCount > maxRecords)
                throw new EtlConnectorPreparationFailure(new EtlPreparationDiagnostic("etl.source-too-many-records"));
            EtlConnectorCsvSupport.WriteIndexEntry(entry, scanner.RecordCount, start, end);
            await destination.WriteAsync(entry, cancellationToken);
        }
    }

    /// <summary>The line state machine; it counts a record when it reports one.</summary>
    private sealed class Scanner(long firstLineStart)
    {
        private long _position = firstLineStart;
        private long _lineStart = firstLineStart;
        private bool _lineBlank = true;

        public long RecordCount { get; private set; }

        public (long Start, long End)? Accept(byte value)
        {
            var position = _position++;
            if (value == (byte)'\n')
            {
                var record = _lineBlank ? ((long, long)?)null : (_lineStart, position + 1);
                _lineStart = position + 1;
                _lineBlank = true;
                return Count(record);
            }

            if (value is not ((byte)' ' or (byte)'\t' or (byte)'\r'))
                _lineBlank = false;
            return null;
        }

        public (long Start, long End)? Finish() =>
            Count(_lineBlank || _position == _lineStart ? null : (_lineStart, _position));

        private (long Start, long End)? Count((long Start, long End)? record)
        {
            if (record is not null)
                RecordCount++;
            return record;
        }
    }
}
