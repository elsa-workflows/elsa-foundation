using CShells.Features;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Expressions.JavaScript.Activities
{
    public class JavaScriptActivitiesFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services
                .AddSingleton<IJavaScriptFunctionDeclarationProvider, RunJavaScriptFunctionsProvider>();
        }
    }
}
