using System.Text;
using System.Text.Json;
using Elsa.Rules.Core;

namespace Elsa.Rules.Runtime;

/// <summary>Stable common framing; identity and cancellation do not interpret a program or select a deployment.</summary>
internal static class RuleInvocationCodec
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
        if (RulesCodec.Hash(artifactBytes.Span) != artifact.Sha256 || artifact.ArtifactId != "rules:" + artifact.Sha256)
            throw new InvalidDataException("rules.artifact-identity");
        var facts = RulesCodec.CanonicalFacts(invocation.InitialFacts);
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
            RulesCodec.WriteFacts(writer, facts);
            writer.WriteNumber("maxRounds", invocation.MaxRounds);
            writer.WritePropertyName("implementation");
            RulesCodec.WritePin(writer, invocation.ExpectedImplementation);
            writer.WritePropertyName("nativeDestination");
            WriteDestination(writer, invocation.NativeDestination);
            writer.WriteEndObject();
        }, RuleLimits.MaxEnvelopeBytes);
    }

    public static RuleInvocationFingerprint Fingerprint(RuleInvocationEnvelope invocation) =>
        new(RulesCodec.Hash(InvocationBytes(invocation)));

    public static byte[] TerminalBytes(RuleTerminalResult result) => RuleJson.Write(writer =>
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!Enum.IsDefined(result.Kind) || result.ResultSchemaVersion != 1)
            throw new InvalidDataException("rules.terminal-schema");
        var facts = RulesCodec.CanonicalFacts(result.ResultFacts);
        if (RulesCodec.FactsHash(facts) != result.FinalFactsSha256)
            throw new InvalidDataException("rules.terminal-facts-digest");
        writer.WriteStartObject();
        writer.WriteString("terminalId", result.TerminalId);
        writer.WriteString("kind", result.Kind.ToString());
        writer.WriteNumber("committedRounds", result.CommittedRounds);
        writer.WriteNumber("resultSchemaVersion", result.ResultSchemaVersion);
        writer.WritePropertyName("facts");
        RulesCodec.WriteFacts(writer, facts);
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
            writer.WriteString("canonicalPayloadSha256", RulesCodec.Hash(body));
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
}
