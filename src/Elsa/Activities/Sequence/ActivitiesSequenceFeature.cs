using CShells.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Sequence;

[ShellFeature(
    name: "ActivitiesSequence",
    DisplayName = "Activities Sequence",
    Description = "Sequence composite activity and executable-node child slot contracts."
)]
public class ActivitiesSequenceFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }
}
