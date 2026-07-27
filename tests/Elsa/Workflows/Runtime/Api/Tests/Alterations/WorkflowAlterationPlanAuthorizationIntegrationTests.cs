using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Encodings.Web;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Api.Handlers.Alterations;
using Elsa.Workflows.Runtime.Api.Requests.Alterations;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Services.Alterations;
using AlterationContracts = Elsa.Workflows.Runtime.Api.Contracts.Alterations;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests.Alterations;

public sealed class WorkflowAlterationPlanAuthorizationIntegrationTests
{
    [Fact]
    public async Task Endpoints_enforce_permissions_header_contract_and_tenant_invisibility()
    {
        await using var host = await AlterationApiHost.StartAsync();
        var body = new
        {
            target = new { executionIds = new[] { "execution-a" } },
            alterations = new[] { new { kind = "CancelWorkflow", schemaVersion = 1, payload = new { } } }
        };

        using var anonymous = await host.Client.PostAsJsonAsync("/runtime/workflows/alteration-plans", body);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        host.Authorize(PermissionNames.WorkflowRuntimeManage, "tenant-a");
        using var missingKey = await host.Client.PostAsJsonAsync("/runtime/workflows/alteration-plans", body);
        Assert.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode);

        using var acceptedRequest = new HttpRequestMessage(HttpMethod.Post, "/runtime/workflows/alteration-plans")
        {
            Content = JsonContent.Create(body)
        };
        acceptedRequest.Headers.Add("Idempotency-Key", "api-key-1");
        using var accepted = await host.Client.SendAsync(acceptedRequest);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.NotNull(accepted.Headers.Location);
        var location = accepted.Headers.Location!.ToString();

        host.Authorize(PermissionNames.WorkflowRuntimeRead, "tenant-a");
        using var read = await host.Client.GetAsync(location);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var readBody = await read.Content.ReadAsStringAsync();
        Assert.DoesNotContain("payload", readBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("idempotency", readBody, StringComparison.OrdinalIgnoreCase);
        using (var document = JsonDocument.Parse(readBody))
        {
            var target = document.RootElement.GetProperty("target");
            Assert.Equal("Explicit", target.GetProperty("kind").GetString());
            Assert.Equal(1, target.GetProperty("explicitCount").GetInt32());
            Assert.Empty(target.GetProperty("filterNames").EnumerateArray().ToArray());
        }

        host.Authorize(PermissionNames.WorkflowRuntimeRead, "tenant-b");
        using var foreignRead = await host.Client.GetAsync(location);
        Assert.Equal(HttpStatusCode.NotFound, foreignRead.StatusCode);
        Assert.DoesNotContain("tenant-a", await foreignRead.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Query_target_read_uses_the_documented_redacted_shape()
    {
        await using var host = await AlterationApiHost.StartAsync();
        host.Authorize(PermissionNames.WorkflowRuntimeManage, "tenant-a");
        var body = new
        {
            target = new { query = new { definitionId = "definition-a", status = "Running" } },
            alterations = new[] { new { kind = "CancelWorkflow", schemaVersion = 1, payload = new { } } }
        };
        var planId = await SubmitPlanAsync(host, body, "query-redaction");

        host.Authorize(PermissionNames.WorkflowRuntimeRead, "tenant-a");
        using var response = await host.Client.GetAsync($"/runtime/workflows/alteration-plans/{planId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var target = document.RootElement.GetProperty("target");
        Assert.Equal("Query", target.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, target.GetProperty("explicitCount").ValueKind);
        Assert.Equal(
            new[] { "definitionId", "status" },
            target.GetProperty("filterNames").EnumerateArray().Select(item => item.GetString()!).ToArray());
    }

    [Fact]
    public async Task Submission_rejects_an_overlong_idempotency_key_with_a_stable_bad_request_problem()
    {
        await using var host = await AlterationApiHost.StartAsync();
        host.Authorize(PermissionNames.WorkflowRuntimeManage, "tenant-a");
        var body = new
        {
            target = new { executionIds = new[] { "execution-a" } },
            alterations = new[] { new { kind = "CancelWorkflow", schemaVersion = 1, payload = new { } } }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/runtime/workflows/alteration-plans") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", new string('k', 257));

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("InvalidIdempotencyKey", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Cancellation_returns_a_bare_plan_view_with_accepted_or_terminal_noop_status()
    {
        await using var host = await AlterationApiHost.StartAsync();
        host.Authorize(PermissionNames.WorkflowRuntimeManage, "tenant-a");
        var activePlanId = await SubmitPlanAsync(host, new
        {
            target = new { executionIds = new[] { "execution-a" } },
            alterations = new[] { new { kind = "CancelWorkflow", schemaVersion = 1, payload = new { } } }
        }, "cancel-active");

        using (var active = await host.Client.PostAsync($"/runtime/workflows/alteration-plans/{activePlanId}/cancel", null))
        {
            Assert.Equal(HttpStatusCode.Accepted, active.StatusCode);
            using var document = JsonDocument.Parse(await active.Content.ReadAsStringAsync());
            Assert.Equal(activePlanId, document.RootElement.GetProperty("planId").GetString());
            Assert.False(document.RootElement.TryGetProperty("plan", out _));
        }

        var terminalPlanId = await SubmitPlanAsync(host, new
        {
            target = new { query = new { definitionId = "unmatched-definition" } },
            alterations = new[] { new { kind = "CancelWorkflow", schemaVersion = 1, payload = new { } } }
        }, "cancel-terminal");
        using (var scope = host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<WorkflowAlterationTargetCaptureTask>().CaptureNextAsync(terminalPlanId, 100);

        using var terminal = await host.Client.PostAsync($"/runtime/workflows/alteration-plans/{terminalPlanId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, terminal.StatusCode);
        using var terminalDocument = JsonDocument.Parse(await terminal.Content.ReadAsStringAsync());
        Assert.Equal(terminalPlanId, terminalDocument.RootElement.GetProperty("planId").GetString());
        Assert.False(terminalDocument.RootElement.TryGetProperty("plan", out _));
    }

    [Fact]
    public async Task Authenticated_bulk_cancel_runs_through_capture_actor_checkpoint_and_redacted_reads()
    {
        await using var host = await AlterationApiHost.StartAsync();
        await SeedWorkflowAsync(host.Services, "execution-a");
        await SeedWorkflowAsync(host.Services, "execution-b");
        host.Authorize(PermissionNames.WorkflowRuntimeManage, "tenant-a");
        var body = new
        {
            target = new { executionIds = new[] { "execution-a", "execution-b" } },
            alterations = new[] { new { kind = "CancelWorkflow", schemaVersion = 1, payload = new { } } }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/runtime/workflows/alteration-plans") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", "bulk-cancel");
        using var accepted = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var planId = (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("planId").GetString()!;

        using (var scope = host.Services.CreateScope())
        {
            var capture = scope.ServiceProvider.GetRequiredService<WorkflowAlterationTargetCaptureTask>();
            await capture.CaptureNextAsync(planId, 100);
            var jobs = scope.ServiceProvider.GetRequiredService<WorkflowAlterationJobTask>();
            await jobs.DispatchAvailableAsync(planId, "api-test", 100, TimeSpan.FromMinutes(1));
        }

        await WaitForTerminalJobsAsync(host.Services, planId, 2);
        string jobId;
        using (var scope = host.Services.CreateScope())
        {
            var workflows = scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>();
            Assert.Equal(WorkflowExecutionStatus.Cancelled, (await workflows.FindAsync("execution-a"))!.Status);
            Assert.Equal(WorkflowExecutionStatus.Cancelled, (await workflows.FindAsync("execution-b"))!.Status);
            var store = scope.ServiceProvider.GetRequiredService<IWorkflowAlterationStore>();
            var jobs = await store.PageJobsAsync(planId, 100, null);
            Assert.All(jobs.Items, job => Assert.Equal(WorkflowAlterationJobStatus.Succeeded, job.Status));
            jobId = jobs.Items.First().JobId;
        }

        host.Authorize(PermissionNames.WorkflowRuntimeRead, "tenant-a");
        using var planRead = await host.Client.GetAsync($"/runtime/workflows/alteration-plans/{planId}");
        using var jobRead = await host.Client.GetAsync($"/runtime/workflows/alteration-plans/{planId}/jobs/{jobId}");
        var planBody = await planRead.Content.ReadAsStringAsync();
        var jobBody = await jobRead.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, planRead.StatusCode);
        Assert.Equal(HttpStatusCode.OK, jobRead.StatusCode);
        Assert.DoesNotContain("payload", planBody + jobBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("value", planBody + jobBody, StringComparison.OrdinalIgnoreCase);
        using var planDocument = JsonDocument.Parse(planBody);
        using var jobDocument = JsonDocument.Parse(jobBody);
        Assert.Equal("operator-1", planDocument.RootElement.GetProperty("submittedBy").GetProperty("subject").GetString());
        Assert.Equal("operator-1", jobDocument.RootElement.GetProperty("submittedBy").GetProperty("subject").GetString());
    }

    [Fact]
    public async Task Authenticated_migration_runs_through_capture_actor_checkpoint_and_redacted_reads()
    {
        await using var host = await AlterationApiHost.StartAsync();
        var identities = await SeedMigratableWorkflowAsync(host.Services, "migration-success");
        host.Authorize(PermissionNames.WorkflowRuntimeManage, "tenant-a");
        var body = new
        {
            target = new { executionIds = new[] { "migration-success" } },
            alterations = new[] { new { kind = "Migrate", schemaVersion = 1, payload = new { targetArtifact = new
            {
                artifactId = identities.Target.ArtifactId,
                definitionId = identities.Target.DefinitionId,
                definitionVersionId = identities.Target.DefinitionVersionId,
                artifactVersion = identities.Target.ArtifactVersion,
                artifactHash = identities.Target.ArtifactHash
            } } } }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/runtime/workflows/alteration-plans") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", "migrate-success");
        using var accepted = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var planId = (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("planId").GetString()!;

        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<WorkflowAlterationTargetCaptureTask>().CaptureNextAsync(planId, 100);
            await scope.ServiceProvider.GetRequiredService<WorkflowAlterationJobTask>().DispatchAvailableAsync(planId, "api-test", 10, TimeSpan.FromMinutes(1));
        }
        await WaitForTerminalJobsAsync(host.Services, planId, 1);

        using (var scope = host.Services.CreateScope())
        {
            var workflow = await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("migration-success");
            Assert.Equal(identities.Target, workflow!.PinnedExecutable);
            Assert.Equal(identities.Target.DefinitionVersionId, workflow.PinnedSource!.DefinitionVersionId);
            var job = Assert.Single((await scope.ServiceProvider.GetRequiredService<IWorkflowAlterationStore>().PageJobsAsync(planId, 10, null)).Items);
            Assert.Equal(WorkflowAlterationJobStatus.Succeeded, job.Status);
        }

        host.Authorize(PermissionNames.WorkflowRuntimeRead, "tenant-a");
        using var planRead = await host.Client.GetAsync($"/runtime/workflows/alteration-plans/{planId}");
        using var jobRead = await host.Client.GetAsync($"/runtime/workflows/alteration-plans/{planId}/jobs/{(await ReadOnlyJobIdAsync(host.Services, planId))}");
        var response = (await planRead.Content.ReadAsStringAsync()) + (await jobRead.Content.ReadAsStringAsync());
        Assert.DoesNotContain(identities.Target.ArtifactHash, response, StringComparison.Ordinal);
        Assert.DoesNotContain("targetArtifact", response, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Migration_target_disappearance_or_retirement_fails_without_repinning_the_execution()
    {
        await AssertMigrationFailureAsync("migration-target-deleted", async (services, target) =>
        {
            using var scope = services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().DeleteAsync(target.ArtifactId);
        }, "TargetArtifactUnavailable");

        await AssertMigrationFailureAsync("migration-target-retired", async (services, _) =>
        {
            using var scope = services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().RetireAsync("migration-target-ref", DateTimeOffset.UtcNow, "test");
        }, "TargetArtifactNotRetained");
    }

    [Theory]
    [InlineData("unknown.alteration", 1)]
    [InlineData("Migrate", 2)]
    public async Task Submission_rejects_unknown_or_unsupported_alteration_schema(string kind, int schemaVersion)
    {
        await using var host = await AlterationApiHost.StartAsync();
        host.Authorize(PermissionNames.WorkflowRuntimeManage, "tenant-a");
        var body = new
        {
            target = new { executionIds = new[] { "execution-a" } },
            alterations = new[] { new { kind, schemaVersion, payload = new { } } }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/runtime/workflows/alteration-plans") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", $"{kind}-{schemaVersion}");

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.DoesNotContain(kind, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authenticated_modify_variable_runs_through_checkpoint_and_never_exposes_values()
    {
        await using var host = await AlterationApiHost.StartAsync();
        await SeedVariableWorkflowAsync(host.Services, "variable-execution", rootFrame: true, new ValueTypeDescriptor("String"));
        host.Authorize(PermissionNames.WorkflowRuntimeManage, "tenant-a");
        var body = new
        {
            target = new { executionIds = new[] { "variable-execution" } },
            alterations = new[] { new { kind = "ModifyVariable", schemaVersion = 1, payload = new { variableKey = "stable-name", value = "replacement-secret" } } }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/runtime/workflows/alteration-plans") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", "modify-variable");
        using var accepted = await host.Client.SendAsync(request);
        var planId = (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("planId").GetString()!;
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);

        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<WorkflowAlterationTargetCaptureTask>().CaptureNextAsync(planId, 100);
            await scope.ServiceProvider.GetRequiredService<WorkflowAlterationJobTask>().DispatchAvailableAsync(planId, "api-test", 10, TimeSpan.FromMinutes(1));
        }
        await WaitForTerminalJobsAsync(host.Services, planId, 1);
        using (var scope = host.Services.CreateScope())
        {
            var workflow = await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("variable-execution");
            Assert.Equal("replacement-secret", workflow!.RootVariableFrame!.Values["stable-name"].InlineValue!.Value.GetString());
            var job = Assert.Single((await scope.ServiceProvider.GetRequiredService<IWorkflowAlterationStore>().PageJobsAsync(planId, 10, null)).Items);
            Assert.Equal(WorkflowAlterationJobStatus.Succeeded, job.Status);
        }

        host.Authorize(PermissionNames.WorkflowRuntimeRead, "tenant-a");
        using var planRead = await host.Client.GetAsync($"/runtime/workflows/alteration-plans/{planId}");
        using var jobRead = await host.Client.GetAsync($"/runtime/workflows/alteration-plans/{planId}/jobs/{(await ReadOnlyJobIdAsync(host.Services, planId))}");
        Assert.DoesNotContain("replacement-secret", await planRead.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain("replacement-secret", await jobRead.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Modify_variable_api_rejects_stale_missing_container_type_and_unsupported_protection_without_value_leakage()
    {
        await AssertModificationFailureAsync("stale", rootFrame: true, new ValueTypeDescriptor("String"), ValueProtectionPolicy.InstanceInline,
            "stable-name", JsonSerializer.SerializeToElement("replacement-secret"), "RootVariableFrameRevisionConflict", async services =>
            {
                using var scope = services.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>();
                var workflow = (await store.FindAsync("variable-stale"))!;
                var frame = workflow.RootVariableFrame!;
                await store.SaveAsync(workflow with { RootVariableFrame = frame.Set("stable-name", frame.Values["stable-name"], frame.Revision) });
            });
        await AssertModificationFailureAsync("missing", rootFrame: true, new ValueTypeDescriptor("String"), ValueProtectionPolicy.InstanceInline,
            "missing-key", JsonSerializer.SerializeToElement("replacement-secret"), "VariableNotDeclared");
        await AssertModificationFailureAsync("container", rootFrame: false, new ValueTypeDescriptor("String"), ValueProtectionPolicy.InstanceInline,
            "stable-name", JsonSerializer.SerializeToElement("replacement-secret"), "RootVariableFrameUnavailable");
        await AssertModificationFailureAsync("type", rootFrame: true, new ValueTypeDescriptor("Int32"), ValueProtectionPolicy.InstanceInline,
            "stable-name", JsonSerializer.SerializeToElement("not-an-integer"), "VariableValueTypeMismatch");
        await AssertModificationFailureAsync("protection", rootFrame: true, new ValueTypeDescriptor("String"), new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.External),
            "stable-name", JsonSerializer.SerializeToElement("replacement-secret"), "VariableProtectionPolicyUnsupported");
    }

    [Fact]
    public async Task Schedule_activity_api_runs_through_capture_actor_checkpoint_and_replay_keeps_one_continuation()
    {
        await using var host = await AlterationApiHost.StartAsync(services =>
            services.AddSingleton<IOperatorActivitySchedulingPolicy, AllowOperatorSchedulingPolicy>());
        await SeedScheduleWorkflowAsync(host.Services, "schedule-success", withCapability: true, WorkflowExecutionStatus.Running);
        host.Authorize(PermissionNames.WorkflowRuntimeManage, "tenant-a");
        var body = ScheduleBody("schedule-success", "child", "parent-exec");

        var planId = await SubmitPlanAsync(host, body, "schedule-success");
        var replayPlanId = await SubmitPlanAsync(host, body, "schedule-success");
        Assert.Equal(planId, replayPlanId);

        await CaptureAndDispatchAsync(host.Services, planId);
        await WaitForTerminalJobsAsync(host.Services, planId, 1);

        using (var scope = host.Services.CreateScope())
        {
            var job = Assert.Single((await scope.ServiceProvider.GetRequiredService<IWorkflowAlterationStore>().PageJobsAsync(planId, 10, null)).Items);
            Assert.Equal(WorkflowAlterationJobStatus.Succeeded, job.Status);
            var continuation = await DeliverSingleSchedulerContinuationAsync(scope.ServiceProvider, "schedule-success");
            Assert.Equal(WorkflowExecutionCommandKind.ScheduleActivity, continuation.CommandKind);
            Assert.Equal("schedule-success", continuation.WorkflowExecutionId);
            Assert.Equal(job.JobId, continuation.EnvelopeId);
        }

        host.Authorize(PermissionNames.WorkflowRuntimeRead, "tenant-a");
        using var planRead = await host.Client.GetAsync($"/runtime/workflows/alteration-plans/{planId}");
        using var jobRead = await host.Client.GetAsync($"/runtime/workflows/alteration-plans/{planId}/jobs/{(await ReadOnlyJobIdAsync(host.Services, planId))}");
        var response = (await planRead.Content.ReadAsStringAsync()) + (await jobRead.Content.ReadAsStringAsync());
        Assert.DoesNotContain("parentActivityExecutionId", response, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payload", response, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Schedule_activity_api_rejects_missing_capability_policy_and_terminal_state_with_safe_outcomes()
    {
        await AssertScheduleFailureAsync("schedule-no-capability", withCapability: false, WorkflowExecutionStatus.Running, configure: null, "OperatorSchedulingNotSupported");
        await AssertScheduleFailureAsync("schedule-no-policy", withCapability: true, WorkflowExecutionStatus.Running, configure: null, "OperatorSchedulingPolicyUnavailable");
        await AssertScheduleFailureAsync("schedule-terminal", withCapability: true, WorkflowExecutionStatus.Completed,
            services => services.AddSingleton<IOperatorActivitySchedulingPolicy, AllowOperatorSchedulingPolicy>(), "WorkflowTerminal");
    }

    [Fact]
    public async Task Reschedule_activity_api_runs_through_capture_actor_checkpoint_and_replay_keeps_one_continuation()
    {
        await using var host = await AlterationApiHost.StartAsync();
        var source = await SeedRescheduleWorkflowAsync(host.Services, "reschedule-success", ActivityExecutionStatus.Scheduled);
        host.Authorize(PermissionNames.WorkflowRuntimeManage, "tenant-a");
        var body = RescheduleBody("reschedule-success", source.Execution.ActivityExecutionId);

        var planId = await SubmitPlanAsync(host, body, "reschedule-success");
        Assert.Equal(planId, await SubmitPlanAsync(host, body, "reschedule-success"));
        await CaptureAndDispatchAsync(host.Services, planId);
        await WaitForTerminalJobsAsync(host.Services, planId, 1);

        using (var scope = host.Services.CreateScope())
        {
            var activities = await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("reschedule-success");
            var superseded = Assert.Single(activities, activity => activity.Execution.ActivityExecutionId == source.Execution.ActivityExecutionId);
            var successor = Assert.Single(activities, activity => activity.Execution.ActivityExecutionId != source.Execution.ActivityExecutionId);
            Assert.Equal(ActivityExecutionStatus.Superseded, superseded.Status);
            Assert.Equal(successor.Execution.ActivityExecutionId, superseded.SupersededByActivityExecutionId);
            Assert.Equal(source.Execution.ActivityExecutionId, successor.Metadata["runtime.supersedesActivityExecutionId"]);
            var continuation = await DeliverSingleSchedulerContinuationAsync(scope.ServiceProvider, "reschedule-success");
            Assert.Equal(WorkflowExecutionCommandKind.ScheduleActivity, continuation.CommandKind);
        }

        host.Authorize(PermissionNames.WorkflowRuntimeRead, "tenant-a");
        using var planRead = await host.Client.GetAsync($"/runtime/workflows/alteration-plans/{planId}");
        using var jobRead = await host.Client.GetAsync($"/runtime/workflows/alteration-plans/{planId}/jobs/{(await ReadOnlyJobIdAsync(host.Services, planId))}");
        var response = (await planRead.Content.ReadAsStringAsync()) + (await jobRead.Content.ReadAsStringAsync());
        Assert.DoesNotContain("sourceActivityExecutionId", response, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payload", response, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reschedule_activity_api_rejects_ineligible_blocking_and_claimed_sources_with_safe_outcomes()
    {
        await AssertRescheduleFailureAsync("reschedule-completed", ActivityExecutionStatus.Completed, "SourceActivityNotReschedulable");
        await AssertRescheduleFailureAsync("reschedule-blocked", ActivityExecutionStatus.Faulted, "BlockingIncident", async (services, source) =>
        {
            using var scope = services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IIncidentStateStore>().SaveAsync(new IncidentState(
                "blocking-incident", source.Execution.WorkflowExecutionId, source.Execution.ActivityExecutionId, source.Execution.ExecutableNodeId,
                IncidentSeverity.Error, IncidentStatus.Blocking, null, "TestFailure", "The activity is blocked.", DateTimeOffset.UtcNow, null));
        });
        await AssertRescheduleFailureAsync("reschedule-claimed", ActivityExecutionStatus.Scheduled, "SourceWorkClaimed", async (services, source) =>
        {
            using var scope = services.CreateScope();
            var queue = scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>();
            var now = DateTimeOffset.UtcNow;
            await queue.EnqueueAsync(new RuntimeSchedulerWorkItem("claimed-work", source.Execution.WorkflowExecutionId, "claimed-command", WorkflowExecutionCommandKind.ScheduleActivity,
                "claimed-envelope", "claimed-key", now, now, executionScopeId: source.Execution.ActivityExecutionId));
            Assert.NotNull(await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(source.Execution.WorkflowExecutionId, "claimer", now, TimeSpan.FromMinutes(1))));
        });
    }

    private static async Task AssertModificationFailureAsync(
        string name,
        bool rootFrame,
        ValueTypeDescriptor type,
        ValueProtectionPolicy policy,
        string variableKey,
        JsonElement value,
        string expectedCode,
        Func<IServiceProvider, Task>? afterCapture = null)
    {
        await using var host = await AlterationApiHost.StartAsync();
        var executionId = $"variable-{name}";
        await SeedVariableWorkflowAsync(host.Services, executionId, rootFrame, type, policy);
        host.Authorize(PermissionNames.WorkflowRuntimeManage, "tenant-a");
        var body = new
        {
            target = new { executionIds = new[] { executionId } },
            alterations = new[] { new { kind = "ModifyVariable", schemaVersion = 1, payload = new { variableKey, value } } }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/runtime/workflows/alteration-plans") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", $"modify-{name}");
        using var accepted = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var planId = (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("planId").GetString()!;
        using (var scope = host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<WorkflowAlterationTargetCaptureTask>().CaptureNextAsync(planId, 100);
        if (afterCapture is not null)
            await afterCapture(host.Services);
        using (var scope = host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<WorkflowAlterationJobTask>().DispatchAvailableAsync(planId, "api-test", 10, TimeSpan.FromMinutes(1));
        await WaitForTerminalJobsAsync(host.Services, planId, 1);

        var jobId = await ReadOnlyJobIdAsync(host.Services, planId);
        using (var scope = host.Services.CreateScope())
        {
            var job = (await scope.ServiceProvider.GetRequiredService<IWorkflowAlterationStore>().FindJobAsync(jobId))!;
            Assert.Equal(WorkflowAlterationJobStatus.Failed, job.Status);
            Assert.Equal(expectedCode, Assert.Single(job.Outcomes).Code);
        }
        host.Authorize(PermissionNames.WorkflowRuntimeRead, "tenant-a");
        using var planRead = await host.Client.GetAsync($"/runtime/workflows/alteration-plans/{planId}");
        using var jobRead = await host.Client.GetAsync($"/runtime/workflows/alteration-plans/{planId}/jobs/{jobId}");
        var response = (await planRead.Content.ReadAsStringAsync()) + (await jobRead.Content.ReadAsStringAsync());
        Assert.DoesNotContain("replacement-secret", response, StringComparison.Ordinal);
        Assert.DoesNotContain("\"value\"", response, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"payload\"", response, StringComparison.OrdinalIgnoreCase);
    }

    private static object ScheduleBody(string executionId, string nodeId, string parentActivityExecutionId) => new
    {
        target = new { executionIds = new[] { executionId } },
        alterations = new[] { new { kind = "ScheduleActivity", schemaVersion = 1, payload = new { nodeId, parentActivityExecutionId } } }
    };

    private static object RescheduleBody(string executionId, string sourceActivityExecutionId) => new
    {
        target = new { executionIds = new[] { executionId } },
        alterations = new[] { new { kind = "RescheduleActivity", schemaVersion = 1, payload = new { sourceActivityExecutionId } } }
    };

    private static async Task<string> SubmitPlanAsync(AlterationApiHost host, object body, string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/runtime/workflows/alteration-plans") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("planId").GetString()!;
    }

    private static async Task CaptureAndDispatchAsync(IServiceProvider services, string planId)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<WorkflowAlterationTargetCaptureTask>().CaptureNextAsync(planId, 100);
        await scope.ServiceProvider.GetRequiredService<WorkflowAlterationJobTask>().DispatchAvailableAsync(planId, "api-test", 100, TimeSpan.FromMinutes(1));
    }

    // Alteration actors commit continuations to the durable post-commit outbox. The normal scheduler drain owns
    // delivery to the work queue, so API tests explicitly run that delivery step rather than assuming actor dispatch
    // synchronously mutates the queue.
    private static async Task<RuntimeSchedulerWorkItem> DeliverSingleSchedulerContinuationAsync(IServiceProvider services, string workflowExecutionId)
    {
        var outbox = services.GetRequiredService<IRuntimePostCommitOutboxStore>();
        var pending = await outbox.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(
            DateTimeOffset.UtcNow,
            limit: 10,
            workflowExecutionId: workflowExecutionId,
            intentKind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork));
        var item = Assert.Single(pending);
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, item.Status);

        var delivery = await services.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(
            new RuntimePostCommitOutboxProcessRequest(
                limit: 10,
                workflowExecutionId: workflowExecutionId,
                intentKind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork));
        Assert.Equal(1, delivery.DeliveredCount);
        var queued = await services.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAsync(new RuntimeSchedulerWorkQuery(workflowExecutionId, 10));
        return Assert.Single(queued.Items);
    }

    private static async Task AssertScheduleFailureAsync(
        string executionId,
        bool withCapability,
        WorkflowExecutionStatus status,
        Action<IServiceCollection>? configure,
        string expectedCode)
    {
        await using var host = await AlterationApiHost.StartAsync(configure);
        await SeedScheduleWorkflowAsync(host.Services, executionId, withCapability, status);
        host.Authorize(PermissionNames.WorkflowRuntimeManage, "tenant-a");
        var planId = await SubmitPlanAsync(host, ScheduleBody(executionId, "child", "parent-exec"), executionId);
        await CaptureAndDispatchAsync(host.Services, planId);
        await WaitForTerminalJobsAsync(host.Services, planId, 1);

        var jobId = await ReadOnlyJobIdAsync(host.Services, planId);
        using (var scope = host.Services.CreateScope())
        {
            var job = (await scope.ServiceProvider.GetRequiredService<IWorkflowAlterationStore>().FindJobAsync(jobId))!;
            Assert.Equal(WorkflowAlterationJobStatus.Failed, job.Status);
            Assert.Equal(expectedCode, Assert.Single(job.Outcomes).Code);
        }

        host.Authorize(PermissionNames.WorkflowRuntimeRead, "tenant-a");
        using var planRead = await host.Client.GetAsync($"/runtime/workflows/alteration-plans/{planId}");
        using var jobRead = await host.Client.GetAsync($"/runtime/workflows/alteration-plans/{planId}/jobs/{jobId}");
        var response = (await planRead.Content.ReadAsStringAsync()) + (await jobRead.Content.ReadAsStringAsync());
        Assert.DoesNotContain("parentActivityExecutionId", response, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payload", response, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AssertRescheduleFailureAsync(
        string executionId,
        ActivityExecutionStatus sourceStatus,
        string expectedCode,
        Func<IServiceProvider, ActivityExecutionState, Task>? afterCapture = null)
    {
        await using var host = await AlterationApiHost.StartAsync();
        var source = await SeedRescheduleWorkflowAsync(host.Services, executionId, sourceStatus);
        host.Authorize(PermissionNames.WorkflowRuntimeManage, "tenant-a");
        var planId = await SubmitPlanAsync(host, RescheduleBody(executionId, source.Execution.ActivityExecutionId), executionId);
        using (var scope = host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<WorkflowAlterationTargetCaptureTask>().CaptureNextAsync(planId, 100);
        if (afterCapture is not null)
            await afterCapture(host.Services, source);
        using (var scope = host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<WorkflowAlterationJobTask>().DispatchAvailableAsync(planId, "api-test", 100, TimeSpan.FromMinutes(1));
        await WaitForTerminalJobsAsync(host.Services, planId, 1);

        var jobId = await ReadOnlyJobIdAsync(host.Services, planId);
        using (var scope = host.Services.CreateScope())
        {
            var job = (await scope.ServiceProvider.GetRequiredService<IWorkflowAlterationStore>().FindJobAsync(jobId))!;
            Assert.Equal(WorkflowAlterationJobStatus.Failed, job.Status);
            Assert.Equal(expectedCode, Assert.Single(job.Outcomes).Code);
        }

        host.Authorize(PermissionNames.WorkflowRuntimeRead, "tenant-a");
        using var planRead = await host.Client.GetAsync($"/runtime/workflows/alteration-plans/{planId}");
        using var jobRead = await host.Client.GetAsync($"/runtime/workflows/alteration-plans/{planId}/jobs/{jobId}");
        var response = (await planRead.Content.ReadAsStringAsync()) + (await jobRead.Content.ReadAsStringAsync());
        Assert.DoesNotContain("sourceActivityExecutionId", response, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payload", response, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task SeedScheduleWorkflowAsync(
        IServiceProvider services,
        string executionId,
        bool withCapability,
        WorkflowExecutionStatus workflowStatus)
    {
        var identity = new WorkflowExecutableIdentity($"artifact-{executionId}", "definition", "version", "1", "hash");
        var child = new ExecutableNode("child", "child", "test", "1", "consumer", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
        var capability = withCapability ? new OperatorActivitySchedulingCapability(AllowOperatorSchedulingPolicy.Key, 1, JsonSerializer.SerializeToElement(new { })) : null;
        var parent = new ExecutableNode("parent", "parent", "test", "1", "consumer", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), [new ExecutableChildSlot("children", [child], capability)]);
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(new WorkflowExecutable(
            identity, parent, new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UtcNow, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference));
        await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(new WorkflowExecutionState(
            executionId, identity, workflowStatus, null, DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow, null, null, null, "tenant-a", new Dictionary<string, string>())
        {
            Partition = new WorkflowExecutionPartition("tenant-a"),
            Authority = new WorkflowExecutionAuthoritySnapshot("runtime-api", "runtime-api")
        });
        await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>().SaveAsync(ParentActivity(executionId));
    }

    private static async Task<ActivityExecutionState> SeedRescheduleWorkflowAsync(
        IServiceProvider services,
        string executionId,
        ActivityExecutionStatus sourceStatus)
    {
        var identity = new WorkflowExecutableIdentity($"artifact-{executionId}", "definition", "version", "1", "hash");
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(new WorkflowExecutable(
            identity,
            new ExecutableNode("node", "node", "test", "1", "consumer", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
            new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UtcNow, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference));
        await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(new WorkflowExecutionState(
            executionId, identity, WorkflowExecutionStatus.Running, null, DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow, null, null, null, "tenant-a", new Dictionary<string, string>())
        {
            Partition = new WorkflowExecutionPartition("tenant-a"),
            Authority = new WorkflowExecutionAuthoritySnapshot("runtime-api", "runtime-api")
        });
        var source = SourceActivity(executionId, sourceStatus);
        await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>().SaveAsync(source);
        return source;
    }

    private static ActivityExecutionState ParentActivity(string workflowExecutionId) => new(
        new ActivityExecution("parent-exec", workflowExecutionId, "parent", "parent", "test", "1"),
        ActivityExecutionStatus.Running, null, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, null, null, null,
        ActivitySchedulingProvenance.From(workflowExecutionId, null, null, null, null, null, "parent-exec", "test"), null, [], [], 0, 0, new Dictionary<string, string>())
    {
        ExecutionScopeId = "parent-exec"
    };

    private static ActivityExecutionState SourceActivity(string workflowExecutionId, ActivityExecutionStatus status) => new(
        new ActivityExecution("source-exec", workflowExecutionId, "node", "node", "test", "1"),
        status, null, 1, DateTimeOffset.UtcNow, null, null, null, null, null, null,
        ActivitySchedulingProvenance.From(workflowExecutionId, null, null, null, null, null, "source-exec", "test"), null, [], [], 0, 0, new Dictionary<string, string>())
    {
        ExecutionScopeId = "source-exec"
    };

    private static async Task SeedWorkflowAsync(IServiceProvider services, string executionId)
    {
        using var scope = services.CreateScope();
        var identity = new WorkflowExecutableIdentity($"artifact-{executionId}", "definition", "version", "1", "hash");
        await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(MigrationExecutable(identity));
        await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(new WorkflowExecutionState(
            executionId,
            identity,
            WorkflowExecutionStatus.Running,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            "tenant-a",
            new Dictionary<string, string>())
        {
            Partition = new WorkflowExecutionPartition("tenant-a"),
            Authority = new WorkflowExecutionAuthoritySnapshot("runtime-api", "runtime-api")
        });
    }

    private static async Task AssertMigrationFailureAsync(
        string executionId,
        Func<IServiceProvider, WorkflowExecutableIdentity, Task> mutateTarget,
        string expectedCode)
    {
        await using var host = await AlterationApiHost.StartAsync();
        var identities = await SeedMigratableWorkflowAsync(host.Services, executionId);
        host.Authorize(PermissionNames.WorkflowRuntimeManage, "tenant-a");
        var body = new
        {
            target = new { executionIds = new[] { executionId } },
            alterations = new[] { new { kind = "Migrate", schemaVersion = 1, payload = new { targetArtifact = new
            {
                artifactId = identities.Target.ArtifactId,
                definitionId = identities.Target.DefinitionId,
                definitionVersionId = identities.Target.DefinitionVersionId,
                artifactVersion = identities.Target.ArtifactVersion,
                artifactHash = identities.Target.ArtifactHash
            } } } }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/runtime/workflows/alteration-plans") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", $"{executionId}-key");
        using var accepted = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var planId = (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("planId").GetString()!;
        using (var scope = host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<WorkflowAlterationTargetCaptureTask>().CaptureNextAsync(planId, 100);
        await mutateTarget(host.Services, identities.Target);
        using (var scope = host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<WorkflowAlterationJobTask>().DispatchAvailableAsync(planId, "api-test", 10, TimeSpan.FromMinutes(1));
        await WaitForTerminalJobsAsync(host.Services, planId, 1);

        using var verificationScope = host.Services.CreateScope();
        var workflow = await verificationScope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(executionId);
        Assert.Equal(identities.Source, workflow!.PinnedExecutable);
        var job = Assert.Single((await verificationScope.ServiceProvider.GetRequiredService<IWorkflowAlterationStore>().PageJobsAsync(planId, 10, null)).Items);
        Assert.Equal(WorkflowAlterationJobStatus.Failed, job.Status);
        Assert.Equal(expectedCode, Assert.Single(job.Outcomes).Code);
    }

    private static async Task<(WorkflowExecutableIdentity Source, WorkflowExecutableIdentity Target)> SeedMigratableWorkflowAsync(IServiceProvider services, string executionId)
    {
        var source = new WorkflowExecutableIdentity($"{executionId}-source", "definition", "source-version", "1.0.0", $"sha256:{executionId}:source");
        var target = new WorkflowExecutableIdentity($"{executionId}-target", "definition", "target-version", "2.0.0", $"sha256:{executionId}:target");
        using var scope = services.CreateScope();
        var executables = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>();
        await executables.SaveAsync(MigrationExecutable(source));
        await executables.SaveAsync(MigrationExecutable(target));
        await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(new WorkflowExecutableSourceReference(
            "migration-target-ref", target.ArtifactId, "published", "target", null, target.DefinitionId, target.DefinitionVersionId,
            target.ArtifactVersion, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, WorkflowExecutableReferenceScope.Published, TenantId: "tenant-a"));
        await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(new WorkflowExecutionState(
            executionId, source, WorkflowExecutionStatus.Suspended, null, DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow,
            null, null, null, "tenant-a", new Dictionary<string, string>())
        {
            Partition = new WorkflowExecutionPartition("tenant-a"),
            Authority = new WorkflowExecutionAuthoritySnapshot("runtime-api", "runtime-api"),
            PinnedSource = new WorkflowExecutableSourceProvenance("migration-source-ref", "published", "source", null, source.DefinitionId, source.DefinitionVersionId, source.ArtifactVersion, null, null)
        });
        return (source, target);
    }

    private static WorkflowExecutable MigrationExecutable(WorkflowExecutableIdentity identity) => new(
        identity,
        new ExecutableNode("root", "root", "test", "1", "test", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
        new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UtcNow, new Dictionary<string, string>(), inputContract: null, dependencies: null,
        runtimeRequirements: null, storageDriverRequirements: null, IncidentStrategyBuiltIns.FaultReference);

    private static async Task SeedVariableWorkflowAsync(IServiceProvider services, string executionId, bool rootFrame, ValueTypeDescriptor type, ValueProtectionPolicy? policy = null)
    {
        var identity = new WorkflowExecutableIdentity($"artifact-{executionId}", "definition", "version", "1", "hash");
        using var scope = services.CreateScope();
        var executable = new WorkflowExecutable(
            identity,
            new ExecutableNode("root", "root", "test", "1", "test", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>(),
            inputContract: null,
            dependencies: null,
            runtimeRequirements: null,
            storageDriverRequirements: null,
            IncidentStrategyBuiltIns.FaultReference,
            workflowVariables: [new RuntimeVariableDeclaration("stable-name", "VisibleName", type, policy ?? ValueProtectionPolicy.InstanceInline)]);
        await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
        var workflow = new WorkflowExecutionState(executionId, identity, WorkflowExecutionStatus.Running, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, null, null, null, "tenant-a", new Dictionary<string, string>())
        {
            Partition = new WorkflowExecutionPartition("tenant-a"),
            Authority = new WorkflowExecutionAuthoritySnapshot("runtime-api", "runtime-api")
        };
        if (rootFrame)
        {
            workflow = workflow with
            {
                RootVariableFrame = new VariableFrameState("root-frame", "workflow", executionId, null, VariableFrameKind.Root,
                    new Dictionary<string, ValueEnvelope> { ["stable-name"] = ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement("before"), policy ?? ValueProtectionPolicy.InstanceInline) })
            };
        }
        await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(workflow);
    }

    private static async Task<string> ReadOnlyJobIdAsync(IServiceProvider services, string planId)
    {
        using var scope = services.CreateScope();
        return Assert.Single((await scope.ServiceProvider.GetRequiredService<IWorkflowAlterationStore>().PageJobsAsync(planId, 10, null)).Items).JobId;
    }

    private static async Task WaitForTerminalJobsAsync(IServiceProvider services, string planId, int expectedCount)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            using var scope = services.CreateScope();
            var page = await scope.ServiceProvider.GetRequiredService<IWorkflowAlterationStore>().PageJobsAsync(planId, 100, null);
            if (page.Items.Count == expectedCount && page.Items.All(job => job.Status is WorkflowAlterationJobStatus.Succeeded or WorkflowAlterationJobStatus.Failed))
                return;
            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException("Alteration jobs did not reach a terminal actor-backed state.");
    }

    private sealed class AlterationApiHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        private const string PermissionHeader = "X-Test-Permission";
        private const string TenantHeader = "X-Test-Tenant";
        private const string PermissionClaim = "permissions";
        public HttpClient Client { get; } = client;
        public IServiceProvider Services => app.Services;

        public static async Task<AlterationApiHost> StartAsync(Action<IServiceCollection>? configureServices = null)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();
            new WorkflowsRuntimeApiFeature().ConfigureServices(builder.Services);
            configureServices?.Invoke(builder.Services);
            builder.Services.AddScoped<IRequestSender, AlterationRequestSender>();
            builder.Services.AddFastEndpoints(options =>
            {
                options.Assemblies = [typeof(WorkflowsRuntimeApiFeature).Assembly];
                options.Filter = type => type.FullName is
                    "Elsa.Workflows.Runtime.Api.Endpoints.Alterations.SubmitWorkflowAlterationPlanEndpoint" or
                    "Elsa.Workflows.Runtime.Api.Endpoints.Alterations.GetWorkflowAlterationPlanEndpoint" or
                    "Elsa.Workflows.Runtime.Api.Endpoints.Alterations.PageWorkflowAlterationJobsEndpoint" or
                    "Elsa.Workflows.Runtime.Api.Endpoints.Alterations.GetWorkflowAlterationJobEndpoint" or
                    "Elsa.Workflows.Runtime.Api.Endpoints.Alterations.CancelWorkflowAlterationPlanEndpoint";
            });
            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                if (context.Request.Headers.TryGetValue(TenantHeader, out var tenant) && !string.IsNullOrWhiteSpace(tenant))
                    context.RequestServices.GetRequiredService<IPersistenceAccessContextBinder>()
                        .Bind(PersistenceAccessContext.Scoped(new PersistenceScope(tenant.ToString())));
                await next();
            });
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseFastEndpoints(options => options.Security.PermissionsClaimType = PermissionClaim);
            await app.StartAsync();
            return new(app, app.GetTestClient());
        }

        public void Authorize(string permission, string tenant)
        {
            Client.DefaultRequestHeaders.Remove(PermissionHeader);
            Client.DefaultRequestHeaders.Remove(TenantHeader);
            Client.DefaultRequestHeaders.Add(PermissionHeader, permission);
            Client.DefaultRequestHeaders.Add(TenantHeader, tenant);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }

    private sealed class AlterationRequestSender(IServiceProvider services) : IRequestSender
    {
        public async Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull
        {
            object response = request switch
            {
                SubmitWorkflowAlterationPlan submit => await new SubmitWorkflowAlterationPlanRequestHandler(
                    services.GetRequiredService<Elsa.Workflows.Runtime.Services.Alterations.WorkflowAlterationPlanService>(),
                    services.GetRequiredService<AlterationContracts.IWorkflowAlterationAdmissionGate>(),
                    services.GetRequiredService<AlterationContracts.IWorkflowAlterationRequestContext>()).Handle(submit, cancellationToken),
                GetWorkflowAlterationPlan get => await new GetWorkflowAlterationPlanRequestHandler(
                    services.GetRequiredService<IWorkflowAlterationStore>(),
                    services.GetRequiredService<AlterationContracts.IWorkflowAlterationRequestContext>()).Handle(get, cancellationToken),
                PageWorkflowAlterationJobs page => await new PageWorkflowAlterationJobsRequestHandler(
                    services.GetRequiredService<IWorkflowAlterationStore>(),
                    services.GetRequiredService<AlterationContracts.IWorkflowAlterationRequestContext>()).Handle(page, cancellationToken),
                GetWorkflowAlterationJob getJob => await new GetWorkflowAlterationJobRequestHandler(
                    services.GetRequiredService<IWorkflowAlterationStore>(),
                    services.GetRequiredService<AlterationContracts.IWorkflowAlterationRequestContext>()).Handle(getJob, cancellationToken),
                CancelWorkflowAlterationPlan cancel => await new CancelWorkflowAlterationPlanRequestHandler(
                    services.GetRequiredService<IWorkflowAlterationStore>(),
                    services.GetRequiredService<Elsa.Workflows.Runtime.Services.Alterations.WorkflowAlterationPlanService>(),
                    services.GetRequiredService<AlterationContracts.IWorkflowAlterationRequestContext>()).Handle(cancel, cancellationToken),
                _ => throw new NotSupportedException($"Unexpected alteration request '{request.GetType().FullName}'.")
            };
            return (T)response;
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "AlterationApiTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Permission", out var permission) || string.IsNullOrWhiteSpace(permission))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "operator-1"), new Claim("permissions", permission.ToString())],
                SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    private sealed class AllowOperatorSchedulingPolicy : IOperatorActivitySchedulingPolicy
    {
        public const string Key = "api-test.operator-schedule";
        public string PolicyKey => Key;
        public int SchemaVersion => 1;

        public OperatorActivitySchedulingPolicyDecision Evaluate(OperatorActivitySchedulingPolicyContext context) =>
            OperatorActivitySchedulingPolicyDecision.Accepted(ActivitySchedulingProvenance.From(
                context.Workflow.WorkflowExecutionId,
                context.Parent.Execution.ActivityExecutionId,
                context.Parent.Execution.ActivityExecutionId,
                null,
                null,
                null,
                context.Parent.ExecutionScopeId ?? context.Parent.Execution.ActivityExecutionId,
                "OperatorScheduleActivity"));
    }
}
