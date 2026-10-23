using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa3.Models;
using System.Buffers;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace Elsa3.Activities.Design.Import.Models;

public sealed class ReusableActivityImportCollection
{
    public ReusableActivityImportCollection(string collectionId, IReadOnlyList<Elsa3WorkflowDefinition> definitions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionId);
        ArgumentNullException.ThrowIfNull(definitions);

        CollectionId = collectionId;
        Definitions = definitions.ToArray();
        if (Definitions.Any(x => x is null))
            throw new ArgumentException("Elsa 3 reusable import collections cannot contain null definitions.", nameof(definitions));
    }

    public string CollectionId { get; }
    public IReadOnlyList<Elsa3WorkflowDefinition> Definitions { get; }
}

public sealed record ReusableActivityImportPlan(
    string PlanId,
    string CollectionId,
    IReadOnlyList<ReusableActivityImportItem> Items,
    IReadOnlyList<Elsa3MigrationDiagnostic> Diagnostics)
{
    public bool CanApply => Diagnostics.All(x => !x.IsError);
}

public sealed record ReusableActivityImportItem(
    string SourceDefinitionId,
    string SourceVersionId,
    int SourceVersion,
    string SourceFingerprint,
    bool IsReusable,
    string WorkflowDefinitionId,
    string WorkflowVersionId,
    string? ActivityDefinitionId,
    string? ActivityDefinitionVersionId,
    string? ActivityTypeKey,
    IReadOnlyList<ReusableActivityDependency> Dependencies,
    IReadOnlyList<ReusableActivityReferenceRewrite> Rewrites,
    IReadOnlyList<ReusableActivityDirectStart> DirectStarts,
    IReadOnlyList<Elsa3MigrationDiagnostic> Diagnostics)
{
    public bool CanApply => Diagnostics.All(x => !x.IsError);
}

public sealed record ReusableActivityDependency(
    string OwnerSourceVersionId,
    string TargetSourceDefinitionId,
    string TargetSourceVersionId,
    string TargetActivityDefinitionVersionId,
    string NodeId);

public sealed record ReusableActivityReferenceRewrite(
    string OwnerSourceVersionId,
    string NodeId,
    string TargetSourceVersionId,
    string TargetActivityDefinitionVersionId);

public sealed record ReusableActivityDirectStart(string NodeId, string ActivityType);

public sealed record ReusableActivityImportApplyRequest(
    string PlanId,
    ReusableActivityImportCollection Collection,
    IReadOnlyCollection<string> SelectedSourceVersionIds,
    ReusableActivityImportAccessScope? AccessScope = null,
    string? IdempotencyKey = null);

/// <param name="Warnings">Conversion warnings of the applied items (runtime differences); the receipt stores them too.</param>
public sealed record ReusableActivityImportApplyResult(
    string PlanId,
    IReadOnlyList<string> AppliedSourceVersionIds,
    bool NoOp,
    ReusableActivityImportReceipt? Receipt = null,
    IReadOnlyList<Elsa3MigrationDiagnostic>? Warnings = null);

/// <summary>
/// Complete Design mutation handed to the atomic adapter. <see cref="Activities"/> are in publish order: dependencies
/// first, then ascending version within one definition.
/// </summary>
public sealed record ReusableActivityImportMutation(
    string PlanId,
    string CollectionId,
    IReadOnlyList<string> SourceVersionIds,
    IReadOnlyList<ImportedReusableActivity> Activities,
    IReadOnlyList<ImportedWorkflow> Workflows,
    ReusableActivityImportAccessScope? AccessScope = null,
    string? IdempotencyKey = null,
    IReadOnlyList<Elsa3MigrationDiagnostic>? Warnings = null);

/// <summary>
/// One reusable Elsa 3 workflow version mapped to a graph activity. <paramref name="Body"/> is the mapped workflow state
/// the version's graph manifest carries, kept so the import can admit it before the commit. The commit writes the
/// definition and its authoring state; the version itself is published afterwards through a draft (spec 246 D-1).
/// </summary>
public sealed record ImportedReusableActivity(
    ActivityDefinition Definition,
    ImportedReusableActivityVersion Version,
    ActivityDefinitionAuthoringState AuthoringState,
    WorkflowDefinitionState Body);

/// <summary>
/// What the publish phase publishes for one imported reusable version: the plan's deterministic id, which wrappers and
/// callers already reference, its SemVer and the draft content.
/// </summary>
/// <param name="Version">The SemVer, <c>{n}.0.0</c> for Elsa 3 version n.</param>
/// <param name="GraphManifest">The schema-2 graph manifest the draft carries as its provider payload.</param>
/// <param name="DependsOn">The ids of the imported versions this one uses; each is published before it.</param>
public sealed record ImportedReusableActivityVersion(
    string Id,
    string DefinitionId,
    string Version,
    string SourceVersionId,
    ActivityContract Contract,
    JsonElement GraphManifest,
    IReadOnlyList<string> DependsOn,
    DateTimeOffset CreatedAt)
{
    /// <summary>The import's tenant, stamped by the importer before the commit.</summary>
    public string? TenantId { get; set; }

    /// <summary>The draft the publish phase publishes this version from; deterministic, so a resumed apply finds it.</summary>
    public string DraftId => ReusableActivityImportIdentity.Create("activity-draft", SourceVersionId);
}

public sealed record ImportedWorkflow(
    string SourceDefinitionId,
    string SourceVersionId,
    WorkflowDefinition Definition,
    WorkflowDefinitionVersion Version);

public sealed record ReusableActivityImportCommitResult(
    bool NoOp,
    ReusableActivityImportReceipt? Receipt = null);

public sealed record ReusableActivityImportAccessScope(string? TenantId, string UserId)
{
    public string TenantScope => TenantId is null ? "global:" : $"tenant:{TenantId}";
}

public sealed record ReusableActivityImportCollectionHandle(
    string Handle,
    ReusableActivityImportAccessScope AccessScope,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    long ContentLength,
    ReusableActivityImportCollection Collection);

public sealed record ReusableActivityImportUploadResult(
    string CollectionHandle,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    int SourceVersionCount,
    long ContentLength);

public sealed record ReusableActivityImportAnalysisPage(
    string CollectionHandle,
    string PlanId,
    int Offset,
    int Limit,
    int Processed,
    int Total,
    int ProcessedDiagnostics,
    int TotalDiagnostics,
    bool IsComplete,
    int? NextOffset,
    IReadOnlyList<ReusableActivityImportItem> Items,
    IReadOnlyList<Elsa3MigrationDiagnostic> Diagnostics);

public sealed record ReusableActivityImportSelectionReadiness(
    string CollectionHandle,
    string PlanId,
    IReadOnlyList<string> RequestedSourceVersionIds,
    IReadOnlyList<string> ExpandedSourceVersionIds,
    IReadOnlyList<string> AddedDependencySourceVersionIds,
    bool IsReady,
    IReadOnlyList<Elsa3MigrationDiagnostic> Diagnostics);

public enum ReusableActivityImportReceiptStatus
{
    Applied,
    AlreadyImported
}

public sealed record ReusableActivityImportReceipt(
    string ReceiptId,
    string CollectionHandle,
    string PlanId,
    string IdempotencyKey,
    string SelectionFingerprint,
    ReusableActivityImportAccessScope AccessScope,
    ReusableActivityImportReceiptStatus Status,
    DateTimeOffset CompletedAt,
    IReadOnlyList<ReusableActivityImportSourceReceipt> Sources)
{
    /// <summary>
    /// The conversion warnings of the apply that produced this receipt (runtime differences, spec 236 R-7). Stored with
    /// the receipt, so a replay and a status read return the same warnings (spec 246 D-3).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<Elsa3MigrationDiagnostic>? Warnings { get; init; }

    /// <summary>
    /// The publication state of each selected reusable version, in publish order (spec 246 R-6). Filled by the apply, a
    /// replay and a status read from the publication store; never persisted.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ReusableActivityPublicationState>? Publications { get; init; }
}

/// <summary>What the commit made or reused for one selected Elsa 3 source version.</summary>
/// <remarks>
/// <c>ActivityVersionDisposition</c> describes the commit, not publication: <c>Created</c> means the version was not yet
/// published when the commit ran, so the publish phase was to publish it. Whether it is published now, and why not, is
/// the receipt's <c>Publications</c> (spec 246 R-6).
/// </remarks>
public sealed record ReusableActivityImportSourceReceipt(
    string SourceDefinitionId,
    string SourceVersionId,
    string WorkflowDefinitionId,
    string WorkflowVersionId,
    ReusableActivityImportResourceDisposition WorkflowDisposition,
    string WorkflowNavigationIdentity,
    string? ActivityDefinitionId,
    string? ActivityDefinitionVersionId,
    ReusableActivityImportResourceDisposition? ActivityDefinitionDisposition,
    ReusableActivityImportResourceDisposition? ActivityVersionDisposition,
    string? ActivityDefinitionNavigationIdentity,
    string? ActivityVersionNavigationIdentity);

public enum ReusableActivityImportResourceDisposition
{
    Created,
    Reused
}

public sealed class ReusableActivityImportValidationException(
    string message,
    IReadOnlyList<Elsa3MigrationDiagnostic> diagnostics)
    : InvalidOperationException(message)
{
    public IReadOnlyList<Elsa3MigrationDiagnostic> Diagnostics { get; } = diagnostics;
}

public sealed class ReusableActivityImportNotFoundException(string message) : InvalidOperationException(message);

public sealed class ReusableActivityImportExpiredException(string handle)
    : InvalidOperationException($"Elsa 3 import collection '{handle}' has expired.")
{
    public string Handle { get; } = handle;
}

public sealed class ReusableActivityImportIdempotencyConflictException(string idempotencyKey)
    : InvalidOperationException($"Elsa 3 import idempotency key '{idempotencyKey}' is already bound to a different request.")
{
    public string IdempotencyKey { get; } = idempotencyKey;
}

public sealed class ReusableActivityImportCollisionException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);

public sealed class ReusableActivityImportPersistenceException(string operation, string identity, Exception innerException)
    : InvalidOperationException($"Elsa 3 import persistence operation '{operation}' failed for '{identity}'.", innerException)
{
    public string Operation { get; } = operation;
    public string Identity { get; } = identity;
}

public sealed class ReusableActivityImportPayloadException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);

public static class ReusableActivityImportDiagnosticCodes
{
    public const string DuplicateSourceVersion = "ELS3-REUSABLE-DUPLICATE-SOURCE-VERSION";
    public const string DuplicateNodeId = "ELS3-REUSABLE-DUPLICATE-NODE-ID";
    public const string InvalidSource = "ELS3-REUSABLE-INVALID-SOURCE";
    public const string MissingReference = "ELS3-REUSABLE-MISSING-REFERENCE";
    public const string AmbiguousReference = "ELS3-REUSABLE-AMBIGUOUS-REFERENCE";
    public const string UnsupportedReference = "ELS3-REUSABLE-UNSUPPORTED-REFERENCE";
    public const string UnsupportedTrigger = "ELS3-REUSABLE-UNSUPPORTED-TRIGGER";
    public const string DependencyCycle = "ELS3-REUSABLE-DEPENDENCY-CYCLE";
    public const string PlanChanged = "ELS3-REUSABLE-PLAN-CHANGED";
    public const string SelectionNotClosed = "ELS3-REUSABLE-SELECTION-NOT-CLOSED";
    public const string SelectionInvalid = "ELS3-REUSABLE-SELECTION-INVALID";
    public const string CollectionExpired = "ELS3-REUSABLE-COLLECTION-EXPIRED";
    public const string IdempotencyConflict = "ELS3-REUSABLE-IDEMPOTENCY-CONFLICT";
    public const string IdentityCollision = "ELS3-REUSABLE-IDENTITY-COLLISION";

    /// <summary>
    /// A reusable workflow starts from a leaf trigger. Elsa 4 cannot start a reusable activity from a trigger, so the
    /// import refuses it rather than leave the trigger in the graph body (spec 246 D-6).
    /// </summary>
    public const string UnsupportedStartTrigger = "ELS3-REUSABLE-UNSUPPORTED-START-TRIGGER";

    /// <summary>
    /// The selection holds a reusable workflow but the host cannot publish imported activities: no
    /// <c>IReusableActivityImportPublisher</c> is registered (spec 246 R-1).
    /// </summary>
    public const string PublishingUnavailable = "ELS3-REUSABLE-PUBLISHING-UNAVAILABLE";

    /// <summary>
    /// An imported reusable version was not published because a version it uses was not published; publishing that one
    /// first publishes this one on the next apply (spec 246 R-5).
    /// </summary>
    public const string DependencyNotPublished = "ELS3-REUSABLE-DEPENDENCY-NOT-PUBLISHED";
}

public static class ReusableActivityImportIdentity
{
    public static string Create(string kind, params string[] fields)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        var writer = new ArrayBufferWriter<byte>();
        Span<byte> length = stackalloc byte[4];
        foreach (var field in fields.Prepend(kind))
        {
            var bytes = Encoding.UTF8.GetBytes(field ?? string.Empty);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            writer.Write(length);
            writer.Write(bytes);
        }
        return $"elsa3-{kind}-{Convert.ToHexStringLower(SHA256.HashData(writer.WrittenSpan))}";
    }

    public static string ActivityTypeKey(string sourceDefinitionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDefinitionId);
        return $"elsa3.workflow.{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sourceDefinitionId)))}";
    }

    /// <summary>The receipt identity of one idempotency key within the exact tenant-plus-user operation scope.</summary>
    public static string Receipt(string idempotencyKey, ReusableActivityImportAccessScope accessScope)
    {
        ArgumentNullException.ThrowIfNull(accessScope);
        return Create("receipt", accessScope.TenantScope, accessScope.UserId, idempotencyKey);
    }

    /// <summary>The tenant-owned provenance binding identity of one imported Design definition.</summary>
    public static string DefinitionBinding(string targetDocumentKind, string targetDefinitionId) =>
        Create("definition-binding", targetDocumentKind, targetDefinitionId);
}
