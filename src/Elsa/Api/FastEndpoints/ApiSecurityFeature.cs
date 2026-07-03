using CShells.Features;
using Elsa.Api.FastEndpoints.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Api.FastEndpoints;

/// <summary>
/// Shell feature that binds <see cref="ApiSecurityOptions"/> for the shell. Enable this feature
/// and set <c>"ApiSecurity": { "AllowAnonymous": true }</c> in the shell configuration to
/// explicitly disable Elsa endpoint security for that shell only. Without this feature (or with
/// <see cref="AllowAnonymous"/> left <c>false</c>) every Elsa endpoint requires authorization.
/// </summary>
[ShellFeature(
    name: "ApiSecurity",
    DisplayName = "Elsa API Security",
    Description = "Binds per-shell Elsa endpoint security options. Allows explicitly disabling endpoint security for a single shell (development/testing only)."
)]
public sealed class ApiSecurityFeature : IShellFeature
{
    private readonly ShellFeatureContext _context;

    public ApiSecurityFeature(ShellFeatureContext context)
    {
        _context = context;
    }

    /// <summary>
    /// When <c>true</c>, every Elsa endpoint in this shell accepts anonymous requests.
    /// Defaults to <c>false</c>. Enabling it produces a prominent startup warning.
    /// </summary>
    public bool AllowAnonymous { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
        var shellName = _context.Settings.Id.ToString();

        services.Configure<ApiSecurityOptions>(options =>
        {
            options.AllowAnonymous = AllowAnonymous;
            options.ShellName = shellName;
        });
    }
}
