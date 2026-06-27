using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Services;
using Elsa.Studio.ExpressionEditors.JavaScript.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.ExpressionEditors.JavaScript;

public static class JavaScriptExpressionEditorStudioServiceCollectionExtensions
{
    public static IServiceCollection AddJavaScriptExpressionEditorStudio(this IServiceCollection services)
    {
        return services.AddStudioEventHandler<OnStudioModuleManifestsCollecting, ContributeJavaScriptExpressionEditorStudioModule>();
    }
}
