using CShells.Features;
using Elsa.Activities.Design.Reconciliation.Clr.Contracts;
using Elsa.Activities.Design.Reconciliation.Clr.Options;
using Elsa.Activities.Design.Reconciliation.Clr.Services;
using Elsa.Activities.Design.Reconciliation.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Reconciliation.Clr;

/// <summary>
/// Registers the CLR assembly-scanning reconciliation source. This is a standalone source feature
/// (§2.6.1): it does NOT derive from <c>ActivitiesDesignReconciliationFeature</c> — it only
/// contributes an <see cref="IActivityReconciliationSource"/> that the reconciliation feature's
/// universal handler discovers from DI. Add both features to a shell to scan a folder.
/// </summary>
/// <remarks>
/// Every collaborator is registered against its contract, so an inheriting feature can override a
/// single piece (e.g. a different category scheme or scanning strategy) by calling
/// <c>base.ConfigureServices</c> and re-registering just that contract afterwards. The options
/// instance is the only singleton — it is an application-wide static value; everything else "just
/// executes" and is therefore scoped.
/// </remarks>
[ShellFeature(
    name: "ClrActivityReconciliation",
    Description = "Reconciliation source that scans a folder of assemblies for IActivity implementations (the CLR kind)."
)]
public class ClrActivityReconciliationFeature : IShellFeature
{
    public ClrReconciliationOptions Options { get; set; } = new();

    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(Options));
        services.AddScoped<IActivityTypeVersionResolver, ActivityTypeVersionResolver>();
        services.AddScoped<IActivityTypeCategoryResolver, ActivityTypeCategoryResolver>();
        services.AddScoped<IClrAssemblyScanner, ClrAssemblyScanner>();
        services.AddScoped<IActivityReconciliationSource, ClrActivityReconciliationSource>();
    }
}
