using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Models;

namespace Elsa3.Activities.Design.Import.Services;

public sealed class ReusableActivityCollectionImporter(
    IReusableActivityCollectionAnalyzer analyzer,
    IReusableActivityImportMaterializer materializer,
    IReusableActivityImportCommand command) : IReusableActivityCollectionImporter
{
    public ValueTask<ReusableActivityImportPlan> AnalyzeAsync(
        ReusableActivityImportCollection collection,
        CancellationToken cancellationToken = default) =>
        analyzer.AnalyzeAsync(collection, cancellationToken);

    public async ValueTask<ReusableActivityImportApplyResult> ApplyAsync(
        ReusableActivityImportApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PlanId);
        ArgumentNullException.ThrowIfNull(request.Collection);
        ArgumentNullException.ThrowIfNull(request.SelectedSourceVersionIds);

        var plan = await analyzer.AnalyzeAsync(request.Collection, cancellationToken);
        if (!StringComparer.Ordinal.Equals(request.PlanId, plan.PlanId))
            throw Validation(
                "The Elsa 3 collection changed after analysis.",
                ReusableActivityImportDiagnosticCodes.PlanChanged,
                plan.CollectionId,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["expectedPlanId"] = request.PlanId,
                    ["actualPlanId"] = plan.PlanId
                });

        var selectedIds = request.SelectedSourceVersionIds.ToHashSet(StringComparer.Ordinal);
        if (selectedIds.Count == 0)
            throw Validation(
                "The Elsa 3 import selection is empty.",
                ReusableActivityImportDiagnosticCodes.SelectionInvalid,
                plan.CollectionId);

        if (selectedIds.Count != request.SelectedSourceVersionIds.Count)
            throw Validation(
                "The Elsa 3 import selection contains duplicate source version identities.",
                ReusableActivityImportDiagnosticCodes.SelectionInvalid,
                plan.CollectionId);

        var itemsById = plan.Items.ToDictionary(x => x.SourceVersionId, StringComparer.Ordinal);
        var unknown = selectedIds.Where(x => !itemsById.ContainsKey(x)).Order(StringComparer.Ordinal).ToArray();
        if (unknown.Length != 0)
            throw Validation(
                $"The Elsa 3 import selection contains unknown source versions: {string.Join(", ", unknown)}.",
                ReusableActivityImportDiagnosticCodes.SelectionInvalid,
                plan.CollectionId,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["unknownSourceVersionIds"] = string.Join("|", unknown) });

        var selection = selectedIds.Select(x => itemsById[x]).OrderBy(x => x.SourceVersionId, StringComparer.Ordinal).ToArray();
        var blocking = selection.SelectMany(x => x.Diagnostics).Where(x => x.IsError).ToArray();
        if (blocking.Length != 0)
            throw new ReusableActivityImportValidationException(
                "The selected Elsa 3 import closure contains blocking analysis diagnostics.",
                blocking);

        var missingClosure = selection
            .SelectMany(x => x.Dependencies)
            .Where(x => !selectedIds.Contains(x.TargetSourceVersionId))
            .OrderBy(x => x.OwnerSourceVersionId, StringComparer.Ordinal)
            .ThenBy(x => x.NodeId, StringComparer.Ordinal)
            .ToArray();
        if (missingClosure.Length != 0)
        {
            var diagnostics = missingClosure.Select(x => new Elsa3MigrationDiagnostic(
                Elsa3MigrationDiagnosticSeverity.Error,
                ReusableActivityImportDiagnosticCodes.SelectionNotClosed,
                $"Selected source version '{x.OwnerSourceVersionId}' depends on unselected reusable source version '{x.TargetSourceVersionId}'.",
                guidance: "Select the complete reusable dependency closure and retry the atomic application.",
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["OwnerSourceVersionId"] = x.OwnerSourceVersionId,
                    ["TargetSourceVersionId"] = x.TargetSourceVersionId,
                    ["NodeId"] = x.NodeId
                },
                pathSegments:
                [
                    new(Elsa3MigrationPathSegmentKind.SourceVersion, x.OwnerSourceVersionId),
                    new(Elsa3MigrationPathSegmentKind.Node, x.NodeId),
                    new(Elsa3MigrationPathSegmentKind.DependencySourceVersion, x.TargetSourceVersionId)
                ])).ToArray();
            throw new ReusableActivityImportValidationException(
                "The selected Elsa 3 import set is not dependency-closed.",
                diagnostics);
        }

        var mutation = await materializer.MaterializeAsync(request.Collection, plan, selection, cancellationToken);
        if (request.AccessScope is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(request.AccessScope.UserId);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);
            foreach (var activity in mutation.Activities)
            {
                activity.Definition.TenantId = request.AccessScope.TenantId;
                activity.Version.TenantId = request.AccessScope.TenantId;
                activity.AuthoringState.TenantId = request.AccessScope.TenantId;
            }
            foreach (var workflow in mutation.Workflows)
            {
                workflow.Definition.TenantId = request.AccessScope.TenantId;
                workflow.Version.TenantId = request.AccessScope.TenantId;
            }
            mutation = mutation with
            {
                AccessScope = request.AccessScope,
                IdempotencyKey = request.IdempotencyKey
            };
        }
        var committed = await command.CommitAsync(mutation, cancellationToken);
        return new(plan.PlanId, selection.Select(x => x.SourceVersionId).ToArray(), committed.NoOp, committed.Receipt);
    }

    private static ReusableActivityImportValidationException Validation(
        string message,
        string code,
        string subject,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(message,
        [new Elsa3MigrationDiagnostic(
            Elsa3MigrationDiagnosticSeverity.Error,
            code,
            message,
            guidance: "Re-analyze the collection and apply a reviewed dependency-closed selection.",
            metadata: new Dictionary<string, string>(metadata ?? new Dictionary<string, string>(), StringComparer.Ordinal)
            {
                ["CollectionId"] = subject
            })]);
}
