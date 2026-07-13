using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Models;
namespace Elsa.Studio.Workflows.Dashboard.Handlers;
public sealed class ContributeWorkflowsDashboardStudioModule : IStudioEventHandler<OnStudioModuleManifestsCollecting> { public Task Handle(OnStudioModuleManifestsCollecting @event, CancellationToken cancellationToken) { @event.Manifests.Add(new StudioModuleManifest("Elsa.Studio.Workflows.Dashboard", "Workflow dashboard widgets", "1.0.0", "/_content/Elsa.Studio.Workflows.Dashboard/studio/modules/workflows-dashboard/module.js", [], "^1.0.0", "^1.0.0", ["dashboard-widgets", "http"])); return Task.CompletedTask; } }
