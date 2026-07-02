using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Activities.Composition.Design.Reconciliation;
using Elsa.Activities.Design.Reconciliation.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Composition.Design;

/// <summary>
/// Design-side of the Workflow activity kind: contributes the
/// <see cref="WorkflowActivityReconciliationSource"/> that turns workflow definition versions marked
/// usable-as-activity into activity catalog rows carrying the <c>WorkflowIdentity</c> descriptor. This is
/// a standalone source feature (§2.6.1) — it does not derive from the reconciliation feature; it only
/// registers an <see cref="IActivityReconciliationSource"/> that the reconciliation feature's universal
/// handler discovers from DI. Pair it with <c>ActivitiesCompositionRuntime</c> for construction and with
/// a reconciliation feature to persist the rows.
/// </summary>
/// <remarks>
/// <b>Runtime dependency (not declared via <c>DependsOn</c>).</b> The adapter requires
/// <c>IWorkflowDefinitionStore</c> + <c>IWorkflowDefinitionVersionStore</c>, so a shell must also enable a
/// Workflows.Design persistence provider (EF Core, Groundwork, …); otherwise reconciliation throws at
/// startup when it resolves the source. This is intentionally <b>not</b> a <c>DependsOn</c> entry, because
/// those stores are a provider-neutral contract with no single feature to name — pinning one provider
/// (e.g. <c>WorkflowsDesignPersistenceEFCoreSqlite</c>) would break provider neutrality. Composition must
/// ensure a design persistence provider is present; see spec 006 T029 / the review defer note.
/// </remarks>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Design")]
[ManifestFeatureCategory("Composition")]
[ShellFeature(
    name: "ActivitiesCompositionDesign",
    DisplayName = "Activities Composition (Design)",
    Description = "Reconciliation source that catalogs workflow definitions marked usable-as-activity as activities (the Workflow kind)."
)]
public class ActivitiesCompositionDesignFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        // The adapter is the only piece that reaches into Workflows Design; the reconciliation source
        // depends only on the port, so it stays a pure mapper (§2.7).
        services.AddScoped<IUsableAsActivityWorkflowSource, WorkflowDefinitionUsableAsActivitySource>();
        services.AddScoped<IActivityReconciliationSource, WorkflowActivityReconciliationSource>();
    }
}
