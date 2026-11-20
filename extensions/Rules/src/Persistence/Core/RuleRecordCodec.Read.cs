using System.Text.Json;
using Elsa.Rules.Core;

namespace Elsa.Rules.Persistence.Core;

public static partial class RuleRecordCodec
{
    /// <summary>Reads the common canonical envelope without interpreting its artifact language.</summary>
    public static RuleInvocationEnvelope ReadInvocation(ReadOnlyMemory<byte> bytes)
    {
        using var document = Parse(bytes);
        var root = document.RootElement;
        var artifact = root.GetProperty("artifact");
        var invocation = new RuleInvocationEnvelope(ReadIdentity(root.GetProperty("identity")),
            new(artifact.GetProperty("formatVersion").GetInt32(), String(artifact, "id"), String(artifact, "sha256"),
                artifact.GetProperty("bytes").GetBytesFromBase64()),
            ReadFactArray(root.GetProperty("initialFacts")), root.GetProperty("maxRounds").GetInt32(),
            ReadPin(root.GetProperty("implementation")), root.GetProperty("invocationSchemaVersion").GetInt32(),
            ReadDestination(root.GetProperty("nativeDestination")));
        RequireCanonical(bytes, InvocationBytes(invocation));
        return invocation;
    }

    /// <summary>Reads bounded canonical committed facts. No inference or vocabulary interpretation is performed.</summary>
    public static IReadOnlyList<RuleFact> ReadFacts(ReadOnlyMemory<byte> bytes)
    {
        using var document = Parse(bytes);
        var facts = ReadFactArray(document.RootElement);
        RequireCanonical(bytes, FactsBytes(facts));
        return Array.AsReadOnly(facts);
    }

    /// <summary>Reads an intact canonical receipt; the semantic validator must still prove its complete firing set.</summary>
    public static RuleRoundReceipt ReadReceiptRecord(ReadOnlyMemory<byte> bytes)
    {
        using var document = Parse(bytes);
        var root = document.RootElement;
        var body = root.GetProperty("receipt");
        var identity = body.GetProperty("identity");
        var round = new RuleRoundIdentity(ReadIdentity(identity), new(String(identity, "fingerprint")),
            identity.GetProperty("ordinal").GetInt32(), String(identity, "inputStateSha256"));
        var receipt = new RuleRoundReceipt(round, String(body, "implementationPinHash"),
            ReadFactArray(body.GetProperty("addedFacts")), body.GetProperty("firings").EnumerateArray()
                .Take(RuleLimits.MaxRules + 1).Select(firing => new RuleFiringEvidence(String(firing, "ruleId"),
                    new(String(firing, "conclusion")))).ToArray(), String(root, "semanticReceiptSha256"));
        RequireCanonical(bytes, ReceiptRecordBytes(receipt));
        Require(Hash(ReceiptBytes(receipt)) == receipt.SemanticReceiptSha256, "rules.receipt-integrity");
        return receipt;
    }

    /// <summary>Reads a canonical terminal result and verifies its complete fact digest.</summary>
    public static RuleTerminalResult ReadTerminal(ReadOnlyMemory<byte> bytes)
    {
        using var document = Parse(bytes);
        var result = ReadResult(document.RootElement);
        RequireCanonical(bytes, TerminalBytes(result));
        return result;
    }

    /// <summary>Reads a canonical delivery and verifies the hash of its complete destination and result.</summary>
    public static RuleTerminalEnvelope ReadDeliveryRecord(ReadOnlyMemory<byte> bytes)
    {
        using var document = Parse(bytes);
        var root = document.RootElement;
        var body = root.GetProperty("delivery");
        var destination = ReadDestination(body.GetProperty("destination"))
            ?? throw new InvalidDataException("rules.missing-native-destination");
        var result = ReadResult(body.GetProperty("result"));
        var envelope = new RuleTerminalEnvelope(String(body, "deliveryId"), destination, result,
            String(root, "canonicalPayloadSha256"));
        RequireCanonical(bytes, DeliveryRecordBytes(envelope.DeliveryId, destination, result));
        return envelope;
    }

    private static JsonDocument Parse(ReadOnlyMemory<byte> bytes)
    {
        Require(bytes.Length <= RuleLimits.MaxEnvelopeBytes, "rules.record-too-large");
        return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
    }

    private static void RequireCanonical(ReadOnlyMemory<byte> bytes, byte[] canonical) =>
        Require(bytes.Span.SequenceEqual(canonical), "rules.noncanonical-record");

    private static RuleFact[] ReadFactArray(JsonElement element) => element.EnumerateArray()
        .Take(RuleLimits.MaxVocabularyFacts + 1).Select(value => new RuleFact(value.GetString()!)).ToArray();

    private static string String(JsonElement element, string property) => element.GetProperty(property).GetString()
        ?? throw new InvalidDataException("rules.null-record-field");

    private static RuleRunIdentity ReadIdentity(JsonElement element) => new(
        new(element.GetProperty("tenant").GetString(), String(element, "partition")), String(element, "invocation"));

    private static RuleSemanticPin ReadPin(JsonElement element) => new(element.GetProperty("pinSchemaVersion").GetInt32(),
        element.GetProperty("packages").EnumerateArray().Select(package => new RulePackagePin(String(package, "id"),
            String(package, "version"), String(package, "contentHash"), package.GetProperty("assets").EnumerateArray()
                .Select(asset => new RuleAssetPin(String(asset, "path"), String(asset, "assembly"), String(asset, "sha256"))).ToArray())).ToArray(),
        String(element, "languageId"), element.GetProperty("languageVersion").GetInt32(),
        element.GetProperty("receiptSchemaVersion").GetInt32(), element.GetProperty("stateSchemaVersion").GetInt32());

    private static RuleNativeResultAddress? ReadDestination(JsonElement element) => element.ValueKind == JsonValueKind.Null ? null : new(
        element.GetProperty("formatVersion").GetInt32(), String(element, "workflowExecutionId"), String(element, "activityExecutionId"),
        String(element, "executableId"), String(element, "executableHash"), String(element, "nodeId"), String(element, "bookmarkId"),
        String(element, "resumeTargetId"), String(element, "registrationId"), String(element, "stimulusType"), String(element, "stimulusHash"),
        String(element, "resultContractId"), element.GetProperty("resultSchemaVersion").GetInt32(), String(element, "deduplicationPolicy"), String(element, "providerId"));

    private static RuleTerminalResult ReadResult(JsonElement element) => new(String(element, "terminalId"),
        Enum.Parse<RuleTerminalKind>(String(element, "kind"), ignoreCase: false), element.GetProperty("committedRounds").GetInt32(),
        element.GetProperty("resultSchemaVersion").GetInt32(), ReadFactArray(element.GetProperty("facts")), String(element, "factsSha256"),
        element.GetProperty("diagnostic").GetString() is { } code ? new(code) : null);
}
