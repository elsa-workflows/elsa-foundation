using System.Text;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// ADR 0077 and #1950 separate "this build cannot read this row's version" from "this row is not
/// internally consistent". #1952 routed every EF store's version comparison through
/// <c>EfSchemaVersion</c>; #1955 moved that term to the front of its condition, because an integrity
/// clause evaluated first reports corruption for a row whose only real problem is skew.
/// <para>
/// This guard keeps that ordering. It scans production sources under <c>src/</c> and fails when a
/// <c>EfSchemaVersion.Readable</c> / <c>EfSchemaVersion.NotReadable</c> call has another boolean
/// clause ahead of it in the same condition, whether the condition is written on one line or spread
/// over several. The detector itself is pinned by fixtures below, so a scanner that silently stops
/// finding anything fails too.
/// </para>
/// </summary>
public sealed class EfSchemaVersionOrderingGuardTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void Schema_version_is_the_first_clause_of_its_condition_in_every_production_source()
    {
        var sourceRoot = Path.Combine(RepoRoot, "src");
        var violations = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .SelectMany(file => FindLateSchemaChecks(File.ReadAllText(file))
                .Select(line => $"{Path.GetRelativePath(RepoRoot, file)}({line})"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "EfSchemaVersion.Readable/NotReadable must be the first clause evaluated in its condition, " +
            "so a skewed row is diagnosed as skew rather than as corruption. Offending call sites:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Guard_scans_the_call_sites_it_claims_to_scan()
    {
        var sourceRoot = Path.Combine(RepoRoot, "src");
        var callSites = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .Sum(file => CountCalls(File.ReadAllText(file)));

        // The ordering rule is only meaningful while EF stores actually route through EfSchemaVersion.
        // If this ever drops to zero the first test passes vacuously, so pin a floor rather than a count.
        Assert.True(callSites >= 30, $"Expected the EF stores to keep routing through EfSchemaVersion; found {callSites} call sites.");
    }

    [Theory]
    [MemberData(nameof(OutOfOrderFixtures))]
    public void Detector_flags_a_condition_whose_schema_check_is_not_first(string name, string source)
    {
        Assert.True(FindLateSchemaChecks(source).Length > 0, $"The detector missed the out-of-order fixture '{name}'.");
    }

    [Theory]
    [MemberData(nameof(InOrderFixtures))]
    public void Detector_accepts_a_condition_whose_schema_check_is_first(string name, string source)
    {
        Assert.True(FindLateSchemaChecks(source).Length == 0, $"The detector wrongly flagged the in-order fixture '{name}'.");
    }

    public static TheoryData<string, string> OutOfOrderFixtures() => new()
    {
        {
            "single-line or-chain",
            """
            if (row.Revision <= 0 || EfSchemaVersion.NotReadable("M", row.SchemaVersion, Module.SchemaVersion))
                throw new InvalidDataException("corrupt");
            """
        },
        {
            "multi-line or-chain",
            """
            if (row.Revision <= 0 ||
                EfSchemaVersion.NotReadable("M", row.SchemaVersion, Module.SchemaVersion) ||
                row.ScopeKey != Encode(scope))
                throw new InvalidDataException("corrupt");
            """
        },
        {
            "multi-line and-chain assigned to a local",
            """
            var valid =
                (expectedId is null || row.Id == expectedId) &&
                EfSchemaVersion.Readable("M", row.SchemaVersion, Module.SchemaVersion) &&
                row.ScopeKey == Encode(scope);
            """
        },
        {
            "trailing clause of a long or-chain",
            """
            if (row.Id != CreateId(scope, id) ||
                row.ScopeKeyHash != Hash(scope) ||
                EfSchemaVersion.NotReadable("M", row.SchemaVersion, Module.SchemaVersion))
                throw new InvalidDataException("corrupt");
            """
        },
        {
            "nested inside a grouping parenthesis",
            """
            if (row.Revision <= 0 || (row.ScopeKey != Encode(scope) && EfSchemaVersion.NotReadable("M", row.SchemaVersion, Module.SchemaVersion)))
                throw new InvalidDataException("corrupt");
            """
        },
        {
            "returned expression body",
            """
            private static bool Matches(Row row) =>
                row.Revision > 0 && EfSchemaVersion.Readable("M", row.SchemaVersion, Module.SchemaVersion);
            """
        },
        {
            "preceded by a clause whose string literal looks like a statement boundary",
            """
            if (row.Id != Hash("(;") && EfSchemaVersion.Readable("M", row.SchemaVersion, Module.SchemaVersion))
                throw new InvalidDataException("corrupt");
            """
        }
    };

    public static TheoryData<string, string> InOrderFixtures() => new()
    {
        {
            "single-line or-chain",
            """
            if (EfSchemaVersion.NotReadable("M", row.SchemaVersion, Module.SchemaVersion) || row.Revision <= 0)
                throw new InvalidDataException("corrupt");
            """
        },
        {
            "multi-line or-chain",
            """
            if (EfSchemaVersion.NotReadable("M", row.SchemaVersion, Module.SchemaVersion) ||
                row.Revision <= 0 ||
                row.ScopeKey != Encode(scope))
                throw new InvalidDataException("corrupt");
            """
        },
        {
            "multi-line and-chain assigned to a local",
            """
            var valid =
                EfSchemaVersion.Readable("M", row.SchemaVersion, Module.SchemaVersion) &&
                (expectedId is null || row.Id == expectedId) &&
                row.ScopeKey == Encode(scope);
            """
        },
        {
            "and-chain opening a returned expression",
            """
            return EfSchemaVersion.Readable("M", state.SchemaVersion, Module.SchemaVersion) &&
                   state.Id == ProjectionId(scope, activation) &&
                   state.Revision > 0;
            """
        },
        {
            "the only earlier clause is commented out",
            """
            var valid =
                /* row.Revision > 0 && */ EfSchemaVersion.Readable("M", row.SchemaVersion, Module.SchemaVersion) &&
                row.Revision > 0;
            """
        },
        {
            "an earlier statement's string literal holds boolean operators",
            """
            var message = "a || b && c";
            if (EfSchemaVersion.NotReadable("M", row.SchemaVersion, Module.SchemaVersion))
                throw new InvalidDataException(message);
            """
        },
        {
            "passed as a method argument after another argument",
            """
            Assert.True(row.Revision > 0, Describe(row));
            Assert.True(EfSchemaVersion.Readable("M", row.SchemaVersion, Module.SchemaVersion));
            """
        },
        {
            "statement earlier in the method carries its own chain",
            """
            if (row.Revision <= 0 || row.ScopeKey != Encode(scope))
                throw new InvalidDataException("corrupt");
            if (EfSchemaVersion.NotReadable("M", row.SchemaVersion, Module.SchemaVersion))
                throw new InvalidDataException("corrupt");
            """
        }
    };

    private static readonly string[] CallTokens = ["EfSchemaVersion.Readable(", "EfSchemaVersion.NotReadable("];

    private static int CountCalls(string source)
    {
        var masked = MaskLiteralsAndComments(source);
        var count = 0;
        foreach (var token in CallTokens)
        {
            var index = masked.IndexOf(token, StringComparison.Ordinal);
            while (index >= 0)
            {
                count++;
                index = masked.IndexOf(token, index + token.Length, StringComparison.Ordinal);
            }
        }

        return count;
    }

    /// <summary>
    /// Returns the one-based line number of every <c>EfSchemaVersion.Readable</c> /
    /// <c>NotReadable</c> call that another boolean clause is evaluated ahead of.
    /// </summary>
    private static int[] FindLateSchemaChecks(string source)
    {
        var masked = MaskLiteralsAndComments(source);
        var violations = new List<int>();
        foreach (var token in CallTokens)
        {
            var index = masked.IndexOf(token, StringComparison.Ordinal);
            while (index >= 0)
            {
                if (HasEarlierClause(masked, index))
                    violations.Add(LineOf(source, index));

                index = masked.IndexOf(token, index + token.Length, StringComparison.Ordinal);
            }
        }

        return violations.Order().ToArray();
    }

    /// <summary>
    /// Walks left from the call, at the call's own parenthesis depth, looking for a <c>&amp;&amp;</c>
    /// or <c>||</c> that would be evaluated first. A grouping parenthesis is stepped out of and the
    /// walk continues, so a clause ahead of the group counts too. The walk stops at the start of the
    /// enclosing statement, argument, lambda body, or <c>if</c>/<c>while</c> condition.
    /// </summary>
    private static bool HasEarlierClause(string masked, int callStart)
    {
        var position = callStart;
        while (true)
        {
            var depth = 0;
            var index = position - 1;
            for (; index >= 0; index--)
            {
                var current = masked[index];
                if (current is ')' or ']')
                {
                    depth++;
                    continue;
                }

                if (current is '(' or '[')
                {
                    if (depth > 0)
                    {
                        depth--;
                        continue;
                    }

                    break;
                }

                if (depth > 0)
                    continue;

                if (index > 0 && (current is '&' or '|') && masked[index - 1] == current)
                    return true;

                if (current is ';' or '{' or '}' or ',' or ':' or '?')
                    return false;

                if (current == '=' && !IsCompoundOperatorTail(masked, index))
                    return false;

                if (char.IsLetter(current) || current == '_')
                {
                    var end = index + 1;
                    var start = index;
                    while (start >= 0 && (char.IsLetterOrDigit(masked[start]) || masked[start] == '_'))
                        start--;

                    var word = masked[(start + 1)..end];
                    if (word is "return" or "yield" or "throw" or "case" or "when" or "do" or "else")
                        return false;

                    index = start + 1;
                }
            }

            if (index < 0)
                return false;

            // Stopped on an unmatched '(' or '['. An argument list or indexer starts a fresh expression;
            // a grouping parenthesis does not, so step out of it and keep looking.
            if (masked[index] == '[' || !IsGroupingParenthesis(masked, index))
                return false;

            position = index;
        }
    }

    private static bool IsGroupingParenthesis(string masked, int index)
    {
        var previous = index - 1;
        while (previous >= 0 && char.IsWhiteSpace(masked[previous]))
            previous--;

        if (previous < 0)
            return false;

        // An invocation's argument list and a keyword's condition ("if (", "while (") both start a
        // fresh expression, so neither is stepped out of. Anything else ("&& (", "!(", "= (", "((")
        // is a grouping parenthesis whose own position still has clauses ahead of it.
        return !char.IsLetterOrDigit(masked[previous]) && masked[previous] is not ('_' or ')' or ']');
    }

    private static bool IsCompoundOperatorTail(string masked, int index)
    {
        if (index + 1 < masked.Length && masked[index + 1] == '=')
            return true;

        if (index == 0)
            return false;

        return masked[index - 1] is '=' or '!' or '<' or '>' or '+' or '-' or '*' or '/' or '%' or '&' or '|' or '^';
    }

    private static int LineOf(string source, int index)
    {
        var line = 1;
        for (var i = 0; i < index; i++)
        {
            if (source[i] == '\n')
                line++;
        }

        return line;
    }

    /// <summary>
    /// Replaces the contents of comments, strings and character literals with spaces, preserving
    /// length and line breaks, so a <c>||</c> inside a message never reads as a clause.
    /// </summary>
    private static string MaskLiteralsAndComments(string source)
    {
        var masked = new StringBuilder(source);
        var index = 0;
        while (index < source.Length)
        {
            var current = source[index];
            if (current == '/' && index + 1 < source.Length && source[index + 1] == '/')
            {
                while (index < source.Length && source[index] != '\n')
                    masked[index++] = ' ';

                continue;
            }

            if (current == '/' && index + 1 < source.Length && source[index + 1] == '*')
            {
                var end = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                end = end < 0 ? source.Length : end + 2;
                Blank(masked, source, index, end);
                index = end;
                continue;
            }

            if (current == '"' || current == '\'' ||
                (current is '@' or '$' && index + 1 < source.Length && source[index + 1] is '"' or '@' or '$'))
            {
                index = MaskLiteral(masked, source, index);
                continue;
            }

            index++;
        }

        return masked.ToString();
    }

    private static int MaskLiteral(StringBuilder masked, string source, int start)
    {
        var index = start;
        var verbatim = false;
        while (index < source.Length && source[index] is '@' or '$')
        {
            verbatim |= source[index] == '@';
            index++;
        }

        if (index >= source.Length || source[index] is not ('"' or '\''))
            return start + 1;

        var quote = source[index];
        var quotes = 0;
        while (index < source.Length && source[index] == quote)
        {
            quotes++;
            index++;
        }

        if (quote == '"' && quotes >= 3)
        {
            var fence = new string('"', quotes);
            var close = source.IndexOf(fence, index, StringComparison.Ordinal);
            var end = close < 0 ? source.Length : close + quotes;
            Blank(masked, source, start, end);
            return end;
        }

        // An empty literal ("" or '') already consumed both quotes.
        if (quotes >= 2)
        {
            Blank(masked, source, start, index);
            return index;
        }

        while (index < source.Length)
        {
            var current = source[index];
            if (!verbatim && current == '\\')
            {
                index += 2;
                continue;
            }

            if (verbatim && current == quote && index + 1 < source.Length && source[index + 1] == quote)
            {
                index += 2;
                continue;
            }

            index++;
            if (current == quote)
                break;

            if (!verbatim && current == '\n')
                break;
        }

        var stop = Math.Min(index, source.Length);
        Blank(masked, source, start, stop);
        return stop;
    }

    private static void Blank(StringBuilder masked, string source, int start, int end)
    {
        for (var i = start; i < end && i < source.Length; i++)
            masked[i] = source[i] == '\n' ? '\n' : ' ';
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
