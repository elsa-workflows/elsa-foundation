using CShells;
using CShells.Lifecycle;

namespace Elsa.Studio.Hosting.Nuplane;

/// <summary>Composes a shell blueprint for feature administration, retrying when configuration changed while it composed.</summary>
/// <remarks>
/// A feature save reloads the host configuration, and the file watcher's debounced reload fires again afterwards. When either
/// lands while CShells composes the blueprint, CShells refuses the composition and asks for a retry. Without one, a second
/// apply that loads the catalog during the first apply's save failed with that refusal instead of reaching the revision check
/// and reporting a conflict (#4092). Only that refusal is retried, at most three attempts in all, each after a pause longer
/// than the file provider's default reload delay (250 ms), the same policy <c>ShellReloader</c> uses for the reload after a
/// save; every other failure propagates on its first attempt.
/// </remarks>
internal static class ShellComposition
{
    internal const int MaxAttempts = 3;
    internal static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromMilliseconds(300);

    public static Task<ShellSettings> ComposeAsync(IShellBlueprint blueprint, CancellationToken cancellationToken) =>
        ComposeAsync(blueprint, DefaultRetryDelay, cancellationToken);

    internal static async Task<ShellSettings> ComposeAsync(IShellBlueprint blueprint, TimeSpan retryDelay, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await blueprint.ComposeAsync(cancellationToken);
            }
            catch (InvalidOperationException error) when (attempt < MaxAttempts && IsConfigurationChangedDuringComposition(error))
            {
                await Task.Delay(retryDelay, cancellationToken);
            }
        }
    }

    // CShells exposes no dedicated type for this refusal. Its message is "Configuration changed <phase> shell '<name>'. Retry activation or reload."
    internal static bool IsConfigurationChangedDuringComposition(Exception error) =>
        error is InvalidOperationException
        && error.Message.StartsWith("Configuration changed ", StringComparison.Ordinal)
        && error.Message.EndsWith("Retry activation or reload.", StringComparison.Ordinal);
}
