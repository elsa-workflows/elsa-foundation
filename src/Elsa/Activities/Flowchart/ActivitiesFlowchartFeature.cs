using CShells.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Flowchart;

[ShellFeature(
    name: "ActivitiesFlowchart",
    DisplayName = "Activities Flowchart",
    Description = "Flowchart composite activity and executable-node graph contracts."
)]
public class ActivitiesFlowchartFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }
}
