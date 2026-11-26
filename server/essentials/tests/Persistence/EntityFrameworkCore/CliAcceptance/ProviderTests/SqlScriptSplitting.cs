using System.Text;

namespace Elsa.Persistence.EntityFrameworkCore.CliAcceptance.ProviderTests;

/// <summary>
/// Turns one <c>dotnet elsa persistence script</c> artifact into the unit of text a specific engine's own
/// raw ADO client actually sends to the server. Neither <c>SqlConnection</c> nor MySQL's client executes the
/// artifact as one opaque blob: SQL Server's <c>GO</c> is a batch separator <c>sqlcmd</c>/SSMS understands
/// and <c>SqlConnection</c> does not, and MySQL's <c>DELIMITER</c> is a directive the <c>mysql</c> CLI reads
/// and never sends to the server either. Getting either wrong here means the acceptance leg (issue #1875)
/// applies only the first batch or statement of a script and reports green having exercised almost nothing.
/// PostgreSQL needs neither: Npgsql executes multi-statement text directly, and the idempotent generator's
/// own <c>DO $EF$ ... END $EF$;</c> blocks dollar-quote their embedded semicolons, so nothing here splits
/// Postgres scripts at all.
/// </summary>
internal static class SqlScriptSplitting
{
    /// <summary>
    /// Splits a T-SQL idempotent script into the batches <c>sqlcmd</c> would send one at a time: a line whose
    /// only content is <c>GO</c>, optionally followed by a repeat count (<c>GO 3</c>), ends the current batch.
    /// The <c>GO</c> line itself is never part of any batch, and an empty batch (two separators in a row, or
    /// one at the very start of the script) contributes nothing to the result.
    /// </summary>
    public static IReadOnlyList<string> SplitSqlServerBatches(string script)
    {
        var batches = new List<string>();
        var buffer = new StringBuilder();
        foreach (var line in Lines(script))
        {
            if (IsGoSeparator(line))
            {
                AppendIfNotBlank(batches, buffer);
                continue;
            }

            buffer.Append(line).Append('\n');
        }

        AppendIfNotBlank(batches, buffer);
        return batches;
    }

    /// <summary>
    /// Splits a MySQL idempotent script into the statements the server actually receives, honouring
    /// <c>DELIMITER &lt;token&gt;</c> the way the <c>mysql</c> CLI does: it changes the terminator that ends
    /// the next statement (needed so a routine or trigger body can carry its own <c>;</c> characters without
    /// ending the enclosing <c>CREATE</c> early) and is itself consumed, never sent to the server. The default
    /// terminator is <c>;</c> until the first <c>DELIMITER</c> line changes it.
    /// </summary>
    public static IReadOnlyList<string> SplitMySqlStatements(string script)
    {
        var statements = new List<string>();
        var buffer = new StringBuilder();
        var delimiter = ";";
        foreach (var line in Lines(script))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("DELIMITER ", StringComparison.OrdinalIgnoreCase))
            {
                // A DELIMITER line always starts a fresh statement: whatever preceded it that had not yet
                // reached the *old* terminator was never a complete statement to begin with.
                AppendIfNotBlank(statements, buffer);
                delimiter = trimmed["DELIMITER ".Length..].Trim();
                continue;
            }

            buffer.Append(line).Append('\n');
            var pending = buffer.ToString().TrimEnd();
            if (delimiter.Length > 0 && pending.EndsWith(delimiter, StringComparison.Ordinal))
            {
                var statement = pending[..^delimiter.Length].Trim();
                if (statement.Length > 0)
                    statements.Add(statement);
                buffer.Clear();
            }
        }

        // A script that ends mid-statement (no trailing terminator) still hands over whatever text remains,
        // rather than silently dropping it -- a truncated statement is a server error either way, and this
        // keeps that error informative instead of turning it into a quietly-shorter script.
        AppendIfNotBlank(statements, buffer);
        return statements;
    }

    private static IEnumerable<string> Lines(string script) =>
        script.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    /// <summary>
    /// A line is a <c>GO</c> separator only when, trimmed, it is exactly <c>GO</c> or <c>GO &lt;count&gt;</c>
    /// (case-insensitive, the way <c>sqlcmd</c> reads it). <c>GO</c> as a word inside a longer line -- a
    /// comment, a string literal, an identifier like <c>GOTO</c> -- is ordinary batch text.
    /// </summary>
    private static bool IsGoSeparator(string line)
    {
        var tokens = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length switch
        {
            1 => string.Equals(tokens[0], "GO", StringComparison.OrdinalIgnoreCase),
            2 => string.Equals(tokens[0], "GO", StringComparison.OrdinalIgnoreCase) && int.TryParse(tokens[1], out _),
            _ => false
        };
    }

    private static void AppendIfNotBlank(List<string> destination, StringBuilder buffer)
    {
        var text = buffer.ToString().Trim();
        if (text.Length > 0)
            destination.Add(text);

        buffer.Clear();
    }
}
