using Elsa.Studio.Core.Models;
using Elsa.Events.Core.Contracts;

namespace Elsa.Studio.Core.Events;

/// <summary>Sequential collection of public module settings; the publisher reads and validates the completed collection.</summary>
public sealed class OnStudioRuntimeConfigurationCollecting(string studioPathBase) : IEvent
{
    public string StudioPathBase { get; } = studioPathBase;
    public ICollection<StudioRuntimeConfigurationEntry> Configuration { get; } = [];
}
