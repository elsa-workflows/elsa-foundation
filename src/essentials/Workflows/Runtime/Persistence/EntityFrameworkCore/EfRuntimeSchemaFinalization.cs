using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>
/// The Runtime EF module's finalization gate in this shell, as the runtime sees it (spec 184, FR-008 and FR-012): the
/// database identity the gate read and whether it refuses writes to any Runtime family. Read live from the gate each
/// time, so it follows every refresh without a restart.
/// </summary>
internal sealed class EfRuntimeSchemaFinalization(IServiceProvider services) : IRuntimeSchemaFinalization
{
    public string? DatabaseIdentity => Gate()?.DatabaseIdentity;

    public string? WritesRefusedReason
    {
        get
        {
            if (Gate() is not { } gate)
                return null;
            var refused = gate.Families.Chains
                .Select(chain => (chain.Family, State: gate.StateOf(chain.Family)))
                .Where(family => family.State is { WritesRefused: true })
                .Select(family => $"schema family '{family.Family}' is finalized at '{family.State!.UnreadableFinalizedVersion}', which this member cannot read")
                .ToArray();
            return refused.Length == 0 ? null : $"its writes to the Runtime EF module are refused: {string.Join("; ", refused)}";
        }
    }

    /// <summary>The gate that admitted the Runtime EF module in this shell, or none before Prepare admitted it.</summary>
    private EfSchemaModuleGate? Gate() => services.GetService<EfSchemaFinalizationGates>()?.FindModuleGate(typeof(RuntimeDbContext));
}
