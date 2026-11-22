using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;

namespace Elsa.Architecture.Analyzers;

/// <summary>
/// The retired terms one source file is held to, parsed from the value of
/// <see cref="RetiredTermAnalyzer.ConfigurationKey"/>.
/// </summary>
/// <remarks>
/// <para>
/// The value is a comma-separated list of entries. Each entry names the spellings of one retired term, separated by
/// <c>|</c>, then <c>=</c> and the replacement to suggest: <c>Pump|Pumps=Worker, Stimulus|Stimuli=Signal</c>. A
/// spelling is one or more PascalCase words, and it matches a name only as a whole run of the name's words, so
/// <c>Stamp</c> matches <c>SchemaStamp</c> and never <c>Timestamp</c>. Every inflection that should count is listed
/// as its own spelling; nothing is derived.
/// </para>
/// <para>
/// Separators are <c>,</c>, <c>|</c> and <c>=</c> rather than <c>;</c> or <c>#</c>, because .editorconfig treats
/// those two as the start of a comment.
/// </para>
/// </remarks>
internal sealed class RetiredTermList
{
    private static readonly Regex SpellingPattern = new("^[A-Z][A-Za-z0-9]*$", RegexOptions.CultureInvariant);
    private static readonly Regex WordPattern = new("[A-Z]+(?=[A-Z][a-z])|[A-Z]?[a-z]+|[A-Z]+|[0-9]+", RegexOptions.CultureInvariant);

    private RetiredTermList(ImmutableArray<RetiredTerm> terms, string? error)
    {
        Terms = terms;
        Error = error;
    }

    /// <summary>The configured terms, in configuration order. Empty when the value is blank or invalid.</summary>
    public ImmutableArray<RetiredTerm> Terms { get; }

    /// <summary>Why the value could not be parsed, or <see langword="null"/> when it parsed.</summary>
    public string? Error { get; }

    /// <summary>Parses a configuration value. A blank value is an empty list, not an error.</summary>
    public static RetiredTermList Parse(string? value)
    {
        if (value is null || value.Trim().Length == 0)
            return new RetiredTermList(ImmutableArray<RetiredTerm>.Empty, null);

        var terms = ImmutableArray.CreateBuilder<RetiredTerm>();
        foreach (var rawEntry in value.Split(','))
        {
            var entry = rawEntry.Trim();
            var separator = entry.IndexOf('=');
            if (separator <= 0 || separator != entry.LastIndexOf('='))
                return Invalid($"entry '{entry}' must read 'Spelling|Spelling=Replacement' with exactly one '='");

            var replacement = entry.Substring(separator + 1).Trim();
            if (replacement.Length == 0 || replacement.IndexOf('|') >= 0)
                return Invalid($"entry '{entry}' names no replacement, or a replacement containing '|'");

            var spellings = ImmutableArray.CreateBuilder<Spelling>();
            foreach (var rawSpelling in entry.Substring(0, separator).Split('|'))
            {
                var spelling = rawSpelling.Trim();
                if (!SpellingPattern.IsMatch(spelling))
                    return Invalid($"spelling '{spelling}' in entry '{entry}' is not a PascalCase word");

                spellings.Add(new Spelling(spelling, Words(spelling)));
            }

            terms.Add(new RetiredTerm(spellings.ToImmutable(), replacement));
        }

        return new RetiredTermList(terms.ToImmutable(), null);
    }

    /// <summary>The retired spellings a name contains, each with its term's replacement, in configuration order.</summary>
    public IEnumerable<(string Spelling, string Replacement)> Find(string name)
    {
        if (Terms.IsEmpty)
            yield break;

        var words = Words(name);
        foreach (var term in Terms)
        {
            var match = term.Spellings.FirstOrDefault(spelling => Contains(words, spelling.Words));
            if (match is not null)
                yield return (match.Text, term.Replacement);
        }
    }

    /// <summary>The PascalCase words of an identifier: <c>HTTPServerPumpTask</c> is HTTP, Server, Pump, Task.</summary>
    public static ImmutableArray<string> Words(string identifier) =>
        WordPattern.Matches(identifier).Cast<Match>().Select(match => match.Value).ToImmutableArray();

    private static bool Contains(ImmutableArray<string> words, ImmutableArray<string> run)
    {
        for (var start = 0; start + run.Length <= words.Length; start++)
        {
            var matches = true;
            for (var offset = 0; offset < run.Length && matches; offset++)
                matches = string.Equals(words[start + offset], run[offset], StringComparison.OrdinalIgnoreCase);

            if (matches)
                return true;
        }

        return false;
    }

    private static RetiredTermList Invalid(string error) => new(ImmutableArray<RetiredTerm>.Empty, error);
}

/// <summary>One retired term: every spelling that counts as it, and the replacement to suggest.</summary>
internal sealed class RetiredTerm(ImmutableArray<Spelling> spellings, string replacement)
{
    public ImmutableArray<Spelling> Spellings { get; } = spellings;

    public string Replacement { get; } = replacement;
}

/// <summary>A spelling of a retired term, with its PascalCase words.</summary>
internal sealed class Spelling(string text, ImmutableArray<string> words)
{
    public string Text { get; } = text;

    public ImmutableArray<string> Words { get; } = words;
}
