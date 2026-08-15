using Elsa.Activities.Runtime.Core.Exceptions;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Classifies unresolved stable Runtime consumers as deployment compatibility incidents. The result is
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

        if (exception is not ActivityResolutionException resolutionException)
            return null;

        var kind = resolutionException switch
        {
            UnknownActivityConsumerException => ActivityActivationFailureKind.MissingConsumer,
            UnsupportedActivityDescriptorSchemaException => ActivityActivationFailureKind.UnsupportedSchema,
            // Must precede the InvalidDescriptor fallback: the descriptor is perfectly valid, it is
            // the CLR type behind its alias that is absent from this host.
            UnknownActivityTypeException => ActivityActivationFailureKind.MissingActivityType,
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
