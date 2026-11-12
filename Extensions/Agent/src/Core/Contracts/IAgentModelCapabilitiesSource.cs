using Elsa.Agent.Core.Models;
using System.Text.Json;

namespace Elsa.Agent.Core.Contracts;

/// <summary>Authoring metadata contribution, resolved in the selected publication scope without opening an inference client.</summary>
public interface IAgentModelCapabilitiesSource
{
    /// <summary>The exact provider key this contribution owns; duplicate registrations are refused.</summary>
    string ProviderKey { get; }

    /// <summary>Reads authorized current model metadata without inference.</summary>
    ValueTask<AgentModelCapabilities> GetCapabilitiesAsync(AgentModelReference model, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks the actual expanded result schema before publication. Existing contributions declaring
    /// structured support admit the kernel's entire bounded profile; provider-specific subsets must
    /// override this method and repeat admission when opening their runtime client.
    /// </summary>
    ValueTask<bool> SupportsResultSchemaAsync(AgentModelReference model, JsonElement schema, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(true);
}
