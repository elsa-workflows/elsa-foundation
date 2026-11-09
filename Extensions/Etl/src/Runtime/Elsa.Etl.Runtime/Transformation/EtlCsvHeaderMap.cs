using Elsa.Etl.Core;
using Elsa.Etl.Runtime.Csv;

namespace Elsa.Etl.Runtime.Transformation;

internal sealed class EtlCsvHeaderMap
{
    private readonly Dictionary<string, int> _fieldIndices;
    private readonly HashSet<string> _schemaNames;

    private EtlCsvHeaderMap(Dictionary<string, int> fieldIndices, HashSet<string> schemaNames)
    {
        _fieldIndices = fieldIndices;
        _schemaNames = schemaNames;
    }

    public int GetFieldIndex(string name) => _fieldIndices.TryGetValue(name, out var index)
        ? index
        : throw new InvalidOperationException("The compiled CSV header does not contain the requested field.");

    public bool MatchesSchema(IReadOnlyList<EtlColumn> schema) =>
        schema.Count == _schemaNames.Count && schema.All(column => _schemaNames.Contains(column.Name));

    public static bool TryCreate(
        IReadOnlyList<EtlColumn> schema,
        CsvRecord headerRecord,
        out EtlCsvHeaderMap? header,
        out EtlHeaderError? error)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(headerRecord);

        header = null;
        error = null;
        if (headerRecord.RecordOrdinal != 0)
        {
            error = new EtlHeaderError(headerRecord.RecordOrdinal, null, "etl.header-ordinal-invalid");
            return false;
        }

        var schemaNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in schema)
        {
            if (!schemaNames.Add(column.Name))
            {
                error = new EtlHeaderError(0, column.Name, "etl.schema-invalid");
                return false;
            }
        }

        var fieldIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < headerRecord.Fields.Length; index++)
        {
            var field = headerRecord.Fields[index];
            if (!schemaNames.Contains(field))
            {
                error = new EtlHeaderError(0, null, "etl.header-field-unexpected");
                return false;
            }

            if (!fieldIndices.TryAdd(field, index))
            {
                error = new EtlHeaderError(0, field, "etl.header-duplicate-field");
                return false;
            }
        }

        foreach (var column in schema)
        {
            if (!fieldIndices.ContainsKey(column.Name))
            {
                error = new EtlHeaderError(0, column.Name, "etl.header-field-missing");
                return false;
            }
        }

        header = new EtlCsvHeaderMap(fieldIndices, schemaNames);
        return true;
    }
}

internal sealed record EtlHeaderError(int RecordOrdinal, string? Field, string Code);
