using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Associates one stable intent kind with its contributed handler type.</summary>
public sealed class RuntimePostCommitIntentHandlerContribution
{
    public RuntimePostCommitIntentHandlerContribution(string intentKind, Type handlerType)
        : this(intentKind, handlerType, RuntimePostCommitRetryPolicy.None)
    {
    }

    public RuntimePostCommitIntentHandlerContribution(
        string intentKind,
        Type handlerType,
        RuntimePostCommitRetryPolicy retryPolicy)
        : this(intentKind, handlerType, retryPolicy, requiresDurableSource: false)
    {
    }

    public RuntimePostCommitIntentHandlerContribution(string intentKind, Type handlerType,
        RuntimePostCommitRetryPolicy retryPolicy, bool requiresDurableSource)
        : this(intentKind, handlerType, retryPolicy, requiresDurableSource, null) { }

    /// <summary>Declares a producer's recipient protocol at commit time; no recipients are selected here.</summary>
    public RuntimePostCommitIntentHandlerContribution(string intentKind, Type handlerType,
        RuntimePostCommitRetryPolicy retryPolicy, bool requiresDurableSource, int? stimulusDeliveryProtocolVersion)
        : this(intentKind, handlerType, retryPolicy, requiresDurableSource, stimulusDeliveryProtocolVersion,
            RuntimeDeliverySourceKind.WorkflowCheckpoint, null)
    {
    }

    /// <summary>Contributes one source family's handler without changing existing workflow contributions.</summary>
    public RuntimePostCommitIntentHandlerContribution(string intentKind, Type handlerType,
        RuntimePostCommitRetryPolicy retryPolicy, bool requiresDurableSource, int? stimulusDeliveryProtocolVersion,
        RuntimeDeliverySourceKind sourceKind, int? subscriptionDeliveryProtocolVersion)
    {
        RuntimePostCommitIntent.ValidateKind(intentKind, nameof(intentKind));
        if (stimulusDeliveryProtocolVersion is not null &&
            (stimulusDeliveryProtocolVersion != StimulusDeliverySnapshot.CurrentVersion || !requiresDurableSource))
            throw new ArgumentException("A supported recipient protocol requires explicit durable source delivery.", nameof(stimulusDeliveryProtocolVersion));
        ArgumentNullException.ThrowIfNull(handlerType);
        ArgumentNullException.ThrowIfNull(retryPolicy);
        if (!Enum.IsDefined(sourceKind)) throw new ArgumentOutOfRangeException(nameof(sourceKind));
        if (subscriptionDeliveryProtocolVersion is not null &&
            (subscriptionDeliveryProtocolVersion != RuntimeSubscriptionDelivery.CurrentVersion || !requiresDurableSource))
            throw new ArgumentException("Subscription delivery requires its supported durable-source protocol.", nameof(subscriptionDeliveryProtocolVersion));
        if (sourceKind == RuntimeDeliverySourceKind.StandaloneIngress && (!requiresDurableSource || stimulusDeliveryProtocolVersion is not null ||
            subscriptionDeliveryProtocolVersion != RuntimeSubscriptionDelivery.CurrentVersion))
            throw new ArgumentException("Standalone ingress requires its own source and subscription protocol.", nameof(sourceKind));

        var handlerContract = sourceKind == RuntimeDeliverySourceKind.StandaloneIngress ? typeof(IRuntimeDeliverySourceHandler) : typeof(IRuntimePostCommitIntentHandler);
        if (!handlerContract.IsAssignableFrom(handlerType) || handlerType.IsAbstract || handlerType.IsInterface)
        {
            throw new ArgumentException(
                $"Runtime post-commit intent handler type '{handlerType.FullName ?? handlerType.Name}' must be a concrete {handlerContract.Name} implementation.",
                nameof(handlerType));
        }

        if (sourceKind == RuntimeDeliverySourceKind.WorkflowCheckpoint && requiresDurableSource && !typeof(IRuntimePostCommitDurableSourceIntentHandler).IsAssignableFrom(handlerType))
            throw new ArgumentException("A required-source contribution needs the explicit context handler contract.", nameof(handlerType));
        RequiresDurableSource = requiresDurableSource;
        StimulusDeliveryProtocolVersion = stimulusDeliveryProtocolVersion;
        IntentKind = intentKind;
        HandlerType = handlerType;
        RetryPolicy = retryPolicy;
        SourceKind = sourceKind;
        SubscriptionDeliveryProtocolVersion = subscriptionDeliveryProtocolVersion;
    }

    public bool RequiresDurableSource { get; }
    public int? StimulusDeliveryProtocolVersion { get; }
    public string IntentKind { get; }
    public Type HandlerType { get; }
    public RuntimePostCommitRetryPolicy RetryPolicy { get; }
    public RuntimeDeliverySourceKind SourceKind { get; }
    public int? SubscriptionDeliveryProtocolVersion { get; }
}
