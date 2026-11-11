using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Events.Core.Contracts;

namespace Elsa.Weaver.Core;

/// <summary>Sequential context fan-in, populated by one aggregator over installed context sources.</summary>
public sealed class WeaverContextCollecting(WeaverContextRequest request) : IEvent
{
    public WeaverContextRequest Request { get; } = request;
    public ICollection<WeaverContextAttachment> Attachments { get; } = [];
    public ICollection<WeaverContributionDiagnostic> Diagnostics { get; } = [];
}

/// <summary>Sequential capability fan-in; duplicate identities are a composition failure.</summary>
public sealed class WeaverCapabilitiesCollecting : IEvent
{
    public ICollection<WeaverCapability> Capabilities { get; } = [];
}

/// <summary>Sequential tool fan-in over the existing installed tool contract, before per-turn admission.</summary>
public sealed class WeaverToolsCollecting : IEvent
{
    public ICollection<IWeaverTool> Tools { get; } = [];
}
