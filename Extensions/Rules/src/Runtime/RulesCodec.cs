using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;

namespace Elsa.Rules.Runtime;

/// <summary>Version-one canonical encoding. Field order and ordinal set order are part of the persisted contract.</summary>
internal static class RulesCodec
{
    internal const string LanguageId = "elsa.rules.finite-positive";

    public static RuleArtifactSnapshot CreateArtifact(RuleProgram program)
    {
        ValidateProgram(program);
        var bytes = Write(writer => WriteProgram(writer, program));
        Require(bytes.Length <= RuleLimits.MaxArtifactBytes, "rules.artifact-too-large");
        var hash = Hash(bytes);
        return new RuleArtifactSnapshot(1, "rules:" + hash, hash, bytes);
    }

    public static RuleProgram ReadArtifact(RuleArtifactSnapshot artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        var bytes = artifact.CanonicalBytes;
        Require(bytes.Length <= RuleLimits.MaxArtifactBytes, "rules.artifact-too-large");
        Require(Hash(bytes.Span) == artifact.Sha256 && artifact.ArtifactId == "rules:" + artifact.Sha256,
            "rules.artifact-identity");
        if (artifact.FormatVersion != 1) throw new NotSupportedException("rules.artifact-version");
        using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 12 });
        var root = json.RootElement;
        var program = new RuleProgram(root.GetProperty("formatVersion").GetInt32(),
            root.GetProperty("languageId").GetString()!, root.GetProperty("languageVersion").GetInt32(),
            ReadFacts(root.GetProperty("vocabulary")),
            root.GetProperty("rules").EnumerateArray().Select(rule => new RuleDefinition(
                rule.GetProperty("id").GetString()!, ReadFacts(rule.GetProperty("premises")),
                new RuleFact(rule.GetProperty("conclusion").GetString()!))).ToArray());
        var canonical = CreateArtifact(program);
        Require(canonical.CanonicalBytes.Span.SequenceEqual(bytes.Span), "rules.noncanonical-artifact");
        return program;
    }

    public static RuleFact[] CanonicalFacts(IEnumerable<RuleFact> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        // Bound enumeration too: an unbounded host enumerable cannot allocate without limit.
        var copy = facts.Take(RuleLimits.MaxVocabularyFacts + 1).ToArray();
        Require(copy.Length <= RuleLimits.MaxVocabularyFacts, "rules.too-many-facts");
        foreach (var fact in copy) RuleValueValidation.Identifier(fact?.Atom!, nameof(facts));
        Require(copy.Select(fact => fact.Atom).Distinct(StringComparer.Ordinal).Count() == copy.Length,
            "rules.duplicate-fact");
        return copy.OrderBy(fact => fact.Atom, StringComparer.Ordinal).ToArray();
    }

    public static string FactsHash(IReadOnlyList<RuleFact> facts) => Hash(Write(writer => WriteFacts(writer, facts)));

    public static int ResolveMaxRounds(RuleProgram program, IReadOnlyList<RuleFact> initialFacts, int? requested)
    {
        ValidateProgram(program);
        var facts = CanonicalFacts(initialFacts);
        var vocabulary = program.Vocabulary.Select(fact => fact.Atom).ToHashSet(StringComparer.Ordinal);
        Require(facts.All(fact => vocabulary.Contains(fact.Atom)), "rules.fact-outside-vocabulary");
        var limit = requested ?? vocabulary.Count - facts.Length + 1;
        Require(limit is > 0 and <= RuleLimits.MaxRounds, "rules.invalid-round-limit");
        return limit;
    }

    public static string PinHash(RuleSemanticPin pin) => Hash(Write(writer => WritePin(writer, pin)));

    public static byte[] ReceiptBytes(RuleRoundReceipt receipt) => Write(writer =>
    {
        writer.WriteStartObject();
        writer.WritePropertyName("identity");
        WriteRoundIdentity(writer, receipt.Identity);
        writer.WriteString("implementationPinHash", receipt.ImplementationPinHash);
        writer.WritePropertyName("addedFacts");
        WriteFacts(writer, receipt.AddedFacts);
        writer.WriteStartArray("firings");
        foreach (var firing in receipt.FiringEvidence)
        {
            writer.WriteStartObject();
            writer.WriteString("ruleId", firing.RuleId);
            writer.WriteString("conclusion", firing.Conclusion.Atom);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    });

    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static void ValidateProgram(RuleProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (program.FormatVersion != 1 || program.LanguageId != LanguageId || program.LanguageVersion != 1)
            throw new NotSupportedException("rules.language-version");
        var vocabulary = CanonicalFacts(program.Vocabulary).Select(fact => fact.Atom).ToHashSet(StringComparer.Ordinal);
        Require(program.Rules.Count <= RuleLimits.MaxRules, "rules.too-many-rules");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in program.Rules)
        {
            Require(rule is not null, "rules.null-rule");
            RuleValueValidation.Identifier(rule!.Id, nameof(program));
            Require(ids.Add(rule.Id), "rules.duplicate-rule");
            Require(rule.Premises.Count <= RuleLimits.MaxPremisesPerRule, "rules.too-many-premises");
            var premises = CanonicalFacts(rule.Premises);
            RuleValueValidation.Identifier(rule.Conclusion?.Atom!, nameof(program));
            Require(premises.All(fact => vocabulary.Contains(fact.Atom)) && vocabulary.Contains(rule.Conclusion!.Atom),
                "rules.rule-outside-vocabulary");
        }
    }

    private static RuleFact[] ReadFacts(JsonElement array) => array.EnumerateArray()
        .Take(RuleLimits.MaxVocabularyFacts + 1).Select(value => new RuleFact(value.GetString()!)).ToArray();

    private static void WriteProgram(Utf8JsonWriter writer, RuleProgram program)
    {
        writer.WriteStartObject();
        writer.WriteNumber("formatVersion", program.FormatVersion);
        writer.WriteString("languageId", program.LanguageId);
        writer.WriteNumber("languageVersion", program.LanguageVersion);
        writer.WritePropertyName("vocabulary");
        WriteFacts(writer, program.Vocabulary.OrderBy(fact => fact.Atom, StringComparer.Ordinal));
        writer.WriteStartArray("rules");
        foreach (var rule in program.Rules.OrderBy(rule => rule.Id, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("id", rule.Id);
            writer.WritePropertyName("premises");
            WriteFacts(writer, rule.Premises.OrderBy(fact => fact.Atom, StringComparer.Ordinal));
            writer.WriteString("conclusion", rule.Conclusion.Atom);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteFacts(Utf8JsonWriter writer, IEnumerable<RuleFact> facts)
    {
        writer.WriteStartArray();
        foreach (var fact in facts) writer.WriteStringValue(fact.Atom);
        writer.WriteEndArray();
    }

    private static void WriteRoundIdentity(Utf8JsonWriter writer, RuleRoundIdentity identity)
    {
        writer.WriteStartObject();
        writer.WriteString("tenant", identity.Run.Scope.TenantId);
        writer.WriteString("partition", identity.Run.Scope.PersistencePartition);
        writer.WriteString("invocation", identity.Run.InvocationId);
        writer.WriteString("fingerprint", identity.Fingerprint.Sha256);
        writer.WriteNumber("ordinal", identity.Ordinal);
        writer.WriteString("inputStateSha256", identity.InputStateSha256);
        writer.WriteEndObject();
    }

    private static void WritePin(Utf8JsonWriter writer, RuleSemanticPin pin)
    {
        writer.WriteStartObject();
        writer.WriteNumber("pinSchemaVersion", pin.PinSchemaVersion);
        writer.WriteString("languageId", pin.LanguageId);
        writer.WriteNumber("languageVersion", pin.LanguageVersion);
        writer.WriteNumber("receiptSchemaVersion", pin.ReceiptSchemaVersion);
        writer.WriteNumber("stateSchemaVersion", pin.StateSchemaVersion);
        writer.WriteStartArray("packages");
        foreach (var package in pin.Packages.OrderBy(package => package.PackageId, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("id", package.PackageId);
            writer.WriteString("version", package.NuGetVersion);
            writer.WriteString("contentHash", package.NuGetContentHash);
            writer.WriteStartArray("assets");
            foreach (var asset in package.SelectedAssets.OrderBy(asset => asset.PackageRelativeAssetPath, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("path", asset.PackageRelativeAssetPath);
                writer.WriteString("assembly", asset.AssemblyFullName);
                writer.WriteString("sha256", asset.AssetSha256);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static byte[] Write(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) write(writer);
        return stream.ToArray();
    }

    private static void Require(bool condition, string code)
    {
        if (!condition) throw new InvalidDataException(code);
    }
}
