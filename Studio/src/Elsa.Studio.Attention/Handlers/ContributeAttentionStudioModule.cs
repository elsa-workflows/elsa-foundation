using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Models;
namespace Elsa.Studio.Attention.Handlers;
public sealed class ContributeAttentionStudioModule : IStudioEventHandler<OnStudioModuleManifestsCollecting> { public Task Handle(OnStudioModuleManifestsCollecting @event, CancellationToken cancellationToken) { @event.Manifests.Add(new StudioModuleManifest("Elsa.Studio.Attention", "Attention", "1.0.0", "/_content/Elsa.Studio.Attention/studio/modules/attention/module.js", [], "^1.0.0", "^1.0.0", ["dashboard-widgets", "http"])); return Task.CompletedTask; } }
