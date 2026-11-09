namespace Elsa.Etl.Runtime.Csv;

internal sealed record CsvRecord(int RecordOrdinal, string[] Fields, long StartByteOffset = 0, long EndByteOffset = 0);
