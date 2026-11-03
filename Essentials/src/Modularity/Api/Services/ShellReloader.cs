using CShells.Lifecycle;
using Elsa.Modularity.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace Elsa.Modularity.Api.Services;

/// <summary>Reloads the current shell after a feature apply.</summary>
/// <remarks>
/// CShells reports a failed reload (composition, provider build or an initializer threw) as <see cref="ReloadResult.Error"/>
/// rather than throwing, and the previous generation keeps serving. The count stays 0 for that outcome, and the error is
/// logged with its cause so an operator, or a qualification run reading the host log, can see why the apply changed
/// nothing; before, it was discarded.
/// <para>
/// A save reloads the host configuration synchronously, but the file watcher's own debounced reload still fires afterwards.
/// When it lands while CShells composes the new generation, CShells refuses that generation as composed from changing
/// configuration and asks for a retry. Only that refusal is retried, at most three attempts in all; every other failure is
/// reported on its first attempt.
/// </para>
/// </remarks>
public sealed class ShellReloader(IShellRegistry shellRegistry, IShell shell, ILogger<ShellReloader>? logger = null) : IShellReloader
{
    private const int MaxAttempts = 3;

    public async Task<int> ReloadAsync(CancellationToken cancellationToken = default)
    {
        var name = shell.Descriptor.Name;
        for (var attempt = 1; ; attempt++)
        {
            var result = await shellRegistry.ReloadAsync(name, cancellationToken);
            if (result.Error is null) return 1;
            if (attempt < MaxAttempts && IsConfigurationChangedDuringComposition(result.Error))
            {
                logger?.LogInformation("Configuration changed while shell {ShellName} was composing; reloading again (attempt {Attempt} of {MaxAttempts}).", name, attempt + 1, MaxAttempts);
                continue;
            }

            logger?.LogError(result.Error, "Reloading shell {ShellName} failed; the previous generation keeps serving and the applied features take effect only after a successful reload.", name);
            return 0;
        }
    }

    // CShells exposes no dedicated type for this refusal. Its message is "Configuration changed <phase> shell '<name>'. Retry activation or reload."
    private static bool IsConfigurationChangedDuringComposition(Exception error) =>
        error is InvalidOperationException
        && error.Message.StartsWith("Configuration changed ", StringComparison.Ordinal)
        && error.Message.EndsWith("Retry activation or reload.", StringComparison.Ordinal);
}
