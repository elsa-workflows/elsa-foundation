using System.Text;
using System.Text.Json;
using Elsa.Etl.Runtime.Transformation;

namespace Elsa.Etl.Runtime.Output;

/// <summary>Formats connector route rows as RFC 4180 CSV: comma-delimited, double-quote quoting, CRLF, UTF-8 without a BOM.</summary>
/// <remarks>
/// Record rows are rewritten from the transformer's JSON objects, whose properties are already in mapping order and whose
/// numbers are already the canonical invariant text of their typed values; no value is re-derived here. Callers bound
/// every row against their output allowance before appending it.
/// </remarks>
internal static class EtlCsvRouteWriter
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly string[] ErrorColumns = ["recordOrdinal", "code", "field"];

    /// <summary>The header row of the mapped output field names, in mapping order.</summary>
    public static byte[] Header(IReadOnlyList<string> fieldNames) => Row(fieldNames.Select(Text));

    /// <summary>The fixed error-route header: <c>recordOrdinal,code,field</c>.</summary>
    public static byte[] ErrorHeader() => Row(ErrorColumns.Select(Text));

    /// <summary>Rewrites one accepted row's JSON object (with or without its trailing LF) as one CSV row.</summary>
    /// <exception cref="InvalidDataException">The row is not one JSON object of scalars with the expected field count.</exception>
    public static byte[] RecordRow(ReadOnlyMemory<byte> jsonObject, int fieldCount)
    {
        using var document = JsonDocument.Parse(jsonObject);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("A transformed connector row is not a JSON object.");
        var cells = new List<string>(fieldCount);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            var value = property.Value;
            cells.Add(value.ValueKind switch
            {
                JsonValueKind.String => Text(value.GetString()!),
                // The transformer wrote int64 and decimal values as their exact invariant text, never with an exponent.
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => string.Empty,
                _ => throw new InvalidDataException("A transformed connector row holds a non-scalar value.")
            });
        }

        if (cells.Count != fieldCount)
            throw new InvalidDataException("A transformed connector row does not match its mapping.");
        return Row(cells);
    }

    /// <summary>One error row: the record ordinal, the stable code and the field name or an empty field.</summary>
    public static byte[] ErrorRow(EtlRowError error) => Row([
        error.RecordOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Text(error.Code),
        error.Field is null ? string.Empty : Text(error.Field)
    ]);

    /// <summary>A string cell: quoted when empty, when it holds a comma, quote, CR or LF, or when it starts or ends with whitespace.</summary>
    internal static string Text(string value)
    {
        var quote = value.Length == 0 || char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]) ||
                    value.AsSpan().IndexOfAny(",\"\r\n") >= 0;
        return quote ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }

    private static byte[] Row(IEnumerable<string> cells) => Utf8.GetBytes(string.Join(',', cells) + "\r\n");
}
