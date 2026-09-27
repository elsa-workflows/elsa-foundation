using System.Text;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

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
/// <para>
/// #2108: within-condition ordering is not enough. Four stores called <c>RuntimeArtifactJson.Deserialize</c>
/// on a row's content in a statement <em>earlier than</em> the condition that checked its version, so the
/// version term being first within its own condition never ran before the deserializer had already thrown
/// on the changed shape a newer schema version wrote. The second guard below widens the rule to: no
/// <c>RuntimeArtifactJson.Deserialize</c> call may precede the version check anywhere in its enclosing
/// method, not just within the condition that holds the check.
/// </para>
/// </summary>
public sealed class EfSchemaVersionOrderingGuardTests
{
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

    /// <summary>
    /// #2108: <c>EfExecutionLivenessStateStore.Read</c>, <c>EfWorkflowHoldStateStore.Read</c>,
    /// <c>EfWorkflowAlterationStore.ReadPlan</c>, and <c>WorkflowTestScopeEfSupport.Read</c> each had the
    /// version term first in its own condition (the first guard above accepted all four), but deserialized
    /// the row's content in an earlier statement that ran unconditionally. This widened scan catches that:
    /// no deserialize call may precede the version check anywhere in the method that holds it.
    /// </summary>
    [Fact]
    public void No_deserialization_of_row_content_precedes_the_version_check_anywhere_in_its_method()
    {
        var sourceRoot = Path.Combine(RepoRoot, "src");
        var violations = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .SelectMany(file => FindDeserializeBeforeVersionCheck(File.ReadAllText(file))
                .Select(line => $"{Path.GetRelativePath(RepoRoot, file)}({line})"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "RuntimeArtifactJson.Deserialize must not run before EfSchemaVersion.Readable/NotReadable " +
            "anywhere in the same method, or a skewed row's changed shape is reported as corruption before " +
            "the version check ever gets to run (#2108). Offending call sites:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Widened_guard_examines_the_stores_number_2108_fixed()
    {
        var sourceRoot = Path.Combine(RepoRoot, "src");
        var deserializeCallSitesByFile = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .GroupBy(Path.GetFileName)
            .ToDictionary(group => group.Key!, group => group.Sum(file => CountDeserializeCalls(File.ReadAllText(file))));

        // If any of these were renamed or merged elsewhere, this floor keeps the widened scan honest about
        // still reaching the exact call sites #2108 fixed, rather than passing because it stopped looking.
        string[] storesFixedByIssue2108 =
        [
            "EfExecutionLivenessStateStore.cs",
            "EfWorkflowHoldStateStore.cs",
            "EfWorkflowAlterationStore.cs",
            "WorkflowTestScopeEfSupport.cs"
        ];

        foreach (var store in storesFixedByIssue2108)
        {
            Assert.True(
                deserializeCallSitesByFile.TryGetValue(store, out var count) && count > 0,
                $"Expected the widened scan to examine '{store}' - one of the four stores #2108 fixed - and find at least one RuntimeArtifactJson.Deserialize call site in it.");
        }
    }

    [Theory]
    [MemberData(nameof(DeserializeBeforeCheckFixtures))]
    public void Widened_detector_flags_a_deserialize_call_earlier_in_the_method_than_the_version_check(string name, string source)
    {
        Assert.True(FindDeserializeBeforeVersionCheck(source).Length > 0, $"The widened detector missed the fixture '{name}'.");
    }

    [Theory]
    [MemberData(nameof(DeserializeAfterCheckFixtures))]
    public void Widened_detector_accepts_a_deserialize_call_that_follows_the_version_check(string name, string source)
    {
        Assert.True(FindDeserializeBeforeVersionCheck(source).Length == 0, $"The widened detector wrongly flagged the fixture '{name}'.");
    }

    [Theory]
    [MemberData(nameof(InOrderFixtures))]
    public void Widened_detector_still_accepts_every_original_in_order_fixture(string name, string source)
    {
        Assert.True(FindDeserializeBeforeVersionCheck(source).Length == 0, $"The widened detector wrongly flagged the original in-order fixture '{name}'.");
    }

    public static TheoryData<string, string> DeserializeBeforeCheckFixtures() => new()
    {
        {
            "deserialize as an earlier statement, version term still first within its own condition",
            """
            var state = RuntimeArtifactJson.Deserialize<State>(row.ContentJson);
            var valid =
                EfSchemaVersion.Readable("M", row.SchemaVersion, Module.SchemaVersion) &&
                row.Id == state.Id;
            if (!valid)
                throw new InvalidDataException("corrupt");
            """
        },
        {
            "deserialize inside an earlier try block, guard clause runs afterwards",
            """
            Row state;
            try { state = RuntimeArtifactJson.Deserialize<Row>(row.ContentJson); }
            catch (JsonException exception) { throw new InvalidDataException("bad json", exception); }
            if (EfSchemaVersion.NotReadable("M", row.SchemaVersion, Module.SchemaVersion) || row.Id != state.Id)
                throw new InvalidDataException("corrupt");
            """
        }
    };

    public static TheoryData<string, string> DeserializeAfterCheckFixtures() => new()
    {
        {
            "version check first, deserialize follows as the next statement",
            """
            if (EfSchemaVersion.NotReadable("M", row.SchemaVersion, Module.SchemaVersion) || row.Revision <= 0)
                throw new InvalidDataException("corrupt");
            var state = RuntimeArtifactJson.Deserialize<Row>(row.ContentJson);
            return state;
            """
        },
        {
            "version check and deserialize both inside the same wrapping try, check first",
            """
            try
            {
                if (EfSchemaVersion.NotReadable("M", row.SchemaVersion, Module.SchemaVersion) || row.Revision <= 0)
                    throw new InvalidDataException("corrupt");
                var state = RuntimeArtifactJson.Deserialize<Row>(row.ContentJson);
                return state;
            }
            catch (JsonException exception) { throw new InvalidDataException("bad json", exception); }
            """
        },
        {
            "an unrelated deserialize call in a different, earlier method",
            """
            private static Cursor DecodeCursor(string token) => RuntimeArtifactJson.Deserialize<Cursor>(token);

            private static Row Read(Entity row, string scope)
            {
                if (EfSchemaVersion.NotReadable("M", row.SchemaVersion, Module.SchemaVersion) || row.Revision <= 0)
                    throw new InvalidDataException("corrupt");
                return Project(row);
            }
            """
        }
    };

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
    private const string DeserializeToken = "RuntimeArtifactJson.Deserialize";

    private static int CountDeserializeCalls(string source)
    {
        var masked = MaskLiteralsAndComments(source);
        var count = 0;
        var index = masked.IndexOf(DeserializeToken, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = masked.IndexOf(DeserializeToken, index + DeserializeToken.Length, StringComparison.Ordinal);
        }

        return count;
    }

    /// <summary>
    /// Returns the one-based line number of every <c>EfSchemaVersion.Readable</c> /
    /// <c>NotReadable</c> call that has a <c>RuntimeArtifactJson.Deserialize</c> call earlier in its
    /// enclosing method - whether that deserialize call sits in an earlier statement, an earlier
    /// nested <c>try</c>/<c>if</c>/<c>using</c> block, or the same condition <see cref="HasEarlierClause"/>
    /// already covers.
    /// </summary>
    private static int[] FindDeserializeBeforeVersionCheck(string source)
    {
        var masked = MaskLiteralsAndComments(source);
        var violations = new List<int>();
        foreach (var token in CallTokens)
        {
            var index = masked.IndexOf(token, StringComparison.Ordinal);
            while (index >= 0)
            {
                var methodStart = FindEnclosingMethodStart(masked, index);
                if (masked.IndexOf(DeserializeToken, methodStart, index - methodStart, StringComparison.Ordinal) >= 0)
                    violations.Add(LineOf(source, index));

                index = masked.IndexOf(token, index + token.Length, StringComparison.Ordinal);
            }
        }

        return violations.Order().ToArray();
    }

    /// <summary>
    /// Walks outward from <paramref name="position"/> through nested control-flow blocks (<c>try</c>,
    /// <c>catch</c>, <c>finally</c>, <c>if</c>, <c>while</c>, <c>for</c>, <c>foreach</c>, <c>using</c>,
    /// <c>lock</c>, <c>switch</c>, <c>else</c>, <c>do</c>, <c>checked</c>, <c>unchecked</c>, <c>fixed</c>,
    /// <c>unsafe</c>) until it reaches the block that is <paramref name="position"/>'s enclosing method,
    /// local function, lambda, or accessor body, and returns the index just past that block's opening
    /// brace. Returns <c>0</c> when no enclosing brace exists at all, so a bare statement fixture with no
    /// wrapping method is treated as the whole method body.
    /// </summary>
    private static int FindEnclosingMethodStart(string masked, int position)
    {
        while (true)
        {
            var brace = FindNearestEnclosingBrace(masked, position);
            if (brace < 0)
                return 0;

            if (!IsControlBlockBrace(masked, brace))
                return brace + 1;

            position = brace;
        }
    }

    /// <summary>Finds the index of the nearest unmatched '{' walking backward from <paramref name="position"/>.</summary>
    private static int FindNearestEnclosingBrace(string masked, int position)
    {
        var depth = 0;
        for (var index = position - 1; index >= 0; index--)
        {
            var current = masked[index];
            if (current == '}')
            {
                depth++;
            }
            else if (current == '{')
            {
                if (depth > 0)
                    depth--;
                else
                    return index;
            }
        }

        return -1;
    }

    private static readonly string[] ControlBlockKeywords =
        ["try", "finally", "else", "do", "unsafe", "checked", "unchecked", "fixed"];

    private static readonly string[] ControlConditionKeywords =
        ["if", "while", "for", "foreach", "using", "lock", "catch", "switch"];

    /// <summary>
    /// True when the '{' at <paramref name="brace"/> opens a control-flow block rather than a member,
    /// local-function, lambda, or accessor body - i.e. <see cref="FindEnclosingMethodStart"/> should keep
    /// stepping outward past it.
    /// </summary>
    private static bool IsControlBlockBrace(string masked, int brace)
    {
        var start = brace;
        while (start > 0 && char.IsWhiteSpace(masked[start - 1]))
            start--;

        if (start > 0 && masked[start - 1] == ')')
        {
            var openParen = FindMatchingOpenParen(masked, start - 1);
            return openParen >= 0 && PrecedingWordIsOneOf(masked, openParen, ControlConditionKeywords);
        }

        return PrecedingWordIsOneOf(masked, start, ControlBlockKeywords);
    }

    private static int FindMatchingOpenParen(string masked, int closeParenIndex)
    {
        var depth = 0;
        for (var index = closeParenIndex; index >= 0; index--)
        {
            var current = masked[index];
            if (current == ')')
                depth++;
            else if (current == '(')
            {
                depth--;
                if (depth == 0)
                    return index;
            }
        }

        return -1;
    }

    private static bool PrecedingWordIsOneOf(string masked, int end, string[] keywords)
    {
        var index = end;
        while (index > 0 && char.IsWhiteSpace(masked[index - 1]))
            index--;

        var wordEnd = index;
        var wordStart = index;
        while (wordStart > 0 && (char.IsLetterOrDigit(masked[wordStart - 1]) || masked[wordStart - 1] == '_'))
            wordStart--;

        return wordStart != wordEnd && keywords.Contains(masked[wordStart..wordEnd], StringComparer.Ordinal);
    }

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
}
