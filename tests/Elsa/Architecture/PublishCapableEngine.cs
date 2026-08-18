using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.DispatchWorkflow.Design;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Events;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using DispatchWorkflowActivity = Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow;

namespace Elsa.Architecture.Tests;

/// <summary>
/// A publish-capable engine — a full execution engine <em>plus</em> the design stores an author writes into, the
/// production <see cref="IWorkflowExecutableCompiler"/>, and the production
/// <see cref="IWorkflowArtifactClosureFactory"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here hand-builds an artifact.</b> Both workflows are authored as
/// <see cref="WorkflowDefinitionState"/> documents and compiled by the real compiler, so their nodes, descriptors,
/// pinned activity contracts, resume targets, derived runtime requirements and content-addressed identities are
/// whatever publication actually produces — including the intrinsic node whose reserved <c>"intrinsic"</c>
/// consumer key is the one axis no CLR fixture can reach. A hand-built artifact would let the round trip pass on
/// a shape no compiler emits, which is precisely the failure mode this test exists to rule out.
/// </para>
/// <para>
/// The parent's dependency edge onto the child is likewise not written by the fixture: it is contributed by
/// <c>DispatchPinSource</c> during compilation, from the child's live Published source reference. That is what
/// makes the exported closure a real closure rather than two artifacts placed in one envelope.
/// </para>
/// <para>
/// <b>Why this engine also executes.</b> "Behavior parity versus compile-in-place" needs a second observation to
/// compare against, and the only honest one is the same workflow run where it was compiled. Composing publishing
/// on top of the shared execution harness — which is what a publish-capable host actually is — gives that
/// observation from the same compiled bytes, so the two runs differ in exactly one variable: whether the artifact
/// travelled through the wire format and the importer.
/// </para>
/// </remarks>
internal sealed class PublishCapableEngine : IAsyncDisposable
{
    internal static readonly DateTimeOffset PublishedAt = new(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The correlation id the child's intrinsic stamps on its own execution — the child's observable effect.</summary>
    internal const string ChildCorrelationEffect = "child-executed";

    internal const string ChildDefinitionId = "definition-child-notify";
    internal const string ChildVersionId = "version-child-notify";
    internal const string ChildNodeId = "node-child-correlate";
    internal const string ParentDefinitionId = "definition-parent-order";
    internal const string ParentVersionId = "version-parent-order";
    internal const string ParentNodeId = "node-dispatch-child";

    private const string DispatchActivityVersionId = "activity-version-dispatch";
    private const string DispatchActivityDefinitionId = "activity-definition-dispatch";

    private readonly WorkflowExecutionHarness _harness;

    private PublishCapableEngine(WorkflowExecutionHarness harness) => _harness = harness;

    internal IServiceProvider Services => _harness.Services;

    internal WorkflowExecutionHarness Harness => _harness;

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    /// <param name="childCorrelationEffect">
    /// The literal the child's intrinsic stamps. Parameterized so a second engine can author a <em>behaviourally
    /// different</em> child — the control that keeps hash-portability assertions from being satisfiable by a pin that
    /// stopped carrying anything meaningful at all.
    /// </param>
    internal static PublishCapableEngine Create(string childCorrelationEffect = ChildCorrelationEffect)
    {
        var harness = RuntimeEngines.NewBuilder()
            .WithFeature(services =>
            {
                services.AddSingleton<IWorkflowDefinitionVersionStore>(
                    new StubWorkflowVersionStore(ChildVersion(childCorrelationEffect), ParentVersion()));
                services.AddSingleton<IActivityDefinitionVersionStore>(new StubActivityVersionStore(DispatchActivityVersion()));
                services.AddSingleton<IActivityDefinitionVersionPublicationStore, EmptyActivityPublicationStore>();
                services.AddSingleton<IExecutableActivityTemplateStore, InMemoryExecutableActivityTemplateStore>();
                services.AddSingleton<IWorkflowExecutableInputValidator, WorkflowExecutableInputValidator>();
            })
            .WithFeature(services => new EventsFeature().ConfigureServices(services))
            .WithFeature(services => new WorkflowsPublishingFeature().ConfigureServices(services))
            .WithFeature(services => new DispatchWorkflowDesignFeature().ConfigureServices(services))
            .Build(RuntimeEngines.ActivityExecutionIds);

        // The compiler resolves a node's CLR activity type through the well-known type registry, so the scan the
        // host runs at startup has to have happened before the first CompileAsync — not lazily on first run.
        harness.InitializeActivityTypes();
        return new PublishCapableEngine(harness);
    }

    /// <summary>Compiles and publishes the child, then the parent — the order a real author must follow.</summary>
    /// <remarks>
    /// The parent cannot compile before the child is published: <c>DispatchPinSource</c> resolves the pin from the
    /// child's live Published source reference and refuses when there is none.
    /// <para>
    /// The publish-local identifiers are parameterized so a second engine can publish the <em>same authored content</em>
    /// under different ones — the setup that exposes whether any of them leaked into a content hash.
    /// </para>
    /// </remarks>
    internal async Task<(WorkflowExecutable Parent, WorkflowExecutable Child)> CompileAndPublishAsync(
        string childSourceReferenceId = "source-child",
        string parentSourceReferenceId = "source-parent",
        string childSlotId = "Default")
    {
        var child = await CompileAsync(ChildVersionId, "artifact-child-");
        await PublishAsync(child, childSourceReferenceId, childSlotId);
        var parent = await CompileAsync(ParentVersionId, "artifact-parent-");
        await PublishAsync(parent, parentSourceReferenceId);
        return (parent, child);
    }

    internal async Task<WorkflowExecutable> CompileAsync(string versionId, string artifactIdPrefix)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableCompiler>().CompileAsync(
            new WorkflowExecutableCompileRequest(
                versionId,
                WorkflowExecutableReferenceScope.Published,
                PublishedAt,
                PublishedAt,
                null,
                artifactIdPrefix));
    }

    /// <summary>
    /// Writes the compiled artifact and its Published source reference straight into the stores.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>WorkflowExecutionHarness.PublishAsync</c>: that helper re-pins CLR contracts on save,
    /// which a compiler-produced artifact does not need and which would leave the in-place artifact subtly
    /// different from the one that was exported. Parity is only meaningful if both sides run the same bytes.
    /// </remarks>
    internal async Task<WorkflowExecutableSourceReference> PublishAsync(
        WorkflowExecutable executable,
        string sourceReferenceId,
        string slotId = "Default")
    {
        await Services.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
        var reference = new WorkflowExecutableSourceReference(
            SourceReferenceId: sourceReferenceId,
            ArtifactId: executable.Identity.ArtifactId,
            SourceKind: WorkflowExecutableSourceKinds.WorkflowDefinitionVersion,
            SourceId: executable.Identity.DefinitionVersionId,
            SourceVersion: executable.Identity.ArtifactVersion,
            DefinitionId: executable.Identity.DefinitionId,
            DefinitionVersionId: executable.Identity.DefinitionVersionId,
            ArtifactVersion: executable.Identity.ArtifactVersion,
            CreatedAt: PublishedAt,
            PublishedAt: PublishedAt,
            Scope: WorkflowExecutableReferenceScope.Published,
            ActivationId: $"activation-{sourceReferenceId}",
            SlotId: slotId);
        await Services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(reference);
        return reference;
    }

    /// <summary>The single live Published source reference this engine minted for <paramref name="artifactId"/>.</summary>
    internal async Task<WorkflowExecutableSourceReference> FindPublishedReferenceAsync(string artifactId)
    {
        var references = await Services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>()
            .ListAllByArtifactAsync(artifactId);
        return references.Single(reference => reference.Scope == WorkflowExecutableReferenceScope.Published);
    }

    /// <summary>Runs the production closure factory over this engine's stores.</summary>
    internal async Task<WorkflowArtifactClosure> ExportAsync(string definitionVersionId)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IWorkflowArtifactClosureFactory>().CreateAsync(definitionVersionId);
    }

    /// <summary>Encodes the exported closure to its wire form through the runtime-owned codec.</summary>
    internal string Encode(WorkflowArtifactClosure closure) =>
        Services.GetRequiredService<IWorkflowArtifactClosureSerializer>().Serialize(closure);

    // ---- Authored documents -------------------------------------------------------------------------------

    /// <summary>
    /// The child: one engine-intrinsic <c>SetCorrelationId</c> node. Deliberately intrinsic-only.
    /// </summary>
    /// <remarks>
    /// Two properties come from that choice and neither is available from a CLR-only fixture. First, the compiled
    /// artifact declares <c>RuntimeRequirement("intrinsic", "1")</c>, so the import gate's requirement axis is
    /// evaluated against the reserved consumer key the compiler stamps — the axis that was dead until the
    /// <c>WorkflowIntrinsicActivityConsumerCapability</c> landed. Second, running it leaves a durable, checkable
    /// mark on the child's own execution state (<c>CorrelationId</c>), so "the child actually ran" is an assertion
    /// about an effect rather than about a row existing somewhere.
    /// </remarks>
    private static WorkflowDefinitionVersion ChildVersion(string correlationEffect = ChildCorrelationEffect) =>
        new(ChildDefinitionId, "1.0.0")
        {
            Id = ChildVersionId,
            Definition = new WorkflowDefinition { Id = ChildDefinitionId, Name = "Notify" },
            State = new WorkflowDefinitionState(
                [],
                new ActivityNode(
                    ChildNodeId,
                    ActivityVersionId: string.Empty,
                    [new ArgumentState(
                        WorkflowIntrinsicInputKeys.Value,
                        new ArgumentValue(correlationEffect, "Literal"),
                        null,
                        null,
                        null,
                        null)],
                    [])
                {
                    Intrinsic = new AuthoredWorkflowIntrinsic(
                        AuthoredWorkflowIntrinsicKind.SetCorrelationId,
                        new TypeReference("String"))
                },
                [],
                [],
                null)
        };

    /// <summary>The parent: one authored <c>DispatchWorkflow</c> node naming the child by definition id.</summary>
    private static WorkflowDefinitionVersion ParentVersion() =>
        new(ParentDefinitionId, "1.0.0")
        {
            Id = ParentVersionId,
            Definition = new WorkflowDefinition { Id = ParentDefinitionId, Name = "Order" },
            State = new WorkflowDefinitionState(
                [],
                new ActivityNode(
                    ParentNodeId,
                    DispatchActivityVersionId,
                    [
                        new ArgumentState(
                            nameof(DispatchWorkflowActivity.WorkflowDefinitionId),
                            new ArgumentValue(ChildDefinitionId, "Literal"),
                            null,
                            null,
                            null,
                            null),
                        new ArgumentState(
                            nameof(DispatchWorkflowActivity.WaitForCompletion),
                            new ArgumentValue(true, "Literal"),
                            null,
                            null,
                            null,
                            null)
                    ],
                    []),
                [],
                [],
                null)
        };

    /// <summary>
    /// The activity-catalog row for <c>DispatchWorkflow</c>, keyed on the <b>CLR-activity consumer</b>.
    /// </summary>
    /// <remarks>
    /// The consumer key is not decoration. <c>ExecutableNodeCompiler</c> only resolves a CLR type — and therefore
    /// only pins an <c>ActivityContract</c> — when the row's consumer key is
    /// <see cref="WellKnownRuntimeActivityConsumers.ClrActivity"/>; a placeholder key compiles to a node the
    /// importing runtime's activator has no strategy for, which the import gate rejects. Pinning it here is what
    /// makes the exported parent executable on another engine at all.
    /// </remarks>
    private static ActivityDefinitionVersion DispatchActivityVersion()
    {
        var alias = TypeAliasConvention.CanonicalAlias(typeof(DispatchWorkflowActivity));
        return new ActivityDefinitionVersion("1.0.0", DispatchActivityDefinitionId)
        {
            Id = DispatchActivityVersionId,
            Definition = new ActivityDefinition
            {
                Id = DispatchActivityDefinitionId,
                ActivityTypeKey = DispatchWorkflowConstants.ActivityType,
                Category = "Workflows"
            },
            ProviderKey = WellKnownRuntimeActivityConsumers.ClrActivity,
            ProviderSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            ConsumerKey = WellKnownRuntimeActivityConsumers.ClrActivity,
            ConsumerSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            DescriptorPayload = JsonSerializer.SerializeToElement(new ClrActivityDescriptor(alias)),
            Inputs =
            [
                new InputDefinition(
                    nameof(DispatchWorkflowActivity.WorkflowDefinitionId),
                    nameof(DispatchWorkflowActivity.WorkflowDefinitionId),
                    new TypeReference("String"),
                    null,
                    "Workflow definition",
                    null,
                    false,
                    IsRequired: true),
                new InputDefinition(
                    nameof(DispatchWorkflowActivity.WaitForCompletion),
                    nameof(DispatchWorkflowActivity.WaitForCompletion),
                    new TypeReference("Boolean"),
                    null,
                    "Wait for completion",
                    null,
                    false),
                new InputDefinition(
                    nameof(DispatchWorkflowActivity.CancelChildOnParentCancellation),
                    nameof(DispatchWorkflowActivity.CancelChildOnParentCancellation),
                    new TypeReference("Boolean"),
                    null,
                    "Cancel child on parent cancellation",
                    null,
                    false),
                new InputDefinition(
                    nameof(DispatchWorkflowActivity.CorrelationId),
                    nameof(DispatchWorkflowActivity.CorrelationId),
                    new TypeReference("String"),
                    null,
                    "Correlation id",
                    null,
                    true),
                new InputDefinition(
                    nameof(DispatchWorkflowActivity.Inputs),
                    nameof(DispatchWorkflowActivity.Inputs),
                    new TypeReference("Object"),
                    null,
                    "Inputs",
                    null,
                    true)
            ]
        };
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    internal sealed class StubWorkflowVersionStore(params WorkflowDefinitionVersion[] versions) : IWorkflowDefinitionVersionStore
    {
        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) =>
            GetWithDefinitionAsync(versionId, cancellationToken);

        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(versions.SingleOrDefault(version => version.Id == versionId));

        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
            versions.SingleOrDefault(version => version.Id == versionId) is { } match
                ? Task.FromResult(match)
                : throw new ArgumentException($"Unknown workflow version '{versionId}'.");

        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(versions.LastOrDefault(version => version.DefinitionId == definitionId));

        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinitionVersion>>(
                versions.Where(version => version.DefinitionId == definitionId).ToArray());

        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(versions.Any(version => version.DefinitionId == definitionId && version.SemVerSortKey == semVerSortKey));
    }

    internal sealed class StubActivityVersionStore(params ActivityDefinitionVersion[] versions) : IActivityDefinitionVersionStore
    {
        public Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) =>
            GetWithDefinitionAsync(versionId, cancellationToken);

        public Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
            versions.SingleOrDefault(version => version.Id == versionId) is { } match
                ? Task.FromResult(match)
                : throw new ArgumentException($"Unknown activity version '{versionId}'.");

        public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(
            string definitionId,
            string semVerSortKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(versions.SingleOrDefault(version =>
                version.DefinitionId == definitionId && version.SemVerSortKey == semVerSortKey));

        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(
            string definitionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>(
                versions.Where(version => version.DefinitionId == definitionId).ToArray());

        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(
            IEnumerable<string> definitionIds,
            CancellationToken cancellationToken = default)
        {
            var ids = definitionIds.ToHashSet(StringComparer.Ordinal);
            return Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>(
                versions.Where(version => ids.Contains(version.DefinitionId)).ToArray());
        }

        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>(versions);
    }

    internal sealed class EmptyActivityPublicationStore : IActivityDefinitionVersionPublicationStore
    {
        public Task<ActivityDefinitionVersionPublication?> FindAsync(
            string definitionVersionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ActivityDefinitionVersionPublication?>(null);

        public Task<IReadOnlyList<ActivityDefinitionVersionPublication>> ListByDefinitionAsync(
            string definitionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionVersionPublication>>([]);
    }
}
