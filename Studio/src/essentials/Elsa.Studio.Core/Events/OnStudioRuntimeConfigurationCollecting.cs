using Elsa.Studio.Core.Models;

namespace Elsa.Studio.Core.Events;

/// <summary>Sequential collection of public module settings; the publisher reads and validates the completed collection.</summary>
public sealed class OnStudioRuntimeConfigurationCollecting(string studioPathBase) : IStudioEvent
{
    public string StudioPathBase { get; } = studioPathBase;
    public ICollection<StudioRuntimeConfigurationEntry> Configuration { get; } = [];
}
