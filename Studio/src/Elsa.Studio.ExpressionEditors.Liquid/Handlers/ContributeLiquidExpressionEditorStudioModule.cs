using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Models;

namespace Elsa.Studio.ExpressionEditors.Liquid.Handlers;

public sealed class ContributeLiquidExpressionEditorStudioModule : IStudioEventHandler<OnStudioModuleManifestsCollecting>
{
    private const string ModuleVersion = "1.0.0";

    public Task Handle(OnStudioModuleManifestsCollecting @event, CancellationToken cancellationToken)
    {
        @event.Manifests.Add(new StudioModuleManifest(
            "Elsa.Studio.ExpressionEditors.Liquid",
            "Liquid expression editor",
            ModuleVersion,
            $"/_content/Elsa.Studio.ExpressionEditors.Liquid/studio/modules/expression-editors/liquid/module.js?v={ModuleVersion}",
            [$"/_content/Elsa.Studio.ExpressionEditors.Liquid/studio/modules/expression-editors/liquid/module.css?v={ModuleVersion}"],
            "^1.0.0",
            "^1.0.0",
            ["expression-editors", "liquid"],
            "LiquidExpressionEditorStudio"));

        return Task.CompletedTask;
    }
}
