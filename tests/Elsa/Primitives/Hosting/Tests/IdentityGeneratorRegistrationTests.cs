using Elsa.Primitives.Contracts;
using Elsa.Primitives.Hosting.Extensions;
using Elsa.Primitives.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Primitives.Hosting.Tests;

public sealed class IdentityGeneratorRegistrationTests
{
    [Theory]
    [InlineData(IdentityGeneratorKind.UuidV7, typeof(UuidV7IdentityGenerator))]
    [InlineData(IdentityGeneratorKind.Short, typeof(ShortIdentityGenerator))]
    [InlineData(IdentityGeneratorKind.Snowflake, typeof(SnowflakeIdentityGenerator))]
    [InlineData(IdentityGeneratorKind.Guid, typeof(GuidIdentityGenerator))]
    public void RegistersSelectedGenerator(IdentityGeneratorKind kind, Type expected)
    {
        using var provider = BuildProvider(services => services.AddIdentityGenerator(kind, o => o.WorkerId = 1));
        using var scope = provider.CreateScope();

        Assert.IsType(expected, scope.ServiceProvider.GetRequiredService<IIdentityGenerator>());
    }

    [Fact]
    public void ReplacesAnyPreviouslyRegisteredGenerator()
    {
        using var provider = BuildProvider(services =>
        {
            // Simulate a persistence feature default, then the integrator overriding it.
            services.AddScoped<IIdentityGenerator, GuidIdentityGenerator>();
            services.AddIdentityGenerator(IdentityGeneratorKind.UuidV7);
        });
        using var scope = provider.CreateScope();

        Assert.IsType<UuidV7IdentityGenerator>(scope.ServiceProvider.GetRequiredService<IIdentityGenerator>());
        Assert.Single(scope.ServiceProvider.GetServices<IIdentityGenerator>());
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddScoped<ISystemClock>(_ => new MutableClock());
        configure(services);
        return services.BuildServiceProvider(validateScopes: true);
    }
}
