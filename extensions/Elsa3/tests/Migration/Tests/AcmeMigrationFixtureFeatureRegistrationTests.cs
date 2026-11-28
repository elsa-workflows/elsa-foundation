using System.Reflection;
using Acme.Elsa3MigrationFixture;
using CShells.Features;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa3.Migration.Tests;

/// <summary>
/// The fixture feature project (spec 1237 T003) registers what the fixture export misses: the custom activity under its
/// Elsa 3 type name through an activity reconciliation source, the <c>acme.</c> JavaScript host functions and the
/// variable type alias.
/// </summary>
public sealed class AcmeMigrationFixtureFeatureRegistrationTests
{
    [Fact]
    public void The_feature_is_a_shell_feature_named_for_shells_Local_json()
    {
        var attribute = typeof(AcmeMigrationFixtureFeature).GetCustomAttribute<ShellFeatureAttribute>();

        Assert.Equal("AcmeElsa3MigrationFixture", attribute?.Name);
        Assert.Equal(AcmeMigrationFixtureFeature.FeatureName, attribute?.Name);
    }

    [Fact]
    public async Task It_catalogs_the_custom_activity_under_its_Elsa_3_type_name_through_a_reconciliation_source()
    {
        await using var provider = Build();
        await using var scope = provider.CreateAsyncScope();

        var source = Assert.Single(scope.ServiceProvider.GetServices<IActivityReconciliationSource>());
        var model = Assert.Single(await source.Read(CancellationToken.None));

        Assert.Equal("Acme.NotifyCustomer", model.ActivityTypeKey);
        Assert.Equal("text", Assert.Single(model.Inputs).ReferenceKey);
        // Not a CLR activity a build-folder scan would find: the activity exists only while the feature is enabled.
        Assert.DoesNotContain(typeof(AcmeMigrationFixtureFeature).Assembly.GetTypes(), type => typeof(IActivity).IsAssignableFrom(type));
    }

    [Fact]
    public async Task It_registers_the_acme_host_functions()
    {
        await using var provider = Build();

        Assert.Equal(
            ["acme.getConfig", "acme.getCustomerData", "acme.lookupCustomer"],
            provider.GetServices<JavaScriptHostFunctionDeclaration>().Select(function => function.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task It_registers_the_variable_type_alias()
    {
        await using var provider = Build();

        var descriptor = Assert.Single(Assert.Single(provider.GetServices<IVariableTypeDescriptorProvider>()).GetDescriptors());
        Assert.Equal("Acme.Orders.OrderDto", descriptor.Alias);
        Assert.Equal(typeof(Acme.Elsa3MigrationFixture.Orders.OrderDto), descriptor.ClrType);
    }

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        new AcmeMigrationFixtureFeature().ConfigureServices(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
