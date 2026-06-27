using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Models;

namespace Elsa.Studio.ExpressionEditors.JavaScript.Handlers;

public sealed class ContributeJavaScriptExpressionEditorStudioModule : IStudioEventHandler<OnStudioModuleManifestsCollecting>
{
    private const string ModuleVersion = "1.0.0";

    public Task Handle(OnStudioModuleManifestsCollecting @event, CancellationToken cancellationToken)
    {
        @event.Manifests.Add(new StudioModuleManifest(
            "Elsa.Studio.ExpressionEditors.JavaScript",
            "JavaScript expression editor",
            ModuleVersion,
            $"/_content/Elsa.Studio.ExpressionEditors.JavaScript/studio/modules/expression-editors/javascript/module.js?v={ModuleVersion}",
            [$"/_content/Elsa.Studio.ExpressionEditors.JavaScript/studio/modules/expression-editors/javascript/module.css?v={ModuleVersion}"],
            "^1.0.0",
            "^1.0.0",
            ["expression-editors", "javascript"],
            "JavaScriptExpressionEditorStudio"));

        return Task.CompletedTask;
    }
}
