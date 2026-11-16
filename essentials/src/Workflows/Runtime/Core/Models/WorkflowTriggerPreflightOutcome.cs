namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Describes the terminal publication-time state of a recognized trigger node.
/// </summary>
public enum WorkflowTriggerPreflightStatus
{
    Registered,
    IntentionallyNonStarting
}

/// <summary>
/// The non-persisted publication-time outcome for one recognized executable trigger node.
/// </summary>
public sealed class WorkflowTriggerNodePreflightOutcome
{
    public WorkflowTriggerNodePreflightOutcome(
        string executableNodeId,
        string activityType,
        string providerId,
        WorkflowTriggerPreflightStatus status,
        IReadOnlyCollection<WorkflowTriggerBinding> bindings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentNullException.ThrowIfNull(bindings);

        var bindingArray = bindings.ToArray();
        if (status == WorkflowTriggerPreflightStatus.Registered && bindingArray.Length == 0)
            throw new ArgumentException("A Registered trigger outcome must contain at least one binding.", nameof(bindings));
        if (status == WorkflowTriggerPreflightStatus.IntentionallyNonStarting && bindingArray.Length != 0)
            throw new ArgumentException("An IntentionallyNonStarting trigger outcome cannot contain bindings.", nameof(bindings));
        if (bindingArray.Any(x => !StringComparer.Ordinal.Equals(x.ExecutableNodeId, executableNodeId)))
            throw new ArgumentException("Every binding must belong to the outcome's executable node.", nameof(bindings));
        if (bindingArray.Select(x => x.TriggerBindingId).Distinct(StringComparer.Ordinal).Count() != bindingArray.Length)
            throw new ArgumentException("Trigger binding ids must be unique within a node outcome.", nameof(bindings));

        ExecutableNodeId = executableNodeId;
        ActivityType = activityType;
        ProviderId = providerId;
        Status = status;
        Bindings = bindingArray;
    }

    public string ExecutableNodeId { get; }
    public string ActivityType { get; }
    public string ProviderId { get; }
    public WorkflowTriggerPreflightStatus Status { get; }
    public IReadOnlyCollection<WorkflowTriggerBinding> Bindings { get; }
}

/// <summary>
/// The complete, immutable, non-persisted trigger assessment for one executable artifact.
/// </summary>
public sealed class WorkflowTriggerPreflightOutcome
{
    public WorkflowTriggerPreflightOutcome(
        WorkflowExecutableIdentity identity,
        IReadOnlyCollection<WorkflowTriggerNodePreflightOutcome> nodeOutcomes)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(nodeOutcomes);

        var nodeOutcomeArray = nodeOutcomes.ToArray();
        var duplicateNodeId = nodeOutcomeArray
            .GroupBy(x => x.ExecutableNodeId, StringComparer.Ordinal)
            .FirstOrDefault(x => x.Count() > 1)?.Key;
        if (duplicateNodeId is not null)
            throw new ArgumentException($"Executable node '{duplicateNodeId}' has more than one preflight outcome.", nameof(nodeOutcomes));

        var bindings = nodeOutcomeArray.SelectMany(x => x.Bindings).ToArray();
        var duplicateBindingId = bindings
            .GroupBy(x => x.TriggerBindingId, StringComparer.Ordinal)
            .FirstOrDefault(x => x.Count() > 1)?.Key;
        if (duplicateBindingId is not null)
            throw new ArgumentException($"Trigger binding id '{duplicateBindingId}' is duplicated in the preflight outcome.", nameof(nodeOutcomes));

        Identity = identity;
        NodeOutcomes = nodeOutcomeArray;
        Bindings = bindings;
    }

    public WorkflowExecutableIdentity Identity { get; }
    public IReadOnlyCollection<WorkflowTriggerNodePreflightOutcome> NodeOutcomes { get; }
    public IReadOnlyCollection<WorkflowTriggerBinding> Bindings { get; }
}
