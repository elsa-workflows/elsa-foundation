using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Events.Core.Contracts;

namespace Elsa.Agent.Core;

/// <summary>Sequential context fan-in, populated by one aggregator over installed context sources.</summary>
public sealed class AgentContextCollecting(AgentContextRequest request) : IEvent
{
    public AgentContextRequest Request { get; } = request;
    public ICollection<AgentContextAttachment> Attachments { get; } = [];
    public ICollection<AgentContributionDiagnostic> Diagnostics { get; } = [];
}

/// <summary>Sequential capability fan-in; duplicate identities are a composition failure.</summary>
public sealed class AgentCapabilitiesCollecting : IEvent
{
    public ICollection<AgentCapability> Capabilities { get; } = [];
}

/// <summary>Sequential tool fan-in over the existing installed tool contract, before per-turn admission.</summary>
public sealed class AgentToolsCollecting : IEvent
{
    public ICollection<IAgentTool> Tools { get; } = [];
}
