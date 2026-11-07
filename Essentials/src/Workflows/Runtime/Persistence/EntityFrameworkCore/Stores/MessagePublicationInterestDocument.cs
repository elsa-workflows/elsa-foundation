using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Messaging;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed record MessagePublicationSourceFacts(string SourceReferenceId, string SourceKind, string SourceId,
    string? SourceVersion, string DefinitionId, string DefinitionVersionId, string ArtifactVersion, string ArtifactId,
    string? ActivationId, string? SlotId, string? TenantId, WorkflowExecutableReferenceScope Scope, DateTimeOffset? ExpiresAt)
{
    internal static MessagePublicationSourceFacts From(WorkflowExecutableSourceReference source) => new(source.SourceReferenceId,
        source.SourceKind, source.SourceId, source.SourceVersion, source.DefinitionId, source.DefinitionVersionId, source.ArtifactVersion,
        source.ArtifactId, source.ActivationId, source.SlotId, source.TenantId, source.Scope, source.ExpiresAt);
    public override string ToString() => nameof(MessagePublicationSourceFacts);
}
internal sealed record MessagePublicationDeclaration(string ReceiverAddress, string[] Names, bool CanStart);
internal sealed record MessagePublicationDescriptor(MessageScope Scope, string DefinitionNamespace, string ActivationId,
    string SlotId, string SlotName, MessagePublicationSourceFacts Source, string SourceIncarnation, string ArtifactHash,
    string MaterialIncarnation, MessagePublicationDeclaration[] Declarations, bool IsModel = false)
{ public override string ToString() => nameof(MessagePublicationDescriptor); }
internal sealed record MessagePublicationDocument(int Version, MessagePublicationDescriptor Descriptor, int State, long? ClosedAtEpoch);
internal sealed record MessageInterestControlDocument(int Version, MessageScope Scope, long Epoch);

internal static class MessagePublicationInterestDocument
{
    internal const int Version = 1;
    internal const int Closed = 4;
    internal static string Id(string scope, string activation) => EfRelationalIdentity.HashLengthFramed(scope, "message-publication/v1", activation);
    internal static string ScopeIdentity(MessageScope scope)
    {
        var value = new StringBuilder("message-interest-scope/v1");
        void Add(string? text) { if (text is null) value.Append("-:"); else value.Append(text.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(':').Append(text); }
        Add(scope.PersistenceScope.Value); Add(scope.TenantId); Add(scope.TestScopeId);
        return EfRelationalIdentity.CreateOrdinalTextOrderKey(value.ToString());
    }
    internal static string ScopeId(MessageScope scope) => EfRelationalIdentity.Hash(ScopeIdentity(scope));
    internal static string Fingerprint(MessagePublicationDescriptor descriptor) => "elsa.message-publication-interest/ef-v1:" +
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(RuntimeArtifactJson.Serialize(descriptor))));
    internal static void Validate(MessagePublicationDescriptor descriptor)
    {
        if (descriptor.Scope is null || descriptor.Source is null || descriptor.Declarations is null ||
            descriptor.Scope.TestScopeId is not null || descriptor.DefinitionNamespace != MessageDefinitionIdentity.WorkflowDefinitionNamespace ||
            string.IsNullOrWhiteSpace(descriptor.ActivationId) || string.IsNullOrWhiteSpace(descriptor.SlotName) ||
            string.IsNullOrWhiteSpace(descriptor.SourceIncarnation) || string.IsNullOrWhiteSpace(descriptor.MaterialIncarnation) ||
            string.IsNullOrWhiteSpace(descriptor.ArtifactHash) || descriptor.Source.Scope != WorkflowExecutableReferenceScope.Published ||
            descriptor.Source.ExpiresAt is not null || descriptor.Source.ActivationId != descriptor.ActivationId ||
            descriptor.Source.SlotId != descriptor.SlotId || descriptor.Source.TenantId != descriptor.Scope.TenantId ||
            descriptor.Source.SourceReferenceId != WorkflowActivationReferenceIdentity.Create(descriptor.ActivationId) ||
            descriptor.SlotId != WorkflowActivationSlotIdentity.Create(descriptor.Source.DefinitionId, descriptor.SlotName)) throw Invalid();
        // Preserve the canonical source owner's structural contract even after cleanup no longer needs a live read.
        var source = descriptor.Source;
        if (new[] { source.SourceReferenceId, source.ArtifactId, source.SourceKind, source.SourceId, source.DefinitionId,
                source.DefinitionVersionId, source.ArtifactVersion }.Any(string.IsNullOrWhiteSpace) ||
            source.SourceVersion is not null && string.IsNullOrWhiteSpace(source.SourceVersion) ||
            new[] { source.SourceReferenceId, source.ArtifactId, source.DefinitionId, source.DefinitionVersionId }
                .Any(value => value.Length > RuntimeArtifactEfModule.IdentityMaximumLength)) throw Invalid();
        _ = MessageDefinitionIdentity.ForWorkflowDefinition(descriptor.Scope, descriptor.Source.DefinitionId);
        var declarations = MessageMaterialInterestResult.Recognized(descriptor.Declarations.Select(item =>
            new MessageMaterialInterestDeclaration(item.ReceiverAddress, item.Names, item.CanStart)).ToArray()).Declarations;
        if (declarations.Count != descriptor.Declarations.Length) throw Invalid();
        for (var index = 0; index < declarations.Count; index++)
            if (declarations[index].ReceiverAddress != descriptor.Declarations[index].ReceiverAddress ||
                !declarations[index].MessageNames.SequenceEqual(descriptor.Declarations[index].Names)) throw Invalid();
    }
    internal static MessagePublicationDocument Read(MessagePublicationInterestEntity row, string scope, string activation)
    {
        EfSchemaVersion.EnsureCurrent(RuntimeMessageInterestEfModule.Chain, row.SchemaVersion);
        try
        {
            using var json = JsonDocument.Parse(row.ContentJson, new JsonDocumentOptions { MaxDepth = 128 });
            Fields(json.RootElement, "version", "descriptor", "state", "closedAtEpoch");
            var descriptorJson = json.RootElement.GetProperty("descriptor");
            Fields(descriptorJson, "scope", "definitionNamespace", "activationId", "slotId", "slotName", "source",
                "sourceIncarnation", "artifactHash", "materialIncarnation", "declarations", "isModel");
            ScopeFields(descriptorJson.GetProperty("scope"));
            Fields(descriptorJson.GetProperty("source"), "sourceReferenceId", "sourceKind", "sourceId", "sourceVersion", "definitionId",
                "definitionVersionId", "artifactVersion", "artifactId", "activationId", "slotId", "tenantId", "scope", "expiresAt");
            foreach (var item in descriptorJson.GetProperty("declarations").EnumerateArray()) Fields(item, "receiverAddress", "names", "canStart");
            var document = RuntimeArtifactJson.Deserialize<MessagePublicationDocument>(row.ContentJson); var value = document.Descriptor;
            Validate(value);
            if (document.Version != Version) throw new NotSupportedException("The publication interest version is unsupported.");
            if (row.Id != Id(scope, activation) || row.ScopeKey != EfRelationalIdentity.Encode(scope) || row.ScopeKeyHash != EfRelationalIdentity.Hash(scope) ||
                value.Scope.PersistenceScope.Value != scope || value.ActivationId != activation || row.ActivationId != EfRelationalIdentity.Encode(activation) ||
                row.ActivationIdHash != EfRelationalIdentity.Hash(activation) || row.SlotId != EfRelationalIdentity.Encode(value.SlotId) ||
                row.SlotIdHash != EfRelationalIdentity.Hash(value.SlotId) || row.TenantId != Encode(value.Scope.TenantId) ||
                row.TenantIdHash != Hash(value.Scope.TenantId) || row.DescriptorFingerprint != Fingerprint(value) || row.Revision <= 0 ||
                document.State != row.State || row.State is < 1 or > Closed || document.ClosedAtEpoch is < 0 ||
                row.State == Closed && document.ClosedAtEpoch is not >= 0) throw Invalid();
            return document;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or NullReferenceException)
        { throw Invalid(); }
    }
    internal static MessageInterestControlDocument Read(MessageInterestScopeControlEntity row, MessageScope expected)
    {
        EfSchemaVersion.EnsureCurrent(RuntimeMessageInterestEfModule.Chain, row.SchemaVersion);
        try
        {
            using var json = JsonDocument.Parse(row.ContentJson); Fields(json.RootElement, "version", "scope", "epoch"); ScopeFields(json.RootElement.GetProperty("scope"));
            var document = RuntimeArtifactJson.Deserialize<MessageInterestControlDocument>(row.ContentJson);
            if (document.Version != Version) throw new NotSupportedException("The interest control version is unsupported.");
            if (document.Scope != expected || row.Id != ScopeId(expected) || row.FullIdentity != ScopeIdentity(expected) ||
                row.ScopeKey != EfRelationalIdentity.Encode(expected.PersistenceScope.Value) || row.ScopeKeyHash != EfRelationalIdentity.Hash(expected.PersistenceScope.Value) ||
                row.TenantId != Encode(expected.TenantId) || row.TenantIdHash != Hash(expected.TenantId) || row.Epoch != document.Epoch ||
                row.Epoch < 0 || row.Revision <= 0) throw Invalid();
            return document;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or NullReferenceException)
        { throw Invalid(); }
    }
    private static void ScopeFields(JsonElement value)
    { Fields(value, "persistenceScope", "tenantId", "testScopeId"); Fields(value.GetProperty("persistenceScope"), "value"); }
    private static void Fields(JsonElement value, params string[] required)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in value.EnumerateObject()) if (!required.Contains(item.Name, StringComparer.Ordinal) || !names.Add(item.Name)) throw Invalid();
        if (names.Count != required.Length) throw Invalid();
    }
    internal static string? Encode(string? value) => value is null ? null : EfRelationalIdentity.Encode(value);
    internal static string? Hash(string? value) => value is null ? null : EfRelationalIdentity.Hash(value);
    internal static WorkflowActivationProjectionState State(MessagePublicationDocument value) => value.State == Closed
        ? WorkflowActivationProjectionState.Missing : (WorkflowActivationProjectionState)value.State;
    internal static InvalidDataException Invalid() => new("The canonical publication interest is invalid.");
}
