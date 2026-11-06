using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Events;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Values;

internal sealed class CollectRuntimeTestScopeContracts(IEnumerable<IRuntimeTestScopeContractSource> sources)
    : IEventHandler<RuntimeTestScopeContractsCollecting>
{
    public async Task Handle(RuntimeTestScopeContractsCollecting domainEvent, CancellationToken cancellationToken)
    {
        foreach (var source in sources)
            if (await source.GetContractAsync(domainEvent.Executable, domainEvent.ScopeNode, domainEvent.Kind, cancellationToken) is { } contract)
                domainEvent.Contracts.Add(contract);
    }
}

internal sealed class NativeRuntimeTestScopeContractSource : IRuntimeTestScopeContractSource
{
    public ValueTask<RuntimeTestScopeContract?> GetContractAsync(WorkflowExecutable executable, ExecutableNode scopeNode,
        VariableFrameKind kind, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (kind != VariableFrameKind.Container || scopeNode.Structure is null ||
            scopeNode.Metadata.GetValueOrDefault(RuntimeMetadataKeys.ActivityExecutionScope) == RuntimeMetadataKeys.ActivityExecutionScopeClosed)
            return ValueTask.FromResult<RuntimeTestScopeContract?>(null);
        return ValueTask.FromResult<RuntimeTestScopeContract?>(new(scopeNode.ExecutableNodeId, kind, false,
            new RuntimeVariableDeclarationProjector().ProjectDeclarations(scopeNode),
            new Dictionary<string, Elsa.Activities.Runtime.Core.Models.ActivityInputContract>(), true));
    }
}
