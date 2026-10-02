using Elsa.Activities.Runtime.Core.Exceptions;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;

namespace Elsa.Workflows.Runtime.Services.Incidents;

/// <summary>
/// Classifies a missing activation capability (an unresolved stable Runtime consumer, a durable-value storage driver
/// or the runtime secret resolver) as a deployment compatibility incident. The result is
/// intentionally separate from domain retry: installing the required feature/schema is the recovery action,
/// and replaying ordinary activity failure policy cannot repair the deployment.
/// </summary>
public sealed class ActivityActivationFailureHandler
{
    public const string IncidentFailureType = "ArtifactActivationFailed";
    public const string RetryEligibleMetadataKey = "runtime.activation.retryEligible";
    public const string RecoveryActionMetadataKey = "runtime.activation.recoveryAction";
    public const string ConsumerKeyMetadataKey = "runtime.activation.consumerKey";
    public const string SchemaVersionMetadataKey = "runtime.activation.schemaVersion";
    public const string FailureKindMetadataKey = "runtime.activation.failureKind";
    public const string CapabilityKindMetadataKey = "runtime.activation.capabilityKind";
    public const string StorageDriverKeyMetadataKey = "runtime.activation.storageDriverKey";
    public const string DeploymentCorrectionRecoveryAction = "CorrectDeploymentAndResume";

    /// <summary>The capability key a missing secret resolver is reported under: the runtime contract it must provide.</summary>
    public const string SecretResolverCapabilityKey = nameof(IRuntimeSecretResolver);

    public ActivityActivationFailure? Classify(
        Exception exception,
        string? artifactId = null,
        string? executableNodeId = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is RuntimeDurableValueStorageDriverNotFoundException storageDriverException)
        {
            return new(
                ActivityActivationFailureKind.MissingStorageDriver,
                RuntimeActivationCapabilityKind.DurableValueStorageDriver,
                storageDriverException.DriverKey,
                schemaVersion: null,
                artifactId,
                executableNodeId,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [RetryEligibleMetadataKey] = bool.FalseString.ToLowerInvariant(),
                    [RecoveryActionMetadataKey] = DeploymentCorrectionRecoveryAction,
                    [StorageDriverKeyMetadataKey] = storageDriverException.DriverKey,
                    [CapabilityKindMetadataKey] = RuntimeActivationCapabilityKind.DurableValueStorageDriver.ToString(),
                    [FailureKindMetadataKey] = ActivityActivationFailureKind.MissingStorageDriver.ToString()
                });
        }

        // A host that cannot resolve secrets at all is missing a module (constitution §E2.6.1): composing a secret
        // resolver repairs it, whereas faulting the activity would destroy an executable that is otherwise sound.
        if (exception is RuntimeSecretResolverNotFoundException)
        {
            return new(
                ActivityActivationFailureKind.MissingSecretResolver,
                RuntimeActivationCapabilityKind.SecretResolver,
                SecretResolverCapabilityKey,
                schemaVersion: null,
                artifactId,
                executableNodeId,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [RetryEligibleMetadataKey] = bool.FalseString.ToLowerInvariant(),
                    [RecoveryActionMetadataKey] = DeploymentCorrectionRecoveryAction,
                    [CapabilityKindMetadataKey] = RuntimeActivationCapabilityKind.SecretResolver.ToString(),
                    [FailureKindMetadataKey] = ActivityActivationFailureKind.MissingSecretResolver.ToString()
                });
        }

        if (exception is not ActivityResolutionException resolutionException)
            return null;

        var kind = resolutionException switch
        {
            UnknownActivityConsumerException => ActivityActivationFailureKind.MissingConsumer,
            UnsupportedActivityDescriptorSchemaException => ActivityActivationFailureKind.UnsupportedSchema,
            _ => ActivityActivationFailureKind.InvalidDescriptor
        };
        return new(
            kind,
            resolutionException.ConsumerKey,
            resolutionException.SchemaVersion,
            artifactId,
            executableNodeId,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RetryEligibleMetadataKey] = bool.FalseString.ToLowerInvariant(),
                [RecoveryActionMetadataKey] = DeploymentCorrectionRecoveryAction,
                [ConsumerKeyMetadataKey] = resolutionException.ConsumerKey,
                [SchemaVersionMetadataKey] = resolutionException.SchemaVersion,
                [CapabilityKindMetadataKey] = RuntimeActivationCapabilityKind.ActivityConsumer.ToString(),
                [FailureKindMetadataKey] = kind.ToString()
            });
    }
}
