using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa3.Models;
using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace Elsa3.Activities.Design.Import.Models;

public sealed class ReusableActivityImportCollection
{
    public ReusableActivityImportCollection(string collectionId, IEnumerable<Elsa3WorkflowDefinition> definitions)
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
    IReadOnlyCollection<string> SelectedSourceVersionIds);

public sealed record ReusableActivityImportApplyResult(
    string PlanId,
    IReadOnlyList<string> AppliedSourceVersionIds,
    bool NoOp);

/// <summary>Complete Design mutation handed to the atomic adapter.</summary>
public sealed record ReusableActivityImportMutation(
    string PlanId,
    string CollectionId,
    IReadOnlyList<string> SourceVersionIds,
    IReadOnlyList<ImportedReusableActivity> Activities,
    IReadOnlyList<ImportedWorkflow> Workflows);

public sealed record ImportedReusableActivity(
    ActivityDefinition Definition,
    ActivityDefinitionVersion Version,
    ActivityDefinitionAuthoringState AuthoringState);

public sealed record ImportedWorkflow(
    WorkflowDefinition Definition,
    WorkflowDefinitionVersion Version);

public sealed record ReusableActivityImportCommitResult(bool NoOp);

public sealed class ReusableActivityImportValidationException(
    string message,
    IReadOnlyList<Elsa3MigrationDiagnostic> diagnostics)
    : InvalidOperationException(message)
{
    public IReadOnlyList<Elsa3MigrationDiagnostic> Diagnostics { get; } = diagnostics;
}

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
}
