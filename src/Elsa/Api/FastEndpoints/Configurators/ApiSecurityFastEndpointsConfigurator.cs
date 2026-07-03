using CShells.FastEndpoints.Contracts;
using Elsa.Api.FastEndpoints.Options;
using FastEndpoints;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elsa.Api.FastEndpoints.Configurators;

/// <summary>
/// Applies the shell's <see cref="ApiSecurityOptions"/> to the FastEndpoints configuration at
/// endpoint-mapping time. When the shell explicitly allows anonymous access, every endpoint
/// definition is relaxed via the global endpoint configurator and a prominent warning is logged;
/// otherwise the global configurator is cleared so endpoints keep the permissions they declared.
/// </summary>
public sealed class ApiSecurityFastEndpointsConfigurator : IFastEndpointsConfigurator
{
    private readonly ApiSecurityOptions _options;
    private readonly ILogger<ApiSecurityFastEndpointsConfigurator> _logger;

    public ApiSecurityFastEndpointsConfigurator(
        IOptions<ApiSecurityOptions> options,
        ILogger<ApiSecurityFastEndpointsConfigurator>? logger = null)
    {
        _options = options.Value;
        _logger = logger ?? NullLogger<ApiSecurityFastEndpointsConfigurator>.Instance;
    }

    /// <inheritdoc />
    public void Configure(Config config)
    {
        if (!_options.AllowAnonymous)
        {
            // FastEndpoints configuration is process-static: a configurator assigned while an
            // insecure shell was mapped would otherwise leak into this shell. Always assign.
            config.Endpoints.Configurator = null;
            return;
        }

        var shellName = _options.ShellName ?? "(unnamed shell)";

        _logger.LogWarning(
            "ELSA API SECURITY IS DISABLED for shell '{ShellName}': every Elsa endpoint in this shell "
            + "accepts anonymous requests. This mode must only be enabled through explicit configuration "
            + "for local development or testing — never in production.",
            shellName);

        config.Endpoints.Configurator = ep => ep.AllowAnonymous();
    }
}
