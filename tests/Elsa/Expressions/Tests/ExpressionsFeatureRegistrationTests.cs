using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Liquid;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.Tests;

public sealed class ExpressionsFeatureRegistrationTests
{
    [Fact]
    public void Liquid_feature_registers_only_the_portable_expression_path()
    {
        var services = new ServiceCollection();

        new LiquidExpressionsFeature().ConfigureServices(services);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IPortableExpressionHandler));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType.FullName is "Elsa.Expressions.Liquid.Contracts.ILiquidTemplateManager"
                or "Elsa.Expressions.Core.Contracts.IExpressionHandler");
    }
}
