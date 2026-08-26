using System.Text.Json;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Capabilities;
using Elsa.Workflows.Runtime.Api.Contracts.Alterations;
using Elsa.Workflows.Runtime.Api.Handlers.Alterations;
using Elsa.Workflows.Runtime.Api.Models.Alterations;
using Elsa.Workflows.Runtime.Api.Requests.Alterations;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests.Alterations;

public sealed class WorkflowAlterationPlanApiTests
{
    [Fact]
    public async Task Submission_replays_a_matching_key_and_rejects_a_different_canonical_request()
    {
        var fixture = Fixture.Create();
        var first = await fixture.Service.SubmitAsync(Submit("key-1", ["execution-a"]), CancellationToken.None);
        var replay = await fixture.Service.SubmitAsync(Submit("key-1", ["execution-a"]), CancellationToken.None);

        Assert.Equal("Accepted", first.SubmissionDisposition);
        Assert.Equal("Replayed", replay.SubmissionDisposition);
        Assert.Equal(first.PlanId, replay.PlanId);
        await Assert.ThrowsAsync<WorkflowAlterationIdempotencyConflictException>(() =>
            fixture.Service.SubmitAsync(Submit("key-1", ["execution-b"]), CancellationToken.None));
    }

    [Fact]
    public async Task Reads_are_tenant_scoped_and_never_expose_protected_payload_material()
    {
        var fixture = Fixture.Create();
        var submitted = await fixture.Service.SubmitAsync(Submit("key-2", ["execution-a"]), CancellationToken.None);
        var plan = await fixture.Store.FindPlanAsync(submitted.PlanId);
        Assert.NotNull(plan);

        var own = await fixture.Service.GetPlanAsync(new GetWorkflowAlterationPlan(submitted.PlanId), CancellationToken.None);
        Assert.Equal("CancelWorkflow", Assert.Single(own.Alterations).Kind);
        Assert.Equal("operator-1", own.SubmittedBy.Subject);
        Assert.Equal("test-request", own.SubmittedBy.CorrelationId);
        await Assert.ThrowsAsync<WorkflowAlterationResourceNotFoundException>(() => fixture.ServiceFor(Fixture.CreateContext("tenant-b"))
            .GetPlanAsync(new GetWorkflowAlterationPlan(submitted.PlanId), CancellationToken.None));
        var serialized = JsonSerializer.Serialize(own);
        Assert.DoesNotContain("ciphertext", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("idempotency", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payload", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Stable_job_page_is_scoped_to_its_plan_and_uses_capture_order()
    {
        var fixture = Fixture.Create();
        var submitted = await fixture.Service.SubmitAsync(Submit("key-3", ["execution-b", "execution-a"]), CancellationToken.None);
        var plan = (await fixture.Store.FindPlanAsync(submitted.PlanId))!;
        await fixture.Store.CaptureAsync(plan.PlanId, plan.Revision,
            [new WorkflowAlterationCapturedTarget("execution-b", "tenant-a"), new WorkflowAlterationCapturedTarget("execution-a", "tenant-a")], null);

        var page = await fixture.Service.PageJobsAsync(new PageWorkflowAlterationJobs(plan.PlanId, 25, null), CancellationToken.None);
        var progress = await fixture.Service.GetPlanAsync(new GetWorkflowAlterationPlan(plan.PlanId), CancellationToken.None);

        Assert.Equal(["execution-a", "execution-b"], page.Items.Select(item => item.WorkflowExecutionId));
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, progress.Counts.CapturedSoFar);
        Assert.Equal(0, progress.Counts.TargetCount);
        var jobRead = await fixture.Service.GetJobAsync(new GetWorkflowAlterationJob(plan.PlanId, page.Items.First().JobId), CancellationToken.None);
        Assert.Equal("operator-1", jobRead.SubmittedBy.Subject);
        Assert.Equal("test-request", jobRead.SubmittedBy.CorrelationId);
        var foreignPlan = await fixture.Service.SubmitAsync(Submit("key-4", ["execution-c"]), CancellationToken.None);
        var firstJob = page.Items.First();
        await Assert.ThrowsAsync<WorkflowAlterationResourceNotFoundException>(() => fixture.Service
            .GetJobAsync(new GetWorkflowAlterationJob(foreignPlan.PlanId, firstJob.JobId), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Service
            .PageJobsAsync(new PageWorkflowAlterationJobs(plan.PlanId, 101, null), CancellationToken.None));
    }

    [Fact]
    public async Task Invalid_query_and_pre_admission_backpressure_are_distinct_before_persistence()
    {
        var fixture = Fixture.Create(new RejectingGate(TimeSpan.FromSeconds(7)));
        var invalid = new SubmitWorkflowAlterationPlan(
            new WorkflowAlterationTargetRequest(null, new WorkflowAlterationQueryRequest(From: DateTimeOffset.UtcNow, To: DateTimeOffset.UtcNow.AddMinutes(-1))),
            [new WorkflowAlterationEnvelopeRequest("CancelWorkflow", 1, JsonSerializer.SerializeToElement(new { }))]) { IdempotencyKey = "invalid" };
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.SubmitAsync(invalid, CancellationToken.None));

        var rejected = await Assert.ThrowsAsync<WorkflowAlterationAdmissionRejectedException>(() =>
            fixture.Service.SubmitAsync(Submit("key-5", ["execution-a"]), CancellationToken.None));
        Assert.Equal(TimeSpan.FromSeconds(7), rejected.RetryAfter);
        Assert.Null(await fixture.Store.FindPlanAsync("alteration-plan-does-not-exist"));
    }

    [Fact]
    public void Endpoints_lock_exact_routes_and_permission_separation()
    {
        var submit = RuntimeApiEndpointTestFactory.FindByRoute("runtime/workflows/alteration-plans");
        var plan = RuntimeApiEndpointTestFactory.FindByRoute("runtime/workflows/alteration-plans/{planId}");
        var page = RuntimeApiEndpointTestFactory.FindByRoute("runtime/workflows/alteration-plans/{planId}/jobs/page");
        var job = RuntimeApiEndpointTestFactory.FindByRoute("runtime/workflows/alteration-plans/{planId}/jobs/{jobId}");
        var cancel = RuntimeApiEndpointTestFactory.FindByRoute("runtime/workflows/alteration-plans/{planId}/cancel");

        Assert.All([submit, cancel], endpoint => RuntimeApiEndpointTestFactory.AssertPermissionPolicy(endpoint, WorkflowRuntimePermissions.WorkflowRuntimeManage));
        Assert.All([plan, page, job], endpoint => RuntimeApiEndpointTestFactory.AssertPermissionPolicy(endpoint, WorkflowRuntimePermissions.WorkflowRuntimeRead));
    }

    [Fact]
    public async Task Capability_links_are_advertised_only_when_store_and_plan_service_are_composed()
    {
        var empty = new ServiceCollection().BuildServiceProvider();
        Assert.Empty(await new RuntimeAlterationCapabilitySource(empty).GetCapabilitiesAsync());

        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        await using var provider = services.BuildServiceProvider();
        var declarations = await new RuntimeAlterationCapabilitySource(provider).GetCapabilitiesAsync();
        var links = Assert.Single(declarations).Links.ToDictionary(link => link.Rel, StringComparer.Ordinal);
        Assert.Equal("runtime/workflows/alteration-plans", links["workflow-alteration-plans"].Href);
        Assert.True(links["workflow-alteration-plan"].Templated);
        Assert.True(links["workflow-alteration-plan-jobs-page"].Templated);
        Assert.True(links["workflow-alteration-job"].Templated);
        Assert.True(links["workflow-alteration-plan-cancel"].Templated);
    }

    private static SubmitWorkflowAlterationPlan Submit(string key, IReadOnlyCollection<string> executionIds) =>
        new(new WorkflowAlterationTargetRequest(executionIds, null),
            [new WorkflowAlterationEnvelopeRequest("CancelWorkflow", 1, JsonSerializer.SerializeToElement(new { }))])
        { IdempotencyKey = key };

    private sealed class Fixture
    {
        private readonly WorkflowAlterationPlanService _planService;
        private readonly IWorkflowAlterationAdmissionGate _gate;

        private Fixture(InMemoryWorkflowAlterationStore store, IWorkflowAlterationPayloadProtector protector, IWorkflowAlterationRequestContext context, WorkflowAlterationPlanService planService, IWorkflowAlterationAdmissionGate gate)
        {
            Store = store;
            Protector = protector;
            Context = context;
            _planService = planService;
            _gate = gate;
            Service = new WorkflowAlterationPlanApiService(planService, store, gate, context);
        }

        public InMemoryWorkflowAlterationStore Store { get; }
        public IWorkflowAlterationPayloadProtector Protector { get; }
        public IWorkflowAlterationRequestContext Context { get; }
        public WorkflowAlterationPlanApiService Service { get; }

        public WorkflowAlterationPlanApiService ServiceFor(IWorkflowAlterationRequestContext context) =>
            new(_planService, Store, _gate, context);

        public static Fixture Create(IWorkflowAlterationAdmissionGate? gate = null)
        {
            var store = new InMemoryWorkflowAlterationStore();
            var protector = new AesGcmWorkflowAlterationPayloadProtector(
                Options.Create(new WorkflowAlterationPayloadProtectionOptions { AllowEphemeralDevelopmentKey = true }),
                new WorkflowAlterationEphemeralKeyRing());
            var context = CreateContext("tenant-a");
            var service = new WorkflowAlterationPlanService(
                new WorkflowAlterationRegistry(
                [
                    new WorkflowAlterationHandlerContribution(
                        new WorkflowAlterationDescriptor("CancelWorkflow", 1, "Cancel workflow"),
                        typeof(CancelWorkflowAlterationHandler))
                ]),
                protector,
                store);
            return new(store, protector, context, service, gate ?? new AllowGate());
        }

        public static IWorkflowAlterationRequestContext CreateContext(string tenant) => new TestContext(tenant);
    }

    private sealed class TestContext(string tenant) : IWorkflowAlterationRequestContext
    {
        public WorkflowAlterationAuthorityScope AuthorityScope { get; } = new(tenant, "runtime-api", "operator-1");
        public WorkflowAlterationOperatorProvenance Operator { get; } = new("operator-1", "test-request");
        public bool CanAccess(WorkflowAlterationPlanState plan) => plan.AuthorityScope.TenantPartition == tenant;
    }

    private sealed class AllowGate : IWorkflowAlterationAdmissionGate
    {
        public ValueTask<WorkflowAlterationAdmissionDecision> EvaluateAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(WorkflowAlterationAdmissionDecision.Accepted);
    }

    private sealed class RejectingGate(TimeSpan retryAfter) : IWorkflowAlterationAdmissionGate
    {
        public ValueTask<WorkflowAlterationAdmissionDecision> EvaluateAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkflowAlterationAdmissionDecision(false, retryAfter));
    }
}
