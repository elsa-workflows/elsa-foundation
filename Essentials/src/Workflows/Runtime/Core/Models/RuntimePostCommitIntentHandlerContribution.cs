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
    {
        RuntimePostCommitIntent.ValidateKind(intentKind, nameof(intentKind));
        if (stimulusDeliveryProtocolVersion is not null &&
            (stimulusDeliveryProtocolVersion != StimulusDeliverySnapshot.CurrentVersion || !requiresDurableSource))
            throw new ArgumentException("A supported recipient protocol requires explicit durable source delivery.", nameof(stimulusDeliveryProtocolVersion));
        ArgumentNullException.ThrowIfNull(handlerType);
        ArgumentNullException.ThrowIfNull(retryPolicy);

        if (!typeof(IRuntimePostCommitIntentHandler).IsAssignableFrom(handlerType) || handlerType.IsAbstract || handlerType.IsInterface)
        {
            throw new ArgumentException(
                $"Runtime post-commit intent handler type '{handlerType.FullName ?? handlerType.Name}' must be a concrete {nameof(IRuntimePostCommitIntentHandler)} implementation.",
                nameof(handlerType));
        }

        if (requiresDurableSource && !typeof(IRuntimePostCommitDurableSourceIntentHandler).IsAssignableFrom(handlerType))
            throw new ArgumentException("A required-source contribution needs the explicit context handler contract.", nameof(handlerType));
        RequiresDurableSource = requiresDurableSource;
        StimulusDeliveryProtocolVersion = stimulusDeliveryProtocolVersion;
        IntentKind = intentKind;
        HandlerType = handlerType;
        RetryPolicy = retryPolicy;
    }

    public bool RequiresDurableSource { get; }
    public int? StimulusDeliveryProtocolVersion { get; }
    public string IntentKind { get; }
    public Type HandlerType { get; }
    public RuntimePostCommitRetryPolicy RetryPolicy { get; }
}
