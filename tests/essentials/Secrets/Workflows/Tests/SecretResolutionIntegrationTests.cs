using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Secrets.Workflows.Tests.Support;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using static Elsa.Secrets.Workflows.Tests.Support.SecretsWorkflowHost;

namespace Elsa.Secrets.Workflows.Tests;

/// <summary>
/// A <c>Secret</c> binding, compiled as publish compiles it, resolves end to end through the bridge and the Secrets module
/// on the embedded SQLite runtime host (spec 188, T026; FR-001, FR-002, FR-005; SC-002): the activity receives the
/// tenant's current value converted with the pinned plan, a rotation takes effect at the next activation without a
/// republish, and no persisted row of the run holds a resolved value.
/// </summary>
public sealed class SecretResolutionIntegrationTests : IAsyncLifetime
{
    private SecretsWorkflowHost _host = null!;

    public async Task InitializeAsync() => _host = await CreateAsync(AlphaTenant);

    public Task DisposeAsync() => _host.DisposeAsync().AsTask();

    [Fact]
    public async Task The_current_value_is_delivered_converted_with_the_pinned_plan_and_is_not_persisted()
    {
        await _host.CreateSecretAsync(AlphaTenant, ReferenceName, "alpha-v1");

        var run = await _host.RunSecretWorkflowAsync(typeof(SecretReadingActivity), "wfexec-delivered");

        run.AssertWorkflowCompleted();
        Assert.Equal([(SecretValueRecorder.Execute, "alpha-v1")], _host.Values.For("wfexec-delivered"));
        // The resolved text reached the String input through the plan publish pinned for it, carried by the withheld envelope.
        Assert.Equal(ValueConversionOperation.Identity, AssertReferenceWithheld(run).ConversionPlan!.Operation);
        await _host.AssertNotPersistedAsync("alpha-v1");
    }

    [Fact]
    public async Task A_rotation_between_runs_takes_effect_without_republishing()
    {
        await _host.CreateSecretAsync(AlphaTenant, ReferenceName, "alpha-v1");
        var executable = await _host.PublishSecretWorkflowAsync(typeof(SecretReadingActivity));
        (await _host.StartAsync(executable, "wfexec-before-rotation")).AssertWorkflowCompleted();

        await _host.RotateSecretAsync(AlphaTenant, ReferenceName, "alpha-v2");
        (await _host.StartAsync(executable, "wfexec-after-rotation")).AssertWorkflowCompleted();

        Assert.Equal([(SecretValueRecorder.Execute, "alpha-v1")], _host.Values.For("wfexec-before-rotation"));
        Assert.Equal([(SecretValueRecorder.Execute, "alpha-v2")], _host.Values.For("wfexec-after-rotation"));
    }

    [Fact]
    public async Task A_rotation_while_suspended_takes_effect_on_resume()
    {
        await _host.CreateSecretAsync(AlphaTenant, ReferenceName, "alpha-v1");
        var suspended = await _host.RunSecretWorkflowAsync(typeof(SecretWaitingActivity), "wfexec-suspended");
        Assert.Equal(ActivityExecutionStatus.Suspended, suspended.State(NodeId).Status);

        await _host.RotateSecretAsync(AlphaTenant, ReferenceName, "alpha-v2");
        var resumed = await _host.ResumeAsync("wfexec-suspended");

        resumed.AssertWorkflowCompleted();
        Assert.Equal(
            [(SecretValueRecorder.Execute, "alpha-v1"), (SecretValueRecorder.Resume, "alpha-v2")],
            _host.Values.For("wfexec-suspended"));
        AssertReferenceWithheld(resumed);
        await _host.AssertNotPersistedAsync("alpha-v1", "alpha-v2");
    }

    [Fact]
    public async Task Two_tenants_with_the_same_secret_name_each_resolve_their_own_value()
    {
        // One host per tenant over one secret store holding both tenants' secrets: on this background drain path a host's
        // work items run under the host's persistence scope (see SecretsWorkflowHost).
        await using var beta = await CreateAsync(BetaTenant, sharedSecrets: _host);
        await _host.CreateSecretAsync(AlphaTenant, ReferenceName, "alpha-value");
        await _host.CreateSecretAsync(BetaTenant, ReferenceName, "beta-value");

        (await _host.RunSecretWorkflowAsync(typeof(SecretReadingActivity), "wfexec-alpha")).AssertWorkflowCompleted();
        (await beta.RunSecretWorkflowAsync(typeof(SecretReadingActivity), "wfexec-beta")).AssertWorkflowCompleted();

        Assert.Equal([(SecretValueRecorder.Execute, "alpha-value")], _host.Values.For("wfexec-alpha"));
        Assert.Equal([(SecretValueRecorder.Execute, "beta-value")], beta.Values.For("wfexec-beta"));
        await _host.AssertNotPersistedAsync("alpha-value", "beta-value");
        await beta.AssertNotPersistedAsync("alpha-value", "beta-value");
    }

    [Fact]
    public async Task A_name_only_reference_resolves_within_the_tenant()
    {
        await _host.CreateSecretAsync(AlphaTenant, ReferenceName, "alpha-value");
        await _host.CreateSecretAsync(BetaTenant, ReferenceName, "beta-value");
        (await _host.RunSecretWorkflowAsync(typeof(SecretReadingActivity), "wfexec-name-only", Secret(typeName: null))).AssertWorkflowCompleted();

        Assert.Equal([(SecretValueRecorder.Execute, "alpha-value")], _host.Values.For("wfexec-name-only"));
        var request = Assert.Single(_host.Resolutions.Requests);
        Assert.Equal(AlphaTenant, request.TenantId);
        Assert.Null(request.Reference.TypeName);
        Assert.Null(request.Reference.Scope);
    }

    [Fact]
    public async Task A_single_tenant_host_resolves_the_secret_stored_under_the_default_tenant()
    {
        // A single-tenant host runs under the default persistence scope, which is the tenant the Secrets module's
        // single-tenant callers write under: no new tenant concept and no fallback.
        await using var host = await CreateAsync(PersistenceScope.DefaultValue);
        Assert.Equal(PersistenceScope.DefaultValue, WorkflowExecutionPartition.DefaultValue);
        await host.CreateSecretAsync(PersistenceScope.DefaultValue, ReferenceName, "single-tenant-value");
        (await host.RunSecretWorkflowAsync(typeof(SecretReadingActivity), "wfexec-single-tenant")).AssertWorkflowCompleted();

        Assert.Equal([(SecretValueRecorder.Execute, "single-tenant-value")], host.Values.For("wfexec-single-tenant"));
    }

    [Fact]
    public async Task A_secret_deleted_between_publish_and_run_faults_with_NotFound()
    {
        await _host.CreateSecretAsync(AlphaTenant, ReferenceName, "alpha-value");
        var executable = await _host.PublishSecretWorkflowAsync(typeof(SecretReadingActivity));

        await _host.DeleteSecretAsync(AlphaTenant, ReferenceName);
        var run = await _host.StartAsync(executable, "wfexec-deleted");

        var fault = Assert.IsType<NormalizedActivityFault>(run.State(NodeId).Fault);
        Assert.Equal("NotFound", fault.Code);
        Assert.False(fault.IsRetryable);
        Assert.Empty(_host.Values.For("wfexec-deleted"));
    }

    /// <summary>The persisted snapshot holds the withheld reference, which this returns.</summary>
    private static WithheldValue AssertReferenceWithheld(WorkflowExecutionRun run)
    {
        var envelope = run.State(NodeId).InputSnapshot!.Values[nameof(SecretReadingActivity.Text)];
        Assert.Equal(ValuePresence.Withheld, envelope.Presence);
        Assert.Equal(ReferenceName, envelope.WithheldValue!.Secret!.Name);
        return envelope.WithheldValue;
    }
}
