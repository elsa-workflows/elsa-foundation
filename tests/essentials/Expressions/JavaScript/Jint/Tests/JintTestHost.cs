using Elsa.Expressions.JavaScript.Jint;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

/// <summary>
/// Shared harness for the two isolated evaluators registered by <see cref="JintFeature"/>.
/// </summary>
internal static class JintTestHost
{
    public static ServiceProvider Build(Action<JintFeature>? configureFeature = null)
    {
        var feature = new JintFeature();
        configureFeature?.Invoke(feature);

        var services = new ServiceCollection();
        feature.ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    public static IJavaScriptScriptEvaluator ScriptEvaluator(this IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IJavaScriptScriptEvaluator>();
}
