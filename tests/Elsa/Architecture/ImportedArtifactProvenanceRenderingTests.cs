using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// FR-B-012 (T104): on a runtime-only engine, an imported artifact's design-provenance ids — which name rows in a
/// design catalog this engine does not have and never will — render as opaque, unresolved values on the
/// inspection surfaces rather than causing an error.
/// </summary>
/// <remarks>
/// <para>
/// <b>The dangling ids are real, not stipulated.</b> The artifacts come from
/// <see cref="PublishCapableEngine"/>'s production compile-and-publish and travel to the runtime-only engine as
/// exported closure bytes, so their <c>DefinitionId</c> and <c>DefinitionVersionId</c> are genuine design-catalog
/// identifiers minted on another engine. The importing engine has no design store at all — asserted here, not
/// assumed — so every one of those ids is structurally unresolvable. A hand-built fixture could only have
/// simulated that.
/// </para>
/// <para>
/// <b>What "renders as opaque/unresolved rather than erroring" is taken to mean</b>, since the requirement does
/// not spell it out: the inspection surfaces complete, they echo the design ids verbatim rather than dropping,
/// blanking or attempting to dereference them, the design-side sidecars that did not travel (layout, activity
/// presentation, authored inputs) render as empty rather than as failures, and asking any surface to resolve a
/// design-provenance id <em>as if it were a local id</em> answers "not found" instead of faulting.
/// </para>
/// <para>
/// The request handlers are exercised beside the inspector because the not-found half only becomes a rendering
/// at the transport boundary: the inspector answers <see langword="null"/>, and the handler turns that into
/// <see cref="EntityNotFoundException"/> — a 404 — which is the correct rendering of an unresolved id and is
/// materially different from a fault.
/// </para>
/// <para>
/// Placed in this suite rather than beside the importer's own tests because only here can a publish-capable
/// engine produce the provenance in the first place; the reconciliation test project deliberately references no
/// Publishing or Design assembly, so every design id it could mount would be one the fixture invented.
/// </para>
/// </remarks>
public sealed class ImportedArtifactProvenanceRenderingTests : IDisposable
{
    private readonly string _mount = Path.Combine(
        Path.GetTempPath(),
        "elsa-imported-provenance",
        Guid.NewGuid().ToString("N"));

    public ImportedArtifactProvenanceRenderingTests() => Directory.CreateDirectory(_mount);

    public void Dispose()
    {
        if (Directory.Exists(_mount))
            Directory.Delete(_mount, true);
    }

    [Fact]
    public async Task Unresolvable_design_provenance_renders_opaquely_on_a_runtime_only_engine()
    {
        // ---- A publish-capable engine compiles, publishes and exports the parent-plus-child closure. -------
        string parentArtifactId;
        string childArtifactId;
        await using (var publisher = PublishCapableEngine.Create())
        {
            var (parent, child) = await publisher.CompileAndPublishAsync();
            parentArtifactId = parent.Identity.ArtifactId;
            childArtifactId = child.Identity.ArtifactId;

            var closure = await publisher.ExportAsync(PublishCapableEngine.ParentVersionId);
            File.WriteAllText(Path.Combine(_mount, "closure.json"), publisher.Encode(closure));

            // The design ids the runtime side will be asked to render belong to that engine's catalog.
            Assert.Equal(PublishCapableEngine.ParentVersionId, parent.Identity.DefinitionVersionId);
            Assert.Equal(PublishCapableEngine.ChildVersionId, child.Identity.DefinitionVersionId);
        }

        // ---- The runtime-only engine imports them, holding nothing that could resolve a design id. ---------
        await using var runtimeOnly = RuntimeEngines.NewRuntimeOnlyEngine(_mount);
        Assert.Null(runtimeOnly.Services.GetService<Elsa.Workflows.Design.Persistence.Core.Stores.IWorkflowDefinitionVersionStore>());
        Assert.Null(runtimeOnly.Services.GetService<Elsa.Workflows.Design.Persistence.Core.Stores.IWorkflowDefinitionStore>());

        runtimeOnly.InitializeActivityTypes();
        await using (var reconcileScope = runtimeOnly.Services.CreateAsyncScope())
        {
            var result = await reconcileScope.ServiceProvider.GetRequiredService<IWorkflowArtifactReconciler>().ReconcileAsync();
            Assert.Equal(2, result.ImportedCount);
            Assert.Equal(0, result.RejectedCount);
        }

        await using var scope = runtimeOnly.Services.CreateAsyncScope();
        var inspector = scope.ServiceProvider.GetRequiredService<WorkflowExecutableInspector>();

        // ---- The list surface renders both, carrying the foreign design ids through untouched. -------------
        var list = await inspector.ListAsync(WorkflowExecutableListScope.All, includeRetired: true);
        Assert.Equal(2, list.Items.Count);

        var parentSummary = Assert.Single(list.Items, item => item.ArtifactId == parentArtifactId);
        var childSummary = Assert.Single(list.Items, item => item.ArtifactId == childArtifactId);
        Assert.Equal(PublishCapableEngine.ParentDefinitionId, parentSummary.DefinitionId);
        Assert.Equal(PublishCapableEngine.ParentVersionId, parentSummary.DefinitionVersionId);
        Assert.Equal(PublishCapableEngine.ChildDefinitionId, childSummary.DefinitionId);
        Assert.Equal(PublishCapableEngine.ChildVersionId, childSummary.DefinitionVersionId);

        // The reference the artifact is reachable through is the importer's, and it says so — the design ids it
        // carries beside that are provenance, not a claim that anything local resolves them.
        Assert.Equal(JsonWorkflowArtifactReconciliationSource.Kind, parentSummary.SourceKind);
        Assert.Equal(RuntimeEngines.SourceId, parentSummary.SourceId);

        // ---- The details surface renders, and the design sidecars that did not travel are empty. -----------
        var details = await inspector.GetAsync(parentArtifactId);
        Assert.NotNull(details);
        var reference = Assert.Single(details!.References);
        Assert.Equal(PublishCapableEngine.ParentDefinitionId, reference.DefinitionId);
        Assert.Equal(PublishCapableEngine.ParentVersionId, reference.DefinitionVersionId);
        Assert.Equal(WorkflowExecutableReferenceScope.Published.ToString(), reference.Scope);
        Assert.True(reference.Live);

        // A design id is never blanked or replaced by a placeholder — echoing it verbatim is what makes it
        // opaque rather than lost, and is the only thing an operator can correlate back to the build engine.
        Assert.NotNull(details.ChosenReference);
        Assert.Empty(details.ChosenReference!.Layout);
        Assert.Empty(details.ChosenReference.ActivityPresentation);

        // The dependency edge onto the child DOES resolve locally — the closure carried it — which is the
        // contrast that keeps "unresolvable" meaningful here rather than a property of every id in the view.
        var dependency = Assert.Single(details.Dependencies);
        Assert.Equal(childArtifactId, dependency.ArtifactId);
        Assert.NotNull(await inspector.GetAsync(childArtifactId));

        // ---- The provenance surface renders, with the imported reference and no design dereference. --------
        var provenance = await inspector.GetProvenanceAsync(parentArtifactId);
        Assert.NotNull(provenance);
        Assert.Equal(parentArtifactId, provenance!.ArtifactId);
        Assert.Equal(
            PublishCapableEngine.ParentVersionId,
            Assert.Single(provenance.SourceReferences).DefinitionVersionId);
        Assert.True(provenance.ProtectedFromCollection);

        // ---- Input sources render: the design-authored inputs simply are not there, and that is not a fault.
        var inputSources = await inspector.GetInputSourcesAsync(parentArtifactId, reference.SourceReferenceId);
        Assert.NotNull(inputSources);
        Assert.Empty(inputSources!.AuthoredInputs);
        Assert.NotEmpty(inputSources.CompiledInputs);
        Assert.All(inputSources.CompiledInputs, input => Assert.Equal("allowed", input.AccessState));

        // ---- Asking any surface to RESOLVE a design-provenance id answers "not found", never faults. -------
        Assert.Null(await inspector.GetAsync(PublishCapableEngine.ParentVersionId));
        Assert.Null(await inspector.GetProvenanceAsync(PublishCapableEngine.ParentDefinitionId));
        // The exporting engine's own source-reference id is inside the closure bytes but was never imported.
        Assert.Null(await inspector.GetInputSourcesAsync(parentArtifactId, "source-parent"));

        // At the transport boundary the same answer renders as a 404, which is a rendering of "unresolved" —
        // not an engine error. Asserted through the real handlers so the mapping is the production one.
        await Assert.ThrowsAsync<EntityNotFoundException>(() => scope.ServiceProvider
            .GetRequiredService<IRequestHandler<GetWorkflowExecutable, WorkflowExecutableDetailsView>>()
            .Handle(new GetWorkflowExecutable(PublishCapableEngine.ParentVersionId), CancellationToken.None));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => scope.ServiceProvider
            .GetRequiredService<IRequestHandler<GetWorkflowExecutableProvenance, ExecutableProvenanceView>>()
            .Handle(new GetWorkflowExecutableProvenance(PublishCapableEngine.ParentDefinitionId), CancellationToken.None));
    }
}
