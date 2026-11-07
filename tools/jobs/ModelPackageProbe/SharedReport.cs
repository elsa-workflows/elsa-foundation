using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Jobs.Core;

// The immutable packaging driver copies the exact existing ControlledReportHandler and its input/result
// records from PackageProbe/ControlledReport.cs into this consumer. No alternate model handler exists.
internal static partial class Probe
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() }, WriteIndented = true };
    internal static readonly JobHandlerSupport Support = new(new("package.report", "2"),
        new("package.report.controlled.input", 2), new("package.report.controlled.result", 2), "package.report.implementation.v2");
    internal static async Task WriteAsync<T>(string path, T value)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, Json));
        File.Move(temporary, path, overwrite: true);
    }
}

internal sealed record ReportLine(string Product, int Quantity, decimal UnitPrice);
