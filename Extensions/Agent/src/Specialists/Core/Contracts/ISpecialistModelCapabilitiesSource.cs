using Elsa.Agent.Specialists.Core.Models;

namespace Elsa.Agent.Specialists.Core.Contracts;

/// <summary>Authoring metadata contribution, resolved in the selected publication scope without opening an inference client.</summary>
public interface ISpecialistModelCapabilitiesSource
{
    /// <summary>The exact provider key this contribution owns; duplicate registrations are refused.</summary>
    string ProviderKey { get; }

    /// <summary>Reads authorized current model metadata without inference.</summary>
    ValueTask<SpecialistModelCapabilities> GetCapabilitiesAsync(SpecialistModelReference model, CancellationToken cancellationToken = default);
}
