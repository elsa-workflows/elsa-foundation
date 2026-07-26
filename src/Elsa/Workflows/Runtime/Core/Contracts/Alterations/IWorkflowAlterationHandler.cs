using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Workflows.Runtime.Core.Contracts.Alterations;

/// <summary>
/// Marker for a trusted, scoped alteration handler. The registry owns its stable descriptor so handler CLR identities
/// never enter a deferred envelope or durable plan record.
/// </summary>
public interface IWorkflowAlterationHandler;

/// <summary>Immutable descriptor lookup assembled once from startup contributions.</summary>
public interface IWorkflowAlterationRegistry
{
    /// <summary>All registered stable descriptors, sorted by kind and schema version.</summary>
    IReadOnlyCollection<WorkflowAlterationDescriptor> Descriptors { get; }

    /// <summary>Finds the exact registered descriptor or returns <see langword="null"/>.</summary>
    WorkflowAlterationDescriptor? Find(string kind, int schemaVersion);

    /// <summary>Returns the exact descriptor or throws when it is not registered.</summary>
    WorkflowAlterationDescriptor GetRequired(string kind, int schemaVersion);
}

/// <summary>Startup contribution that associates a stable descriptor with a scoped handler service.</summary>
public sealed record WorkflowAlterationHandlerContribution(WorkflowAlterationDescriptor Descriptor, Type HandlerType)
{
    /// <summary>Validates a descriptor/handler contribution before startup registry construction.</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Descriptor);
        ArgumentNullException.ThrowIfNull(HandlerType);
        if (!typeof(IWorkflowAlterationHandler).IsAssignableFrom(HandlerType))
            throw new ArgumentException($"Handler type '{HandlerType}' does not implement {nameof(IWorkflowAlterationHandler)}.", nameof(HandlerType));
    }
}
