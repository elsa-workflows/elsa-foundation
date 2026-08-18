using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Workflows.Runtime.Reconciliation.Tests;

/// <summary>
/// Composes a real engine with the artifact importer mounted on a folder, and runs a reconcile pass through it.
/// </summary>
/// <remarks>
/// One owner for the composition so every import test asks the same engine the same way. Nothing below the seam
/// under test is stubbed: the production JSON source reads the bytes off disk, the production gates run, and the
/// production activation coordinator flips the slot.
/// </remarks>
internal static class ArtifactImportHarness
{
    /// <summary>The source id every import test's mount identifies itself by — also its activation ownership key.</summary>
    public const string SourceId = "mounted-artifacts";

    /// <param name="mount">The folder the JSON artifact source reads.</param>
    /// <param name="configureLast">
    /// Runs after every feature has registered, so a test can <em>remove</em> a production registration and prove a
    /// gate still fires without it. Composing over the real engine rather than hand-building a stripped one keeps
    /// the counter-test honest: everything else is exactly what a host would get.
    /// </param>
    public static WorkflowExecutionHarness Build(string mount, Action<IServiceCollection>? configureLast = null) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new WorkflowsRuntimeTriggersFeature().ConfigureServices(services))
            .WithFeature(services => new JsonWorkflowArtifactReconciliationFeature
            {
                Options =
                {
                    SourceId = SourceId,
                    FolderPath = mount,
                },
            }.ConfigureServices(services))
            .ConfigureServices(services =>
            {
                // The host always composes logging; the bare harness does not, and the reconciliation services take
                // a non-optional ILogger<T>.
                services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
                services.AddSingleton<IActivityTriggerStimulusProvider, ProbeTriggerStimulusProvider>();
            })
            .ConfigureServices(services => configureLast?.Invoke(services))
            .Build(Enumerable.Range(1, 32).Select(index => $"activity-execution-{index}").ToArray());

    public static async Task<WorkflowArtifactReconciliationResult> ReconcileAsync(WorkflowExecutionHarness harness)
    {
        // The activity-type registry must be populated before the import gate asks whether each node's CLR type is
        // present — the same ordering [TaskDependency(typeof(RegisterActivityTypesStartupTask))] enforces at boot.
        harness.InitializeActivityTypes();

        await using var scope = harness.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IWorkflowArtifactReconciler>().ReconcileAsync();
    }

    /// <summary>
    /// Puts a <b>different</b> activation source in charge of a definition's default slot, through the production
    /// authority and the production reference store.
    /// </summary>
    /// <remarks>
    /// Written directly rather than by running a second importer because two <c>JsonWorkflowArtifactReconciliation</c>
    /// features on one container share a single <c>IOptions</c> registration and would therefore read the same
    /// mount. This is real engine state, not a double: the reconciler reads the slot and the live reference exactly
    /// as it would for any incumbent, and the reference is what makes the latest-wins comparison run at all.
    /// </remarks>
    public static async Task GiveTheSlotToAsync(
        WorkflowExecutionHarness harness,
        WorkflowActivationSource incumbent,
        string activationId,
        WorkflowExecutable executable)
    {
        var definitionId = executable.Identity.DefinitionId;
        var slotId = WorkflowActivationSlotIdentity.Create(definitionId, WorkflowArtifactReconciler.DefaultSlotName);
        await harness.Services.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
        await harness.Services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(
            new WorkflowExecutableSourceReference(
                SourceReferenceId: WorkflowActivationReferenceIdentity.Create(activationId),
                ArtifactId: executable.Identity.ArtifactId,
                SourceKind: "workflow-artifact-closure",
                // The reference's SourceId is provenance and is required; an unscoped owner (publishing carries no
                // source id) falls back to its kind. Ownership itself is read from the slot's own
                // WorkflowActivationSource, never from here.
                SourceId: incumbent.SourceId ?? incumbent.Kind,
                SourceVersion: null,
                DefinitionId: definitionId,
                DefinitionVersionId: executable.Identity.DefinitionVersionId,
                ArtifactVersion: executable.Identity.ArtifactVersion,
                CreatedAt: ArtifactClosureFixture.CreatedAt,
                PublishedAt: ArtifactClosureFixture.CreatedAt,
                Scope: WorkflowExecutableReferenceScope.Published,
                ActivationId: activationId,
                SlotId: slotId));

        var claimed = await harness.Services.GetRequiredService<IWorkflowActivationAuthority>().TryActivateAsync(
            new WorkflowActivationSlotRequest(
                definitionId,
                WorkflowArtifactReconciler.DefaultSlotName,
                activationId,
                incumbent,
                0,
                ArtifactClosureFixture.CreatedAt));

        if (!claimed.Succeeded)
            throw new InvalidOperationException(
                $"the incumbent could not claim '{definitionId}', so nothing about foreign ownership can be proven: {claimed.Diagnostic}");
    }

    /// <summary>Whether the executable store holds anything at all for <paramref name="artifactId"/>.</summary>
    public static async Task<bool> IsInStoreAsync(WorkflowExecutionHarness harness, string artifactId) =>
        await harness.Services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(artifactId) is not null;

    /// <summary>The definition's default activation slot, or <see langword="null"/> when nothing ever activated it.</summary>
    public static async Task<WorkflowActivationSlot?> FindSlotAsync(WorkflowExecutionHarness harness, string definitionId) =>
        await harness.Services.GetRequiredService<IWorkflowActivationAuthority>()
            .FindAsync(definitionId, WorkflowArtifactReconciler.DefaultSlotName);

    /// <summary>
    /// The source reference one activation id resolves to — live or retired. Retirement is the assertion subject in
    /// the supersession and compensation tests, so this deliberately does not filter on <c>DeletedAt</c>.
    /// </summary>
    public static async Task<WorkflowExecutableSourceReference?> FindReferenceAsync(
        WorkflowExecutionHarness harness,
        string activationId) =>
        await harness.Services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>()
            .FindAsync(WorkflowActivationReferenceIdentity.Create(activationId));

    /// <summary>Every source reference in the store, for duplicate-record assertions.</summary>
    public static async Task<IReadOnlyList<WorkflowExecutableSourceReference>> ListAllReferencesAsync(
        WorkflowExecutionHarness harness) =>
        (await harness.Services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>()
            .ListPageAsync(new WorkflowExecutableSourceReferencePageQuery(limit: RuntimeStorePageRequest.MaximumLimit)))
        .Items;

    /// <summary>
    /// The <b>serving</b> trigger bindings a stimulus resolves to. <c>ListByStimulus</c> returns active rows only, so
    /// an empty result for a superseded version's stimulus is proof its projection stopped serving.
    /// </summary>
    public static async Task<IReadOnlyList<WorkflowTriggerBinding>> ListServingBindingsAsync(
        WorkflowExecutionHarness harness,
        string stimulusHash) =>
        await harness.Services.GetRequiredService<IWorkflowTriggerBindingStore>()
            .ListAllByStimulusAsync(ArtifactClosureFixture.TriggerStimulusType, stimulusHash);
}
