using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Activities.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// <see cref="IRuntimeSecretResolver"/> is a replacement contract (framework constitution §2.6.2): a host that composes
/// more than one does not start, whatever order they were registered in, and the diagnostic names every registration
/// (spec 188, T027). A host that composes one, or none, starts.
/// </summary>
public sealed class SecretResolverCompositionTests
{
    private const string ShellName = "secret-resolvers";
    private readonly ServiceCollection _services = new();

    public SecretResolverCompositionTests() => new ActivitiesRuntimeFeature().ConfigureServices(_services);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_composed_resolvers_fail_startup_naming_both_in_either_registration_order(bool secondRegisteredFirst)
    {
        Type[] resolvers = secondRegisteredFirst
            ? [typeof(SecondSecretResolver), typeof(FirstSecretResolver)]
            : [typeof(FirstSecretResolver), typeof(SecondSecretResolver)];
        foreach (var resolver in resolvers)
            _services.AddScoped(typeof(IRuntimeSecretResolver), resolver);

        var exception = await Assert.ThrowsAsync<MultipleRuntimeSecretResolversException>(InitializeAsync);

        Assert.Equal([.. resolvers.Select(resolver => $"'{resolver.FullName}'")], exception.Implementations);
        foreach (var resolver in resolvers)
            Assert.Contains(resolver.FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_composed_resolver_starts()
    {
        _services.AddScoped<IRuntimeSecretResolver, FirstSecretResolver>();

        await InitializeAsync();
    }

    [Fact]
    public async Task No_composed_resolver_starts() => await InitializeAsync();

    [Fact]
    public async Task Composing_the_runtime_twice_registers_the_check_once()
    {
        new ActivitiesRuntimeFeature().ConfigureServices(_services);

        Assert.Single(_services, descriptor => descriptor.ServiceType == typeof(RuntimeSecretResolverComposition));
        await using var provider = _services.BuildServiceProvider();
        Assert.Single(provider.GetServices<IShellInitializer>(), initializer => initializer is RuntimeSecretResolverCompositionValidator);
    }

    [Fact]
    public async Task A_shell_composing_two_resolvers_fails_activation_naming_both()
    {
        var services = new ServiceCollection();
        services.AddCShells(builder => builder
            .WithAssemblies(typeof(ActivitiesRuntimeFeature).Assembly, typeof(SecretResolverCompositionTests).Assembly)
            .AddShell(ShellName, shell => shell
                .WithFeature<ActivitiesRuntimeFeature>()
                .WithFeature<FirstSecretResolverFeature>()
                .WithFeature<SecondSecretResolverFeature>()));
        await using var root = services.BuildServiceProvider();

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName, CancellationToken.None));

        var conflict = Assert.IsType<MultipleRuntimeSecretResolversException>(exception as MultipleRuntimeSecretResolversException ?? exception.InnerException);
        Assert.Contains(typeof(FirstSecretResolver).FullName!, conflict.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(SecondSecretResolver).FullName!, conflict.Message, StringComparison.Ordinal);
    }

    /// <summary>Runs the shell initializers the composed services register, as shell activation does.</summary>
    private async Task InitializeAsync()
    {
        await using var provider = _services.BuildServiceProvider(validateScopes: true);
        foreach (var initializer in provider.GetServices<IShellInitializer>())
            await initializer.InitializeAsync();
    }

    private sealed class FirstSecretResolver : IRuntimeSecretResolver
    {
        public ValueTask<RuntimeSecretResolution> ResolveAsync(RuntimeSecretResolutionRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(RuntimeSecretResolution.Failure("NotFound", isRetryable: false));
    }

    private sealed class SecondSecretResolver : IRuntimeSecretResolver
    {
        public ValueTask<RuntimeSecretResolution> ResolveAsync(RuntimeSecretResolutionRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(RuntimeSecretResolution.Failure("NotFound", isRetryable: false));
    }

    [ShellFeature(name: "TestFirstSecretResolver", DisplayName = "Test first secret resolver", Description = "Registers a secret resolver for a composition test.")]
    public sealed class FirstSecretResolverFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services) => services.AddScoped<IRuntimeSecretResolver, FirstSecretResolver>();
    }

    [ShellFeature(name: "TestSecondSecretResolver", DisplayName = "Test second secret resolver", Description = "Registers a second secret resolver for a composition test.")]
    public sealed class SecondSecretResolverFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services) => services.AddScoped<IRuntimeSecretResolver, SecondSecretResolver>();
    }
}
