using System.Text.RegularExpressions;

namespace Elsa.Activities.Http.CommandCost;

/// <summary>One SQL statement of a command: its verb and the table it targets, if it names one.</summary>
internal sealed record SqlStatement(string Kind, string Table);

/// <summary>
/// Splits a command's text into the statements a provider sends for it and names each one's verb and table. EF batches
/// several modifications into one command on PostgreSQL, separated by semicolons; SQLite sends one statement per
/// command. Semicolons inside quotes and comments do not separate statements.
/// </summary>
internal static partial class SqlStatements
{
    private const string RuntimeTablePrefix = "elsa_runtime_";

    public static IReadOnlyList<SqlStatement> Parse(string commandText) =>
        Split(commandText).Select(Classify).ToArray();

    private static SqlStatement Classify(string statement)
    {
        var verb = FirstWord(statement);
        var kind = verb switch
        {
            "SELECT" or "INSERT" or "UPDATE" or "DELETE" => verb,
            "WITH" => statement.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase) ? "INSERT"
                : statement.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase) ? "UPDATE"
                : statement.Contains("DELETE FROM", StringComparison.OrdinalIgnoreCase) ? "DELETE"
                : "SELECT",
            _ => "OTHER"
        };

        var match = kind switch
        {
            "INSERT" => InsertTable().Match(statement),
            "UPDATE" => UpdateTable().Match(statement),
            "DELETE" => DeleteTable().Match(statement),
            "SELECT" => SelectTable().Match(statement),
            _ => Match.Empty
        };
        var table = match.Success ? Unquote(match.Groups["table"].Value) : "-";
        return new SqlStatement(kind, table.StartsWith(RuntimeTablePrefix, StringComparison.Ordinal) ? table[RuntimeTablePrefix.Length..] : table);
    }

    private static string FirstWord(string statement)
    {
        var start = 0;
        while (start < statement.Length && !char.IsLetter(statement[start]))
            start++;
        var end = start;
        while (end < statement.Length && char.IsLetter(statement[end]))
            end++;
        return statement[start..end].ToUpperInvariant();
    }

    private static string Unquote(string name)
    {
        var last = name.Split('.')[^1];
        return last.Trim('"', '`', '[', ']');
    }

    private static IEnumerable<string> Split(string text)
    {
        var start = 0;
        var index = 0;
        while (index < text.Length)
        {
            var current = text[index];
            if (current is '\'' or '"' or '`')
                index = SkipQuoted(text, index, current);
            else if (current == '-' && index + 1 < text.Length && text[index + 1] == '-')
                index = SkipLine(text, index);
            else if (current == '/' && index + 1 < text.Length && text[index + 1] == '*')
                index = SkipBlockComment(text, index);
            else if (current == ';')
            {
                if (!string.IsNullOrWhiteSpace(text[start..index]))
                    yield return text[start..index].Trim();
                start = ++index;
            }
            else
                index++;
        }

        if (start < text.Length && !string.IsNullOrWhiteSpace(text[start..]))
            yield return text[start..].Trim();
    }

    private static int SkipQuoted(string text, int index, char quote)
    {
        index++;
        while (index < text.Length)
        {
            if (text[index] == quote)
            {
                if (index + 1 < text.Length && text[index + 1] == quote)
                {
                    index += 2;
                    continue;
                }

                return index + 1;
            }

            index++;
        }

        return index;
    }

    private static int SkipLine(string text, int index)
    {
        var end = text.IndexOf('\n', index);
        return end < 0 ? text.Length : end + 1;
    }

    private static int SkipBlockComment(string text, int index)
    {
        var end = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
        return end < 0 ? text.Length : end + 2;
    }

    [GeneratedRegex("""INSERT\s+INTO\s+(?<table>(?:["`\[]?[\w]+["`\]]?\.)?["`\[]?[\w]+["`\]]?)""", RegexOptions.IgnoreCase)]
    private static partial Regex InsertTable();

    [GeneratedRegex("""UPDATE\s+(?<table>(?:["`\[]?[\w]+["`\]]?\.)?["`\[]?[\w]+["`\]]?)""", RegexOptions.IgnoreCase)]
    private static partial Regex UpdateTable();

    [GeneratedRegex("""DELETE\s+FROM\s+(?<table>(?:["`\[]?[\w]+["`\]]?\.)?["`\[]?[\w]+["`\]]?)""", RegexOptions.IgnoreCase)]
    private static partial Regex DeleteTable();

    [GeneratedRegex("""\bFROM\s+(?<table>(?:["`\[]?[\w]+["`\]]?\.)?["`\[]?[\w]+["`\]]?)""", RegexOptions.IgnoreCase)]
    private static partial Regex SelectTable();
}
