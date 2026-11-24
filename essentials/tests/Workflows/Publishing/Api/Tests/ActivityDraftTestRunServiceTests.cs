using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Publishing.Api.Contracts;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Dispatch;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.Time.Testing;
using Elsa.Workflows.Runtime.Services.Values;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using WorkflowExecutableHasher = Elsa.Workflows.Publishing.Services.WorkflowExecutableHasher;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed partial class ActivityDraftTestRunServiceTests
{
    [Fact]
    public async Task Published_version_idempotency_compares_canonical_input_values()
    {
        var fixture = await CreateFixture(childTemplate: ValidChildTemplate(), includePublishedVersion: true, acceptDispatch: true);
        var version = Assert.IsType<ActivityDefinitionVersionPublication>(fixture.PublishedVersion);
        StartActivityVersionTestRun Request(JsonElement? value) => new(version.DefinitionVersionId, "version-json-contract",
            new Dictionary<string, ActivityDraftTestRunInput> { ["probe"] = new("Present", value) });
        var first = await fixture.Service.StartVersionAsync(Request(Json("{\"n\":9007199254740993.0,\"text\":\"\\u0061\",\"nil\":null}")));
        var replay = await fixture.Service.StartVersionAsync(Request(Json("{\"nil\":null,\"text\":\"a\",\"n\":9007199254740993e0}")));
        Assert.Equal(first.TestRunId, replay.TestRunId);
        foreach (var changed in new JsonElement?[] { Json("{\"n\":9007199254740992,\"text\":\"a\",\"nil\":null}"),
                     Json("{\"n\":9007199254740993,\"text\":\"a\"}"), Json("[1,2]"), Json("[2,1]"), Json("null"), null })
        {
            var refusal = await Assert.ThrowsAsync<ActivityPublicationRejectedException>(() => fixture.Service.StartVersionAsync(Request(changed)));
            Assert.Equal("activity.test-run.idempotency-mismatch", refusal.ErrorCode);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_rejected_missing_input_receipt_cannot_replay_an_explicit_null(bool published)
    {
        var fixture = await CreateFixture(childTemplate: ValidChildTemplate(), includePublishedVersion: true, acceptDispatch: true);
        var version = Assert.IsType<ActivityDefinitionVersionPublication>(fixture.PublishedVersion);
        async Task<ActivityDraftTestRunView> Start(JsonElement? value)
        {
            var inputs = new Dictionary<string, ActivityDraftTestRunInput> { ["probe"] = new("Present", value) };
            return published
                ? await fixture.Service.StartVersionAsync(new(version.DefinitionVersionId, "presence-contract", inputs))
                : await fixture.Service.StartAsync(fixture.Request with { Inputs = inputs });
        }
        var missing = await Start(null);
        Assert.Equal("ValidationRejected", missing.Status);
        var refusal = await Assert.ThrowsAsync<ActivityPublicationRejectedException>(() => Start(Json("null")));
        Assert.Equal("activity.test-run.idempotency-mismatch", refusal.ErrorCode);
    }

    [Fact]
    public async Task Idempotency_compares_input_json_values_and_distinguishes_missing_null_and_exact_numbers()
    {
        var fixture = await CreateFixture();
        StartActivityDraftTestRun Request(JsonElement? value) => fixture.Request with
        {
            Inputs = new Dictionary<string, ActivityDraftTestRunInput> { ["probe"] = new("Present", value) }
        };
        var first = await fixture.Service.StartAsync(Request(Json("{\"n\":100000000000000000000000000000000000001.0,\"text\":\"\\u0061\",\"nil\":null}")));
        var replay = await fixture.Service.StartAsync(Request(Json("{\"nil\":null,\"text\":\"a\",\"n\":100000000000000000000000000000000000001}")));
        Assert.Equal(first.TestRunId, replay.TestRunId);
        foreach (var changed in new JsonElement?[]
                 {
                     Json("{\"n\":100000000000000000000000000000000000002,\"text\":\"a\",\"nil\":null}"),
                     Json("{\"n\":100000000000000000000000000000000000001,\"text\":\"a\"}"),
                     Json("null"), null
                 })
        {
            var refusal = await Assert.ThrowsAsync<ActivityPublicationRejectedException>(() => fixture.Service.StartAsync(Request(changed)));
            Assert.Equal("activity.test-run.idempotency-mismatch", refusal.ErrorCode);
        }
    }

    [Fact]
    public async Task Missing_required_input_is_persisted_as_safe_actionable_pre_dispatch_rejection()
    {
        var fixture = await CreateFixture();

        var response = await fixture.Service.StartAsync(fixture.Request);

        Assert.Equal("ValidationRejected", response.Status);
        Assert.Equal("Validation", response.Failure?.Kind);
        Assert.Equal("activity.test-run.required-input", response.Failure?.Code);
        Assert.Equal("A required activity input has no value or default. Provide values for required inputs before running the Test Run.", response.Failure?.Message);
        Assert.DoesNotContain("VF-ACT-003", response.Failure?.Message ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("required-value", response.Failure?.Message ?? string.Empty, StringComparison.Ordinal);
        Assert.Null(response.ArtifactId);
        Assert.Null(response.SourceReferenceId);
        Assert.Null(response.CommandDispatchStatus);
        Assert.False(fixture.Dispatcher.WasCalled);

        var replay = await fixture.Service.StartAsync(fixture.Request);
        Assert.Equal("ValidationRejected", replay.Status);
        Assert.Equal(response.TestRunId, replay.TestRunId);
        Assert.Equal(response.Failure, replay.Failure);
        Assert.False(fixture.Dispatcher.WasCalled);

        var polled = await fixture.Service.GetAsync(response.TestRunId);
        Assert.Equal("ValidationRejected", polled.Status);
        Assert.Equal("activity.test-run.required-input", polled.Failure?.Code);
        Assert.Equal(response.Failure?.Message, polled.Failure?.Message);

        var stored = await fixture.Receipts.FindByIdempotencyKeyAsync(
            ActivityDraftTestRunIdentity.CreateOperationScope(null), fixture.Draft.Id, fixture.Request.IdempotencyKey);
        Assert.NotNull(stored);
        Assert.Equal(ActivityDraftTestRunReceiptStatus.ValidationRejected, stored.Status);
        Assert.Equal("activity.test-run.required-input", stored.Failure?.Code);
        Assert.Empty(stored.Failure!.Diagnostics);
        Assert.Null(stored.ArtifactId);
        Assert.Null(stored.SourceReferenceId);
        Assert.Null(stored.CommandDispatchStatus);
        Assert.Equal(2, stored.Revision);
        Assert.False(fixture.Dispatcher.WasCalled);
    }

    [Fact]
    public async Task Unexpected_placement_failure_keeps_the_redacted_pre_dispatch_rejection_on_replay()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = await CreateFixture(wrapTemplates: inner => new CancelOnDependencyReadTemplateStore(
            inner,
            cancellation,
            "child-template",
            new InvalidOperationException("hidden authored value")));

        var response = await fixture.Service.StartAsync(fixture.Request);

        Assert.Equal("ValidationRejected", response.Status);
        Assert.Equal("activity.test-run.preparation-failed", response.Failure?.Code);
        Assert.Equal("The activity draft could not be prepared for a Test Run.", response.Failure?.Message);
        Assert.DoesNotContain("hidden authored value", response.Failure?.Message ?? string.Empty, StringComparison.Ordinal);
        Assert.Null(response.ArtifactId);
        Assert.Null(response.SourceReferenceId);
        Assert.Null(response.CommandDispatchStatus);
        Assert.False(fixture.Dispatcher.WasCalled);

        var replay = await fixture.Service.StartAsync(fixture.Request);
        Assert.Equal(response.TestRunId, replay.TestRunId);
        Assert.Equal(response.Failure, replay.Failure);
        Assert.False(fixture.Dispatcher.WasCalled);
    }

    [Fact]
    public async Task Placement_failure_returns_authoritative_receipt_when_another_start_advanced_it()
    {
        var receipts = new CompetingReceiptStore(
            ActivityDraftTestRunReceiptStatus.ValidationRejected,
            ActivityDraftTestRunReceiptStatus.DispatchAccepted);
        var fixture = await CreateFixture(receipts: receipts);

        var response = await fixture.Service.StartAsync(fixture.Request);

        Assert.True(receipts.AdvancedReceipt);
        Assert.Equal("DispatchAccepted", response.Status);
        Assert.Equal("Accepted", response.CommandDispatchStatus);
        Assert.Equal("artifact-from-concurrent-start", response.ArtifactId);
        Assert.Equal("source-from-concurrent-start", response.SourceReferenceId);
        Assert.Null(response.Failure);
        Assert.False(fixture.Dispatcher.WasCalled);

        var stored = await fixture.Receipts.FindAsync(response.TestRunId);
        Assert.NotNull(stored);
        Assert.Equal(ActivityDraftTestRunReceiptStatus.DispatchAccepted, stored.Status);
        Assert.Equal(2, stored.Revision);
        Assert.Null(stored.Failure);
        Assert.False(fixture.Dispatcher.WasCalled);
    }

    [Fact]
    public async Task A_successful_preparation_does_not_dispatch_after_concurrent_validation_rejection()
    {
        var receipts = new CompetingReceiptStore(
            ActivityDraftTestRunReceiptStatus.Dispatching,
            ActivityDraftTestRunReceiptStatus.ValidationRejected);
        var fixture = await CreateFixture(receipts: receipts, childTemplate: ValidChildTemplate());

        var response = await fixture.Service.StartAsync(fixture.Request);

        Assert.True(receipts.AdvancedReceipt);
        Assert.Equal("ValidationRejected", response.Status);
        Assert.Equal("activity.test-run.preparation-failed", response.Failure?.Code);
        Assert.Null(response.ArtifactId);
        Assert.Null(response.SourceReferenceId);
        Assert.Null(response.CommandDispatchStatus);
        Assert.False(fixture.Dispatcher.WasCalled);

        var stored = await fixture.Receipts.FindAsync(response.TestRunId);
        Assert.NotNull(stored);
        Assert.Equal(ActivityDraftTestRunReceiptStatus.ValidationRejected, stored.Status);
        Assert.Null(stored.ArtifactId);
        Assert.Null(stored.SourceReferenceId);
        Assert.False(fixture.Dispatcher.WasCalled);
    }

    [Fact]
    public async Task Cancellation_during_placement_leaves_receipt_unmodified_and_does_not_dispatch()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = await CreateFixture(
            wrapTemplates: inner => new CancelOnDependencyReadTemplateStore(inner, cancellation, "child-template"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Service.StartAsync(fixture.Request, cancellation.Token));

        var testRunId = ActivityDraftTestRunIdentity.CreateTestRunId(
            ActivityDraftTestRunIdentity.CreateOperationScope(null),
            fixture.Draft.Id,
            fixture.Request.IdempotencyKey);
        var stored = await fixture.Receipts.FindAsync(testRunId);
        Assert.NotNull(stored);
        Assert.Equal(ActivityDraftTestRunReceiptStatus.Preparing, stored.Status);
        Assert.Equal(1, stored.Revision);
        Assert.Null(stored.Failure);
        Assert.Null(stored.ArtifactId);
        Assert.Null(stored.SourceReferenceId);
        Assert.Null(stored.CommandDispatchStatus);
        Assert.False(fixture.Dispatcher.WasCalled);
    }

    [Fact]
    public async Task Published_version_test_run_uses_the_exact_version_and_replays_without_reading_a_draft()
    {
        var fixture = await CreateFixture(childTemplate: ValidChildTemplate(), includePublishedVersion: true, acceptDispatch: true);
        var version = Assert.IsType<ActivityDefinitionVersionPublication>(fixture.PublishedVersion);
        var request = new StartActivityVersionTestRun(version.DefinitionVersionId, "published-test-run-1");

        var response = await fixture.Service.StartVersionAsync(request);

        Assert.Equal(version.DefinitionVersionId, response.SourceVersionId);
        Assert.Equal(string.Empty, response.DraftId);
        Assert.Equal(0, response.DraftRevision);
        Assert.Equal("DispatchAccepted", response.Status);
        Assert.Equal("Accepted", response.CommandDispatchStatus);
        Assert.Equal(0, fixture.DraftStore.FindCount);
        Assert.Equal(response.ArtifactId, fixture.Dispatcher.LastRequest?.ArtifactId);
        var storedTemplate = await fixture.Templates.FindAsync(version.TemplateId);
        Assert.NotNull(storedTemplate);
        Assert.Equal(version.TemplateHash, storedTemplate.TemplateHash);
        Assert.Equal(version.DefinitionVersionId, fixture.Dispatcher.LastRequest?.Metadata["activity.versionId"]);
        Assert.DoesNotContain("activity.draftId", fixture.Dispatcher.LastRequest!.Metadata.Keys);
        Assert.Equal(1, fixture.Dispatcher.DispatchCount);

        var replay = await fixture.Service.StartVersionAsync(request);
        var lookup = await fixture.Service.GetVersionByIdempotencyKeyAsync(version.DefinitionVersionId, request.IdempotencyKey);

        Assert.Equal(response.TestRunId, replay.TestRunId);
        Assert.Equal(response.TestRunId, lookup.TestRunId);
        Assert.Equal(version.DefinitionVersionId, lookup.SourceVersionId);
        Assert.Equal(1, fixture.Dispatcher.DispatchCount);
        Assert.Equal(0, fixture.DraftStore.FindCount);
    }

    [Fact]
    public async Task Published_version_test_run_rejects_an_unauthorized_tenant_before_receipt_or_dispatch()
    {
        var fixture = await CreateFixture(childTemplate: ValidChildTemplate(), includePublishedVersion: true,
            publishedVersionTenantId: "tenant-other", acceptDispatch: true);
        var version = Assert.IsType<ActivityDefinitionVersionPublication>(fixture.PublishedVersion);

        var exception = await Assert.ThrowsAsync<ActivityPublicationRejectedException>(() => fixture.Service.StartVersionAsync(
            new StartActivityVersionTestRun(version.DefinitionVersionId, "unauthorized-published-test-run")));

        Assert.Equal("activity.tenant.reference-denied", exception.ErrorCode);
        Assert.False(fixture.Dispatcher.WasCalled);
        Assert.Equal(0, fixture.DraftStore.FindCount);
        Assert.Null(await fixture.Receipts.FindByIdempotencyKeyAsync(
            ActivityDraftTestRunIdentity.CreateOperationScope(null),
            $"activity-version:{version.DefinitionVersionId}",
            "unauthorized-published-test-run"));
    }

    private static async Task<TestRunFixture> CreateFixture(
        Func<InMemoryExecutableActivityTemplateStore, IExecutableActivityTemplateStore>? wrapTemplates = null,
        IActivityDraftTestRunStore? receipts = null,
        ExecutableActivityTemplate? childTemplate = null,
        IServiceProvider? runtimeServices = null,
        Func<IWorkflowExecutableRootWriteLeaseManager, IWorkflowExecutableRootWriteLeaseManager>? wrapRootWrites = null,
        Func<IExecutableActivityTemplateStore, IWorkflowExecutableSourceReferenceStore,
            (IExecutableActivityTemplateStore Templates, IWorkflowExecutableSourceReferenceStore References)>? selectRootPorts = null,
        bool includePublishedVersion = false,
        bool acceptDispatch = false,
        string? publishedVersionTenantId = null)
    {
        var now = new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);
        var clock = runtimeServices is null ? new FakeTimeProvider(now) : (FakeTimeProvider)runtimeServices.GetRequiredService<TimeProvider>();
        childTemplate ??= RequiredInputTemplate();
        var rootTemplate = GraphTemplate(childTemplate);
        var childPublication = Publication("child-definition", "child-version", "child-type", childTemplate);
        var childReference = new WorkflowExecutableSourceReference(
            "child-source-reference",
            childTemplate.TemplateId,
            "ActivityDefinitionVersion",
            childPublication.DefinitionId,
            childPublication.Version,
            childPublication.DefinitionId,
            childPublication.DefinitionVersionId,
            childPublication.Version,
            now,
            now,
            WorkflowExecutableReferenceScope.Published);
        var artifacts = runtimeServices?.GetRequiredService<IWorkflowExecutableStore>() ?? new InMemoryWorkflowExecutableStore();
        var sourceReferences = runtimeServices?.GetRequiredService<IWorkflowExecutableSourceReferenceStore>() ?? new InMemoryWorkflowExecutableSourceReferenceStore(artifacts, clock);
        var innerTemplates = runtimeServices is null
            ? InMemoryExecutableActivityTemplateStore.CreateForMaintenance(artifacts, sourceReferences, clock)
            : (InMemoryExecutableActivityTemplateStore)runtimeServices.GetRequiredService<IExecutableActivityTemplateStore>();
        await innerTemplates.SaveAsync(childTemplate);
        var templates = wrapTemplates?.Invoke(innerTemplates) ?? innerTemplates;
        if (selectRootPorts is not null) (templates, sourceReferences) = selectRootPorts(templates, sourceReferences);
        await sourceReferences.SaveAsync(childReference);
        var draftContract = new Elsa.Activities.Design.Core.Models.ActivityContract("1", [], [], []);
        var draft = new ActivityDefinitionDraft
        {
            Id = "draft-1",
            DefinitionId = "definition-1",
            Revision = 4,
            Status = ActivityDefinitionDraftStatus.Active,
            State = new(draftContract, new("test.provider", "1", Json("{}")), new Dictionary<string, string>())
        };
        var definition = new ActivityDefinition
        {
            Id = "definition-1",
            ActivityTypeKey = "activity-under-test",
            Category = "Tests",
            DisplayName = "Activity under test"
        };
        ActivityDefinitionVersionPublication? publishedVersion = null;
        if (includePublishedVersion)
        {
            publishedVersion = Publication(definition.Id, "published-version-1", definition.ActivityTypeKey,
                rootTemplate, "published-source-reference", publishedVersionTenantId);
            var publishedReference = new WorkflowExecutableSourceReference(
                publishedVersion.SourceReferenceId,
                rootTemplate.TemplateId,
                "ActivityDefinitionVersion",
                definition.Id,
                publishedVersion.Version,
                definition.Id,
                publishedVersion.DefinitionVersionId,
                publishedVersion.Version,
                now,
                now,
                WorkflowExecutableReferenceScope.Published);
            await innerTemplates.SaveAsync(rootTemplate);
            await sourceReferences.SaveAsync(publishedReference);
        }
        var layout = new ActivityDefinitionDraftLayout { Id = "layout-1", DraftId = draft.Id, Revision = draft.Revision };
        receipts ??= new InMemoryActivityDraftTestRunStore();
        var publications = new PublicationStore(publishedVersion is null ? [childPublication] : [childPublication, publishedVersion]);
        ITestRunDispatcher dispatcher = acceptDispatch
            ? new CapturingAcceptedDispatcher(artifacts, now)
            : new RejectIfDispatched();
        var draftStore = new DraftStore(draft);
        var service = new ActivityDraftTestRunService(
            new DefinitionStore(definition),
            draftStore,
            new DraftLayoutStore(layout),
            new TemplateCompiler(rootTemplate),
            new TestActivityPublishingAuthorizationContext(),
            templates,
            sourceReferences,
            new ActivityTemplatePlacer(
                publications,
                templates,
                sourceReferences,
                new Sha256ActivityPlacementHasher(),
                new DefaultActivityStructureService([]),
                new RuntimeInputBindingCompiler(TestWellKnownTypeRegistry.Create()),
                new RuntimeOutputCaptureCompiler(new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]))),
            new WorkflowExecutableHasher(),
            artifacts,
            dispatcher,
            new InMemoryWorkflowTestScopeStore(),
            new FixedPartitionAccessor(),
            receipts,
            new InMemoryWorkflowExecutionStateStore(),
            new InMemoryActivityExecutionStateStore(),
            new DefaultActivityDraftTestRunCancellationPolicy(),
            null!,
            null!,
            clock,
            wrapRootWrites?.Invoke(new WorkflowExecutableRootWriteLeaseManager(artifacts,
                Microsoft.Extensions.Options.Options.Create(new Elsa.Workflows.Runtime.Core.Configuration.WorkflowExecutableGarbageCollectionOptions()), clock))
            ?? runtimeServices?.GetRequiredService<IWorkflowExecutableRootWriteLeaseManager>()
            ?? new WorkflowExecutableRootWriteLeaseManager(artifacts,
                Microsoft.Extensions.Options.Options.Create(new Elsa.Workflows.Runtime.Core.Configuration.WorkflowExecutableGarbageCollectionOptions()), clock),
            publications);
        return new(service, draft, new(draft.Id, draft.Revision, "placement-fails"), receipts, dispatcher, templates, sourceReferences, artifacts, clock,
            rootTemplate, draftStore, publishedVersion);
    }

    private static ExecutableActivityTemplate RequiredInputTemplate() => ChildTemplate(includeRequiredInput: true);

    private static ExecutableActivityTemplate ValidChildTemplate() => ChildTemplate(includeRequiredInput: false);

    private static ExecutableActivityTemplate ChildTemplate(bool includeRequiredInput)
    {
        IReadOnlyCollection<Elsa.Activities.Runtime.Core.Models.ActivityInputContract> inputs = includeRequiredInput
            ? [new(
                "required-value",
                "Required value",
                new ValueTypeDescriptor("String"),
                isRequired: true,
                isNullable: false,
                hasDefault: false,
                defaultValue: null,
                ActivityValuePolicy.Default)]
            : [];
        var contract = new Elsa.Activities.Runtime.Core.Models.ActivityContract(
            "child-type",
            "1",
            "child-consumer",
            Json("{}"),
            inputs,
            new ActivityResultContract(new ValueTypeDescriptor("Object"), false, ActivityValuePolicy.Default, []),
            [],
            new ActivityActivationRequirement("child-consumer", "child-capability"));
        var root = new ExecutableNode(
            "child-root",
            "child-root",
            "child-type",
            "1",
            "child-consumer",
            Json("{}"),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>(),
            activityContract: contract);
        return Template("child-template", "child-template-hash", root);
    }

    private static ExecutableActivityTemplate GraphTemplate(ExecutableActivityTemplate childTemplate)
    {
        var graphRoot = new ExecutableNode(
            "graph-root",
            "graph-root",
            "graph-type",
            "1",
            WellKnownRuntimeActivityConsumers.GraphActivity,
            Json("{\"entryOccurrenceId\":\"child-occurrence\"}"),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>());
        return Template(
            "root-template",
            "root-template-hash",
            graphRoot,
            [new(
                "child-definition",
                "child-version",
                "1",
                childTemplate.TemplateId,
                childTemplate.TemplateHash,
                "child-occurrence",
                ActivityInvocationOrigin.Empty,
                OccurrenceOverlay: new("1", Json("[]"), null))]);
    }

    private static ExecutableActivityTemplate Template(
        string id,
        string hash,
        ExecutableNode root,
        IReadOnlyCollection<ExecutableActivityTemplateDependency>? dependencies = null) => new(
        id,
        hash,
        root,
        new Dictionary<string, WorkflowExecutableResumeTarget>(),
        dependencies ?? [],
        [],
        [],
        "provider-fingerprint",
        new Dictionary<string, string>(),
        DateTimeOffset.UnixEpoch);

    private static ActivityDefinitionVersionPublication Publication(
        string definitionId,
        string versionId,
        string activityTypeKey,
        ExecutableActivityTemplate template,
        string sourceReferenceId = "child-source-reference",
        string? tenantId = null) => new()
    {
        DefinitionId = definitionId,
        TenantId = tenantId,
        DefinitionVersionId = versionId,
        Version = "1",
        ActivityTypeKey = activityTypeKey,
        Contract = new Elsa.Activities.Design.Core.Models.ActivityContract("1", [], [], []),
        Provider = new("test.provider", "1", Json("{}")),
        TemplateId = template.TemplateId,
        TemplateHash = template.TemplateHash,
        SourceReferenceId = sourceReferenceId,
        ProviderFingerprint = template.ProviderFingerprint,
        DirectDependencyCount = template.DirectDependencies.Count,
        ClosedTemplateCount = template.ClosedTemplates.Count,
        RuntimeRequirements = [],
        PublishedAt = DateTimeOffset.UnixEpoch,
        Lifecycle = ActivityDefinitionVersionLifecycle.Active
    };

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();

    private sealed record TestRunFixture(
        ActivityDraftTestRunService Service,
        ActivityDefinitionDraft Draft,
        StartActivityDraftTestRun Request,
        IActivityDraftTestRunStore Receipts,
        ITestRunDispatcher Dispatcher,
        IExecutableActivityTemplateStore Templates,
        IWorkflowExecutableSourceReferenceStore References,
        IWorkflowExecutableStore Artifacts,
        FakeTimeProvider Clock,
        ExecutableActivityTemplate RootTemplate,
        DraftStore DraftStore,
        ActivityDefinitionVersionPublication? PublishedVersion);

    private sealed class CancelOnDependencyReadTemplateStore(
        IExecutableActivityTemplateStore inner,
        CancellationTokenSource cancellation,
        string dependencyTemplateId,
        Exception? placementFailure = null) : IExecutableActivityTemplateStore
    {
        public bool CanMaintainReferences(IWorkflowExecutableStore executables, IWorkflowExecutableSourceReferenceStore references) =>
            inner.CanMaintainReferences(executables, references);
        public ValueTask<WorkflowExecutableSourceReference> SaveWithReferenceAsync(ExecutableActivityTemplate template,
            WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default) =>
            inner.SaveWithReferenceAsync(template, reference, cancellationToken);
        public ValueTask SaveAsync(ExecutableActivityTemplate template, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(template, cancellationToken);

        public async ValueTask<ExecutableActivityTemplate?> FindAsync(string templateId, CancellationToken cancellationToken = default)
        {
            var template = await inner.FindAsync(templateId, cancellationToken);
            if (StringComparer.Ordinal.Equals(templateId, dependencyTemplateId))
            {
                if (placementFailure is not null)
                    throw placementFailure;
                cancellation.Cancel();
            }
            return template;
        }

        public ValueTask<ExecutableActivityTemplate?> FindByHashAsync(string templateHash, CancellationToken cancellationToken = default) =>
            inner.FindByHashAsync(templateHash, cancellationToken);

        public ValueTask<RuntimeStorePage<ExecutableActivityTemplate>> ListPageAsync(
            RuntimeStorePageRequest request,
            CancellationToken cancellationToken = default) => inner.ListPageAsync(request, cancellationToken);
    }

    private sealed class CompetingReceiptStore(
        ActivityDraftTestRunReceiptStatus attemptedStatus,
        ActivityDraftTestRunReceiptStatus competingStatus) : IActivityDraftTestRunStore
    {
        private readonly InMemoryActivityDraftTestRunStore _inner = new();

        public bool AdvancedReceipt { get; private set; }

        public ValueTask<ActivityDraftTestRunCreateResult> TryCreateAsync(
            ActivityDraftTestRunReceipt receipt,
            CancellationToken cancellationToken = default) => _inner.TryCreateAsync(receipt, cancellationToken);

        public ValueTask<ActivityDraftTestRunReceipt?> FindAsync(string testRunId, CancellationToken cancellationToken = default) =>
            _inner.FindAsync(testRunId, cancellationToken);

        public ValueTask<ActivityDraftTestRunReceipt?> FindByIdempotencyKeyAsync(
            string operationScope,
            string draftId,
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            _inner.FindByIdempotencyKeyAsync(operationScope, draftId, idempotencyKey, cancellationToken);

        public async ValueTask<bool> TryUpdateAsync(
            ActivityDraftTestRunReceipt receipt,
            long expectedRevision,
            CancellationToken cancellationToken = default)
        {
            if (!AdvancedReceipt && receipt.Status == attemptedStatus)
            {
                var current = await _inner.FindAsync(receipt.TestRunId, cancellationToken)
                              ?? throw new InvalidOperationException("The test receipt was not created.");
                var advanced = current with
                {
                    Status = competingStatus,
                    Failure = competingStatus == ActivityDraftTestRunReceiptStatus.ValidationRejected
                        ? new(
                            ActivityDraftTestRunFailureKind.Validation,
                            "activity.test-run.preparation-failed",
                            "The activity draft could not be prepared for a Test Run.",
                            [])
                        : null,
                    CommandDispatchStatus = competingStatus == ActivityDraftTestRunReceiptStatus.DispatchAccepted ? "Accepted" : null,
                    ArtifactId = competingStatus == ActivityDraftTestRunReceiptStatus.DispatchAccepted
                        ? "artifact-from-concurrent-start"
                        : null,
                    SourceReferenceId = competingStatus == ActivityDraftTestRunReceiptStatus.DispatchAccepted
                        ? "source-from-concurrent-start"
                        : null,
                    Revision = current.Revision + 1
                };
                AdvancedReceipt = await _inner.TryUpdateAsync(advanced, current.Revision, cancellationToken);
                return false;
            }

            return await _inner.TryUpdateAsync(receipt, expectedRevision, cancellationToken);
        }

        public ValueTask<int> DeleteExpiredAsync(DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default) =>
            _inner.DeleteExpiredAsync(asOf, limit, cancellationToken);
    }

    private sealed class DefinitionStore(ActivityDefinition definition) : IActivityDefinitionStore
    {
        public Task<ActivityDefinition> GetAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(definition);
        public Task<ActivityDefinition?> FindAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinition?>(definition);
        public Task<IReadOnlyList<ActivityDefinition>> ListAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinition>>([definition]);
        public Task<ActivityDefinition?> FindByIdOrActivityTypeKeyAsync(string id, string activityTypeKey, CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinition?>(definition);
        public Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class DraftStore(ActivityDefinitionDraft draft) : IActivityDefinitionDraftStore
    {
        public int FindCount { get; private set; }
        public Task<ActivityDefinitionDraft?> FindAsync(string draftId, CancellationToken cancellationToken = default)
        {
            FindCount++;
            return Task.FromResult<ActivityDefinitionDraft?>(draft);
        }
        public Task<IReadOnlyList<ActivityDefinitionDraft>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinitionDraft>>([draft]);
    }

    private sealed class DraftLayoutStore(ActivityDefinitionDraftLayout layout) : IActivityDefinitionLayoutStore
    {
        public Task<ActivityDefinitionDraftLayout?> FindDraftLayoutAsync(string draftId, CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinitionDraftLayout?>(layout);
        public Task<ActivityDefinitionVersionLayout?> FindVersionLayoutAsync(string definitionVersionId, CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinitionVersionLayout?>(null);
    }

    private sealed class TemplateCompiler(ExecutableActivityTemplate template) : IActivityTemplateCompiler
    {
        public ValueTask<ActivityTemplateCompilerResult> CompileAsync(ActivityTemplateCompilerRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityTemplateCompilerResult(
                template,
                new(1, 1, template.DirectDependencies.Count, 1, 1, request.LayoutBytes, 1),
                [],
                []));
    }

    private sealed class PublicationStore(params ActivityDefinitionVersionPublication[] publications) : IActivityDefinitionVersionPublicationStore
    {
        public Task<ActivityDefinitionVersionPublication?> FindAsync(string definitionVersionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(publications.FirstOrDefault(publication => StringComparer.Ordinal.Equals(definitionVersionId, publication.DefinitionVersionId)));

        public Task<IReadOnlyList<ActivityDefinitionVersionPublication>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionVersionPublication>>(publications.Where(publication => StringComparer.Ordinal.Equals(definitionId, publication.DefinitionId)).ToArray());
    }

    private sealed class FixedPartitionAccessor : IWorkflowExecutionPartitionAccessor
    {
        public WorkflowExecutionPartition Current { get; } = new(WorkflowExecutionPartition.DefaultValue);
    }

    private interface ITestRunDispatcher : IWorkflowStartDispatcher
    {
        bool WasCalled { get; }
        int DispatchCount { get; }
        WorkflowExecutionStartDispatchRequest? LastRequest { get; }
    }

    private sealed class RejectIfDispatched : ITestRunDispatcher
    {
        public bool WasCalled { get; private set; }
        public int DispatchCount { get; private set; }
        public WorkflowExecutionStartDispatchRequest? LastRequest => null;

        public ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(
            WorkflowExecutionStartDispatchRequest request,
            WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.Published,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            DispatchCount++;
            throw new InvalidOperationException("The test run must not reach Runtime dispatch.");
        }
    }

    private sealed class CapturingAcceptedDispatcher(IWorkflowExecutableStore executables, DateTimeOffset now) : ITestRunDispatcher
    {
        public bool WasCalled => DispatchCount > 0;
        public int DispatchCount { get; private set; }
        public WorkflowExecutionStartDispatchRequest? LastRequest { get; private set; }

        public async ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(
            WorkflowExecutionStartDispatchRequest request,
            WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.Published,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
            CancellationToken cancellationToken = default)
        {
            DispatchCount++;
            LastRequest = request;
            var executionId = request.WorkflowExecutionId ?? throw new InvalidOperationException("The test run execution ID is required.");
            var executable = await executables.FindAsync(request.ArtifactId, cancellationToken)
                             ?? throw new InvalidOperationException("The persisted Test Run wrapper was not found.");
            return new WorkflowExecutionStartDispatchResult(
                executionId,
                executable.Identity,
                new WorkflowExecutionCommandDispatchResult("accepted-envelope", executionId,
                    WorkflowExecutionCommandDispatchStatus.Accepted, now),
                new WorkflowExecutionActorDescriptor(executionId, "test-agent", "test-provider",
                    WorkflowExecutionActorStatus.Active, WorkflowExecutionActorCapabilities.InProcessMailbox, now));
        }
    }
}
