using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Services;
using Xunit;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 235 R-8, #3921: an imported Elsa 3 version n is numbered <c>{n}.{R}.0</c>, R the converter revision. Revision 0 is
/// what every import before revisions existed wrote, so stored data stays valid; a later revision re-imports the same
/// Elsa 3 input as new versions beside the earlier ones, and the publish phase publishes them as successors of the
/// earlier revision's publication without touching it.
/// </summary>
public sealed class ConverterRevisionTests
{
    [Fact]
    public async Task Revision_zero_keeps_the_identities_and_version_numbers_of_earlier_imports()
    {
        var collection = Collection();
        var analyzer = new ReusableActivityCollectionAnalyzer();
        var plan = await analyzer.AnalyzeAsync(collection);
        var mutation = await MaterializeAsync(analyzer, collection, plan);

        var sub = plan.Items.Single(item => item.SourceVersionId == "sub-v1");
        Assert.Equal(0, sub.ConverterRevision);
        Assert.Equal("sub-v1", sub.WorkflowVersionId);
        Assert.Equal(ReusableActivityImportIdentity.Create("activity-version", "sub-v1"), sub.ActivityDefinitionVersionId);
        Assert.Equal("caller-v1", plan.Items.Single(item => item.SourceVersionId == "caller-v1").WorkflowVersionId);
        Assert.All(mutation.Workflows, workflow => Assert.Equal("1.0.0", workflow.Version.Version));
        var activity = Assert.Single(mutation.Activities).Version;
        Assert.Equal("1.0.0", activity.Version);
        Assert.Equal(ReusableActivityImportIdentity.Create("activity-draft", "sub-v1"), activity.DraftId);
        Assert.Equal(ReusableActivityImportIdentity.Create("activity-draft-layout", "sub-v1"), ReusableActivityImportDrafts.Layout(activity).Id);
        Assert.Equal("Elsa 3 v1", ReusableActivityImportDrafts.Draft(activity, null).PresentationLabel);
    }

    [Fact]
    public async Task A_converter_revision_gives_the_same_source_new_identities_numbered_n_R_0()
    {
        var collection = Collection();
        var earlier = new ReusableActivityCollectionAnalyzer();
        var later = new ReusableActivityCollectionAnalyzer(converterRevision: new FixedRevision(1));
        var earlierPlan = await earlier.AnalyzeAsync(collection);
        var laterPlan = await later.AnalyzeAsync(collection);
        var mutation = await MaterializeAsync(later, collection, laterPlan);

        // A different plan for the same input, over the same definitions.
        Assert.NotEqual(earlierPlan.PlanId, laterPlan.PlanId);
        var earlierSub = earlierPlan.Items.Single(item => item.SourceVersionId == "sub-v1");
        var sub = laterPlan.Items.Single(item => item.SourceVersionId == "sub-v1");
        var caller = laterPlan.Items.Single(item => item.SourceVersionId == "caller-v1");
        Assert.Equal(1, sub.ConverterRevision);
        Assert.Equal(earlierSub.WorkflowDefinitionId, sub.WorkflowDefinitionId);
        Assert.Equal(earlierSub.ActivityDefinitionId, sub.ActivityDefinitionId);
        Assert.Equal(earlierSub.ActivityTypeKey, sub.ActivityTypeKey);

        // New version identities, derived from the source version and the revision.
        Assert.Equal(ReusableActivityImportIdentity.WorkflowVersion("sub-v1", 1), sub.WorkflowVersionId);
        Assert.Equal(ReusableActivityImportIdentity.ActivityVersion("sub-v1", 1), sub.ActivityDefinitionVersionId);
        Assert.Equal(ReusableActivityImportIdentity.WorkflowVersion("caller-v1", 1), caller.WorkflowVersionId);
        Assert.NotEqual(earlierSub.WorkflowVersionId, sub.WorkflowVersionId);
        Assert.NotEqual(earlierSub.ActivityDefinitionVersionId, sub.ActivityDefinitionVersionId);
        Assert.NotEqual(ReusableActivityImportIdentity.WorkflowVersion("sub-v1", 1), ReusableActivityImportIdentity.WorkflowVersion("sub-v1", 2));
        Assert.NotEqual(ReusableActivityImportIdentity.ActivityVersion("sub-v1", 1), ReusableActivityImportIdentity.ActivityVersion("sub-v1", 2));

        // The caller's rewritten reference and the wrapper point at the revised activity version.
        Assert.Equal(sub.ActivityDefinitionVersionId, Assert.Single(caller.Rewrites).TargetActivityDefinitionVersionId);
        var wrapper = mutation.Workflows.Single(workflow => workflow.SourceVersionId == "sub-v1");
        var callerWorkflow = mutation.Workflows.Single(workflow => workflow.SourceVersionId == "caller-v1");
        Assert.Equal(sub.WorkflowVersionId, wrapper.Version.Id);
        Assert.Equal(sub.ActivityDefinitionVersionId, wrapper.Version.State.RootActivity!.ActivityVersionId);
        Assert.Equal(caller.WorkflowVersionId, callerWorkflow.Version.Id);
        Assert.Equal("sub", wrapper.Definition.Id);
        Assert.Equal("caller", callerWorkflow.Definition.Id);

        // {n}.{R}.0 everywhere, with a draft of its own.
        Assert.All(mutation.Workflows, workflow => Assert.Equal("1.1.0", workflow.Version.Version));
        var activity = Assert.Single(mutation.Activities).Version;
        Assert.Equal(sub.ActivityDefinitionVersionId, activity.Id);
        Assert.Equal("1.1.0", activity.Version);
        Assert.Equal(1, activity.ConverterRevision);
        Assert.Equal(ReusableActivityImportIdentity.ActivityDraft("sub-v1", 1), activity.DraftId);
        Assert.NotEqual(ReusableActivityImportIdentity.Create("activity-draft", "sub-v1"), activity.DraftId);
        Assert.NotEqual(ReusableActivityImportIdentity.Create("activity-draft-layout", "sub-v1"), ReusableActivityImportDrafts.Layout(activity).Id);
        Assert.Equal("Elsa 3 v1 (converter revision 1)", ReusableActivityImportDrafts.Draft(activity, null).PresentationLabel);
    }

    [Fact]
    public void Imported_version_numbers_put_the_elsa3_version_first_and_the_revision_second()
    {
        Assert.Equal("7.0.0", ReusableActivityImportIdentity.ImportedVersion(7, 0));
        Assert.Equal("7.3.0", ReusableActivityImportIdentity.ImportedVersion(7, 3));
        // A later revision sorts after the earlier one and before the next Elsa 3 version.
        Assert.True(string.CompareOrdinal(Elsa.Primitives.Versioning.SemVer.ToSortKey("7.3.0"), Elsa.Primitives.Versioning.SemVer.ToSortKey("7.0.0")) > 0);
        Assert.True(string.CompareOrdinal(Elsa.Primitives.Versioning.SemVer.ToSortKey("8.0.0"), Elsa.Primitives.Versioning.SemVer.ToSortKey("7.3.0")) > 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => ReusableActivityImportIdentity.ImportedVersion(7, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReusableActivityImportIdentity.WorkflowVersion("sub-v1", -1));
    }

    [Fact]
    public async Task The_publish_phase_publishes_a_later_revision_as_a_successor_of_the_earlier_revisions_publication()
    {
        // Revision 0 of sub-v1 is published and is the definition's head; revision 1 arrives.
        var earlier = Activity(revision: 0);
        var later = Activity(revision: 1);
        var stores = new PublishedEarlierRevision(earlier);
        var publisher = new RecordingPublisher();
        var phase = new ReusableActivityImportPublishPhase(stores, stores, stores, stores, publisher);

        var state = Assert.Single(await phase.PublishAsync([later], "receipt-2"));

        Assert.Equal(ReusableActivityPublicationStatus.Published, state.Status);
        var request = Assert.Single(publisher.Requests);
        Assert.Equal(later.Version.Id, request.RequestedVersionId);
        Assert.Equal("1.1.0", request.Version);
        Assert.Equal(later.Version.DraftId, request.DraftId);
        Assert.NotEqual(earlier.Version.DraftId, request.DraftId);
        // Published after the earlier revision, from a draft of its own; the earlier publication is read, never written.
        Assert.Equal(earlier.Version.Id, request.ExpectedDefinitionHeadVersionId);
        Assert.Equal([later.Version.DraftId], stores.CreatedDrafts);
    }

    private static ReusableActivityImportCollection Collection()
    {
        var call = ReusableActivityImportFixtures.Reference("call", targetVersionId: "sub-v1");
        return ReusableActivityImportFixtures.Collection(
            ReusableActivityImportFixtures.Workflow("sub", "sub-v1", 1, true, ReusableActivityImportFixtures.Leaf("sub-root")),
            ReusableActivityImportFixtures.Workflow("caller", "caller-v1", 1, false, call));
    }

    private static async Task<ReusableActivityImportMutation> MaterializeAsync(
        ReusableActivityCollectionAnalyzer analyzer, ReusableActivityImportCollection collection, ReusableActivityImportPlan plan)
    {
        var command = new ReusableActivityImportFixtures.CapturingCommand();
        var importer = new ReusableActivityCollectionImporter(analyzer, ReusableActivityImportFixtures.Materializer(), command, ReusableActivityImportFixtures.Validator());
        await importer.ApplyAsync(new(plan.PlanId, collection, ["sub-v1", "caller-v1"]));
        return Assert.IsType<ReusableActivityImportMutation>(command.Mutation);
    }

    private static ImportedReusableActivity Activity(int revision) => new(
        new ActivityDefinition { Id = "sub-definition", ActivityTypeKey = "elsa3.sub", Category = "Test" },
        new ImportedReusableActivityVersion(
            ReusableActivityImportIdentity.ActivityVersion("sub-v1", revision), "sub-definition",
            ReusableActivityImportIdentity.ImportedVersion(1, revision), "sub-v1",
            new ActivityContract("1", [], [], []), JsonSerializer.SerializeToElement(new { revision }), [], DateTimeOffset.UnixEpoch, revision),
        new ActivityDefinitionAuthoringState { Id = "sub-authoring", DefinitionId = "sub-definition" },
        new WorkflowDefinitionState([], null, [], [], null));

    private sealed class FixedRevision(int current) : IElsa3ConverterRevision
    {
        public int Current => current;
    }

    private sealed class RecordingPublisher : IReusableActivityImportPublisher
    {
        public List<ReusableActivityImportPublishRequest> Requests { get; } = [];

        public ValueTask<ReusableActivityImportPublishOutcome> PublishAsync(ReusableActivityImportPublishRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return ValueTask.FromResult(ReusableActivityImportPublishOutcome.Applied);
        }
    }

    /// <summary>Stores holding the publication, draft and head of one earlier revision; they record the drafts created.</summary>
    private sealed class PublishedEarlierRevision(ImportedReusableActivity earlier) :
        IActivityDefinitionVersionPublicationStore, IActivityDefinitionAuthoringStore, IActivityDefinitionDraftStore, ICreateActivityDraftCommand
    {
        public List<string> CreatedDrafts { get; } = [];

        public Task<ActivityDefinitionVersionPublication?> FindAsync(string definitionVersionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(definitionVersionId == earlier.Version.Id
                ? new ActivityDefinitionVersionPublication
                {
                    DefinitionVersionId = earlier.Version.Id,
                    DefinitionId = earlier.Definition.Id,
                    Version = earlier.Version.Version,
                    SourceDraftId = earlier.Version.DraftId,
                    DirectDependencyCount = 0,
                    ClosedTemplateCount = 0,
                    RuntimeRequirements = []
                }
                : null);

        public Task<IReadOnlyList<ActivityDefinitionVersionPublication>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionVersionPublication>>([]);

        Task<ActivityDefinitionAuthoringState?> IActivityDefinitionAuthoringStore.FindAsync(string definitionId, CancellationToken cancellationToken) =>
            Task.FromResult<ActivityDefinitionAuthoringState?>(new() { Id = "sub-authoring", DefinitionId = earlier.Definition.Id, HeadVersionId = earlier.Version.Id });

        public Task<IReadOnlyList<ActivityDefinitionAuthoringState>> ListAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionAuthoringState>>([]);

        Task<ActivityDefinitionDraft?> IActivityDefinitionDraftStore.FindAsync(string draftId, CancellationToken cancellationToken) =>
            Task.FromResult(draftId == earlier.Version.DraftId ? ReusableActivityImportDrafts.Draft(earlier.Version, null) : null);

        Task<IReadOnlyList<ActivityDefinitionDraft>> IActivityDefinitionDraftStore.ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionDraft>>([]);

        public Task ExecuteAsync(CreateActivityDraftRequest request, CancellationToken cancellationToken = default)
        {
            CreatedDrafts.Add(request.Draft.Id);
            return Task.CompletedTask;
        }
    }
}
