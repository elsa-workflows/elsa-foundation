using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Cluster.Readability.Tests;

/// <summary>
/// <c>AddShellStartupValidation</c>: a check a shell feature registers with <c>ValidateOnStart</c> runs as the shell
/// activates and refuses the activation, which neither the generic host nor CShells does for a shell container (#2331).
/// </summary>
public sealed class ShellStartupValidationTests : IAsyncDisposable
{
    private const string ShellName = "validated";

    private readonly ServiceCollection _hostServices = new();
    private readonly InitializerLog _log = new();
    private ServiceProvider? _host;

    public ShellStartupValidationTests() => _hostServices.AddSingleton(_log);

    [Fact]
    public async Task A_check_a_shell_feature_registers_on_start_refuses_the_activation_before_an_earlier_Prepare_initializer_runs()
    {
        _hostServices.AddShellStartupValidation();

        var refusal = await Assert.ThrowsAnyAsync<Exception>(ActivateAsync<RefusedOptionsFeature>);

        Assert.Contains(RefusedOptionsFeature.Refusal, refusal.ToString(), StringComparison.Ordinal);
        Assert.Empty(_log.Ran);
    }

    /// <summary>
    /// What the hook exists for. If this fails because the activation is refused, CShells has started running a shell's
    /// startup validators itself, and the hook can go.
    /// </summary>
    [Fact]
    public async Task Without_it_the_same_shell_activates_with_its_check_never_run()
    {
        await ActivateAsync<RefusedOptionsFeature>();

        Assert.Equal([nameof(RecordingInitializer)], _log.Ran);
    }

    [Fact]
    public async Task A_shell_whose_container_registers_no_check_activates()
    {
        _hostServices.AddShellStartupValidation();

        await ActivateAsync<NoChecksFeature>();

        Assert.Equal([nameof(RecordingInitializer)], _log.Ran);
    }

    [Fact]
    public void Composing_it_twice_registers_one_initializer()
    {
        _hostServices.AddShellStartupValidation().AddShellStartupValidation();

        Assert.Single(_hostServices, descriptor =>
            descriptor.ImplementationInstance is ShellInitializerRegistration registration && registration.InitializerType == typeof(ShellStartupValidation));
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
            await _host.DisposeAsync();
    }

    private async Task ActivateAsync<TFeature>() where TFeature : class, IShellFeature
    {
        _hostServices.AddCShells(shells => shells
            .WithAssemblies(typeof(TFeature).Assembly)
            .AddShell(ShellName, shell => shell.WithFeature<TFeature>()));
        _host = _hostServices.BuildServiceProvider();
        await _host.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
    }

    /// <summary>A marker options type nothing resolves, as the composition checks that own a replacement contract use.</summary>
    private sealed class RefusedOptions;

    private sealed class InitializerLog
    {
        public List<string> Ran { get; } = [];
    }

    private sealed class RecordingInitializer(InitializerLog log) : IShellInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            log.Ran.Add(nameof(RecordingInitializer));
            return Task.CompletedTask;
        }
    }

    [ShellFeature(name: "ShellStartupValidationNoChecks")]
    public sealed class NoChecksFeature : IShellFeature
    {
        // The order of the EF provider-binding check, the lowest a shipped feature registers an initializer at.
        public void ConfigureServices(IServiceCollection services) =>
            services.AddShellInitializer<RecordingInitializer>(LifecyclePhase.Prepare, -100);
    }

    [ShellFeature(name: "ShellStartupValidationRefusedOptions")]
    public sealed class RefusedOptionsFeature : IShellFeature
    {
        public const string Refusal = "refused by the probe feature";

        public void ConfigureServices(IServiceCollection services)
        {
            new NoChecksFeature().ConfigureServices(services);
            services.AddOptions<RefusedOptions>().Validate(_ => false, Refusal).ValidateOnStart();
        }
    }
}
