using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Libraries;
using Elsa.Expressions.Liquid;
using Elsa.Expressions.Liquid.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.Tests;

public sealed class ExpressionsFeatureRegistrationTests
{
    [Fact]
    public void Liquid_feature_registers_template_manager()
    {
        var services = new ServiceCollection();
        var feature = new LiquidExpressionsFeature();

        feature.ConfigureServices(services);

        // MD-10 (§2.23.1): assert the template-manager contract is registered, not its implementation type or lifetime.
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ILiquidTemplateManager));
    }

    [Fact]
    public void JavaScript_libraries_feature_registers_script_preprocessor()
    {
        var services = new ServiceCollection();
        var feature = new JavaScriptLibrariesFeature { ModuleName = "lodash" };

        feature.ConfigureServices(services);

        // MD-10 (§2.23.1): assert the script-preprocessor contract is registered, not its implementation type or lifetime.
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IScriptPreProcessor));
    }
}
