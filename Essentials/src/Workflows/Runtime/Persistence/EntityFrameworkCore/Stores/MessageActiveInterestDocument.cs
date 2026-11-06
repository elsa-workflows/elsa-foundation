using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Messaging;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Messaging;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Canonical typed active root and retained create-only terminal control.</summary>
internal static class MessageActiveInterestDocument
{
    internal sealed record Document(int Version, MessageScope Scope, string Incarnation, string DefinitionId,
        WorkflowExecutionState Owner, MessagePublicationSourceFacts Source, string SourceIncarnation,
        string MaterialIncarnation, MessagePublicationDeclaration[] Declarations,
        WorkflowExecutionStatus? TerminalStatus, long? ClosedAtEpoch);
    internal static string Id(string scope, string execution) => EfRelationalIdentity.HashLengthFramed("message-active-interest/v1", scope, execution);
    internal static string Fingerprint(Document value) => "elsa.message-active-interest/ef-v1:" + Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(RuntimeArtifactJson.Serialize(value with { TerminalStatus = null, ClosedAtEpoch = null }))));
    internal static Document Create(MessageScope scope, WorkflowExecutionState state, WorkflowExecutableSourceReference source,
        string sourceIncarnation, string materialIncarnation, IReadOnlyList<MessageMaterialInterestDeclaration> declarations,
        string incarnation, long? closedAtEpoch)
    {
        var value = new Document(1, scope, incarnation, source.DefinitionId,
            state with { CorrelationId = null, SystemMetadata = RuntimeModelMetadata.Snapshot(null), RootVariableFrame = null },
            MessagePublicationSourceFacts.From(source), sourceIncarnation, materialIncarnation,
            declarations.Select(item => new MessagePublicationDeclaration(item.ReceiverAddress, item.MessageNames.ToArray(), item.CanStartWorkflow)).ToArray(),
            state.Status.IsTerminal() ? state.Status : null, closedAtEpoch);
        Validate(value); return value;
    }
    private static void Validate(Document value)
    {
        if (value.Version != 1) throw new NotSupportedException("The active-interest document version is unsupported.");
        if (value.Scope is null || value.Source is null || value.Owner is null || value.Declarations is null ||
            value.Scope.TestScopeId is not null || !Guid.TryParseExact(value.Incarnation, "N", out _) ||
            string.IsNullOrWhiteSpace(value.SourceIncarnation) || string.IsNullOrWhiteSpace(value.MaterialIncarnation) ||
            value.Owner.RootVariableFrame is not null || value.Owner.CorrelationId is not null || value.Owner.SystemMetadata.Count != 0 ||
            value.Owner.RunKind != WorkflowRunKind.PublishedRun || value.Owner.TestScope is not null || value.Owner.ModelWorkOrigin is not null ||
            value.Owner.ParentWorkflowExecutionId is not null || value.Owner.Partition is null || value.Owner.Authority is null ||
            value.Owner.PinnedSource is null || value.Owner.PinnedExecutable is null || !Enum.IsDefined(value.Owner.Status) ||
            value.Owner.WorkflowExecutionId.Length is 0 or > RuntimeArtifactEfModule.IdentityMaximumLength ||
            value.Owner.TenantId != value.Scope.TenantId || value.DefinitionId != value.Source.DefinitionId ||
            value.Source.ArtifactId != value.Owner.PinnedExecutable.ArtifactId || value.Source.TenantId != value.Scope.TenantId ||
            value.Source.Scope != WorkflowExecutableReferenceScope.Published || value.Source.SourceKind != "WorkflowDefinitionVersion" ||
            string.IsNullOrWhiteSpace(value.Source.ActivationId) || string.IsNullOrWhiteSpace(value.Source.SlotId) ||
            value.Source.SourceReferenceId != WorkflowActivationReferenceIdentity.Create(value.Source.ActivationId) ||
            value.Owner.PinnedSource != new WorkflowExecutableSourceProvenance(value.Source.SourceReferenceId, value.Source.SourceKind,
                value.Source.SourceId, value.Source.SourceVersion, value.Source.DefinitionId, value.Source.DefinitionVersionId,
                value.Source.ArtifactVersion, value.Source.ActivationId, value.Source.SlotId) ||
            value.ClosedAtEpoch.HasValue != value.TerminalStatus.HasValue || value.ClosedAtEpoch is < 0 ||
            value.TerminalStatus is { } status && (!Enum.IsDefined(status) || !status.IsTerminal())) throw Invalid();
        if (new[] { value.Source.SourceReferenceId, value.Source.ArtifactId, value.Source.SourceKind, value.Source.SourceId,
                value.Source.DefinitionId, value.Source.DefinitionVersionId, value.Source.ArtifactVersion }.Any(string.IsNullOrWhiteSpace) ||
            value.Source.SourceVersion is not null && string.IsNullOrWhiteSpace(value.Source.SourceVersion) ||
            new[] { value.Source.SourceReferenceId, value.Source.ArtifactId, value.Source.DefinitionId, value.Source.DefinitionVersionId }
                .Any(item => item.Length > RuntimeArtifactEfModule.IdentityMaximumLength)) throw Invalid();
        _ = MessageDefinitionIdentity.ForWorkflowDefinition(value.Scope, value.DefinitionId);
        var normalized = MessageMaterialInterestResult.Recognized(value.Declarations.Select(item =>
            new MessageMaterialInterestDeclaration(item.ReceiverAddress, item.Names, item.CanStart)).ToArray()).Declarations;
        if (normalized.Count != value.Declarations.Length) throw Invalid();
        for (var i = 0; i < normalized.Count; i++)
            if (normalized[i].ReceiverAddress != value.Declarations[i].ReceiverAddress || !normalized[i].MessageNames.SequenceEqual(value.Declarations[i].Names)) throw Invalid();
    }
    internal static Document Read(MessageActiveInterestEntity row, string scope, string execution)
    {
        EfSchemaVersion.EnsureCurrent(RuntimeMessageInterestEfModule.Chain, row.SchemaVersion);
        try
        {
            var value = RuntimeArtifactJson.Deserialize<Document>(row.ContentJson); Validate(value);
            // This unpublished family has one canonical encoding; unknown/duplicate nested fields cannot disappear on read.
            if (RuntimeArtifactJson.Serialize(value) != row.ContentJson || row.Id != Id(scope, execution) ||
                value.Scope.PersistenceScope.Value != scope || value.Owner.WorkflowExecutionId != execution ||
                row.ScopeKey != EfRelationalIdentity.Encode(scope) || row.ScopeKeyHash != EfRelationalIdentity.Hash(scope) ||
                row.WorkflowExecutionId != EfRelationalIdentity.Encode(execution) || row.WorkflowExecutionIdHash != EfRelationalIdentity.Hash(execution) ||
                row.TenantId != MessagePublicationInterestDocument.Encode(value.Scope.TenantId) || row.TenantIdHash != MessagePublicationInterestDocument.Hash(value.Scope.TenantId) ||
                row.Incarnation != value.Incarnation || row.Closed != value.ClosedAtEpoch.HasValue || row.DescriptorFingerprint != Fingerprint(value) || row.Revision <= 0) throw Invalid();
            return value;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or NullReferenceException or InvalidOperationException)
        { throw Invalid(); }
    }
    internal static MessageActiveInterestEntity ToEntity(Document value)
    {
        Validate(value); var scope = value.Scope.PersistenceScope.Value; var execution = value.Owner.WorkflowExecutionId;
        return new() { Id = Id(scope, execution), ScopeKey = EfRelationalIdentity.Encode(scope), ScopeKeyHash = EfRelationalIdentity.Hash(scope),
            WorkflowExecutionId = EfRelationalIdentity.Encode(execution), WorkflowExecutionIdHash = EfRelationalIdentity.Hash(execution),
            TenantId = MessagePublicationInterestDocument.Encode(value.Scope.TenantId), TenantIdHash = MessagePublicationInterestDocument.Hash(value.Scope.TenantId),
            Incarnation = value.Incarnation, DescriptorFingerprint = Fingerprint(value), Closed = value.ClosedAtEpoch.HasValue,
            ContentJson = RuntimeArtifactJson.Serialize(value), SchemaVersion = RuntimeMessageInterestEfModule.SchemaVersion, Revision = 1 };
    }
    internal static void Update(MessageActiveInterestEntity row, Document value)
    {
        Validate(value); if (row.DescriptorFingerprint != Fingerprint(value) || row.Incarnation != value.Incarnation || row.Revision == long.MaxValue) throw Invalid();
        row.Closed = value.ClosedAtEpoch.HasValue; row.ContentJson = RuntimeArtifactJson.Serialize(value); row.Revision++;
    }
    internal static bool SameOwner(WorkflowExecutionState left, WorkflowExecutionState right) =>
        left.WorkflowExecutionId == right.WorkflowExecutionId && left.PinnedExecutable == right.PinnedExecutable && left.PinnedSource == right.PinnedSource &&
        left.TenantId == right.TenantId && left.RunKind == right.RunKind && left.Partition == right.Partition && left.TestScope == right.TestScope &&
        left.ModelWorkOrigin == right.ModelWorkOrigin && left.ParentWorkflowExecutionId == right.ParentWorkflowExecutionId &&
        left.Authority is { } a && right.Authority is { } b && WorkflowExecutionAuthoritySnapshot.Matches(a, b.SystemIdentity, b.RootInitiator, b.Metadata);
    private static InvalidDataException Invalid() => new("The persisted native active-interest control is corrupt.");
}
