using Elsa.Studio.Core.Models;

namespace Elsa.Studio.Core.Events;

public sealed class OnStudioModuleManifestsCollecting : IStudioEvent
{
    public ICollection<StudioModuleManifest> Manifests { get; } = [];
}

