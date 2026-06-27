using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Services;
using Elsa.Studio.ExpressionEditors.Liquid.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.ExpressionEditors.Liquid;

public static class LiquidExpressionEditorStudioServiceCollectionExtensions
{
    public static IServiceCollection AddLiquidExpressionEditorStudio(this IServiceCollection services)
    {
        return services.AddStudioEventHandler<OnStudioModuleManifestsCollecting, ContributeLiquidExpressionEditorStudioModule>();
    }
}
