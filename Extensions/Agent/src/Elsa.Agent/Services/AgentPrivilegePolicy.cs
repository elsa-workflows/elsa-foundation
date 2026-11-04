using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Elsa.Agent.Services;

/// <summary>Copies the current host observation; neither a session nor a startup value grants a class.</summary>
internal sealed class AgentPrivilegePolicy(IOptionsMonitor<AgentPrivilegePolicyOptions> options)
{
    internal AgentPrivilegePolicySnapshot Read()
    {
        var current = options.CurrentValue;
        return new(current.Administrative, current.Irrecoverable, current.Activation);
    }
}

internal sealed record AgentPrivilegePolicySnapshot(bool Administrative, bool Irrecoverable, bool Activation);

/// <summary>
/// Options infrastructure is application-wide and holds only configuration, never scoped execution or a principal.
/// Missing configuration preserves legacy host composition and the default-off values; actual reloads invalidate the monitor.
/// </summary>
internal sealed class AgentPrivilegePolicyConfiguration(IConfiguration? configuration = null)
    : IConfigureOptions<AgentPrivilegePolicyOptions>, IOptionsChangeTokenSource<AgentPrivilegePolicyOptions>
{
    private static readonly IChangeToken NoChanges = new CancellationChangeToken(CancellationToken.None);
    public string Name => Options.DefaultName;
    public IChangeToken GetChangeToken() => configuration?.GetReloadToken() ?? NoChanges;

    public void Configure(AgentPrivilegePolicyOptions options)
    {
        if (configuration is null) return;
        var section = configuration.GetSection(AgentPrivilegePolicyOptions.SectionName);
        options.Administrative = Read(section, nameof(options.Administrative), options.Administrative);
        options.Irrecoverable = Read(section, nameof(options.Irrecoverable), options.Irrecoverable);
        options.Activation = Read(section, nameof(options.Activation), options.Activation);
    }

    private static bool Read(IConfigurationSection section, string key, bool fallback)
    {
        if (section[key] is not { } raw) return fallback;
        if (bool.TryParse(raw, out var value)) return value;
        // Do not include a misconfigured value: it might have been placed in this section by mistake.
        throw new InvalidOperationException($"Configuration key '{section.Path}:{key}' must be true or false.");
    }
}
