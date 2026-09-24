using CShells.Configuration;
using Elsa.Modularity.Api;
using Elsa.Persistence.EntityFramework.Tooling;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Configuration;

[assembly: EfToolingShellDefaults(typeof(Elsa.Workbench.WorkbenchEfToolingShellDefaults))]

namespace Elsa.Workbench;

/// <summary>Workbench defaults shared by its live CShells host and EF tooling.</summary>
public sealed class WorkbenchEfToolingShellDefaults : IEfToolingShellDefaults
{
    public void Configure(ShellBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        builder.WithFeature<ModularityApiFeature>()
            // The action remains deferred until CShells builds the feature instance.
            .WithFeature<RuntimeFaultStackTraceFeature>(feature =>
                configuration.GetSection(RuntimeFaultCaptureOptions.SectionName).Bind(feature));
    }
}
