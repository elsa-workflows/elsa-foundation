using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>
/// Resolves a stable envelope identity to its one scoped trusted handler. Persisted plans carry no CLR handler name;
/// contributions are validated at startup and only used by this in-process resolver.
/// </summary>
public sealed class WorkflowAlterationHandlerResolver : IWorkflowAlterationHandlerResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IReadOnlyDictionary<(string Kind, int SchemaVersion), Type> _handlerTypes;

    public WorkflowAlterationHandlerResolver(
        IServiceProvider serviceProvider,
        IEnumerable<WorkflowAlterationHandlerContribution> contributions)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(contributions);

        _serviceProvider = serviceProvider;
        _handlerTypes = contributions
            .Select(contribution =>
            {
                contribution.Validate();
                return contribution;
            })
            .ToDictionary(
                contribution => (contribution.Descriptor.Kind, contribution.Descriptor.SchemaVersion),
                contribution => contribution.HandlerType,
                StringComparerTuple.Ordinal);
    }

    public IWorkflowAlterationPreflightHandler Resolve(WorkflowAlterationEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (!_handlerTypes.TryGetValue((envelope.Kind, envelope.SchemaVersion), out var handlerType))
            throw new InvalidOperationException($"No trusted handler is registered for workflow alteration '{envelope.Kind}/{envelope.SchemaVersion}'.");

        var handler = _serviceProvider.GetService(handlerType);
        return handler as IWorkflowAlterationPreflightHandler
            ?? throw new InvalidOperationException($"Workflow alteration handler '{handlerType.FullName}' must implement {nameof(IWorkflowAlterationPreflightHandler)}.");
    }

    private sealed class StringComparerTuple : IEqualityComparer<(string Kind, int SchemaVersion)>
    {
        public static StringComparerTuple Ordinal { get; } = new();

        public bool Equals((string Kind, int SchemaVersion) left, (string Kind, int SchemaVersion) right) =>
            left.SchemaVersion == right.SchemaVersion && StringComparer.Ordinal.Equals(left.Kind, right.Kind);

        public int GetHashCode((string Kind, int SchemaVersion) value) =>
            HashCode.Combine(StringComparer.Ordinal.GetHashCode(value.Kind), value.SchemaVersion);
    }
}
