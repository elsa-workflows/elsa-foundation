using System.Text.Json;
using Elsa.Serialization.Core;

namespace Elsa3.Migration.Tooling.Report;

/// <summary>
/// Writes <c>report.json</c> with the JSON baseline (ADR 0091), indented for reading, with a final newline. The bytes
/// depend only on the report, so equal reports are byte-identical (FR-022).
/// </summary>
internal static class ReportJson
{
    public static byte[] Serialize(MigrationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JsonBaseline.Options.Encoder }))
            JsonSerializer.Serialize(writer, report, JsonBaseline.Options);

        buffer.WriteByte((byte)'\n');
        return buffer.ToArray();
    }

    /// <summary>Gets the embedded report schema, a copy of the specification's contract (FR-020).</summary>
    public static string ReadSchema() => ReadResource("Elsa3.Migration.Tooling.report.schema.json");

    internal static string ReadResource(string name)
    {
        using var stream = typeof(ReportJson).Assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"The tooling assembly carries no resource '{name}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
