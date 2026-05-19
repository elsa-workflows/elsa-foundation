using CShells.Features;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Expressions.JavaScript.Activities.RunJavaScript;
using Elsa.Expressions.JavaScript.Activities.RunJavaScript.TestClasses;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Expressions.JavaScript.Activities
{
    [ShellFeature(
      name: "JavaScriptActivities",
      DisplayName = "JavaScript activities"
    )]
    public class JavaScriptActivitiesFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services
                .AddScoped<IActivityCompletionHandler, ActivityCompletionHandler>()
                .AddScoped<IJavaScriptFunctionDeclarationProvider, FunctionsProvider>();
        }
    }
}
