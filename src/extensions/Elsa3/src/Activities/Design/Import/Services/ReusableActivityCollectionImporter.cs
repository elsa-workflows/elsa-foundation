using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Exceptions;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Models;

namespace Elsa3.Activities.Design.Import.Services;

/// <summary>
/// Applies a reviewed Elsa 3 collection selection: re-analyzes it, checks the selection, maps it, admits every activity
/// node the mapping produced through the credential-literal rule (spec 188, FR-008) and commits the mutation as one unit.
/// </summary>
/// <remarks>
/// <para>
/// The nodes judged are those of each imported workflow version's state and of each reusable activity's mapped body,
/// from which <see cref="IReusableActivityImportMaterializer"/> must build that activity version's descriptor payload: the root and every node nested under
/// it, which the mapping limits to <see cref="Elsa3ImportedActivityStructure.MaxNestingDepth"/> containers. The mapping nests child activities under <see cref="Elsa3ImportedActivityStructure.Kind"/>, which no
/// structure handler projects, so the rule's own tree walk does not reach them; the import enumerates them through
/// <see cref="Elsa3ImportedActivityStructure.Nodes"/> and judges each through <see cref="ICredentialLiteralValidator"/>.
/// </para>
/// <para>
/// The apply is all or nothing, so a refused binding refuses the whole apply before any workflow or activity is committed,
/// and <see cref="ReusableActivityImportOperationService"/> then deletes the upload, because this refusal does not leave it
/// usable: one
/// <see cref="CredentialLiteralRefusedException"/> names the rule, and the node and the input of every refused binding,
/// never the value. As everywhere the rule runs, a binding is matched to its input by reference key, and a node whose
/// activity version the catalog does not hold is not judged.
/// </para>
/// </remarks>
public sealed class ReusableActivityCollectionImporter(
    IReusableActivityCollectionAnalyzer analyzer,
    IReusableActivityImportMaterializer materializer,
    IReusableActivityImportCommand command,
    ICredentialLiteralValidator credentialLiterals) : IReusableActivityCollectionImporter
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
        await AdmitAsync(mutation, cancellationToken);
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

    /// <summary>
    /// Judges every activity node the commit would store, nested ones included, and throws one
    /// <see cref="CredentialLiteralRefusedException"/> naming every refused binding when any node holds one, each finding
    /// located in the Elsa 3 workflow it came from, because node ids repeat across a collection.
    /// </summary>
    /// <remarks>
    /// The rule judges each binding against its own node's activity declaration, so a node judged as the root of an
    /// otherwise empty state gets the findings it would get in place.
    /// </remarks>
    private async Task AdmitAsync(ReusableActivityImportMutation mutation, CancellationToken cancellationToken)
    {
        var workflowRoots = mutation.Workflows.Select(workflow => (
            Root: workflow.Version.State.RootActivity,
            Location: $"Elsa 3 workflow '{workflow.SourceDefinitionId}' version '{workflow.SourceVersionId}'"));
        var activityRoots = mutation.Activities.Select(activity => (
            Root: activity.Body.RootActivity,
            Location: $"the reusable activity imported from Elsa 3 workflow version '{activity.Version.SourceId}'"));
        var nodes = workflowRoots.Concat(activityRoots)
            .Where(root => root.Root is not null)
            .SelectMany(root => Elsa3ImportedActivityStructure.Nodes(root.Root!).Select(node => (Node: node, root.Location)));
        var findings = new List<ValidationError>();
        foreach (var (node, location) in nodes)
        {
            var nodeFindings = await credentialLiterals.Validate(new WorkflowDefinitionState([], node, [], [], null), cancellationToken);
            findings.AddRange(nodeFindings.Select(finding => CredentialLiteralFinding.Located(finding, location)));
        }

        if (findings.Count > 0)
            throw new CredentialLiteralRefusedException(findings);
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
