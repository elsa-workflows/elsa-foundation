using Elsa3.Activities.Design.Import.Models;

namespace Elsa3.Activities.Design.Import.Services;

/// <summary>
/// Backend-neutral rules every <see cref="Contracts.IReusableActivityImportCommand"/> applies identically:
/// mutation validation, definition provenance bindings, and the durable receipt shape.
/// </summary>
public static class ReusableActivityImportCommitRules
{
    public const string ActivityDefinitionKind = "activityDefinition";
    public const string ActivityVersionKind = "activityDefinitionVersion";
    public const string WorkflowDefinitionKind = "workflowDefinition";
    public const string WorkflowVersionKind = "workflowDefinitionVersion";
    public const string Elsa3WorkflowDefinitionSourceKind = "Elsa3WorkflowDefinition";

    public static void Validate(ReusableActivityImportMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentException.ThrowIfNullOrWhiteSpace(mutation.PlanId);
        ArgumentException.ThrowIfNullOrWhiteSpace(mutation.CollectionId);
        if (mutation.SourceVersionIds.Count != mutation.SourceVersionIds.Distinct(StringComparer.Ordinal).Count())
            throw new ArgumentException("Elsa 3 import source version identities must be unique.", nameof(mutation));
        foreach (var activity in mutation.Activities)
        {
            if (!StringComparer.Ordinal.Equals(activity.Version.DefinitionId, activity.Definition.Id) ||
                !StringComparer.Ordinal.Equals(activity.AuthoringState.DefinitionId, activity.Definition.Id))
                throw new ArgumentException("Imported activity definition/version/authoring identities do not align.", nameof(mutation));
            if (!StringComparer.Ordinal.Equals(activity.Version.SourceKind, Elsa3WorkflowDefinitionSourceKind) ||
                string.IsNullOrWhiteSpace(activity.Version.SourceId))
                throw new ArgumentException("Imported activity versions must identify their Elsa 3 source version.", nameof(mutation));
        }
        foreach (var workflow in mutation.Workflows)
        {
            if (!StringComparer.Ordinal.Equals(workflow.Version.DefinitionId, workflow.Definition.Id))
                throw new ArgumentException("Imported workflow definition/version identities do not align.", nameof(mutation));
            if (!mutation.SourceVersionIds.Contains(workflow.SourceVersionId, StringComparer.Ordinal))
                throw new ArgumentException("Imported workflow source identity is not part of the selected source versions.", nameof(mutation));
        }
    }

    /// <summary>
    /// One provenance binding per imported workflow and activity definition. A binding is tenant-owned:
    /// user identity never participates, so another user in the tenant reuses the exact imported resources.
    /// </summary>
    public static IReadOnlyList<ReusableActivityImportDefinitionBinding> DefinitionBindings(ReusableActivityImportMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var sourceDefinitionsByVersion = mutation.Workflows.ToDictionary(
            x => x.SourceVersionId,
            x => x.SourceDefinitionId,
            StringComparer.Ordinal);
        return mutation.Workflows.Select(workflow => new ReusableActivityImportDefinitionBinding(
                ReusableActivityImportIdentity.DefinitionBinding(WorkflowDefinitionKind, workflow.Definition.Id),
                WorkflowDefinitionKind,
                workflow.Definition.Id,
                Elsa3WorkflowDefinitionSourceKind,
                workflow.SourceDefinitionId,
                workflow.Definition.TenantId,
                workflow.Definition.CreatedAt))
            .Concat(mutation.Activities.Select(activity =>
            {
                if (!sourceDefinitionsByVersion.TryGetValue(activity.Version.SourceId!, out var sourceDefinitionId))
                    throw new ArgumentException(
                        $"Imported activity source version '{activity.Version.SourceId}' has no matching workflow lineage.",
                        nameof(mutation));
                return new ReusableActivityImportDefinitionBinding(
                    ReusableActivityImportIdentity.DefinitionBinding(ActivityDefinitionKind, activity.Definition.Id),
                    ActivityDefinitionKind,
                    activity.Definition.Id,
                    Elsa3WorkflowDefinitionSourceKind,
                    sourceDefinitionId,
                    activity.Definition.TenantId,
                    activity.Definition.CreatedAt);
            }))
            .ToArray();
    }

    /// <summary>
    /// Builds the durable receipt for a scoped mutation. <paramref name="wasCreated"/> reports whether the
    /// commit creates the resource of the given kind and identity, as opposed to reusing an identical one.
    /// </summary>
    public static ReusableActivityImportReceipt BuildReceipt(
        ReusableActivityImportMutation mutation,
        Func<string, string, bool> wasCreated,
        DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentNullException.ThrowIfNull(wasCreated);
        var scope = mutation.AccessScope ?? throw new ArgumentException("Only a scoped mutation has a receipt.", nameof(mutation));
        var idempotencyKey = mutation.IdempotencyKey ?? throw new ArgumentException("Only a keyed mutation has a receipt.", nameof(mutation));
        var sources = mutation.SourceVersionIds.Order(StringComparer.Ordinal).Select(sourceVersionId =>
        {
            var workflow = mutation.Workflows.Single(x => StringComparer.Ordinal.Equals(x.SourceVersionId, sourceVersionId));
            var activity = mutation.Activities.SingleOrDefault(x => StringComparer.Ordinal.Equals(x.Version.SourceId, sourceVersionId));
            return new ReusableActivityImportSourceReceipt(
                workflow.SourceDefinitionId,
                sourceVersionId,
                workflow.Definition.Id,
                workflow.Version.Id,
                Disposition(WorkflowVersionKind, workflow.Version.Id),
                $"/design/workflows/definitions/{Uri.EscapeDataString(workflow.Definition.Id)}/versions/{Uri.EscapeDataString(workflow.Version.Id)}",
                activity?.Definition.Id,
                activity?.Version.Id,
                activity is null ? null : Disposition(ActivityDefinitionKind, activity.Definition.Id),
                activity is null ? null : Disposition(ActivityVersionKind, activity.Version.Id),
                activity is null ? null : $"/design/activities/definitions/{Uri.EscapeDataString(activity.Definition.Id)}",
                activity is null ? null : $"/design/activities/versions/{Uri.EscapeDataString(activity.Version.Id)}");
        }).ToArray();
        return new(
            ReusableActivityImportIdentity.Receipt(idempotencyKey, scope),
            mutation.CollectionId,
            mutation.PlanId,
            idempotencyKey,
            ReusableActivityImportOperationService.SelectionFingerprint(
                mutation.CollectionId,
                mutation.PlanId,
                mutation.SourceVersionIds,
                scope),
            scope,
            ReusableActivityImportReceiptStatus.Applied,
            completedAt,
            sources);

        ReusableActivityImportResourceDisposition Disposition(string kind, string id) =>
            wasCreated(kind, id)
                ? ReusableActivityImportResourceDisposition.Created
                : ReusableActivityImportResourceDisposition.Reused;
    }

    /// <summary>
    /// Confirms a stored receipt answers the same request as <paramref name="mutation"/>. A receipt bound to
    /// another collection, plan, selection, or scope is an idempotency-key conflict.
    /// </summary>
    public static bool Matches(ReusableActivityImportReceipt receipt, ReusableActivityImportMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(mutation);
        var scope = mutation.AccessScope ?? throw new ArgumentException("Only a scoped mutation has a receipt.", nameof(mutation));
        var expectedFingerprint = ReusableActivityImportOperationService.SelectionFingerprint(
            mutation.CollectionId,
            mutation.PlanId,
            mutation.SourceVersionIds,
            scope);
        return StringComparer.Ordinal.Equals(receipt.CollectionHandle, mutation.CollectionId) &&
               StringComparer.Ordinal.Equals(receipt.PlanId, mutation.PlanId) &&
               StringComparer.Ordinal.Equals(receipt.SelectionFingerprint, expectedFingerprint) &&
               StringComparer.Ordinal.Equals(receipt.AccessScope.TenantId, scope.TenantId) &&
               StringComparer.Ordinal.Equals(receipt.AccessScope.UserId, scope.UserId);
    }
}

/// <summary>Tenant-owned provenance that binds one imported Design definition to its Elsa 3 source lineage.</summary>
public sealed record ReusableActivityImportDefinitionBinding(
    string Id,
    string TargetDocumentKind,
    string TargetDefinitionId,
    string SourceKind,
    string SourceDefinitionId,
    string? TenantId,
    DateTimeOffset CreatedAt);
