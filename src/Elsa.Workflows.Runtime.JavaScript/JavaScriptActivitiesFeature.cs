using CShells.Features;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.JavaScript.Activities.RunJavaScript.TestClasses;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.JavaScript
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
                .AddScoped<IActivityCompletionHandler, ActivityCompletionHandler>();
        }
    }
}
