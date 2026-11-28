using Elsa.Studio.Core.Models;

namespace Elsa.Studio.Core.Contracts;

/// <summary>Supplies allowlisted browser-visible configuration; never return credentials or raw host configuration.</summary>
public interface IStudioRuntimeConfigurationSource
{
    IEnumerable<StudioRuntimeConfigurationEntry> GetConfiguration(string studioPathBase);
}
