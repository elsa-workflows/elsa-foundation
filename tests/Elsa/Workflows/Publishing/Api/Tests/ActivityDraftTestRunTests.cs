using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Services;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Graph.Runtime;
using Elsa.Activities.Graph.Runtime.Models;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Api.Contracts;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.Groundwork;
using Elsa.Workflows.Publishing.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using FastEndpoints;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Sqlite.Documents;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class ActivityDraftTestRunTests
{
    [Fact]
    public void Endpoint_and_request_match_the_reviewed_activity_draft_test_run_contract()
    {
        AssertEndpoint(
            "ActivityDraftTestRunEndpoint",
            "POST",
            "publishing/activity-drafts/{draftId}/test-runs");
        AssertEndpoint(
            "GetActivityDraftTestRunEndpoint",
            "GET",
            "publishing/activity-test-runs/{testRunId}");
        AssertEndpoint(
            "GetActivityDraftTestRunByIdempotencyKeyEndpoint",
            "GET",
            "publishing/activity-drafts/{draftId}/test-runs/idempotency/{idempotencyKey}");
        AssertEndpoint(
            "CancelActivityDraftTestRunEndpoint",
            "POST",
            "publishing/activity-test-runs/{testRunId}/cancel");
        var json = JsonSerializer.Serialize(new StartActivityDraftTestRun(
            "draft-1",
            8,
            "rerun-42",
            new Dictionary<string, ActivityDraftTestRunInput>
            {
                ["order"] = new("Present", JsonSerializer.SerializeToElement(new { id = "order-42" }))
            },
            "designer-test-42"), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(
            "{\"expectedRevision\":8,\"idempotencyKey\":\"rerun-42\",\"inputs\":{\"order\":{\"state\":\"Present\",\"value\":{\"id\":\"order-42\"}}},\"correlationId\":\"designer-test-42\"}",
            json);
        Assert.DoesNotContain("draftId", json, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertEndpoint(string typeName, string verb, string route)
    {
        var endpointType = typeof(StartActivityDraftTestRun).Assembly.GetType(
            $"Elsa.Workflows.Publishing.Api.Endpoints.{typeName}",
            throwOnError: true)!;
        var loggerType = typeof(NullLogger<>).MakeGenericType(endpointType);
        var logger = loggerType.GetProperty("Instance")?.GetValue(null)
                     ?? loggerType.GetField("Instance")!.GetValue(null)!;
        var endpoint = (BaseEndpoint)typeof(Factory).GetMethods()
            .Single(x => x.Name == nameof(Factory.Create) && x.IsGenericMethodDefinition &&
                         x.GetParameters() is [var first, var second] &&
                         first.ParameterType == typeof(Action<DefaultHttpContext>) && second.ParameterType == typeof(object[]))
            .MakeGenericMethod(endpointType)
            .Invoke(null, [(Action<DefaultHttpContext>)(_ => { }), new object[] { new Sender(), logger }])!;
        endpoint.Configure();

        Assert.Equal(verb, Assert.Single(endpoint.Definition.Verbs));
        Assert.Equal(route, Assert.Single(endpoint.Definition.Routes));
    }

    [Fact]
    public async Task Draft_test_run_denies_a_foreign_exact_draft_before_compilation_without_disclosure()
    {
        var documents = new InMemoryDocumentStore(CombinedStorageManifest());
        var authoring = AuthoringState.Create("tenant-b");
        await using var provider = BuildProvider(
            documents,
            TimeProvider.System,
            authoring,
            new TestActivityPublishingAuthorizationContext("tenant-a"));
        await using var scope = provider.CreateAsyncScope();

        var exception = await Assert.ThrowsAsync<ActivityPublicationRejectedException>(() =>
            scope.ServiceProvider.GetRequiredService<IActivityDraftTestRunService>().StartAsync(new(
                AuthoringState.DraftId,
                authoring.Draft.Revision,
                "foreign-run")));

        Assert.Equal("activity.tenant.reference-denied", exception.ErrorCode);
        Assert.Empty(exception.Diagnostics);
        Assert.DoesNotContain("tenant-b", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Same_tenant_local_draft_and_key_create_distinct_cross_tenant_runs()
    {
        var manifest = CombinedStorageManifest();
        await using var tenantA = BuildProvider(
            new InMemoryDocumentStore(
                manifest,
                DocumentStoreAccess.Scoped(new StorageScope("tenant-a"))),
            TimeProvider.System,
            AuthoringState.Create("tenant-a"),
            new TestActivityPublishingAuthorizationContext("tenant-a"),
            persistenceScope: "tenant-a");
        await using var tenantB = BuildProvider(
            new InMemoryDocumentStore(
                manifest,
                DocumentStoreAccess.Scoped(new StorageScope("tenant-b"))),
            TimeProvider.System,
            AuthoringState.Create("tenant-b"),
            new TestActivityPublishingAuthorizationContext("tenant-b"),
            persistenceScope: "tenant-b");

        var first = await StartAsync(tenantA, 4, "same-tenant-local-key");
        var second = await StartAsync(tenantB, 4, "same-tenant-local-key");

        Assert.NotEqual(first.TestRunId, second.TestRunId);
        Assert.NotEqual(first.WorkflowExecutionId, second.WorkflowExecutionId);
    }

    [Fact]
    public async Task Same_global_draft_and_key_create_tenant_owned_runs_for_each_caller()
    {
        var manifest = CombinedStorageManifest();
        var globalAuthoring = AuthoringState.Create();
        await using var tenantA = BuildProvider(
            new InMemoryDocumentStore(
                manifest,
                DocumentStoreAccess.Scoped(new StorageScope("tenant-a"))),
            TimeProvider.System,
            globalAuthoring,
            new TestActivityPublishingAuthorizationContext("tenant-a"),
            persistenceScope: "tenant-a");
        await using var tenantB = BuildProvider(
            new InMemoryDocumentStore(
                manifest,
                DocumentStoreAccess.Scoped(new StorageScope("tenant-b"))),
            TimeProvider.System,
            globalAuthoring,
            new TestActivityPublishingAuthorizationContext("tenant-b"),
            persistenceScope: "tenant-b");

        var first = await StartAsync(tenantA, 4, "same-global-key");
        var second = await StartAsync(tenantB, 4, "same-global-key");
        var firstReceipt = await tenantA.GetRequiredService<IActivityDraftTestRunStore>().FindAsync(first.TestRunId);
        var secondReceipt = await tenantB.GetRequiredService<IActivityDraftTestRunStore>().FindAsync(second.TestRunId);

        Assert.NotEqual(first.TestRunId, second.TestRunId);
        Assert.NotEqual(first.WorkflowExecutionId, second.WorkflowExecutionId);
        Assert.Equal("tenant-a", firstReceipt?.TenantId);
        Assert.Equal("tenant-b", secondReceipt?.TenantId);
        Assert.Null(firstReceipt?.ResourceTenantId);
        Assert.Null(secondReceipt?.ResourceTenantId);
        Assert.Equal(
            "tenant-a",
            (await tenantA.GetRequiredService<IWorkflowExecutionStateStore>()
                .FindAsync(first.WorkflowExecutionId))?.TenantId);
        Assert.Equal(
            "tenant-b",
            (await tenantB.GetRequiredService<IWorkflowExecutionStateStore>()
                .FindAsync(second.WorkflowExecutionId))?.TenantId);
    }

    [Fact]
    public async Task Foreign_caller_cannot_learn_a_receipt_loaded_from_the_operation_scope()
    {
        var authorization = new MutableAuthorizationContext("tenant-a");
        var authoring = AuthoringState.Create();
        await using var provider = BuildProvider(
            new InMemoryDocumentStore(
                CombinedStorageManifest(),
                DocumentStoreAccess.Scoped(new StorageScope("tenant-a"))),
            TimeProvider.System,
            authoring,
            authorization,
            persistenceScope: "tenant-a");
        var started = await StartAsync(provider, authoring.Draft.Revision, "private-run");

        authorization.TenantId = "tenant-b";
        var exception = await Assert.ThrowsAsync<ActivityPublicationRejectedException>(
            () => provider.GetRequiredService<IActivityDraftTestRunService>().GetAsync(started.TestRunId));

        Assert.Equal("activity.tenant.reference-denied", exception.ErrorCode);
        Assert.DoesNotContain(started.TestRunId, exception.Message, StringComparison.Ordinal);
        Assert.Empty(exception.Diagnostics);
    }

    [Fact]
    public async Task Test_run_rejects_unbounded_request_identity_material()
    {
        var authoring = AuthoringState.Create();
        await using var provider = BuildProvider(
            new InMemoryDocumentStore(CombinedStorageManifest()),
            TimeProvider.System,
            authoring);
        var testRuns = provider.GetRequiredService<IActivityDraftTestRunService>();

        var exception = await Assert.ThrowsAsync<ActivityPublicationRejectedException>(() =>
            testRuns.StartAsync(new(
                AuthoringState.DraftId,
                authoring.Draft.Revision,
                new string('k', 201))));

        Assert.Equal("activity.request.invalid", exception.ErrorCode);
        Assert.Null(await provider.GetRequiredService<IActivityDraftTestRunStore>()
            .FindByIdempotencyKeyAsync(
                ActivityDraftTestRunIdentity.CreateOperationScope(null),
                AuthoringState.DraftId,
                new string('k', 201)));
    }

    [Fact]
    public async Task Expiring_test_run_reference_collects_its_unreferenced_activity_template()
    {
        var now = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        var templates = new InMemoryExecutableActivityTemplateStore();
        var workflows = new InMemoryWorkflowExecutableStore();
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        var template = Template();
        await templates.SaveAsync(template);
        await references.SaveAsync(new(
            "test-ref", template.TemplateId, "ActivityDraft", "draft-1", "8", "definition-1", "draft-version-1", "draft",
            now.AddHours(-1), null, WorkflowExecutableReferenceScope.TestRun, now.AddMinutes(-1)));
        var collector = new WorkflowExecutableReferenceGarbageCollector(
            workflows,
            references,
            templates,
            TimeProvider.System,
            NullLogger<WorkflowExecutableReferenceGarbageCollector>.Instance);

        var result = await collector.SweepAsync(now);

        Assert.Equal(1, result.DeletedReferenceCount);
        Assert.Equal(1, result.DeletedActivityTemplateCount);
        Assert.Null(await templates.FindAsync(template.TemplateId));
    }

    [Fact]
    public async Task Full_host_distinguishes_validation_and_runtime_dispatch_rejection()
    {
        var authoring = AuthoringState.Create();
        await using (var validationHost = BuildProvider(
                         new InMemoryDocumentStore(CombinedStorageManifest()),
                         TimeProvider.System,
                         authoring))
        {
            var rejected = await StartAsync(validationHost, authoring.Draft.Revision + 1, "stale-revision");

            Assert.Equal(ActivityDraftTestRunReceiptStatus.ValidationRejected.ToString(), rejected.Status);
            Assert.Equal(ActivityDraftTestRunFailureKind.Validation.ToString(), rejected.Failure?.Kind);
            Assert.Equal("activity.draft.stale-revision", rejected.Failure?.Code);
            Assert.False(rejected.Cancellation.Available);
            Assert.Equal(ActivityDraftTestRunCancellationStatus.Unavailable.ToString(), rejected.Cancellation.Status);
            Assert.Null(await validationHost.GetRequiredService<IWorkflowExecutionStateStore>()
                .FindAsync(rejected.WorkflowExecutionId));
        }

        await using (var runtimeHost = BuildProvider(
                         new InMemoryDocumentStore(CombinedStorageManifest()),
                         TimeProvider.System,
                         authoring,
                         startPolicy: new DenyStartPolicy()))
        {
            var rejected = await StartAsync(runtimeHost, authoring.Draft.Revision, "runtime-rejection");

            Assert.Equal(ActivityDraftTestRunReceiptStatus.DispatchRejected.ToString(), rejected.Status);
            Assert.Equal(ActivityDraftTestRunFailureKind.RuntimeDispatch.ToString(), rejected.Failure?.Kind);
            Assert.Equal("runtime.start.test-policy-denied", rejected.Failure?.Code);
            Assert.Equal("Runtime rejected the Test Run start request.", rejected.Failure?.Message);
            Assert.DoesNotContain("provider-secret", rejected.Failure!.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Tri_state_inputs_reject_contradictions_and_accept_explicit_json_null()
    {
        var authoring = AuthoringState.Create();
        await using var provider = BuildProvider(
            new InMemoryDocumentStore(CombinedStorageManifest()),
            TimeProvider.System,
            authoring);
        var testRuns = provider.GetRequiredService<IActivityDraftTestRunService>();
        var invalid = new[]
        {
            new ActivityDraftTestRunInput("Absent", JsonSerializer.SerializeToElement("contradiction")),
            new ActivityDraftTestRunInput("Present"),
            new ActivityDraftTestRunInput("Unknown", JsonSerializer.SerializeToElement("value"))
        };

        for (var index = 0; index < invalid.Length; index++)
        {
            var rejected = await testRuns.StartAsync(new(
                AuthoringState.DraftId,
                authoring.Draft.Revision,
                $"invalid-input-{index}",
                new Dictionary<string, ActivityDraftTestRunInput> { ["order"] = invalid[index] }));
            Assert.Equal(ActivityDraftTestRunReceiptStatus.ValidationRejected.ToString(), rejected.Status);
            Assert.Equal(ActivityDraftTestRunFailureKind.Validation.ToString(), rejected.Failure?.Kind);
            Assert.Equal("activity.test-run.input-state-invalid", rejected.Failure?.Code);
        }

        Assert.Empty(await provider.GetRequiredService<IWorkflowExecutionStateStore>().ListAsync());
        var explicitNull = await testRuns.StartAsync(new(
            AuthoringState.DraftId,
            authoring.Draft.Revision,
            "explicit-null",
            new Dictionary<string, ActivityDraftTestRunInput>
            {
                ["order"] = new("Present", JsonSerializer.SerializeToElement<object?>(null))
            }));
        Assert.Null(explicitNull.Failure);
        Assert.NotNull(await provider.GetRequiredService<IWorkflowExecutionStateStore>()
            .FindAsync(explicitNull.WorkflowExecutionId));
    }

    [Fact]
    public async Task Full_host_cancellation_is_idempotent_and_reconciles_to_terminal()
    {
        var authoring = AuthoringState.Create();
        await using var provider = BuildProvider(
            new InMemoryDocumentStore(CombinedStorageManifest()),
            TimeProvider.System,
            authoring);
        var started = await StartAsync(provider, authoring.Draft.Revision, "cancel-run");
        await RedriveAsync(provider);
        var testRuns = provider.GetRequiredService<IActivityDraftTestRunService>();

        var first = await testRuns.CancelAsync(started.TestRunId);
        var duplicate = await testRuns.CancelAsync(started.TestRunId);
        await RedriveAsync(provider);
        var terminal = await testRuns.GetAsync(started.TestRunId);

        Assert.True(first.Cancellation.CapabilityAdvertised);
        Assert.Contains(first.Cancellation.Status, new[]
        {
            ActivityDraftTestRunCancellationStatus.Cancelling.ToString(),
            ActivityDraftTestRunCancellationStatus.Terminal.ToString()
        });
        Assert.Equal(first.WorkflowExecutionId, duplicate.WorkflowExecutionId);
        Assert.Equal(WorkflowExecutionStatus.Cancelled.ToString(), terminal.Status);
        Assert.Equal(ActivityDraftTestRunCancellationStatus.Terminal.ToString(), terminal.Cancellation.Status);
        Assert.False(terminal.Cancellation.Available);
    }

    [Fact]
    public async Task Ambiguous_acknowledgement_reconciles_the_original_run_after_the_draft_is_removed()
    {
        var authoring = AuthoringState.Create();
        await using var provider = BuildProvider(
            new InMemoryDocumentStore(CombinedStorageManifest()),
            TimeProvider.System,
            authoring,
            makeFirstDispatchAmbiguous: true);

        var ambiguous = await StartAsync(provider, authoring.Draft.Revision, "ambiguous-run");
        authoring.Drafts.Remove();
        var reconciled = await StartAsync(provider, authoring.Draft.Revision, "ambiguous-run");
        var byKey = await provider.GetRequiredService<IActivityDraftTestRunService>()
            .GetByIdempotencyKeyAsync(AuthoringState.DraftId, "ambiguous-run");
        var executions = await provider.GetRequiredService<IWorkflowExecutionStateStore>().ListAsync();

        Assert.Equal("activity.test-run.dispatch-ambiguous", ambiguous.Failure?.Code);
        Assert.Null(reconciled.Failure);
        Assert.NotEqual(ActivityDraftTestRunReceiptStatus.ValidationRejected.ToString(), reconciled.Status);
        Assert.Equal(ambiguous.TestRunId, reconciled.TestRunId);
        Assert.Equal(reconciled.TestRunId, byKey.TestRunId);
        Assert.Equal(ambiguous.WorkflowExecutionId, reconciled.WorkflowExecutionId);
        Assert.Equal(reconciled.WorkflowExecutionId, Assert.Single(executions).WorkflowExecutionId);
    }

    [Fact]
    public async Task Preparing_receipt_rejects_safely_when_the_exact_draft_disappears_before_materialization()
    {
        var authoring = AuthoringState.Create();
        await using var provider = BuildProvider(
            new InMemoryDocumentStore(CombinedStorageManifest()),
            TimeProvider.System,
            authoring,
            templateCompiler: new ThrowingTemplateCompiler());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => StartAsync(provider, authoring.Draft.Revision, "preparing-run"));
        authoring.Drafts.Remove();
        var rejected = await StartAsync(provider, authoring.Draft.Revision, "preparing-run");

        Assert.Equal(ActivityDraftTestRunReceiptStatus.ValidationRejected.ToString(), rejected.Status);
        Assert.Equal("activity.draft.stale-revision", rejected.Failure?.Code);
        Assert.Null(rejected.ArtifactId);
        Assert.Empty(await provider.GetRequiredService<IWorkflowExecutionStateStore>().ListAsync());
    }

    [Fact]
    public async Task Full_host_rejects_rebinding_an_idempotency_key_to_a_different_request()
    {
        var authoring = AuthoringState.Create();
        await using var provider = BuildProvider(
            new InMemoryDocumentStore(CombinedStorageManifest()),
            TimeProvider.System,
            authoring);
        var original = await StartAsync(provider, authoring.Draft.Revision, "bound-key");
        await using var scope = provider.CreateAsyncScope();
        var testRuns = scope.ServiceProvider.GetRequiredService<IActivityDraftTestRunService>();

        var exception = await Assert.ThrowsAsync<ActivityPublicationRejectedException>(() =>
            testRuns.StartAsync(new(
                AuthoringState.DraftId,
                authoring.Draft.Revision,
                "bound-key",
                new Dictionary<string, ActivityDraftTestRunInput>
                {
                    ["order"] = new("Present", JsonSerializer.SerializeToElement(new { id = "different-order" }))
                },
                "different-correlation")));

        Assert.Equal("activity.test-run.idempotency-mismatch", exception.ErrorCode);
        Assert.Equal(original.WorkflowExecutionId, Assert.Single(
            await provider.GetRequiredService<IWorkflowExecutionStateStore>().ListAsync()).WorkflowExecutionId);
    }

    [Fact]
    public async Task Policy_denied_cancellation_is_projected_and_persisted_as_unavailable()
    {
        var authoring = AuthoringState.Create();
        await using var provider = BuildProvider(
            new InMemoryDocumentStore(CombinedStorageManifest()),
            TimeProvider.System,
            authoring,
            cancellationPolicy: new DenyCancellationPolicy());
        var started = await StartAsync(provider, authoring.Draft.Revision, "policy-denied-cancel");
        await RedriveAsync(provider);
        var testRuns = provider.GetRequiredService<IActivityDraftTestRunService>();

        var projected = await testRuns.GetAsync(started.TestRunId);
        var cancelled = await testRuns.CancelAsync(started.TestRunId);
        var receipt = await provider.GetRequiredService<IActivityDraftTestRunStore>().FindAsync(started.TestRunId);

        Assert.True(projected.Cancellation.CapabilityAdvertised);
        Assert.False(projected.Cancellation.Available);
        Assert.Equal(ActivityDraftTestRunCancellationStatus.Unavailable.ToString(), projected.Cancellation.Status);
        Assert.Equal("Test policy denies cancellation.", projected.Cancellation.Reason);
        Assert.Equal(ActivityDraftTestRunCancellationStatus.Unavailable.ToString(), cancelled.Cancellation.Status);
        Assert.Equal(ActivityDraftTestRunCancellationStatus.Unavailable, receipt?.CancellationStatus);
    }

    [Fact]
    public async Task Full_host_fault_records_wait_for_intervention_without_disclosing_provider_payload()
    {
        var authoring = AuthoringState.Create();
        await using var provider = BuildProvider(
            new InMemoryDocumentStore(CombinedStorageManifest()),
            TimeProvider.System,
            authoring,
            templateCompiler: new FaultingTemplateCompiler());

        var started = await StartAsync(provider, authoring.Draft.Revision, "faulting-run");
        await RedriveAsync(provider);
        var waiting = await provider.GetRequiredService<IActivityDraftTestRunService>().GetAsync(started.TestRunId);
        await using var scope = provider.CreateAsyncScope();
        var incident = Assert.Single(await scope.ServiceProvider.GetRequiredService<IIncidentStateStore>()
            .ListAsync(started.WorkflowExecutionId));

        Assert.Equal(WorkflowExecutionStatus.Running.ToString(), waiting.Status);
        Assert.Null(waiting.Failure);
        Assert.Null(waiting.Reason);
        Assert.Equal(ActivityDraftTestRunCancellationStatus.Available.ToString(), waiting.Cancellation.Status);
        Assert.NotNull(waiting.OuterActivityExecutionId);
        Assert.Equal(IncidentStatus.Blocking, incident.Status);
        Assert.Equal(IncidentResolutionActionKinds.WaitForIntervention, incident.ResolutionOutcome?.ActionKind);
        Assert.Equal(IncidentResolutionSystemSources.PoisonedSchedulerWork, incident.ResolutionOutcome?.SystemSource);
        Assert.DoesNotContain("provider-secret", incident.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Groundwork_sqlite_graph_run_suspends_restarts_in_runtime_only_host_then_reopens_status()
    {
        var clock = new FakeTimeProvider(new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));
        var databasePath = Path.Combine(Path.GetTempPath(), $"elsa-activity-restart-{Guid.NewGuid():N}.db");
        // Pooling=False so disposing the store releases the OS file handle immediately; otherwise a pooled
        // SQLite connection keeps the temp .db open and the cleanup File.Delete throws IOException on Windows.
        var connectionString = $"Data Source={databasePath};Pooling=False";
        var authoring = AuthoringState.Create();
        var externalPayloads = new InMemoryExternalPayloadStore();
        ActivityDraftTestRunView first;
        ActivityDraftTestRunView second;
        string templateId;
        string outerActivityExecutionId;
        IReadOnlyDictionary<string, long> committedExecutionSequences;

        try
        {
            var (generation1Documents, generation1Queries) = await OpenSqliteAsync(connectionString);
            try
            {
                await using var generation1 = BuildProvider(
                    generation1Documents,
                    clock,
                    authoring,
                    externalPayloadStore: externalPayloads,
                    queries: generation1Queries);
                first = await StartAsync(generation1, authoring.Draft.Revision, "same-request");
                var duplicate = await StartAsync(generation1, authoring.Draft.Revision, "same-request");
                second = await StartAsync(generation1, authoring.Draft.Revision, "new-rerun");
                await RedriveAsync(generation1);

                Assert.Equal(first.ArtifactId, second.ArtifactId);
                Assert.Equal(first.TestRunId, duplicate.TestRunId);
                Assert.Equal(first.WorkflowExecutionId, duplicate.WorkflowExecutionId);
                Assert.NotEqual(first.WorkflowExecutionId, second.WorkflowExecutionId);
                var firstStatus = await StatusAsync(generation1, first.WorkflowExecutionId);
                var secondStatus = await StatusAsync(generation1, second.WorkflowExecutionId);
                if (firstStatus is null || secondStatus is null || firstStatus.Value.IsTerminal() || secondStatus.Value.IsTerminal())
                {
                    var firstStates = await generation1.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(first.WorkflowExecutionId);
                    var firstIncidents = await generation1.GetRequiredService<IIncidentStateStore>().ListAsync(first.WorkflowExecutionId);
                    var firstPoison = await generation1.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(first.WorkflowExecutionId);
                    throw new Xunit.Sdk.XunitException(
                        $"Expected suspended runs. First={firstStatus} Second={secondStatus} States={JsonSerializer.Serialize(firstStates)} Incidents={JsonSerializer.Serialize(firstIncidents)} Poison={JsonSerializer.Serialize(firstPoison)}");
                }
                var firstBookmarks = await generation1.GetRequiredService<IBookmarkStateStore>().ListAllBookmarkStatesAsync(first.WorkflowExecutionId);
                if (firstBookmarks.Count == 0)
                {
                    var pending = await generation1.GetRequiredService<IWorkflowSchedulerWorkQueue>()
                        .ListAllAsync(new RuntimeSchedulerWorkQuery(first.WorkflowExecutionId));
                    var poison = await generation1.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(first.WorkflowExecutionId);
                    throw new Xunit.Sdk.XunitException($"No bookmark. Pending={JsonSerializer.Serialize(pending)} Poison={JsonSerializer.Serialize(poison)} Dispatch={first.CommandDispatchStatus}/{first.Reason}");
                }
                Assert.Single(firstBookmarks);
                Assert.Single(await generation1.GetRequiredService<IBookmarkStateStore>().ListAllBookmarkStatesAsync(second.WorkflowExecutionId));
                var suspended = await generation1.GetRequiredService<IActivityDraftTestRunService>().GetAsync(first.TestRunId);
                Assert.Contains(suspended.Status, new[]
                {
                    WorkflowExecutionStatus.Running.ToString(),
                    WorkflowExecutionStatus.Suspended.ToString()
                });
                Assert.NotNull(suspended.OuterActivityExecutionId);
                Assert.False(suspended.Expiration.SourceReferenceExpired);
                Assert.True(suspended.Expiration.RunStillActive);

                var executions = await generation1.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(first.WorkflowExecutionId);
                Assert.Equal(2, executions.Count);
                committedExecutionSequences = executions.ToDictionary(
                    state => state.Execution.ActivityExecutionId,
                    state => state.ExecutionSequence,
                    StringComparer.Ordinal);
                Assert.Single((await generation1.GetRequiredService<IDurableValueStateStore>().ListAllDurableValueStatesAsync(first.WorkflowExecutionId))
                    .Where(IsBoundaryInput));

                var artifactId = Assert.IsType<string>(first.ArtifactId);
                var sourceReferenceId = Assert.IsType<string>(first.SourceReferenceId);
                var executable = await generation1.GetRequiredService<IWorkflowExecutableStore>().FindAsync(artifactId);
                Assert.NotNull(executable);
                templateId = Assert.Single(await generation1.GetRequiredService<IExecutableActivityTemplateStore>().ListAllAsync()).TemplateId;
                Assert.Equal("sha256:test-run-suspending-template", executable!.CompatibilityMetadata["activity.templateHash"]);

                clock.Advance(ActivityDraftTestRunService.DefaultRetention + TimeSpan.FromSeconds(1));
                var sweep = await generation1.GetRequiredService<IWorkflowExecutableReferenceGarbageCollector>().SweepAsync();
                Assert.False(sweep.DidWork);
                Assert.NotNull(await generation1.GetRequiredService<IWorkflowExecutableStore>().FindAsync(artifactId));
                Assert.NotNull(await generation1.GetRequiredService<IExecutableActivityTemplateStore>().FindAsync(templateId));
                Assert.NotNull(await generation1.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().FindAsync(sourceReferenceId));
                var expiredButRunning = await generation1.GetRequiredService<IActivityDraftTestRunService>().GetAsync(first.TestRunId);
                Assert.True(expiredButRunning.Expiration.SourceReferenceExpired);
                Assert.True(expiredButRunning.Expiration.SourceReferenceRetained);
                Assert.True(expiredButRunning.Expiration.EvidenceRetained);
                Assert.True(expiredButRunning.Expiration.RunStillActive);
            }
            finally
            {
                await DisposeStoreAsync(generation1Documents);
            }

            var (generation2Documents, generation2Queries) = await OpenSqliteAsync(connectionString);
            try
            {
                await using var generation2 = BuildRuntimeOnlyProvider(
                    generation2Documents,
                    clock,
                    externalPayloads,
                    generation2Queries);
                await ResumeAsync(generation2, first.WorkflowExecutionId);
                await ResumeAsync(generation2, second.WorkflowExecutionId);
                var resumedFirstStatus = await StatusAsync(generation2, first.WorkflowExecutionId);
                if (resumedFirstStatus != WorkflowExecutionStatus.Completed)
                {
                    var poison = await generation2.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(first.WorkflowExecutionId);
                    var incidents = await generation2.GetRequiredService<IIncidentStateStore>().ListAsync(first.WorkflowExecutionId);
                    throw new Xunit.Sdk.XunitException($"Expected completed run after restart. Status={resumedFirstStatus} Poison={JsonSerializer.Serialize(poison)} Incidents={JsonSerializer.Serialize(incidents)}");
                }
                Assert.Equal(WorkflowExecutionStatus.Completed, await StatusAsync(generation2, second.WorkflowExecutionId));

                var resumedExecutions = await generation2.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(first.WorkflowExecutionId);
                Assert.Equal(committedExecutionSequences.Keys.Order(StringComparer.Ordinal),
                    resumedExecutions.Select(x => x.Execution.ActivityExecutionId).Order(StringComparer.Ordinal));
                Assert.All(resumedExecutions, state =>
                    Assert.Equal(committedExecutionSequences[state.Execution.ActivityExecutionId], state.ExecutionSequence));

                var durableValues = await generation2.GetRequiredService<IDurableValueStateStore>().ListAllDurableValueStatesAsync(first.WorkflowExecutionId);
                Assert.Single(durableValues.Where(IsBoundaryInput));
                var output = Assert.Single(durableValues.Where(IsBoundaryOutput));
                Assert.Equal("completed", output.InlineValue?.GetString());

                var outer = Assert.Single(resumedExecutions.Where(x => x.ParentActivityExecutionId is null));
                outerActivityExecutionId = outer.Execution.ActivityExecutionId;
                var hierarchy = await generation2.GetRequiredService<ActivityExecutionHierarchyReader>().ReadAsync(
                    first.WorkflowExecutionId,
                    outer.Execution.ActivityExecutionId,
                    null,
                    10,
                    "bookmarks,outcomes,incidents",
                    CancellationToken.None);
                Assert.NotNull(hierarchy);
                Assert.Single(hierarchy!.Items);
                Assert.Null(hierarchy.NextCursor);
                var layout = await generation2.GetRequiredService<ActivityExecutionLayoutReader>().ReadAsync(
                    first.WorkflowExecutionId,
                    outer.Execution.ActivityExecutionId,
                    CancellationToken.None);
                Assert.NotNull(layout);
                Assert.Equal("ExecutedReference", layout!.Selection);
                Assert.Equal(first.SourceReferenceId, layout.SourceReferenceId);
                var retainedSweep = await generation2.GetRequiredService<IWorkflowExecutableReferenceGarbageCollector>().SweepAsync();
                Assert.False(retainedSweep.DidWork);
                Assert.NotNull(await generation2.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().FindAsync(first.SourceReferenceId));
            }
            finally
            {
                await DisposeStoreAsync(generation2Documents);
            }

            var (generation3Documents, generation3Queries) = await OpenSqliteAsync(connectionString);
            try
            {
                await using var generation3 = BuildProvider(
                    generation3Documents,
                    clock,
                    authoring,
                    externalPayloadStore: externalPayloads,
                    queries: generation3Queries);
                var completed = await generation3.GetRequiredService<IActivityDraftTestRunService>().GetAsync(first.TestRunId);
                Assert.Equal(WorkflowExecutionStatus.Completed.ToString(), completed.Status);
                Assert.Equal(outerActivityExecutionId, completed.OuterActivityExecutionId);
                Assert.False(completed.Expiration.RunStillActive);
                Assert.Equal(
                    completed,
                    await generation3.GetRequiredService<IActivityDraftTestRunService>()
                        .GetByIdempotencyKeyAsync(AuthoringState.DraftId, "same-request"));

                var executionStore = generation3.GetRequiredService<IWorkflowExecutionStateStore>();
                Assert.True(await executionStore.DeleteAsync(first.WorkflowExecutionId));
                Assert.True(await executionStore.DeleteAsync(second.WorkflowExecutionId));
                var sweep = await generation3.GetRequiredService<IWorkflowExecutableReferenceGarbageCollector>().SweepAsync();
                Assert.Equal(3, sweep.DeletedReferenceCount);
                Assert.Equal(1, sweep.DeletedArtifactCount);
                Assert.Equal(1, sweep.DeletedActivityTemplateCount);
                Assert.Null(await generation3.GetRequiredService<IWorkflowExecutableStore>().FindAsync(Assert.IsType<string>(first.ArtifactId)));
                Assert.Null(await generation3.GetRequiredService<IExecutableActivityTemplateStore>().FindAsync(templateId));
                Assert.Null(await generation3.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().FindAsync(Assert.IsType<string>(first.SourceReferenceId)));
            }
            finally
            {
                await DisposeStoreAsync(generation3Documents);
            }
        }
        finally
        {
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }

    private static async Task<ActivityDraftTestRunView> StartAsync(
        ServiceProvider provider,
        long revision,
        string correlationId)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IActivityDraftTestRunService>().StartAsync(new(
            AuthoringState.DraftId,
            revision,
            correlationId,
            new Dictionary<string, ActivityDraftTestRunInput>
            {
                ["order"] = new("Present", JsonSerializer.SerializeToElement(new { id = "order-42" }))
            },
            correlationId));
    }

    private static async Task ResumeAsync(ServiceProvider provider, string workflowExecutionId)
    {
        var result = await provider.GetRequiredService<IBookmarkResumeDispatcher>().DispatchAsync(new(
            workflowExecutionId,
            SuspendingActivity.StimulusType,
            SuspendingActivity.StimulusHash,
            JsonSerializer.SerializeToElement(new SuspendingTrigger("ready")),
            requestedBy: "restart-test",
            payloadType: new ValueTypeDescriptor(
                TypeAliasConvention.CanonicalAlias(typeof(SuspendingTrigger)),
                schemaVersion: 1),
            providerId: SuspendingActivityActivationStrategy.ConsumerKeyValue));
        Assert.Equal(BookmarkResumeDispatchStatus.Dispatched, result.Status);
        await RedriveAsync(provider);
    }

    private static async Task RedriveAsync(ServiceProvider provider)
    {
        var service = new RuntimeResumptionService(
            provider.GetRequiredService<IRuntimePostCommitOutboxProcessor>(),
            provider.GetRequiredService<IWorkflowSchedulerWorkQueue>(),
            provider.GetRequiredService<IRuntimeRecoveryScanner>(),
            provider.GetRequiredService<IWorkflowExecutionActorProvider>(),
            provider.GetRequiredService<IRuntimeExecutionIdGenerator>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IWorkflowExecutionStateStore>());
        await service.SweepAsync(new());
    }

    private static async Task<WorkflowExecutionStatus?> StatusAsync(ServiceProvider provider, string workflowExecutionId) =>
        (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(workflowExecutionId))?.Status;

    private static ServiceProvider BuildProvider(
        IDocumentStore documents,
        TimeProvider clock,
        AuthoringState authoring,
        IActivityPublishingAuthorizationContext? authorization = null,
        IActivityTemplateCompiler? templateCompiler = null,
        IWorkflowExecutableStartPolicy? startPolicy = null,
        bool makeFirstDispatchAmbiguous = false,
        string? persistenceScope = null,
        IActivityDraftTestRunCancellationPolicy? cancellationPolicy = null,
        IExternalPayloadStore? externalPayloadStore = null,
        IBoundedDocumentStore? queries = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock);
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IIdentityGenerator, SequentialIdentityGenerator>();
        services.AddSingleton<IActivityDefinitionStore>(authoring.Definitions);
        services.AddSingleton<IActivityDefinitionDraftStore>(authoring.Drafts);
        services.AddSingleton<IActivityDefinitionLayoutStore>(authoring.Layouts);
        services.AddSingleton<IActivityDefinitionVersionPublicationStore, EmptyPublicationStore>();
        services.AddSingleton<IWellKnownTypeRegistry>(TestWellKnownTypeRegistry.Create());
        services.AddSingleton<IActivityTemplateCompiler>(
            templateCompiler ?? new SuspendingTemplateCompiler());
        services.AddScoped<IActivityActivationStrategy, SuspendingActivityActivationStrategy>();
        services.AddScoped<IActivityActivationStrategy, FaultingActivityActivationStrategy>();
        services.AddSingleton(externalPayloadStore ?? new InMemoryExternalPayloadStore());
        if (authorization is not null)
            services.AddSingleton(authorization);
        services.AddSingleton<IActivityExecutionInspectionAuthorizationContext, AllowAllActivityExecutionInspectionAuthorizationContext>();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        new GraphActivitiesRuntimeFeature().ConfigureServices(services);
        // spec 145: the publish engine moved to WorkflowsPublishing (pulled via DependsOn at shell
        // composition). This harness composes features directly, so it composes the engine explicitly.
        new WorkflowsPublishingFeature().ConfigureServices(services);
        new WorkflowsPublishingApiFeature().ConfigureServices(services);
        services.AddSingleton(documents);
        services.AddSingleton(queries ?? new RuntimeTestBoundedDocumentStore(documents));
        services.AddGroundworkRuntimeStores();
        services.AddGroundworkPublishingStores();
        if (persistenceScope is not null)
        {
            services.Replace(ServiceDescriptor.Scoped<IPersistenceAccessContextAccessor>(
                _ => GroundworkTestAccess.AccessContext(persistenceScope)));
        }
        if (startPolicy is not null)
        {
            services.RemoveAll<IWorkflowExecutableStartPolicy>();
            services.AddSingleton(startPolicy);
        }
        if (makeFirstDispatchAmbiguous)
            DecorateFirstStartDispatchAsAmbiguous(services);
        if (cancellationPolicy is not null)
        {
            services.RemoveAll<IActivityDraftTestRunCancellationPolicy>();
            services.AddSingleton(cancellationPolicy);
        }

        return services.BuildServiceProvider();
    }

    private static void DecorateFirstStartDispatchAsAmbiguous(IServiceCollection services)
    {
        var registration = services.Last(x => x.ServiceType == typeof(IWorkflowStartDispatcher));
        services.Remove(registration);
        var ambiguity = new AmbiguousDispatchState();
        services.Add(ServiceDescriptor.Describe(
            typeof(IWorkflowStartDispatcher),
            serviceProvider => new ThrowAfterFirstSuccessfulDispatch(
                (IWorkflowStartDispatcher)CreateService(registration, serviceProvider),
                ambiguity),
            registration.Lifetime));
    }

    private static object CreateService(ServiceDescriptor registration, IServiceProvider serviceProvider) =>
        registration.ImplementationInstance
        ?? registration.ImplementationFactory?.Invoke(serviceProvider)
        ?? ActivatorUtilities.GetServiceOrCreateInstance(
            serviceProvider,
            registration.ImplementationType
            ?? throw new InvalidOperationException("The service registration has no implementation."));

    private static ServiceProvider BuildRuntimeOnlyProvider(
        IDocumentStore documents,
        TimeProvider clock,
        IExternalPayloadStore? externalPayloadStore = null,
        IBoundedDocumentStore? queries = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock);
        services.AddSingleton<TimeProvider>(clock);
        services.AddScoped<IActivityActivationStrategy, SuspendingActivityActivationStrategy>();
        services.AddSingleton(externalPayloadStore ?? new InMemoryExternalPayloadStore());
        services.AddSingleton<IActivityExecutionInspectionAuthorizationContext, AllowAllActivityExecutionInspectionAuthorizationContext>();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        new GraphActivitiesRuntimeFeature().ConfigureServices(services);
        services.AddSingleton(documents);
        services.AddSingleton(queries ?? new RuntimeTestBoundedDocumentStore(documents));
        services.AddGroundworkRuntimeStores();

        return services.BuildServiceProvider();
    }

    // The real provider serves the bounded routes here, so the run is driven through the same compiled
    // query plans a host would admit rather than a test evaluator.
    private static async Task<GroundworkPhysicalTestStore<SqlitePhysicalDocumentStore>> OpenSqliteAsync(
        string connectionString) =>
        await GroundworkPhysicalTestStores.OpenSqliteAsync(
            connectionString,
            CombinedStorageManifest(),
            new ProviderIdentity("groundwork-sqlite", "1.0.0"),
            GroundworkTestAccess.DefaultScoped);

    private static StorageManifest CombinedStorageManifest()
    {
        var runtime = ElsaRuntimeStorageManifest.CreatePhysicalized();
        var publishing = PublishingGroundworkStorageManifest.Create();
        return new StorageManifest(
            new("elsa-activity-test-run-tests"),
            new("elsa.tests"),
            new("1.0.0"),
            runtime.StorageUnits.Concat(publishing.StorageUnits).ToArray(),
            new HashSet<string> { "optimistic-concurrency" },
            [])
        {
            SharedDocumentStorages = runtime.SharedDocumentStorages
                .Concat(publishing.SharedDocumentStorages)
                .DistinctBy(storage => storage.Binding.Value)
                .ToArray()
        };
    }

    private static async ValueTask DisposeStoreAsync(IDocumentStore store)
    {
        if (store is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else if (store is IDisposable disposable)
            disposable.Dispose();
    }

    private static bool IsBoundaryInput(DurableValueState value) =>
        value.Metadata.TryGetValue(RuntimeMetadataKeys.BoundaryValueRole, out var role) &&
        StringComparer.Ordinal.Equals(role, "input");

    private static bool IsBoundaryOutput(DurableValueState value) =>
        value.Metadata.TryGetValue(RuntimeMetadataKeys.BoundaryValueRole, out var role) &&
        StringComparer.Ordinal.Equals(role, "output");

    private static ExecutableActivityTemplate Template()
    {
        var root = new ExecutableNode(
            "root", "root", "test", "1", new("test", "1", JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>());
        return new(
            "activity-template-test", "sha256:test", root, new Dictionary<string, WorkflowExecutableResumeTarget>(),
            [], [], [new RuntimeRequirement("test", "1")], "test/1", new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);
    }

    private sealed class Sender : IRequestSender
    {
        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull =>
            throw new InvalidOperationException("Configuration-only test.");
    }

    private sealed class SuspendingTemplateCompiler : IActivityTemplateCompiler
    {
        public ValueTask<ActivityTemplateCompilerResult> CompileAsync(
            ActivityTemplateCompilerRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var child = new ExecutableNode(
                "suspending-root",
                "suspending-root",
                "test.suspending-activity",
                "1",
                new(SuspendingActivityActivationStrategy.ConsumerKeyValue, "1", JsonSerializer.SerializeToElement(new { })),
                new Dictionary<string, RuntimeInputBinding>(),
                new Dictionary<string, RuntimeOutputCapture>(),
                new Dictionary<string, string>(),
                activityContract: SuspendingActivityContract());
            var descriptor = new GraphActivityDescriptor(
                request.Definition.Id,
                request.CandidateDefinitionVersionId,
                request.CandidateVersion,
                "sha256:test-run-suspending-template",
                child.ExecutableNodeId,
                null,
                [new("order", Type(), WellKnownRuntimeDurableValueStorageDrivers.Json, null, "Order")],
                [],
                [new("result", Type(), WellKnownRuntimeDurableValueStorageDrivers.Json, "Result")],
                [new("result", new("Literal", JsonSerializer.SerializeToElement("completed")))],
                ["order"],
                ["result"]);
            var root = new ExecutableNode(
                "graph-boundary",
                "graph-boundary",
                "test.graph-activity",
                request.CandidateVersion,
                new(
                    WellKnownRuntimeActivityConsumers.GraphActivity,
                    RuntimeActivityDescriptor.InitialSchemaVersion,
                    JsonSerializer.SerializeToElement(descriptor, new JsonSerializerOptions(JsonSerializerDefaults.Web))),
                new Dictionary<string, RuntimeInputBinding>(),
                new Dictionary<string, RuntimeOutputCapture>(),
                new Dictionary<string, string> { ["graph.templateHash"] = descriptor.TemplateHash },
                [new ExecutableChildSlot("Graph.Entry", [child])],
                activityContract: GraphActivityContract(descriptor));
            var targets = new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal)
            {
                [SuspendingActivity.ResumeTargetId] = new(
                    SuspendingActivity.ResumeTargetId,
                    child.ExecutableNodeId,
                    "test-handler",
                    new Dictionary<string, string>())
            };
            var template = new ExecutableActivityTemplate(
                "activity-template-test-run-suspending",
                "sha256:test-run-suspending-template",
                root,
                targets,
                [],
                [],
                [
                    new(WellKnownRuntimeActivityConsumers.GraphActivity, RuntimeActivityDescriptor.InitialSchemaVersion),
                    new(SuspendingActivityActivationStrategy.ConsumerKeyValue, "1")
                ],
                "test-provider/1",
                new Dictionary<string, string>(),
                DateTimeOffset.UnixEpoch);
            return ValueTask.FromResult(new ActivityTemplateCompilerResult(
                template,
                new(2, 1, 0, 2, 1, 0, 0),
                [],
                []));
        }

        private static JsonElement Type() => JsonSerializer.SerializeToElement(new { alias = "object" });

        private static Elsa.Activities.Runtime.Core.Models.ActivityContract SuspendingActivityContract() => new(
            SuspendingActivityActivationStrategy.ConsumerKeyValue,
            "1",
            SuspendingActivityActivationStrategy.ConsumerKeyValue,
            JsonSerializer.SerializeToElement(new { }),
            [],
            new ActivityResultContract(new ValueTypeDescriptor("Elsa.Unit"), true, ActivityValuePolicy.Default, []),
            [ActivityOutcomes.Done],
            new ActivityActivationRequirement(
                SuspendingActivityActivationStrategy.ConsumerKeyValue,
                SuspendingActivityActivationStrategy.ConsumerKeyValue));

        private static Elsa.Activities.Runtime.Core.Models.ActivityContract GraphActivityContract(
            GraphActivityDescriptor descriptor)
        {
            var objectType = new ValueTypeDescriptor("object");
            var payload = JsonSerializer.SerializeToElement(
                descriptor,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return new(
                "test.graph-activity",
                "1",
                WellKnownRuntimeActivityConsumers.GraphActivity,
                payload,
                [new Elsa.Activities.Runtime.Core.Models.ActivityInputContract(
                    "order", "Order", objectType, true, false, false, null, ActivityValuePolicy.Default)],
                new ActivityResultContract(
                    objectType,
                    true,
                    ActivityValuePolicy.Default with { Lifecycle = ActivityValueLifecycle.Result },
                    [new ActivityResultProjectionContract("result", "result", objectType, true, ActivityValuePolicy.Default)]),
                [ActivityOutcomes.Done],
                new ActivityActivationRequirement(
                    WellKnownRuntimeActivityConsumers.GraphActivity,
                    WellKnownRuntimeActivityConsumers.GraphActivity));
        }
    }

    private sealed class FaultingTemplateCompiler : IActivityTemplateCompiler
    {
        public ValueTask<ActivityTemplateCompilerResult> CompileAsync(
            ActivityTemplateCompilerRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = new ExecutableNode(
                "faulting-root",
                "faulting-root",
                "test.faulting-activity",
                "1",
                new(
                    FaultingActivityActivationStrategy.ConsumerKeyValue,
                    RuntimeActivityDescriptor.InitialSchemaVersion,
                    JsonSerializer.SerializeToElement(new { })),
                new Dictionary<string, RuntimeInputBinding>(),
                new Dictionary<string, RuntimeOutputCapture>(),
                new Dictionary<string, string>());
            var template = new ExecutableActivityTemplate(
                "activity-template-test-run-faulting",
                "sha256:test-run-faulting-template",
                root,
                new Dictionary<string, WorkflowExecutableResumeTarget>(),
                [],
                [],
                [new(FaultingActivityActivationStrategy.ConsumerKeyValue, RuntimeActivityDescriptor.InitialSchemaVersion)],
                "test-provider/1",
                new Dictionary<string, string>(),
                DateTimeOffset.UnixEpoch);
            return ValueTask.FromResult(new ActivityTemplateCompilerResult(
                template,
                new(1, 0, 0, 1, 0, 0, 0),
                [],
                []));
        }
    }

    private sealed class SuspendingActivityActivationStrategy : IActivityActivationStrategy
    {
        public const string ConsumerKeyValue = "test.suspending-activity";
        public string ConsumerKey => ConsumerKeyValue;
        public IReadOnlyCollection<string> SupportedSchemaVersions => [RuntimeActivityDescriptor.InitialSchemaVersion];
        public bool RequiresInputHydration => false;

        public ValueTask<ActivityActivationLease> ActivateAsync(
            ActivityActivationStrategyRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(ConsumerKeyValue, request.Descriptor.ConsumerKey);
            return ValueTask.FromResult(new ActivityActivationLease(new SuspendingActivity()));
        }
    }

    private sealed record SuspendingState(string Status);
    private sealed record SuspendingTrigger(string Status);

    private sealed class SuspendingActivity : StatefulActivity<ActivityUnit, SuspendingState, SuspendingTrigger>
    {
        public const string ResumeTargetId = "resume-target:test-run";
        public const string StimulusType = "activity-test-run-signal";
        public const string StimulusHash = "activity-test-run-signal:ready";

        protected override ValueTask<ActivityTransition<ActivityUnit, SuspendingState>> ExecuteAsync(
            ActivityExecutionContext context) =>
            ValueTask.FromResult(Suspend(
                new SuspendingState("waiting"),
                [new ActivityTriggerRegistration<SuspendingTrigger>(ResumeTargetId, StimulusType, StimulusHash)]));

        protected override ValueTask<ActivityTransition<ActivityUnit, SuspendingState>> ResumeAsync(
            ActivityResumeContext<SuspendingState, SuspendingTrigger> context) =>
            ValueTask.FromResult(Complete(ActivityUnit.Value));
    }

    private sealed class FaultingActivityActivationStrategy : IActivityActivationStrategy
    {
        public const string ConsumerKeyValue = "test.faulting-activity";
        public string ConsumerKey => ConsumerKeyValue;
        public IReadOnlyCollection<string> SupportedSchemaVersions => [RuntimeActivityDescriptor.InitialSchemaVersion];
        public bool RequiresInputHydration => false;

        public ValueTask<ActivityActivationLease> ActivateAsync(
            ActivityActivationStrategyRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(ConsumerKeyValue, request.Descriptor.ConsumerKey);
            return ValueTask.FromResult(new ActivityActivationLease(new FaultingActivity()));
        }
    }

    private sealed class FaultingActivity : Activity
    {
        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            throw new InvalidOperationException("provider-secret fault details stay in Runtime Evidence.");
    }

    private sealed class SequentialIdentityGenerator : IIdentityGenerator
    {
        private int _value;
        public string Generate() => $"id-{Interlocked.Increment(ref _value)}";
    }

    private sealed class InMemoryExternalPayloadStore : IExternalPayloadStore
    {
        private readonly ConcurrentDictionary<string, JsonElement> _payloads =
            new(StringComparer.Ordinal);

        public ValueTask<DurableValueExternalReference> WriteAsync(
            ExternalPayloadWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var locator = $"{request.WorkflowExecutionId}/{request.OwnerKey}";
            _payloads[locator] = request.Payload.Clone();
            return ValueTask.FromResult(new DurableValueExternalReference(
                request.StorageProfile,
                locator,
                new Dictionary<string, string>(StringComparer.Ordinal)));
        }

        public ValueTask<JsonElement> ReadAsync(
            DurableValueExternalReference reference,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_payloads[reference.Locator].Clone());
        }
    }

    private sealed class DenyStartPolicy : IWorkflowExecutableStartPolicy
    {
        public ValueTask<WorkflowExecutableStartDecision> EvaluateAsync(
            WorkflowExecutableStartPolicyContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(WorkflowExecutableStartDecision.Deny(
                "test-policy-denied",
                "provider-secret must never reach the activity Test Run response."));
    }

    private sealed class DenyCancellationPolicy : IActivityDraftTestRunCancellationPolicy
    {
        public ValueTask<ActivityDraftTestRunCancellationDecision> EvaluateAsync(
            ActivityDraftTestRunReceipt receipt,
            WorkflowExecutionState? execution,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityDraftTestRunCancellationDecision(
                true,
                false,
                "Test policy denies cancellation."));
    }

    private sealed class ThrowingTemplateCompiler : IActivityTemplateCompiler
    {
        public ValueTask<ActivityTemplateCompilerResult> CompileAsync(
            ActivityTemplateCompilerRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Compilation stopped after the Preparing receipt was persisted.");
    }

    private sealed class MutableAuthorizationContext(string? tenantId) : IActivityPublishingAuthorizationContext
    {
        public string? TenantId { get; set; } = tenantId;

        public bool CanAccessTenant(string? candidateTenantId) =>
            candidateTenantId is null || StringComparer.Ordinal.Equals(candidateTenantId, TenantId);
    }

    private sealed class AmbiguousDispatchState
    {
        public int HasThrown;
    }

    private sealed class ThrowAfterFirstSuccessfulDispatch(
        IWorkflowStartDispatcher inner,
        AmbiguousDispatchState state) : IWorkflowStartDispatcher
    {
        public async ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(
            WorkflowExecutionStartDispatchRequest request,
            WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.Published,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.DispatchAsync(
                request,
                requiredScope,
                dispatchOptions,
                cancellationToken);
            if (Interlocked.Exchange(ref state.HasThrown, 1) == 0)
                throw new InvalidOperationException("The transport dropped the first dispatch acknowledgement.");
            return result;
        }
    }

    private sealed class AuthoringState
    {
        public const string DraftId = "draft-test-run";

        private AuthoringState(
            ActivityDefinition definition,
            ActivityDefinitionDraft draft,
            ActivityDefinitionDraftLayout layout)
        {
            Draft = draft;
            Definitions = new DefinitionStore(definition);
            Drafts = new DraftStore(draft);
            Layouts = new LayoutStore(layout);
        }

        public ActivityDefinitionDraft Draft { get; }
        public DefinitionStore Definitions { get; }
        public DraftStore Drafts { get; }
        public LayoutStore Layouts { get; }

        public static AuthoringState Create(string? tenantId = null)
        {
            var definition = new ActivityDefinition
            {
                Id = "definition-test-run",
                ActivityTypeKey = "test.suspending",
                Category = "Tests",
                DisplayName = "Suspending test activity",
                TenantId = tenantId
            };
            var draft = new ActivityDefinitionDraft
            {
                Id = DraftId,
                DefinitionId = definition.Id,
                Revision = 4,
                Status = ActivityDefinitionDraftStatus.Active,
                TenantId = tenantId,
                State = new(
                    new(
                        "1",
                        [new("order", "Order", new("object"), true, false, null, WellKnownRuntimeDurableValueStorageDrivers.Json)],
                        [new("result", "Result", new("object"), true, false, WellKnownRuntimeDurableValueStorageDrivers.Json)],
                        [new("done", "Done", true)]),
                    new("test.provider", "1", JsonSerializer.SerializeToElement(new { })),
                    new Dictionary<string, string>())
            };
            var layout = new ActivityDefinitionDraftLayout
            {
                Id = "layout-test-run",
                DraftId = draft.Id,
                Revision = draft.Revision,
                TenantId = tenantId,
                Records = []
            };
            return new(definition, draft, layout);
        }
    }

    private sealed class DefinitionStore(ActivityDefinition definition) : IActivityDefinitionStore
    {
        public Task<ActivityDefinition> GetAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(StringComparer.Ordinal.Equals(id, definition.Id) ? definition : throw new KeyNotFoundException(id));
        public Task<ActivityDefinition?> FindAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinition?>(definition);
        public Task<IReadOnlyList<ActivityDefinition>> ListAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinition>>([definition]);
        public Task<ActivityDefinition?> FindByIdOrActivityTypeKeyAsync(string id, string activityTypeKey, CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinition?>(definition);
        public Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class DraftStore(ActivityDefinitionDraft draft) : IActivityDefinitionDraftStore
    {
        private ActivityDefinitionDraft? _draft = draft;

        public void Remove() => _draft = null;

        public Task<ActivityDefinitionDraft?> FindAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult(StringComparer.Ordinal.Equals(draftId, _draft?.Id) ? _draft : null);
        public Task<IReadOnlyList<ActivityDefinitionDraft>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionDraft>>(_draft is null ? [] : [_draft]);
    }

    private sealed class LayoutStore(ActivityDefinitionDraftLayout layout) : IActivityDefinitionLayoutStore
    {
        public Task<ActivityDefinitionDraftLayout?> FindDraftLayoutAsync(string draftId, CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinitionDraftLayout?>(layout);
        public Task<ActivityDefinitionVersionLayout?> FindVersionLayoutAsync(string definitionVersionId, CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinitionVersionLayout?>(null);
    }

    private sealed class EmptyPublicationStore : IActivityDefinitionVersionPublicationStore
    {
        public Task<ActivityDefinitionVersionPublication?> FindAsync(string definitionVersionId, CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinitionVersionPublication?>(null);
        public Task<IReadOnlyList<ActivityDefinitionVersionPublication>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinitionVersionPublication>>([]);
    }
}
