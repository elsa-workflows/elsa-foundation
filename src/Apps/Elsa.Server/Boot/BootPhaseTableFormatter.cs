using System.Globalization;
using System.Text;

namespace Elsa.Server.Boot;

/// <summary>Renders the recorded boot phases as a fixed-width console table for the cold-start baseline.</summary>
internal static class BootPhaseTableFormatter
{
    public static string Format(IReadOnlyList<BootPhaseRecord> records, double totalElapsedMs, string reason)
    {
        var builder = new StringBuilder();
        builder.Append("  phase").Append(new string(' ', 30)).Append("start(ms)   dur(ms)  detail\n");
        builder.Append("  ").Append(new string('-', 78)).Append('\n');

        foreach (var record in records)
        {
            var phase = Truncate(record.Phase, 33).PadRight(33);
            var start = record.StartMs.ToString("F1", CultureInfo.InvariantCulture).PadLeft(9);
            var duration = record.DurationMs > 0d
                ? record.DurationMs.ToString("F1", CultureInfo.InvariantCulture).PadLeft(9)
                : "-".PadLeft(9);
            var detail = record.Detail ?? string.Empty;
            builder.Append("  ").Append(phase).Append(start).Append(' ').Append(duration).Append("  ").Append(detail).Append('\n');
        }

        builder.Append("  ").Append(new string('-', 78)).Append('\n');
        builder.Append("  total elapsed since process entry: ")
            .Append(totalElapsedMs.ToString("F1", CultureInfo.InvariantCulture))
            .Append(" ms");
        return builder.ToString();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";
}
