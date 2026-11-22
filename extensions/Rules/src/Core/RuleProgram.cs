using System.Collections.ObjectModel;
using System.Text;

namespace Elsa.Rules.Core;

/// <summary>An ordinal, nonempty atom in the finite Rules vocabulary.</summary>
public sealed record RuleFact
{
    public RuleFact(string atom)
    {
        RuleValueValidation.Identifier(atom, nameof(atom));
        Atom = atom;
    }

    public string Atom { get; }
}

/// <summary>A propositional rule whose premises jointly imply one vocabulary fact.</summary>
/// <remarks>An empty premise list is the true empty conjunction and is permitted.</remarks>
public sealed class RuleDefinition
{
    private readonly ReadOnlyCollection<RuleFact> _premises;

    public RuleDefinition(string id, IReadOnlyList<RuleFact> premises, RuleFact conclusion)
    {
        RuleValueValidation.Identifier(id, nameof(id));
        ArgumentNullException.ThrowIfNull(premises);
        ArgumentNullException.ThrowIfNull(conclusion);
        if (premises.Count > RuleLimits.MaxPremisesPerRule)
            throw new ArgumentOutOfRangeException(nameof(premises), "A rule exceeds the finite v1 premise limit.");

        var copy = premises.ToArray();
        if (copy.Any(static fact => fact is null))
            throw new ArgumentException("Rule premises cannot contain null facts.", nameof(premises));
        Id = id;
        _premises = Array.AsReadOnly(copy);
        Conclusion = conclusion;
    }

    public string Id { get; }
    public IReadOnlyList<RuleFact> Premises => _premises;
    public RuleFact Conclusion { get; }
}

/// <summary>An immutable finite-positive program carrying an explicit vocabulary.</summary>
/// <remarks>
/// The vocabulary is explicit even when empty. Initial facts belong to an invocation, not to this
/// reusable artifact. This value preserves supplied order; canonical ordering and semantic
/// validation, including vocabulary membership, belong to Runtime.
/// </remarks>
public sealed class RuleProgram
{
    private readonly ReadOnlyCollection<RuleFact> _vocabulary;
    private readonly ReadOnlyCollection<RuleDefinition> _rules;

    public RuleProgram(
        int formatVersion,
        string languageId,
        int languageVersion,
        IReadOnlyList<RuleFact> vocabulary,
        IReadOnlyList<RuleDefinition> rules)
    {
        if (formatVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(formatVersion));
        RuleValueValidation.Identifier(languageId, nameof(languageId));
        if (languageVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(languageVersion));
        ArgumentNullException.ThrowIfNull(vocabulary);
        ArgumentNullException.ThrowIfNull(rules);
        if (vocabulary.Count > RuleLimits.MaxVocabularyFacts)
            throw new ArgumentOutOfRangeException(nameof(vocabulary), "The vocabulary exceeds the finite v1 limit.");
        if (rules.Count > RuleLimits.MaxRules)
            throw new ArgumentOutOfRangeException(nameof(rules), "The program exceeds the finite v1 rule limit.");

        var vocabularyCopy = vocabulary.ToArray();
        var rulesCopy = rules.ToArray();
        if (vocabularyCopy.Any(static fact => fact is null))
            throw new ArgumentException("The vocabulary cannot contain null facts.", nameof(vocabulary));
        if (rulesCopy.Any(static rule => rule is null))
            throw new ArgumentException("A program cannot contain null rules.", nameof(rules));

        FormatVersion = formatVersion;
        LanguageId = languageId;
        LanguageVersion = languageVersion;
        _vocabulary = Array.AsReadOnly(vocabularyCopy);
        _rules = Array.AsReadOnly(rulesCopy);
    }

    public int FormatVersion { get; }
    public string LanguageId { get; }
    public int LanguageVersion { get; }
    public IReadOnlyList<RuleFact> Vocabulary => _vocabulary;
    public IReadOnlyList<RuleDefinition> Rules => _rules;
}

/// <summary>Shared constructor validation for immutable Rules contract values.</summary>
internal static class RuleValueValidation
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static void Identifier(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length == 0)
            throw new ArgumentException("The value must be nonempty.", parameterName);

        int byteCount;
        try
        {
            byteCount = StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException("The value must contain valid Unicode.", parameterName, exception);
        }

        if (byteCount > RuleLimits.MaxIdentifierUtf8Bytes)
            throw new ArgumentOutOfRangeException(parameterName, "The value exceeds the UTF-8 byte limit.");
    }

    internal static void Sha256(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length != 64 || value.Any(static character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("A SHA-256 identity must be 64 lowercase hexadecimal characters.", parameterName);
    }
}
