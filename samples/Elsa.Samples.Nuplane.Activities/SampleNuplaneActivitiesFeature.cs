using CShells.Features;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Samples.Nuplane.Activities.Constructors;
using Elsa.Samples.Nuplane.Activities.Reconciliation;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Samples.Nuplane.Activities;

[ShellFeature(
    name: "SampleNuplaneActivities",
    DisplayName = "Sample Nuplane Activities",
    Description = "Sample package-loaded activity feature for Nuplane demonstrations."
)]
public sealed class SampleNuplaneActivitiesFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IActivityReconciliationSource, SampleNuplaneActivityReconciliationSource>();
        services.AddSingleton<IActivityConstructor, SampleNuplaneActivityConstructor>();
    }
}
