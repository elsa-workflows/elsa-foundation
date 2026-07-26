using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>Startup-built immutable lookup for Runtime-owned and host-contributed alteration descriptors.</summary>
public sealed class WorkflowAlterationRegistry : IWorkflowAlterationRegistry
{
    private static readonly IReadOnlyCollection<WorkflowAlterationDescriptor> BuiltIns =
    [
        new("CancelWorkflow", 1, "Cancel workflow"),
        new("ModifyVariable", 1, "Modify variable"),
        new("ScheduleActivity", 1, "Schedule activity"),
        new("RescheduleActivity", 1, "Reschedule activity"),
        new("Migrate", 1, "Migrate workflow")
    ];

    private readonly IReadOnlyDictionary<(string Kind, int SchemaVersion), WorkflowAlterationDescriptor> _descriptors;

    /// <summary>Builds one registry from startup contributions and validates duplicate exact identities.</summary>
    public WorkflowAlterationRegistry(IEnumerable<WorkflowAlterationHandlerContribution> contributions)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        var descriptors = BuiltIns.Concat(contributions.Select(contribution =>
        {
            contribution.Validate();
            ValidateCustomDescriptor(contribution.Descriptor);
            return contribution.Descriptor;
        })).ToArray();
        var duplicate = descriptors
            .GroupBy(descriptor => (descriptor.Kind, descriptor.SchemaVersion), StringComparerTuple.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Workflow alteration '{duplicate.Key.Kind}/{duplicate.Key.SchemaVersion}' is registered more than once.");

        _descriptors = descriptors
            .OrderBy(descriptor => descriptor.Kind, StringComparer.Ordinal)
            .ThenBy(descriptor => descriptor.SchemaVersion)
            .ToDictionary(descriptor => (descriptor.Kind, descriptor.SchemaVersion));
        Descriptors = _descriptors.Values.ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<WorkflowAlterationDescriptor> Descriptors { get; }

    /// <inheritdoc />
    public WorkflowAlterationDescriptor? Find(string kind, int schemaVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentOutOfRangeException.ThrowIfLessThan(schemaVersion, 1);
        return _descriptors.GetValueOrDefault((kind, schemaVersion));
    }

    /// <inheritdoc />
    public WorkflowAlterationDescriptor GetRequired(string kind, int schemaVersion) =>
        Find(kind, schemaVersion) ?? throw new InvalidOperationException($"Workflow alteration '{kind}/{schemaVersion}' is not registered.");

    /// <summary>Rejects public custom descriptors that could collide with Runtime-owned built-ins.</summary>
    public static void ValidateCustomDescriptor(WorkflowAlterationDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (BuiltIns.Any(builtIn => StringComparer.Ordinal.Equals(builtIn.Kind, descriptor.Kind)))
            throw new ArgumentException($"'{descriptor.Kind}' is reserved for a Runtime-owned alteration.", nameof(descriptor));
        if (!descriptor.Kind.Contains('.', StringComparison.Ordinal) || descriptor.Kind.Split('.').Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Custom workflow alteration kinds must be dotted and namespaced.", nameof(descriptor));
    }

    private sealed class StringComparerTuple : IEqualityComparer<(string Kind, int SchemaVersion)>
    {
        public static StringComparerTuple Ordinal { get; } = new();
        public bool Equals((string Kind, int SchemaVersion) left, (string Kind, int SchemaVersion) right) =>
            left.SchemaVersion == right.SchemaVersion && StringComparer.Ordinal.Equals(left.Kind, right.Kind);
        public int GetHashCode((string Kind, int SchemaVersion) value) => HashCode.Combine(StringComparer.Ordinal.GetHashCode(value.Kind), value.SchemaVersion);
    }
}
