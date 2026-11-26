using System.Text;

namespace Elsa3.Migration.Tooling.Report;

/// <summary>
/// Renders <c>report.html</c> (spec 1237 FR-021, R-9): one embedded, self-contained template with inline CSS and script,
/// and <c>report.json</c> inlined as a <c>&lt;script type="application/json"&gt;</c> block. The page holds no data of its
/// own and loads nothing from a network.
/// </summary>
internal static class ReportHtml
{
    private const string Placeholder = "/*REPORT_JSON*/";

    /// <summary>Renders the page for the exact bytes written to <c>report.json</c>.</summary>
    public static byte[] Render(byte[] reportJson)
    {
        ArgumentNullException.ThrowIfNull(reportJson);

        var template = ReportJson.ReadResource("Elsa3.Migration.Tooling.report.html");
        var index = template.IndexOf(Placeholder, StringComparison.Ordinal);
        if (index < 0)
            throw new InvalidOperationException("The report template has no JSON placeholder.");

        var page = string.Concat(template.AsSpan(0, index), EscapeForScript(Encoding.UTF8.GetString(reportJson)), template.AsSpan(index + Placeholder.Length));
        return Encoding.UTF8.GetBytes(page);
    }

    /// <summary>
    /// Rewrites <c>&lt;</c>, <c>&gt;</c> and <c>&amp;</c> as JSON <c>\u</c> escapes, whatever the serializer's encoder did,
    /// so no string in the report can close the script block. The result parses to the same JSON value: the three
    /// characters only ever occur inside JSON strings.
    /// </summary>
    internal static string EscapeForScript(string json)
    {
        var builder = new StringBuilder(json.Length);
        foreach (var character in json)
        {
            builder.Append(character switch
            {
                '<' => "\\u003c",
                '>' => "\\u003e",
                '&' => "\\u0026",
                _ => character.ToString()
            });
        }

        return builder.ToString();
    }
}
