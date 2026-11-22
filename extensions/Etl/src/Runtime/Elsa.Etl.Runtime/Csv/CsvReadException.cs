namespace Elsa.Etl.Runtime.Csv;

internal sealed class CsvReadException(
    string stableCode,
    int recordOrdinal,
    string safeMessage,
    Exception? innerException = null) : Exception(safeMessage, innerException)
{
    public string StableCode { get; } = stableCode;
    public int RecordOrdinal { get; } = recordOrdinal;
    public string SafeMessage { get; } = safeMessage;
}
