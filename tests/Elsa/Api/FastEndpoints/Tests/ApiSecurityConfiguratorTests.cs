using System.Reflection;
using Elsa.Api.FastEndpoints.Configurators;
using Elsa.Api.FastEndpoints.Options;
using Elsa.Api.FastEndpoints.Tests.Support;
using FastEndpoints;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Api.FastEndpoints.Tests;

/// <summary>
/// Behavioral tests for <see cref="ApiSecurityFastEndpointsConfigurator"/>. These exercise the real
/// class against FastEndpoints' process-static <see cref="Config"/> to prove the two properties that
/// kill MS-12 at the unit level: a secure shell always clears the global endpoint configurator (so a
/// previously mapped insecure shell cannot leak its relaxer), and an insecure shell relaxes every
/// endpoint while logging exactly one prominent warning that names the shell.
/// </summary>
public sealed class ApiSecurityConfiguratorTests
{
    [Fact]
    public void Secure_options_clear_the_endpoint_configurator_and_log_nothing()
    {
        // Simulate a relaxer leaked into the static Config by a previously mapped insecure shell.
        var config = new Config();
        config.Endpoints.Configurator = _ => { };

        var (configurator, logs) = CreateConfigurator(allowAnonymous: false, shellName: "secure");
        configurator.Configure(config);

        Assert.Null(ReadConfigurator(config));
        Assert.DoesNotContain(logs.Logs, log => log.Level == LogLevel.Warning);
    }

    [Fact]
    public void Insecure_options_relax_every_endpoint_and_log_a_single_named_warning()
    {
        var config = new Config();
        config.Endpoints.Configurator = null;

        var (configurator, logs) = CreateConfigurator(allowAnonymous: true, shellName: "dev-shell");
        configurator.Configure(config);

        var relax = ReadConfigurator(config);
        Assert.NotNull(relax);

        var definition = new EndpointDefinition(typeof(object), typeof(object), typeof(object));
        relax!(definition);
        Assert.NotNull(definition.AnonymousVerbs);

        var warnings = logs.Logs.Where(log => log.Level == LogLevel.Warning).ToList();
        Assert.Single(warnings);
        Assert.Contains("dev-shell", warnings[0].Message);
        Assert.Contains("DISABLED", warnings[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_secure_shell_clears_an_insecure_shells_leaked_relaxer()
    {
        var config = new Config();
        config.Endpoints.Configurator = null;

        // Insecure shell maps first and installs its relaxer into the static Config.
        var (insecure, _) = CreateConfigurator(allowAnonymous: true, shellName: "insecure");
        insecure.Configure(config);
        Assert.NotNull(ReadConfigurator(config));

        // A different, secure shell maps next; its configurator must deterministically clear the leak.
        var (secure, _) = CreateConfigurator(allowAnonymous: false, shellName: "secure");
        secure.Configure(config);

        Assert.Null(ReadConfigurator(config));
    }

    private static (ApiSecurityFastEndpointsConfigurator Configurator, RecordingLoggerProvider Logs) CreateConfigurator(bool allowAnonymous, string shellName)
    {
        var logs = new RecordingLoggerProvider();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var logger = factory.CreateLogger<ApiSecurityFastEndpointsConfigurator>();

        var options = Microsoft.Extensions.Options.Options.Create(new ApiSecurityOptions
        {
            AllowAnonymous = allowAnonymous,
            ShellName = shellName
        });

        return (new ApiSecurityFastEndpointsConfigurator(options, logger), logs);
    }

    private static Action<EndpointDefinition>? ReadConfigurator(Config config)
    {
        // FastEndpoints exposes Configurator with an internal getter; read it reflectively for tests.
        var property = typeof(EndpointOptions).GetProperty(
            nameof(EndpointOptions.Configurator),
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        return (Action<EndpointDefinition>?)property!.GetValue(config.Endpoints);
    }
}
