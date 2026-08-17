using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Locking.Core;
using Elsa.Persistence.Core;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Persistence.Groundwork.Services;
using Elsa3.Activities.Design.Import.Services;
using Elsa3.Activities.Design.Import.Persistence.Groundwork;
using Xunit;

namespace Elsa3.Mapping.Tests;

public sealed class ReusableActivityCollectionApplyTests
{
    [Fact]
    public async Task Apply_materializes_dependency_closed_exact_rewrites_and_original_identity_wrappers()
    {
        var collection = ValidCollection();
        var analyzer = new ReusableActivityCollectionAnalyzer();
        var plan = await analyzer.AnalyzeAsync(collection);
        var command = new CapturingCommand();
        var importer = new ReusableActivityCollectionImporter(analyzer, ReusableActivityImportFixtures.Materializer(), command);

        var result = await importer.ApplyAsync(new(plan.PlanId, collection, ["a-v1", "b-v1", "consumer-v1"]));

        Assert.False(result.NoOp);
        var mutation = Assert.IsType<ReusableActivityImportMutation>(command.Mutation);
        Assert.Equal(2, mutation.Activities.Count);
        Assert.Equal(3, mutation.Workflows.Count);
        var aItem = plan.Items.Single(x => x.SourceVersionId == "a-v1");
        var bItem = plan.Items.Single(x => x.SourceVersionId == "b-v1");
        var aWrapper = mutation.Workflows.Single(x => x.Version.Id == "a-v1");
        Assert.Equal("a", aWrapper.Definition.Id);
        Assert.Equal(aItem.ActivityDefinitionVersionId, aWrapper.Version.State.RootActivity!.ActivityVersionId);
        var bGraph = mutation.Activities.Single(x => x.Version.Id == bItem.ActivityDefinitionVersionId);
        Assert.Contains(aItem.ActivityDefinitionVersionId!, bGraph.Version.DescriptorPayload.GetRawText(), StringComparison.Ordinal);
        var consumer = mutation.Workflows.Single(x => x.Version.Id == "consumer-v1");
        Assert.Equal(bItem.ActivityDefinitionVersionId, consumer.Version.State.RootActivity!.ActivityVersionId);
        Assert.All(mutation.Activities, x => Assert.Equal("elsa.activity-graph", x.Version.ProviderKey));
        Assert.All(mutation.Activities, x => Assert.Equal("elsa.graph-activity", x.Version.ConsumerKey));
    }

    [Fact]
    public async Task Apply_rejects_non_closed_selection_before_materialization_or_commit()
    {
        var collection = ValidCollection();
        var analyzer = new ReusableActivityCollectionAnalyzer();
        var plan = await analyzer.AnalyzeAsync(collection);
        var materializer = new CapturingMaterializer();
        var command = new CapturingCommand();
        var importer = new ReusableActivityCollectionImporter(analyzer, materializer, command);

        var exception = await Assert.ThrowsAsync<ReusableActivityImportValidationException>(async () =>
            await importer.ApplyAsync(new(plan.PlanId, collection, ["b-v1"])));

        Assert.Contains(exception.Diagnostics, x => x.Code == ReusableActivityImportDiagnosticCodes.SelectionNotClosed);
        Assert.False(materializer.Called);
        Assert.Null(command.Mutation);
    }

    [Fact]
    public async Task Apply_rejects_an_empty_selection_before_materialization_or_commit()
    {
        var collection = ValidCollection();
        var analyzer = new ReusableActivityCollectionAnalyzer();
        var plan = await analyzer.AnalyzeAsync(collection);
        var materializer = new CapturingMaterializer();
        var command = new CapturingCommand();
        var importer = new ReusableActivityCollectionImporter(analyzer, materializer, command);

        var exception = await Assert.ThrowsAsync<ReusableActivityImportValidationException>(async () =>
            await importer.ApplyAsync(new(plan.PlanId, collection, [])));

        Assert.Equal(ReusableActivityImportDiagnosticCodes.SelectionInvalid, Assert.Single(exception.Diagnostics).Code);
        Assert.False(materializer.Called);
        Assert.Null(command.Mutation);
    }

    [Fact]
    public async Task Apply_can_commit_a_valid_selection_when_unrelated_collection_members_are_invalid()
    {
        var valid = ReusableActivityImportFixtures.Workflow("valid", "valid-v1", 1, true, ReusableActivityImportFixtures.Leaf("valid-root"));
        var duplicateOne = ReusableActivityImportFixtures.Workflow("duplicate-one", "duplicate-v1", 1, true, ReusableActivityImportFixtures.Leaf("one"));
        var duplicateTwo = ReusableActivityImportFixtures.Workflow("duplicate-two", "duplicate-v1", 1, true, ReusableActivityImportFixtures.Leaf("two"));
        var collection = ReusableActivityImportFixtures.Collection(valid, duplicateOne, duplicateTwo);
        var analyzer = new ReusableActivityCollectionAnalyzer();
        var plan = await analyzer.AnalyzeAsync(collection);
        var command = new CapturingCommand();
        var importer = new ReusableActivityCollectionImporter(analyzer, ReusableActivityImportFixtures.Materializer(), command);

        var result = await importer.ApplyAsync(new(plan.PlanId, collection, ["valid-v1"]));

        Assert.Equal(["valid-v1"], result.AppliedSourceVersionIds);
        Assert.Single(command.Mutation!.Activities);
        Assert.Single(command.Mutation.Workflows);
    }

    [Fact]
    public async Task Apply_does_not_report_or_retain_a_partial_result_when_atomic_command_fails()
    {
        var collection = ValidCollection();
        var analyzer = new ReusableActivityCollectionAnalyzer();
        var plan = await analyzer.AnalyzeAsync(collection);
        var command = new FailingCommand();
        var importer = new ReusableActivityCollectionImporter(analyzer, ReusableActivityImportFixtures.Materializer(), command);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await importer.ApplyAsync(new(plan.PlanId, collection, ["a-v1", "b-v1"])));

        Assert.True(command.Called);
        Assert.False(command.Committed);
    }

    [Fact]
    public async Task Groundwork_reapply_is_idempotent_and_collision_preflight_writes_nothing_else()
    {
        var collection = ValidCollection();
        var analyzer = new ReusableActivityCollectionAnalyzer();
        var plan = await analyzer.AnalyzeAsync(collection);
        var selection = plan.Items.Where(x => x.SourceVersionId is "a-v1" or "b-v1").ToArray();
        var mutation = await ReusableActivityImportFixtures.Materializer().MaterializeAsync(collection, plan, selection);
        using var harness = ReusableActivityImportV2TestHarness.Create();
        var command = Command(harness);

        var first = await command.CommitAsync(mutation);
        var second = await command.CommitAsync(mutation);

        Assert.False(first.NoOp);
        Assert.True(second.NoOp);
        Assert.Equal(2, harness.Snapshot("activityDefinition").Count);
        Assert.Equal(2, harness.Snapshot("activityDefinitionVersion").Count);
        Assert.Equal(2, harness.Snapshot("workflowDefinition").Count);
        Assert.Equal(2, harness.Snapshot("workflowDefinitionVersion").Count);

        using var conflictingHarness = ReusableActivityImportV2TestHarness.Create();
        var conflicting = mutation.Activities[0].Definition.Id;
        await conflictingHarness.Store.SaveAsync(new(
            "activityDefinition",
            conflicting,
            ActivitiesDesignStorageManifest.SchemaVersion,
            "{\"different\":true}",
            ExpectedVersion: 0));
        var conflictingCommand = Command(conflictingHarness);

        await Assert.ThrowsAsync<ReusableActivityImportPersistenceException>(async () => await conflictingCommand.CommitAsync(mutation));
        Assert.Empty(conflictingHarness.Snapshot("activityDefinitionVersion"));
        Assert.Empty(conflictingHarness.Snapshot("workflowDefinition"));
        Assert.Empty(conflictingHarness.Snapshot("workflowDefinitionVersion"));
    }

    [Fact]
    public async Task Groundwork_coalesces_shared_definition_and_authoring_documents_across_selected_lineage_versions()
    {
        var first = ReusableActivityImportFixtures.Workflow("lineage", "lineage-v1", 1, true, ReusableActivityImportFixtures.Leaf("root-v1"));
        var second = ReusableActivityImportFixtures.Workflow("lineage", "lineage-v2", 2, true, ReusableActivityImportFixtures.Leaf("root-v2"));
        var collection = ReusableActivityImportFixtures.Collection(first, second);
        var analyzer = new ReusableActivityCollectionAnalyzer();
        var plan = await analyzer.AnalyzeAsync(collection);
        var mutation = await ReusableActivityImportFixtures.Materializer().MaterializeAsync(collection, plan, plan.Items);
        using var harness = ReusableActivityImportV2TestHarness.Create();
        var command = Command(harness);

        await command.CommitAsync(mutation);

        Assert.Single(harness.Snapshot("activityDefinition"));
        Assert.Equal(2, harness.Snapshot("activityDefinitionVersion").Count);
        Assert.Single(harness.Snapshot("activityDefinitionAuthoringState"));
        Assert.Single(harness.Snapshot("workflowDefinition"));
        Assert.Equal(2, harness.Snapshot("workflowDefinitionVersion").Count);
    }

    [Fact]
    public async Task Changed_collection_cannot_apply_an_observed_plan_id()
    {
        var collection = ValidCollection();
        var analyzer = new ReusableActivityCollectionAnalyzer();
        var plan = await analyzer.AnalyzeAsync(collection);
        collection.Definitions[0].Name = "changed after review";
        var importer = new ReusableActivityCollectionImporter(analyzer, new CapturingMaterializer(), new CapturingCommand());

        var exception = await Assert.ThrowsAsync<ReusableActivityImportValidationException>(async () =>
            await importer.ApplyAsync(new(plan.PlanId, collection, ["a-v1"])));

        Assert.Equal(ReusableActivityImportDiagnosticCodes.PlanChanged, Assert.Single(exception.Diagnostics).Code);
    }

    private static ReusableActivityImportCollection ValidCollection()
    {
        var input = new Elsa3.Models.Elsa3WorkflowArgumentDefinition { Name = "message", Type = typeof(string).FullName! };
        var a = ReusableActivityImportFixtures.Workflow("a", "a-v1", 1, true, ReusableActivityImportFixtures.Leaf("a-root", directStart: true), input);
        var b = ReusableActivityImportFixtures.Workflow("b", "b-v1", 1, true, ReusableActivityImportFixtures.Reference("b-to-a", targetVersionId: "a-v1"));
        var consumer = ReusableActivityImportFixtures.Workflow("consumer", "consumer-v1", 1, false, ReusableActivityImportFixtures.Reference("consumer-to-b", targetVersionId: "b-v1"));
        return ReusableActivityImportFixtures.Collection(a, b, consumer);
    }

    private static IPayloadSerializer Serializer() => new JsonPayloadSerializer(new JsonPayloadConverterRegistry());

    private static GroundworkReusableActivityImportCommand Command(
        ReusableActivityImportV2TestHarness harness) =>
        new(
            harness.Store,
            harness.WorkflowStorage,
            harness.Sessions,
            Serializer(),
            new GroundworkActivityManagementProjectionWriter(harness.Store, new ImmediateLockProvider(), harness.Store),
            harness.Access);

    private sealed class ImmediateLockProvider : IDistributedLockProvider
    {
        public IDistributedSynchronizationHandle? TryAcquireLock(
            string name,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) => new Handle();

        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(
            string name,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDistributedSynchronizationHandle?>(new Handle());

        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(
            string name,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle());

        private sealed class Handle : IDistributedSynchronizationHandle
        {
            public CancellationToken HandleLostToken => CancellationToken.None;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class CapturingCommand : IReusableActivityImportCommand
    {
        public ReusableActivityImportMutation? Mutation { get; private set; }
        public ValueTask<ReusableActivityImportCommitResult> CommitAsync(ReusableActivityImportMutation mutation, CancellationToken cancellationToken = default)
        {
            Mutation = mutation;
            return ValueTask.FromResult(new ReusableActivityImportCommitResult(false));
        }
    }

    private sealed class FailingCommand : IReusableActivityImportCommand
    {
        public bool Called { get; private set; }
        public bool Committed { get; private set; }
        public ValueTask<ReusableActivityImportCommitResult> CommitAsync(ReusableActivityImportMutation mutation, CancellationToken cancellationToken = default)
        {
            Called = true;
            throw new InvalidOperationException("atomic failpoint");
        }
    }

    private sealed class CapturingMaterializer : IReusableActivityImportMaterializer
    {
        public bool Called { get; private set; }
        public ValueTask<ReusableActivityImportMutation> MaterializeAsync(ReusableActivityImportCollection collection, ReusableActivityImportPlan plan, IReadOnlyList<ReusableActivityImportItem> selection, CancellationToken cancellationToken = default)
        {
            Called = true;
            return ValueTask.FromResult(new ReusableActivityImportMutation(plan.PlanId, plan.CollectionId, selection.Select(x => x.SourceVersionId).ToArray(), [], []));
        }
    }
}
