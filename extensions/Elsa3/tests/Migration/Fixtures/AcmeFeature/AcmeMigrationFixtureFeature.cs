using CShells.Features;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Expressions.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Acme.Elsa3MigrationFixture;

/// <summary>
/// The fixture feature project of spec 1237 (T003). It provides what the fixture export's definitions miss in a plain
/// migration host, so a run of <c>assess</c> with this feature enabled clears those findings (SC-007):
/// <list type="bullet">
/// <item>the custom activity <c>Acme.NotifyCustomer</c>, cataloged under its Elsa 3 type name by an activity
/// reconciliation source, so it exists only while this feature is enabled (no CLR activity type a build-folder scan
/// could find);</item>
/// <item>the JavaScript host functions <c>acme.lookupCustomer</c>, <c>acme.getConfig</c> and
/// <c>acme.getCustomerData</c>, which take over the Elsa 3 globals of the same member name;</item>
/// <item>the variable type alias <c>Acme.Orders.OrderDto</c>.</item>
/// </list>
/// </summary>
[ShellFeature(FeatureName, DisplayName = "Acme Elsa 3 migration fixture",
    Description = "Test-only: the custom activity, JavaScript host functions and variable type of the Elsa 3 migration CLI fixture export.")]
public sealed class AcmeMigrationFixtureFeature : IShellFeature
{
    /// <summary>The CShells feature name a shell lists to enable this feature.</summary>
    public const string FeatureName = "AcmeElsa3MigrationFixture";

    /// <summary>
    /// Test-only: when set, the feature throws while the shell is composed, so the migration CLI's
    /// <c>composition-failed</c> refusal can be tested (spec 1237 T026). The value is never echoed.
    /// </summary>
    public string? Throw { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
        if (!string.IsNullOrEmpty(Throw))
            throw new InvalidOperationException($"The {FeatureName} feature was told to fail while the shell is composed.");

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IActivityReconciliationSource, NotifyCustomerActivitySource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IVariableTypeDescriptorProvider, OrderDtoTypeDescriptorProvider>());
        foreach (var function in AcmeHostFunctions.All)
            services.AddSingleton(function);
    }
}
