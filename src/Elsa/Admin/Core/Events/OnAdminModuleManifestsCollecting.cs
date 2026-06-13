using Elsa.Admin.Core.Models;
using Elsa.Events.Core.Contracts;

namespace Elsa.Admin.Core.Events;

public sealed class OnAdminModuleManifestsCollecting : IEvent
{
    public ICollection<AdminModuleManifest> Manifests { get; } = [];
}
