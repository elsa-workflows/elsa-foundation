using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;

namespace Elsa.Rules.Workflows.Runtime.Models;

/// <summary>The publication-owned artifact, implementation identity and finite budget metadata.</summary>
/// <remarks>Runtime reads this frozen contract without recompiling the program or selecting a deployment.</remarks>
public sealed record RuleWorkflowProgramPin
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 24
    };

    public RuleWorkflowProgramPin(int formatVersion, string sourceSha256, RuleArtifactSnapshot artifact,
        RuleSemanticPin implementation, int vocabularyCount)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(implementation);
        if (formatVersion != 1 || artifact.FormatVersion != 1 || implementation.PinSchemaVersion != 1 ||
            implementation.LanguageId != RuleLanguage.Id || implementation.LanguageVersion != 1 ||
            implementation.ReceiptSchemaVersion != 1 || implementation.StateSchemaVersion != 1)
            throw new NotSupportedException("The published Rules program requires an unsupported contract version.");
        if (vocabularyCount is < 0 or > RuleLimits.MaxVocabularyFacts)
            throw new ArgumentOutOfRangeException(nameof(vocabularyCount));
        _ = new RuleInvocationFingerprint(sourceSha256);
        if (artifact.ArtifactId != "rules:" + artifact.Sha256 ||
            RuleRecordCodec.Hash(artifact.CanonicalBytes.Span) != artifact.Sha256)
            throw new InvalidDataException("The published Rules artifact does not match its identity.");
        _ = RuleRecordCodec.PinHash(implementation);
        FormatVersion = formatVersion;
        SourceSha256 = sourceSha256;
        Artifact = artifact;
        Implementation = implementation;
        VocabularyCount = vocabularyCount;
    }

    public int FormatVersion { get; }
    public string SourceSha256 { get; }
    public RuleArtifactSnapshot Artifact { get; }
    public RuleSemanticPin Implementation { get; }
    public int VocabularyCount { get; }

    /// <summary>Hashes the exact authored literal, including whitespace, without interpreting it.</summary>
    public static string SourceHash(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length > RuleLimits.MaxArtifactBytes || Utf8.GetByteCount(source) > RuleLimits.MaxArtifactBytes)
            throw new InvalidDataException("The authored Rules program exceeds its byte limit.");
        return RuleRecordCodec.Hash(Utf8.GetBytes(source));
    }

    public string Serialize()
    {
        var json = JsonSerializer.Serialize(this, Options);
        EnsureSize(json);
        return json;
    }

    public static RuleWorkflowProgramPin Parse(string json, string source)
    {
        EnsureSize(json);
        var value = JsonSerializer.Deserialize<RuleWorkflowProgramPin>(json, Options)
            ?? throw new InvalidDataException("The published Rules program is missing.");
        if (value.SourceSha256 != SourceHash(source))
            throw new InvalidDataException("The published Rules program does not match the authored literal.");
        return value;
    }

    private static void EnsureSize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > RuleLimits.MaxEnvelopeBytes || Utf8.GetByteCount(json) > RuleLimits.MaxEnvelopeBytes)
            throw new InvalidDataException("The published Rules program exceeds its envelope limit.");
    }
}
