using CShells;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Weaver.Api;
using Elsa.Weaver.Api.Extensions;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Extensions;
using Elsa.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Weaver.Tests;

public sealed class WeaverAuthorizerRegistrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_late_additive_authorizer_aborts_real_shell_activation_with_a_replacement_diagnostic(bool inheritedDefaults)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        if (inheritedDefaults) services.AddWeaver();
        services.AddCShells(builder => builder
            .WithAssemblies(typeof(AmbiguousAuthorizerFeature).Assembly, typeof(EventsFeature).Assembly)
            .AddShell("weaver-authorizer-conflict", shell => shell.WithFeature<AmbiguousAuthorizerFeature>()));
        await using var root = services.BuildServiceProvider();

        var failure = await Assert.ThrowsAnyAsync<Exception>(() =>
            root.GetRequiredService<IShellRegistry>().GetOrActivateAsync("weaver-authorizer-conflict"));

        var diagnostic = string.Join(" ", ExceptionChain(failure).Select(x => x.Message));
        Assert.Contains(nameof(IWeaverToolAuthorizer), diagnostic);
        Assert.Contains("exactly one", diagnostic);
        Assert.Contains("Replace", diagnostic);
    }

    [Theory]
    [InlineData(typeof(IWeaverToolInvoker))]
    [InlineData(typeof(IWeaverContextCollector))]
    [InlineData(typeof(IWeaverActionProposalExecutor))]
    [InlineData(typeof(IWeaverProposalService))]
    [InlineData(typeof(IWeaverStreamingService))]
    public void A_plain_consumer_cannot_resolve_a_default_authorized_path_with_an_ambiguous_authorizer(Type contract)
    {
        using var root = WeaverTestServices.Create(services =>
        {
            services.AddWeaverApi();
            services.AddScoped<IWeaverToolAuthorizer, AllowTestWeaverToolAuthorizer>();
        });
        using var scope = root.CreateScope();

        var failure = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService(contract));

        Assert.Contains(nameof(IWeaverToolAuthorizer), failure.Message);
        Assert.Contains("exactly one", failure.Message);
        Assert.Contains("Replace", failure.Message);
    }

    [Fact]
    public async Task Repeated_default_and_api_configuration_retains_one_authorizer_and_clean_initialization()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new EventsFeature().ConfigureServices(services);
        services.AddWeaver();
        services.AddWeaverApi();
        services.AddWeaver();
        services.AddWeaverApi();
        services.AddWeaver();
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IWeaverToolAuthorizer));
        using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = root.CreateScope();

        foreach (var initializer in scope.ServiceProvider.GetServices<IShellInitializer>())
            await initializer.InitializeAsync(CancellationToken.None);

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWeaverToolInvoker>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWeaverContextCollector>());
    }

    [Fact]
    public async Task An_explicit_authorizer_replacement_keeps_the_real_effect_path_available()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool(), configure: services =>
        {
            services.AddWeaverApi();
            services.Replace(ServiceDescriptor.Scoped<IWeaverToolAuthorizer, AllowTestWeaverToolAuthorizer>());
        });
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);

        var applied = await fixture.ExecuteAsync(proposal);

        Assert.True(applied.Value?.Executed);
        Assert.Equal("new", fixture.Mutation!.Value);
        Assert.Equal(1, fixture.Mutation.ExecuteCalls);
        Assert.NotNull(applied.Value!.ToolResult!.Receipt);
    }

    private static IEnumerable<Exception> ExceptionChain(Exception? exception)
    {
        for (; exception is not null; exception = exception.InnerException)
            yield return exception;
    }

    [ShellFeature("WeaverAmbiguousAuthorizerProbe", DependsOn = new object[] { typeof(WeaverApiFeature) })]
    public sealed class AmbiguousAuthorizerFeature : WeaverApiFeature
    {
        public override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.AddScoped<IWeaverToolAuthorizer, AllowTestWeaverToolAuthorizer>();
        }
    }
}
