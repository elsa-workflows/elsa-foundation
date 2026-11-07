using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Messaging;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Messaging;

/// <summary>Immutable source-owned planning obligation. No payload/cohort copy, target run or dispatch capability.</summary>
internal sealed class BusinessMessageDefinitionWork
{
    internal const int Version = 1;
    internal const string Kind = "business-message.plan-definition/1";
    internal const string FingerprintVersion = "elsa.business-message-work/v1";
    private const string IdentityVersion = "elsa.business-message-work-address/v1";

    private BusinessMessageDefinitionWork(MessageScope scope, string messageId, MessageDefinitionIdentity definition,
        string headerFingerprint, string cohortDigest, long generation, DateTimeOffset notBefore)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(messageId) || definition.Scope != scope || generation <= 0 || notBefore == default)
            throw BusinessMessageSourceState.Invalid();
        BusinessMessageAcceptedHeaderCodec.RequireFingerprint(headerFingerprint, BusinessMessageAcceptedHeaderCodec.HeaderFingerprintVersion);
        BusinessMessageAcceptedHeaderCodec.RequireFingerprint(cohortDigest, BusinessMessageAcceptedHeaderCodec.CohortVersion);
        Scope = scope; Definition = definition; Source = new(BusinessMessageAcceptedHeader.Family, scope.PersistenceScope, messageId);
        HeaderFingerprint = headerFingerprint; CohortDigest = cohortDigest; Generation = generation; NotBefore = notBefore;
        WorkId = BusinessMessageWire.Digest(IdentityVersion, Fields(includeId: false));
    }

    internal static IReadOnlyList<BusinessMessageDefinitionWork> Initial(BusinessMessageSourceState source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Header.Receipt.Mode == MessageDeliveryMode.ImmediateOnly)
        {
            if (!source.IsImmediateUnmatched) throw new NotSupportedException("Selected immediate-only work requires real winner and target capture.");
            return Array.Empty<BusinessMessageDefinitionWork>();
        }
        if (source.Progress.Any(item => item.Status != BusinessMessageDefinitionStatus.Pending || item.Discovery is not { Generation: 1 } discovery ||
            discovery.NotBefore != source.Header.Receipt.AcceptedAt))
            throw new NotSupportedException("Only complete initial buffered planning work is implemented.");
        _ = source.PayloadSlot.BindForPlanning(source.Header);
        return Array.AsReadOnly(source.Progress.Select(item => new BusinessMessageDefinitionWork(source.Header.Scope, source.Header.Address.SourceId,
            item.Definition, BusinessMessageAcceptedHeaderCodec.Fingerprint(source.Header), source.Header.CohortDigest,
            item.Discovery!.Generation, item.Discovery.NotBefore)).ToArray());
    }

    internal MessageScope Scope { get; }
    internal RuntimeDeliverySourceAddress Source { get; }
    internal MessageDefinitionIdentity Definition { get; }
    internal string WorkId { get; }
    internal string HeaderFingerprint { get; }
    internal string CohortDigest { get; }
    internal long Generation { get; }
    internal DateTimeOffset NotBefore { get; }
    internal string Encode() => Fields(includeId: true);
    internal string Fingerprint() => BusinessMessageWire.Digest(FingerprintVersion, Encode());

    internal static BusinessMessageDefinitionWork Decode(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 128 });
            var root = BusinessMessageWire.Object(document.RootElement,
                ["version", "family", "kind", "scope", "messageId", "namespace", "definitionId", "headerFingerprint", "cohortDigest", "generation", "notBeforeUtcTicks", "workId"]);
            if (BusinessMessageWire.Int(root["version"]) != Version || BusinessMessageWire.Text(root["family"]) != BusinessMessageAcceptedHeader.Family ||
                BusinessMessageWire.Text(root["kind"]) != Kind) throw new NotSupportedException("The BusinessMessage work protocol is unsupported.");
            var scopeValues = BusinessMessageWire.Object(root["scope"], ["persistenceScope", "tenant", "testScopeId"]);
            var scope = new MessageScope(new(BusinessMessageWire.O(scopeValues["persistenceScope"])),
                BusinessMessageWire.NullableO(scopeValues["tenant"]), BusinessMessageWire.NullableO(scopeValues["testScopeId"]));
            var work = new BusinessMessageDefinitionWork(scope, BusinessMessageWire.O(root["messageId"]),
                new(scope, BusinessMessageWire.O(root["namespace"]), BusinessMessageWire.O(root["definitionId"])),
                BusinessMessageWire.Text(root["headerFingerprint"]), BusinessMessageWire.Text(root["cohortDigest"]),
                BusinessMessageWire.Long(root["generation"]), BusinessMessageWire.At(root["notBeforeUtcTicks"]));
            if (work.WorkId != BusinessMessageWire.Text(root["workId"])) throw BusinessMessageSourceState.Invalid();
            return work;
        }
        catch (Exception error) when (BusinessMessageWire.IsMalformed(error)) { throw BusinessMessageSourceState.Invalid(); }
    }

    internal void ValidatePlanningBinding(BusinessMessageSourceState source)
    {
        var header = source.Header;
        if (header.Receipt.Mode != MessageDeliveryMode.Buffered || Source != header.Address || Scope != header.Scope ||
            HeaderFingerprint != BusinessMessageAcceptedHeaderCodec.Fingerprint(header) || CohortDigest != header.CohortDigest)
            throw BusinessMessageSourceState.Invalid();
        var progress = source.Definition(Definition);
        if (progress.Status != BusinessMessageDefinitionStatus.Pending || progress.Discovery is not { } discovery ||
            discovery.Generation != Generation || discovery.NotBefore != NotBefore) throw BusinessMessageSourceState.Invalid();
        _ = source.PayloadSlot.BindForPlanning(header);
        // A planner must still compare the current buffer deadline before proposing selection. Work after expiry
        // remains owed so the typed completion can record Expired; this codec never chooses or targets a recipient.
    }

    private string Fields(bool includeId) => BusinessMessageWire.Json(writer =>
    {
        writer.WriteStartObject(); writer.WriteNumber("version", Version); writer.WriteString("family", BusinessMessageAcceptedHeader.Family);
        writer.WriteString("kind", Kind); writer.WritePropertyName("scope"); writer.WriteStartObject();
        BusinessMessageWire.O(writer, "persistenceScope", Scope.PersistenceScope.Value); BusinessMessageWire.NullableO(writer, "tenant", Scope.TenantId);
        BusinessMessageWire.NullableO(writer, "testScopeId", Scope.TestScopeId); writer.WriteEndObject();
        BusinessMessageWire.O(writer, "messageId", Source.SourceId); BusinessMessageWire.O(writer, "namespace", Definition.DefinitionNamespace);
        BusinessMessageWire.O(writer, "definitionId", Definition.DefinitionId); writer.WriteString("headerFingerprint", HeaderFingerprint);
        writer.WriteString("cohortDigest", CohortDigest); writer.WriteNumber("generation", Generation); writer.WriteNumber("notBeforeUtcTicks", NotBefore.UtcTicks);
        if (includeId) writer.WriteString("workId", WorkId); writer.WriteEndObject();
    });
    public override string ToString() => nameof(BusinessMessageDefinitionWork);
}
