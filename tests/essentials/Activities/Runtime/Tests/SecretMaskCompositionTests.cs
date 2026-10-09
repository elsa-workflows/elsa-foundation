using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Activities.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// <see cref="IRuntimeSecretMask"/> is a replacement contract (framework constitution §2.6.2, spec 188 T074): the runtime
/// registers one scoped default, a host replaces it rather than adding a second, and a host that composes more than one
/// does not start, whatever order they were registered in, naming every registration.
/// </summary>
public sealed class SecretMaskCompositionTests
{
    private const string ShellName = "secret-masks";
    private readonly ServiceCollection _services = new();

    public SecretMaskCompositionTests() => new ActivitiesRuntimeFeature().ConfigureServices(_services);

    [Fact]
    public void The_runtime_registers_one_scoped_default_mask()
    {
        var services = new ServiceCollection().AddWorkflowRuntime();

        var registration = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRuntimeSecretMask));
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
        Assert.Equal(typeof(DefaultRuntimeSecretMask), registration.ImplementationType);
    }

    [Theory]
    [InlineData(typeof(FirstSecretMask), typeof(SecondSecretMask))]
    [InlineData(typeof(SecondSecretMask), typeof(FirstSecretMask))]
    public async Task Two_composed_masks_fail_startup_naming_both_in_either_registration_order(Type registeredFirst, Type registeredSecond)
    {
        _services.AddScoped(typeof(IRuntimeSecretMask), registeredFirst);
        _services.AddScoped(typeof(IRuntimeSecretMask), registeredSecond);

        var exception = await Assert.ThrowsAsync<MultipleRuntimeSecretMasksException>(InitializeAsync);

        Assert.Equal([$"'{registeredFirst.FullName}'", $"'{registeredSecond.FullName}'"], exception.Implementations);
        Assert.Contains(registeredFirst.FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(registeredSecond.FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_default_replaced_with_services_Replace_starts_and_resolves_the_replacement()
    {
        _services.TryAddScoped<IRuntimeSecretMask, DefaultRuntimeSecretMask>();
        _services.Replace(ServiceDescriptor.Scoped<IRuntimeSecretMask, FirstSecretMask>());

        await InitializeAsync();

        Assert.Equal(typeof(FirstSecretMask), Assert.Single(_services, descriptor => descriptor.ServiceType == typeof(IRuntimeSecretMask)).ImplementationType);
        await using var provider = _services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        Assert.IsType<FirstSecretMask>(scope.ServiceProvider.GetRequiredService<IRuntimeSecretMask>());
    }

    [Fact]
    public async Task Composing_the_runtime_twice_registers_the_check_once()
    {
        new ActivitiesRuntimeFeature().ConfigureServices(_services);

        Assert.Single(_services, descriptor => descriptor.ServiceType == typeof(RuntimeSecretMaskComposition));
        await using var provider = _services.BuildServiceProvider();
        Assert.Single(provider.GetServices<IShellInitializer>(), initializer => initializer is RuntimeSecretMaskCompositionValidator);
    }

    [Fact]
    public async Task A_shell_composing_two_masks_fails_activation_naming_both()
    {
        var services = new ServiceCollection();
        services.AddCShells(builder => builder
            .WithAssemblies(typeof(ActivitiesRuntimeFeature).Assembly, typeof(SecretMaskCompositionTests).Assembly)
            .AddShell(ShellName, shell => shell
                .WithFeature<ActivitiesRuntimeFeature>()
                .WithFeature<FirstSecretMaskFeature>()
                .WithFeature<SecondSecretMaskFeature>()));
        await using var root = services.BuildServiceProvider();

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName, CancellationToken.None));

        var conflict = Assert.IsType<MultipleRuntimeSecretMasksException>(exception as MultipleRuntimeSecretMasksException ?? exception.InnerException);
        Assert.Contains(typeof(FirstSecretMask).FullName!, conflict.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(SecondSecretMask).FullName!, conflict.Message, StringComparison.Ordinal);
    }

    /// <summary>Runs the shell initializers the composed services register, as shell activation does.</summary>
    private async Task InitializeAsync()
    {
        await using var provider = _services.BuildServiceProvider(validateScopes: true);
        foreach (var initializer in provider.GetServices<IShellInitializer>())
            await initializer.InitializeAsync();
    }

    public sealed class FirstSecretMask : IRuntimeSecretMask
    {
        public void Register(string activityExecutionId, string referenceName, string value) { }
        public bool HasRegistrations(string activityExecutionId) => false;
        public string Mask(string activityExecutionId, string text) => text;
        public void Release(string activityExecutionId) { }
    }

    public sealed class SecondSecretMask : IRuntimeSecretMask
    {
        public void Register(string activityExecutionId, string referenceName, string value) { }
        public bool HasRegistrations(string activityExecutionId) => false;
        public string Mask(string activityExecutionId, string text) => text;
        public void Release(string activityExecutionId) { }
    }

    [ShellFeature(name: "TestFirstSecretMask", DisplayName = "Test first secret mask", Description = "Registers a secret mask for a composition test.")]
    public sealed class FirstSecretMaskFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services) => services.AddScoped<IRuntimeSecretMask, FirstSecretMask>();
    }

    [ShellFeature(name: "TestSecondSecretMask", DisplayName = "Test second secret mask", Description = "Registers a second secret mask for a composition test.")]
    public sealed class SecondSecretMaskFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services) => services.AddScoped<IRuntimeSecretMask, SecondSecretMask>();
    }
}
