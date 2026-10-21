using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Rules.Core;

namespace Elsa.Rules.Persistence.Core;

/// <summary>Stable common framing; identity and cancellation do not interpret a program or select a deployment.</summary>
public static partial class RuleRecordCodec
{
    public static byte[] ScopeBytes(RuleRunIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            // Versioned little-endian Int32 lengths; -1 is the distinct null-tenant tag.
            writer.Write(1);
            WritePart(identity.Scope.TenantId);
            WritePart(identity.Scope.PersistencePartition);
            WritePart(identity.InvocationId);

            void WritePart(string? value)
            {
                if (value is null) { writer.Write(-1); return; }
                var bytes = Encoding.UTF8.GetBytes(value);
                writer.Write(bytes.Length);
                writer.Write(bytes);
            }
        }
        return stream.ToArray();
    }

    public static byte[] InvocationBytes(RuleInvocationEnvelope invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        if (invocation.InvocationSchemaVersion != 1)
            throw new NotSupportedException("rules.invocation-schema");
        var artifact = invocation.Artifact;
        var artifactBytes = artifact.CanonicalBytes;
        if (Hash(artifactBytes.Span) != artifact.Sha256 || artifact.ArtifactId != "rules:" + artifact.Sha256)
            throw new InvalidDataException("rules.artifact-identity");
        var facts = CanonicalFacts(invocation.InitialFacts);
        ValidatePinShape(invocation.ExpectedImplementation);
        if (invocation.NativeDestination is { FormatVersion: not 1 })
            throw new NotSupportedException("rules.destination-schema");

        return RuleJson.Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("invocationSchemaVersion", invocation.InvocationSchemaVersion);
            writer.WritePropertyName("identity");
            WriteIdentity(writer, invocation.Identity);
            writer.WriteStartObject("artifact");
            writer.WriteNumber("formatVersion", artifact.FormatVersion);
            writer.WriteString("id", artifact.ArtifactId);
            writer.WriteString("sha256", artifact.Sha256);
            // Preserve opaque bytes, even when this deployment cannot interpret their language.
            writer.WriteBase64String("bytes", artifactBytes.Span);
            writer.WriteEndObject();
            writer.WritePropertyName("initialFacts");
            WriteFacts(writer, facts);
            writer.WriteNumber("maxRounds", invocation.MaxRounds);
            writer.WritePropertyName("implementation");
            WritePin(writer, invocation.ExpectedImplementation);
            writer.WritePropertyName("nativeDestination");
            WriteDestination(writer, invocation.NativeDestination);
            writer.WriteEndObject();
        }, RuleLimits.MaxEnvelopeBytes);
    }

    public static RuleInvocationFingerprint Fingerprint(RuleInvocationEnvelope invocation) =>
        new(Hash(InvocationBytes(invocation)));

    public static byte[] TerminalBytes(RuleTerminalResult result) => RuleJson.Write(writer =>
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!Enum.IsDefined(result.Kind))
            throw new InvalidDataException("rules.terminal-kind");
        if (result.ResultSchemaVersion != 1)
            throw new NotSupportedException("rules.terminal-schema");
        var facts = CanonicalFacts(result.ResultFacts);
        if (FactsHash(facts) != result.FinalFactsSha256)
            throw new InvalidDataException("rules.terminal-facts-digest");
        writer.WriteStartObject();
        writer.WriteString("terminalId", result.TerminalId);
        writer.WriteString("kind", result.Kind.ToString());
        writer.WriteNumber("committedRounds", result.CommittedRounds);
        writer.WriteNumber("resultSchemaVersion", result.ResultSchemaVersion);
        writer.WritePropertyName("facts");
        WriteFacts(writer, facts);
        writer.WriteString("factsSha256", result.FinalFactsSha256);
        writer.WriteString("diagnostic", result.Diagnostic?.Code);
        writer.WriteEndObject();
    }, RuleLimits.MaxEnvelopeBytes);

    public static byte[] DeliveryBytes(string deliveryId, RuleNativeResultAddress destination, RuleTerminalResult result)
    {
        RuleValueValidation.Identifier(deliveryId, nameof(deliveryId));
        ArgumentNullException.ThrowIfNull(destination);
        var terminal = TerminalBytes(result);
        return RuleJson.Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("deliveryId", deliveryId);
            writer.WritePropertyName("destination");
            WriteDestination(writer, destination);
            writer.WritePropertyName("result");
            writer.WriteRawValue(terminal);
            writer.WriteEndObject();
        }, RuleLimits.MaxEnvelopeBytes);
    }

    public static byte[] DeliveryRecordBytes(string deliveryId, RuleNativeResultAddress destination, RuleTerminalResult result)
    {
        var body = DeliveryBytes(deliveryId, destination, result);
        return RuleJson.Write(writer =>
        {
            writer.WriteStartObject();
            writer.WritePropertyName("delivery");
            writer.WriteRawValue(body);
            writer.WriteString("canonicalPayloadSha256", Hash(body));
            writer.WriteEndObject();
        }, RuleLimits.MaxEnvelopeBytes);
    }

    private static void ValidatePinShape(RuleSemanticPin pin)
    {
        var packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (pin.Packages.Count == 0) throw new InvalidDataException("rules.empty-package-pin");
        Span<byte> hash = stackalloc byte[64];
        foreach (var package in pin.Packages)
        {
            if (!packages.Add(package.PackageId) || package.SelectedAssets.Count == 0)
                throw new InvalidDataException("rules.ambiguous-package-pin");
            if (!Convert.TryFromBase64String(package.NuGetContentHash, hash, out var count) || count != 64
                || Convert.ToBase64String(hash) != package.NuGetContentHash)
                throw new InvalidDataException("rules.package-content-hash");
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var asset in package.SelectedAssets)
            {
                if (!paths.Add(asset.PackageRelativeAssetPath))
                    throw new InvalidDataException("rules.duplicate-package-asset");
            }
        }
    }

    private static void WriteIdentity(Utf8JsonWriter writer, RuleRunIdentity identity)
    {
        writer.WriteStartObject();
        writer.WriteString("tenant", identity.Scope.TenantId);
        writer.WriteString("partition", identity.Scope.PersistencePartition);
        writer.WriteString("invocation", identity.InvocationId);
        writer.WriteEndObject();
    }

    private static void WriteDestination(Utf8JsonWriter writer, RuleNativeResultAddress? destination)
    {
        if (destination is null) { writer.WriteNullValue(); return; }
        writer.WriteStartObject();
        writer.WriteNumber("formatVersion", destination.FormatVersion);
        writer.WriteString("workflowExecutionId", destination.WorkflowExecutionId);
        writer.WriteString("activityExecutionId", destination.ActivityExecutionId);
        writer.WriteString("executableId", destination.ExecutableId);
        writer.WriteString("executableHash", destination.ExecutableHash);
        writer.WriteString("nodeId", destination.NodeId);
        writer.WriteString("bookmarkId", destination.BookmarkId);
        writer.WriteString("resumeTargetId", destination.ResumeTargetId);
        writer.WriteString("registrationId", destination.RegistrationId);
        writer.WriteString("stimulusType", destination.StimulusType);
        writer.WriteString("stimulusHash", destination.StimulusHash);
        writer.WriteString("resultContractId", destination.ResultContractId);
        writer.WriteNumber("resultSchemaVersion", destination.ResultSchemaVersion);
        writer.WriteString("deduplicationPolicy", destination.DeduplicationPolicy);
        writer.WriteString("providerId", destination.ProviderId);
        writer.WriteEndObject();
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

    public static string FactsHash(IReadOnlyList<RuleFact> facts) => Hash(FactsBytes(facts));

    public static string PinHash(RuleSemanticPin pin) => Hash(RuleJson.Write(writer => WritePin(writer, pin), RuleLimits.MaxEnvelopeBytes));

    public static byte[] ReceiptBytes(RuleRoundReceipt receipt) => RuleJson.Write(writer =>
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
    }, RuleLimits.MaxEnvelopeBytes);

    public static byte[] ReceiptRecordBytes(RuleRoundReceipt receipt)
    {
        var body = ReceiptBytes(receipt);
        return RuleJson.Write(writer =>
        {
            writer.WriteStartObject();
            writer.WritePropertyName("receipt");
            writer.WriteRawValue(body);
            writer.WriteString("semanticReceiptSha256", receipt.SemanticReceiptSha256);
            writer.WriteEndObject();
        }, RuleLimits.MaxEnvelopeBytes);
    }

    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

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

    public static byte[] FactsBytes(IReadOnlyList<RuleFact> facts) => RuleJson.Write(writer => WriteFacts(writer, CanonicalFacts(facts)), RuleLimits.MaxEnvelopeBytes);

    private static void Require(bool condition, string code)
    {
        if (!condition) throw new InvalidDataException(code);
    }
}
