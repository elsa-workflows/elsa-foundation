using System.Text.Json;
using Elsa.Secrets.Workflows.Tests.Support;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Secrets.Workflows.Tests.Support.SecretsWorkflowHost;

namespace Elsa.Secrets.Workflows.Tests;

/// <summary>
/// The tenant a secret is resolved under is the executing instance's own tenant on every path that activates a CLR
/// activity (spec 188, T093; research R2 and R3a): invoke, bookmark resume, structural parent evaluation, child-completion
/// re-materialization and operator reschedule. Two tenants run, one host each, over one secret store that holds a secret
/// of the same name for both, because on this background drain path a host's work items run under the host's
/// persistence scope (see <see cref="SecretsWorkflowHost"/>); every request the bridge hands the Secrets module carries
/// the instance's tenant. An
/// instance whose recorded tenant differs from the partition it runs under is refused with <c>TenantMismatch</c> before
/// any secret is read.
/// </summary>
/// <remarks>
/// The hosts run on the in-memory runtime stores, which keep an instance whose tenant disagrees with its partition: the
/// EF stores refuse to store one at all, which the last test shows.
/// </remarks>
public sealed class TenantAgreementTests : IAsyncLifetime
{
    private SecretsWorkflowHost _alpha = null!;
    private SecretsWorkflowHost _beta = null!;

    public async Task InitializeAsync()
    {
        _alpha = await CreateAsync(AlphaTenant, SecretsWorkflowHostStore.InMemory);
        _beta = await CreateAsync(BetaTenant, SecretsWorkflowHostStore.InMemory, sharedSecrets: _alpha);
        await _alpha.CreateSecretAsync(AlphaTenant, ReferenceName, ValueOf(AlphaTenant));
        await _alpha.CreateSecretAsync(BetaTenant, ReferenceName, ValueOf(BetaTenant));
    }

    public async Task DisposeAsync()
    {
        await _beta.DisposeAsync();
        await _alpha.DisposeAsync();
    }

    [Fact]
    public Task Invoke_resolves_under_the_instance_tenant() =>
        AssertEachTenantResolvesUnderItsInstanceTenantAsync(
            typeof(SecretReadingActivity),
            (_, _) => Task.CompletedTask,
            resolutions: 1,
            SecretValueRecorder.Execute);

    [Fact]
    public Task Bookmark_resume_resolves_under_the_instance_tenant() =>
        AssertEachTenantResolvesUnderItsInstanceTenantAsync(
            typeof(SecretWaitingActivity),
            (host, workflowExecutionId) => host.ResumeAsync(workflowExecutionId),
            resolutions: 2,
            SecretValueRecorder.Execute,
            SecretValueRecorder.Resume);

    [Fact]
    public Task Structural_parent_evaluation_resolves_under_the_instance_tenant() =>
        AssertEachTenantResolvesUnderItsInstanceTenantAsync(
            typeof(SecretReadingParentActivity),
            (_, _) => Task.CompletedTask,
            resolutions: 2,
            SecretValueRecorder.Execute,
            SecretValueRecorder.ChildCompleted);

    [Fact]
    public Task Child_completion_rematerialization_resolves_under_the_instance_tenant() =>
        // The child-completion evaluation activates the parent on its committed snapshot, then again on the
        // re-materialized one, whose activity runs the callback: three resolutions for two recorded steps.
        AssertEachTenantResolvesUnderItsInstanceTenantAsync(
            typeof(SecretRematerializingParentActivity),
            (_, _) => Task.CompletedTask,
            resolutions: 3,
            SecretValueRecorder.Execute,
            SecretValueRecorder.ChildCompleted);

    [Fact]
    public Task Operator_reschedule_resolves_under_the_instance_tenant() =>
        AssertEachTenantResolvesUnderItsInstanceTenantAsync(
            typeof(SecretWaitingActivity),
            RescheduleAsync,
            resolutions: 2,
            SecretValueRecorder.Execute,
            SecretValueRecorder.Execute);

    [Fact]
    public async Task An_instance_whose_tenant_differs_from_its_partition_faults_with_TenantMismatch_before_any_secret_is_read()
    {
        var run = await _alpha.RunSecretWorkflowAsync(typeof(SecretReadingActivity), "wfexec-mismatch", instanceTenantId: BetaTenant);

        Assert.Equal(BetaTenant, run.WorkflowState!.TenantId);
        var fault = Assert.IsType<NormalizedActivityFault>(run.State(NodeId).Fault);
        Assert.Equal(RuntimeSecretResolutionException.TenantMismatch, fault.Code);
        Assert.False(fault.IsRetryable);
        Assert.Equal($"Secret '{ReferenceName}' could not be resolved ({RuntimeSecretResolutionException.TenantMismatch}).", fault.Message);
        Assert.Empty(_alpha.Resolutions.Requests);
        Assert.Empty(_alpha.Values.For("wfexec-mismatch"));
    }

    [Fact]
    public async Task The_EF_store_refuses_an_instance_whose_tenant_differs_from_its_partition_before_activation()
    {
        await using var host = await CreateAsync(AlphaTenant, SecretsWorkflowHostStore.Sqlite, sharedSecrets: _alpha);
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.RunSecretWorkflowAsync(typeof(SecretReadingActivity), "wfexec-ef-mismatch", instanceTenantId: BetaTenant));

        // The store refuses to save the instance into a scope its tenant does not name, so nothing runs to be refused later.
        Assert.Contains("does not belong to the current persistence scope", refusal.Message, StringComparison.Ordinal);
        var run = await host.ReadRunAsync("wfexec-ef-mismatch");
        Assert.Null(run.WorkflowState);
        Assert.Empty(run.ActivityStates);
        Assert.Empty(host.Resolutions.Requests);
    }

    private static string ValueOf(string tenantId) => $"{tenantId}-value";

    /// <summary>
    /// Runs <paramref name="activityType"/>'s workflow once per tenant, each on its own host, drives it on with
    /// <paramref name="drive"/>, and asserts that every resolution carried the instance's tenant and that the activity
    /// read that tenant's own value at each of <paramref name="steps"/>.
    /// </summary>
    private async Task AssertEachTenantResolvesUnderItsInstanceTenantAsync(
        Type activityType,
        Func<SecretsWorkflowHost, string, Task> drive,
        int resolutions,
        params string[] steps)
    {
        foreach (var host in new[] { _alpha, _beta })
        {
            var workflowExecutionId = $"wfexec-{host.TenantId}";
            await host.RunSecretWorkflowAsync(activityType, workflowExecutionId);

            await drive(host, workflowExecutionId);

            var instanceTenantId = (await host.ReadRunAsync(workflowExecutionId)).WorkflowState!.TenantId;
            Assert.Equal(host.TenantId, instanceTenantId);
            Assert.Equal(Enumerable.Repeat(instanceTenantId, resolutions), host.Resolutions.Requests.Select(request => request.TenantId));
            Assert.Equal(steps.Select(step => (step, (string?)ValueOf(host.TenantId))), host.Values.For(workflowExecutionId));
        }
    }

    /// <summary>
    /// Reschedules the suspended activity as an operator alteration does, then delivers the successor's continuation
    /// through the scheduler work handlers: the successor's invoke activates it again.
    /// </summary>
    private static async Task RescheduleAsync(SecretsWorkflowHost host, string workflowExecutionId)
    {
        var source = (await host.ReadRunAsync(workflowExecutionId)).State(NodeId);
        Assert.Equal(ActivityExecutionStatus.Suspended, source.Status);
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var workflow = (await services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(workflowExecutionId))!;
        var staging = new Staging();

        var preflight = await services.GetRequiredService<RescheduleActivityAlterationHandler>().PreflightAsync(
            new WorkflowAlterationPreflightContext(
                new WorkflowAlterationJobState("job", "plan", workflowExecutionId, host.TenantId, 0, WorkflowAlterationJobStatus.Running,
                    new WorkflowAlterationJobClaim("worker", "claim", DateTimeOffset.MaxValue), 1, [], null, null, DateTimeOffset.UnixEpoch, null, null, 0),
                new WorkflowAlterationEnvelope("RescheduleActivity", 1, JsonSerializer.SerializeToElement(new { sourceActivityExecutionId = source.Execution.ActivityExecutionId })),
                0,
                new WorkflowAlterationProjectedState([workflow, new WorkflowAlterationActorCommandExecutor.WorkflowAlterationActivityExecutionProjection([source])]),
                staging));

        Assert.True(preflight.IsAccepted, preflight.Failure?.Message);
        var builder = new WorkflowAlterationRuntimeCheckpointStateBuilder(workflow);
        Assert.IsAssignableFrom<IWorkflowAlterationRuntimeCheckpointStagedChange>(Assert.Single(staging.StagedChanges)).Apply(builder);
        var activityStates = services.GetRequiredService<IActivityExecutionStateStore>();
        foreach (var change in builder.ActivityExecutions)
            await activityStates.SaveAsync(change.State);

        var handlers = services.GetServices<IWorkflowSchedulerWorkHandler>().OrderBy(handler => handler is IFallbackWorkflowSchedulerWorkHandler).ToArray();
        var queue = services.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        for (RuntimeSchedulerWorkItem? item = Assert.Single(builder.PostCommitIntents).MaterializedSchedulerWorkItem!; item is not null; item = await queue.DequeueAsync(workflowExecutionId))
            await handlers.First(handler => handler.CanHandle(item)).HandleAsync(item);
    }

    private sealed class Staging : IWorkflowAlterationStagingWorkspace
    {
        public List<IWorkflowAlterationStagedChange> StagedChanges { get; } = [];
        IReadOnlyList<IWorkflowAlterationStagedChange> IWorkflowAlterationStagingWorkspace.StagedChanges => StagedChanges;
        public void Stage(IWorkflowAlterationStagedChange change) => StagedChanges.Add(change);
    }
}
