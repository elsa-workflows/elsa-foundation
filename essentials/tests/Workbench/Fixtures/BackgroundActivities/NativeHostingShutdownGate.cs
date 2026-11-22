using System.Globalization;
using Elsa.Tasks.Core;

namespace Elsa.Workbench.Tests.BackgroundActivities;

/// <summary>
/// Test-only stop gate used to observe the real membership drain while the owning Workbench is still alive.
/// It is inert unless the owner role is stopping and the scenario creates the hold marker.
/// </summary>
internal sealed class NativeHostingShutdownGate(string journalDirectory, string role) : IBackgroundTask
{
    internal const string HoldFileName = "graceful.stop.hold";
    internal const string ReleaseFileName = "graceful.stop.release";
    internal const string EnteredFileName = "graceful.stop.entered.owner";
    internal const string TimedOutFileName = "graceful.stop.timed-out.owner";

    private static readonly TimeSpan MaximumHold = TimeSpan.FromSeconds(24);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!StringComparer.Ordinal.Equals(role, "owner"))
            return;

        var holdPath = Path.Combine(journalDirectory, HoldFileName);
        if (!File.Exists(holdPath))
            return;

        Directory.CreateDirectory(journalDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(journalDirectory, EnteredFileName),
            Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
            cancellationToken);

        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(MaximumHold);
        try
        {
            var releasePath = Path.Combine(journalDirectory, ReleaseFileName);
            while (!File.Exists(releasePath))
                await Task.Delay(PollInterval, bounded.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await File.WriteAllTextAsync(
                Path.Combine(journalDirectory, TimedOutFileName),
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                CancellationToken.None);
            throw new TimeoutException($"The native-hosting shutdown gate exceeded {MaximumHold}.");
        }
    }
}
