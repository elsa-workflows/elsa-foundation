using Elsa.Serialization.Core;
using Elsa.Tasks.Core;

namespace Elsa.Serialization.SystemText.Startup;

/// <summary>Refuses competing runtime null contracts instead of selecting the last service registration.</summary>
internal sealed class RuntimeTypeNullabilityCompositionCheck(IEnumerable<IRuntimeTypeNullability> authorities) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (authorities.Count() != 1)
            throw new InvalidOperationException("Exactly one IRuntimeTypeNullability implementation must be active. Replace the existing registration instead of adding a competing runtime null contract.");
        return Task.CompletedTask;
    }
}
