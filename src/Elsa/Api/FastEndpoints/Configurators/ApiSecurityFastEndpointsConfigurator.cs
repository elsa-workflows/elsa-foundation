using CShells.FastEndpoints.Contracts;
using Elsa.Api.FastEndpoints.Options;
using FastEndpoints;
using Microsoft.Extensions.Hosting;
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
/// <remarks>
/// The <c>AllowAnonymous</c> kill-switch is a locked product decision: <b>there is no auth off-switch in
/// production</b>. It is honored only when the host environment is Development. Outside Development the flag
/// is ignored — the shell stays secure — and a prominent warning is logged explaining that the request was
/// neutralized. This mirrors the repo's other dangerous-flag guardrails (the identity
/// <c>SecurityDefaultGuard</c>s), which likewise refuse weak defaults outside development, and follows this
/// configurator's existing "warn, don't crash" style rather than hard-faulting a misconfigured production
/// startup into a boot loop.
/// </remarks>
public sealed class ApiSecurityFastEndpointsConfigurator : IFastEndpointsConfigurator
{
    private readonly ApiSecurityOptions _options;
    private readonly bool _isDevelopment;
    private readonly ILogger<ApiSecurityFastEndpointsConfigurator> _logger;

    public ApiSecurityFastEndpointsConfigurator(
        IOptions<ApiSecurityOptions> options,
        IHostEnvironment? environment = null,
        ILogger<ApiSecurityFastEndpointsConfigurator>? logger = null)
    {
        _options = options.Value;
        // No environment resolved (e.g. a pure unit test) is treated as non-production/dev so the flag
        // behaves as authored; a real host always supplies IHostEnvironment.
        _isDevelopment = environment?.IsDevelopment() ?? true;
        _logger = logger ?? NullLogger<ApiSecurityFastEndpointsConfigurator>.Instance;
    }

    /// <inheritdoc />
    public void Configure(Config config)
    {
        var shellName = _options.ShellName ?? "(unnamed shell)";

        // Locked decision: the anonymous kill-switch is never honored outside Development. If a
        // production shell requests it, ignore it — stay secure — and log why, prominently.
        if (_options.AllowAnonymous && !_isDevelopment)
        {
            _logger.LogWarning(
                "ELSA API SECURITY: the ApiSecurity.AllowAnonymous kill-switch is set for shell "
                + "'{ShellName}' but the host is not in the Development environment. The flag is IGNORED "
                + "and endpoint security stays ENABLED. Disabling authentication is not permitted outside "
                + "Development — remove ApiSecurity.AllowAnonymous from this deployment.",
                shellName);
        }

        if (!_options.AllowAnonymous || !_isDevelopment)
        {
            // FastEndpoints configuration is process-static: a configurator assigned while an
            // insecure shell was mapped would otherwise leak into this shell. Always assign.
            config.Endpoints.Configurator = null;
            return;
        }

        _logger.LogWarning(
            "ELSA API SECURITY IS DISABLED for shell '{ShellName}': every Elsa endpoint in this shell "
            + "accepts anonymous requests. This mode is honored only in the Development environment and "
            + "must only be enabled through explicit configuration for local development or testing.",
            shellName);

        config.Endpoints.Configurator = ep => ep.AllowAnonymous();
    }
}
