using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Services.Incidents;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class ActivityActivationFailureHandlerTests
{
    private readonly ActivityActivationFailureHandler _handler = new();

    [Fact]
    public void A_missing_secret_resolver_classifies_as_an_activation_failure_with_recovery_metadata()
    {
        // A host that composes no secret resolver is missing a module: composing one repairs it, so the activity parks
        // for deployment correction instead of being retried or faulted (spec 188, T032, constitution §E2.6.1).
        var failure = _handler.Classify(new RuntimeSecretResolverNotFoundException("token"), "artifact-1", "node-1");

        Assert.NotNull(failure);
        Assert.Equal(ActivityActivationFailureKind.MissingSecretResolver, failure!.Kind);
        Assert.Equal(RuntimeActivationCapabilityKind.SecretResolver, failure.CapabilityKind);
        Assert.Equal(ActivityActivationFailureHandler.SecretResolverCapabilityKey, failure.CapabilityKey);
        Assert.Null(failure.ConsumerKey);
        Assert.Null(failure.StorageDriverKey);
        Assert.Null(failure.SchemaVersion);
        Assert.Equal("artifact-1", failure.ArtifactId);
        Assert.Equal("node-1", failure.ExecutableNodeId);
        Assert.Equal("false", failure.Metadata[ActivityActivationFailureHandler.RetryEligibleMetadataKey]);
        Assert.Equal(ActivityActivationFailureHandler.DeploymentCorrectionRecoveryAction, failure.Metadata[ActivityActivationFailureHandler.RecoveryActionMetadataKey]);
        Assert.Equal(nameof(RuntimeActivationCapabilityKind.SecretResolver), failure.Metadata[ActivityActivationFailureHandler.CapabilityKindMetadataKey]);
        Assert.Equal(nameof(ActivityActivationFailureKind.MissingSecretResolver), failure.Metadata[ActivityActivationFailureHandler.FailureKindMetadataKey]);
    }

    [Theory]
    [InlineData("TenantMismatch", false)]
    [InlineData("StoreUnavailable", true)]
    public void A_secret_resolution_failure_is_not_an_activation_failure(string failureCode, bool isRetryable)
    {
        // A secret the store cannot serve is a domain outcome that faults the activity, not a missing module.
        Assert.Null(_handler.Classify(new RuntimeSecretResolutionException("payments.api-key", failureCode, isRetryable)));
    }
}
