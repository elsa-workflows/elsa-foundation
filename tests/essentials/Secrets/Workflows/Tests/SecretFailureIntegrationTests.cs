using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Workflows.Tests.Support;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Incidents;
using Xunit;
using static Elsa.Secrets.Workflows.Tests.Support.SecretsWorkflowHost;

namespace Elsa.Secrets.Workflows.Tests;

/// <summary>
/// A secret the Secrets module refuses faults the activity naming the reference and the code, with no value and no store
/// detail, retryable only when its store, the payload store or the secret repository, was unavailable, and permanent for
/// a repository row that cannot be served; a host without the bridge parks the activity instead (spec 188, T027, T037;
/// FR-003, SC-006, A05, A06).
/// </summary>
public sealed class SecretFailureIntegrationTests : IAsyncLifetime
{
    private const string Value = "alpha-secret-value";
    private SecretsWorkflowHost _host = null!;

    /// <summary>The state the referenced secret is put in before the run.</summary>
    public enum SecretState
    {
        Revoked,
        Expired,
        Deleted,
        Missing,
        TypeMismatched,
        PayloadStoreUnavailable,
        RepositoryUnavailable,
        RepositoryRowDamaged
    }

    public async Task InitializeAsync() => _host = await CreateAsync(AlphaTenant);

    public Task DisposeAsync() => _host.DisposeAsync().AsTask();

    public static TheoryData<SecretState, string, bool> Failures => new()
    {
        { SecretState.Revoked, "Revoked", false },
        { SecretState.Expired, "Expired", false },
        // The Secrets module reports a deleted secret as not found.
        { SecretState.Deleted, "NotFound", false },
        { SecretState.Missing, "NotFound", false },
        { SecretState.TypeMismatched, "TypeMismatch", false },
        { SecretState.PayloadStoreUnavailable, "StoreUnavailable", true },
        { SecretState.RepositoryUnavailable, "StoreUnavailable", true },
        { SecretState.RepositoryRowDamaged, "CorruptState", false }
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task A_refused_secret_faults_naming_the_reference_and_code_with_no_value_or_store_detail(SecretState state, string code, bool isRetryable)
    {
        var workflowExecutionId = $"wfexec-{state}";
        var typeName = await ArrangeAsync(state);

        var run = await _host.RunSecretWorkflowAsync(typeof(SecretReadingActivity), workflowExecutionId, Secret(typeName: typeName));

        var activity = run.State(NodeId);
        Assert.Equal(ActivityExecutionStatus.Faulted, activity.Status);
        var fault = Assert.IsType<NormalizedActivityFault>(activity.Fault);
        Assert.Equal(code, fault.Code);
        Assert.Equal(isRetryable, fault.IsRetryable);
        Assert.Equal(typeof(RuntimeSecretResolutionException).FullName, fault.ExceptionType);
        Assert.Equal($"Secret '{ReferenceName}' could not be resolved ({code}).", fault.Message);
        var incident = Assert.Single(await _host.ListIncidentsAsync(workflowExecutionId));
        Assert.Equal(fault.Message, incident.Message);
        await _host.AssertNotPersistedAsync(Value, UnavailableSecretStore.StoreDetail);
        Assert.Empty(_host.Values.For(workflowExecutionId));
    }

    [Fact]
    public async Task A_host_without_the_bridge_leaves_the_activity_waiting_with_an_activation_failure_incident()
    {
        await using var host = await CreateAsync(AlphaTenant, composeBridge: false);
        await host.CreateSecretAsync(AlphaTenant, ReferenceName, Value);

        var run = await host.RunSecretWorkflowAsync(typeof(SecretReadingActivity), "wfexec-no-bridge");

        await AssertParkedAsync(host, run, "wfexec-no-bridge", ActivityActivationFailureKind.MissingSecretResolver);
    }

    /// <summary>
    /// Asserts the activity waits with an activation-failure incident of <paramref name="kind"/>, no secret was read and
    /// none reached the activity.
    /// </summary>
    private static async Task AssertParkedAsync(SecretsWorkflowHost host, WorkflowExecutionRun run, string workflowExecutionId, ActivityActivationFailureKind kind)
    {
        var activity = run.State(NodeId);
        Assert.Equal(ActivityExecutionStatus.Waiting, activity.Status);
        Assert.Equal(ActivityActivationFailureHandler.IncidentFailureType, activity.SubStatus);
        Assert.Null(activity.Fault);
        var incident = Assert.Single(await host.ListIncidentsAsync(workflowExecutionId));
        Assert.Equal(ActivityActivationFailureHandler.IncidentFailureType, incident.FailureType);
        Assert.Equal(kind.ToString(), incident.Metadata[ActivityActivationFailureHandler.FailureKindMetadataKey]);
        Assert.Empty(host.Resolutions.Requests);
        Assert.Empty(host.Values.For(workflowExecutionId));
    }

    /// <summary>Puts the referenced secret in <paramref name="state"/>, and returns the type name the reference asks for.</summary>
    private async Task<string> ArrangeAsync(SecretState state)
    {
        switch (state)
        {
            case SecretState.Missing:
                return SecretTypeNames.Text;
            case SecretState.Expired:
                await _host.CreateSecretAsync(AlphaTenant, ReferenceName, Value, expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));
                return SecretTypeNames.Text;
            case SecretState.Revoked:
                await _host.CreateSecretAsync(AlphaTenant, ReferenceName, Value);
                await _host.RevokeSecretAsync(AlphaTenant, ReferenceName);
                return SecretTypeNames.Text;
            case SecretState.Deleted:
                await _host.CreateSecretAsync(AlphaTenant, ReferenceName, Value);
                await _host.DeleteSecretAsync(AlphaTenant, ReferenceName);
                return SecretTypeNames.Text;
            case SecretState.TypeMismatched:
                await _host.CreateSecretAsync(AlphaTenant, ReferenceName, Value);
                return SecretTypeNames.RsaKey;
            case SecretState.PayloadStoreUnavailable:
                await _host.SeedUnavailableSecretAsync(AlphaTenant, ReferenceName);
                return SecretTypeNames.Text;
            case SecretState.RepositoryUnavailable:
                await _host.CreateSecretAsync(AlphaTenant, ReferenceName, Value);
                _host.TakeSecretRepositoryOffline();
                return SecretTypeNames.Text;
            case SecretState.RepositoryRowDamaged:
                await _host.CreateSecretAsync(AlphaTenant, ReferenceName, Value);
                _host.DamageSecretRepository();
                return SecretTypeNames.Text;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, "No arrangement for this secret state.");
        }
    }
}
