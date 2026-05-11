using CShells.Features;
using Elsa.Activities.Core.Contracts;
using Elsa.Activities.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities
{
    [ShellFeature(
        name: "Activities",
        DisplayName = "Activities",
        Description = "Enables working with activities"
    )]
    public class ActivitiesFeature : IShellFeature
    {
        public IEnumerable<string> ActivityTypes { get; set; } = [];

        public IEnumerable<string> ActivityAssemblies { get; set; } = [];

        public void ConfigureServices(IServiceCollection services)
        {
            services
                .AddScoped<IActivityDescriber, ActivityDescriber>();
        }


    }
}
