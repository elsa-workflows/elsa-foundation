using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Associates one stable intent kind with its contributed handler type.</summary>
public sealed class RuntimePostCommitIntentHandlerContribution
{
    public RuntimePostCommitIntentHandlerContribution(string intentKind, Type handlerType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intentKind);
        ArgumentNullException.ThrowIfNull(handlerType);

        if (!typeof(IRuntimePostCommitIntentHandler).IsAssignableFrom(handlerType) || handlerType.IsAbstract || handlerType.IsInterface)
        {
            throw new ArgumentException(
                $"Runtime post-commit intent handler type '{handlerType.FullName ?? handlerType.Name}' must be a concrete {nameof(IRuntimePostCommitIntentHandler)} implementation.",
                nameof(handlerType));
        }

        IntentKind = intentKind;
        HandlerType = handlerType;
    }

    public string IntentKind { get; }
    public Type HandlerType { get; }
}
