namespace Elsa.Rules.Core;

/// <summary>Immutable resource limits for the finite-positive Rules v1 contract.</summary>
public static class RuleLimits
{
    public const int MaxRules = 1024;
    public const int MaxVocabularyFacts = 4096;
    public const int MaxPremisesPerRule = 64;
    public const int MaxIdentifierUtf8Bytes = 256;
    public const int MaxArtifactBytes = 1_048_576;
    public const int MaxEnvelopeBytes = 4_194_304;
    public const int MaxStateProjectionBytes = 8_388_608;
    public const int MaxStateMetadataBytes = 65_536;
    public const int MaxPageEntries = 256;
    public const int MaxRounds = 4097;
}

/// <summary>Stable identifier and version tuple for the built-in finite-positive language.</summary>
public static class RuleLanguage
{
    public const string Id = "elsa.rules.finite-positive";
    public const int Version = 1;
    public const int FormatVersion = 1;
}
