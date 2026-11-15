using System.Text.RegularExpressions;

namespace Elsa.Etl.Persistence.EntityFrameworkCore;

/// <summary>
/// One engine's spelling of the runtime model's check constraints: identifier quoting, the character-length
/// function and boolean literals. SQLite's rendering is the text the retained SQLite migrations created, byte for byte.
/// </summary>
internal sealed partial class EtlRuntimeConstraintDialect(Func<string, string> quote, string length, string @true, string @false)
{
    public static readonly EtlRuntimeConstraintDialect Sqlite = new(name => name, "length", "1", "0");
    public static readonly EtlRuntimeConstraintDialect SqlServer = new(name => $"[{name}]", "LEN", "1", "0");
    public static readonly EtlRuntimeConstraintDialect PostgreSql = new(name => $"\"{name}\"", "length", "TRUE", "FALSE");
    // MySQL's LENGTH counts bytes; CHAR_LENGTH counts characters, as SQLite's length does for text.
    public static readonly EtlRuntimeConstraintDialect MySql = new(name => $"`{name}`", "CHAR_LENGTH", "1", "0");

    public string Render(string template) => Token().Replace(template, match => match.Groups[1].Value switch
    {
        "length" => length,
        "true" => @true,
        "false" => @false,
        var name => quote(name)
    });

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Token();
}
