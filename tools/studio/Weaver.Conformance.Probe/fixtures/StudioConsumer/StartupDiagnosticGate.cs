using System.Text.Json;
using Nuplane.Abstractions.PackageStoreProtection;

// Optional application observability only. No lock, provider or resource policy changes.
static class StartupDiagnosticGate
{
    public static async Task ObserveAsync(IConfiguration configuration, string contentRoot, string stage,
        string observedMembership, Exception? failure = null)
    {
        var configured = configuration["Qualification:DiagnosticGateDirectory"];
        if (string.IsNullOrWhiteSpace(configured)) return;
        if (!Path.IsPathFullyQualified(configured))
            throw new InvalidOperationException("The diagnostic gate requires an absolute path inside this owned content root.");
        var root = Path.GetFullPath(contentRoot).TrimEnd(Path.DirectorySeparatorChar);
        var directory = Path.GetFullPath(configured);
        if (!directory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("The diagnostic gate must remain inside this owned content root.");
        for (var parent = new DirectoryInfo(directory); parent is not null && parent.FullName != root; parent = parent.Parent)
            if (parent.LinkTarget is not null)
                throw new InvalidOperationException("The diagnostic gate cannot traverse a symbolic link.");
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var ready = Path.Combine(directory, stage + ".ready.json");
        var release = Path.Combine(directory, stage + ".release");
        if (File.Exists(ready) || File.Exists(release))
            throw new InvalidOperationException("Use a fresh diagnostic directory for each owned process.");
        var record = new {
            stage, pid = Environment.ProcessId, observedAt = DateTimeOffset.UtcNow,
            publicMembershipObservation = observedMembership,
            failureType = failure?.GetType().FullName,
            failureAdmissionReason = (failure as PackageStoreAdmissionException)?.Reason.ToString(),
            boundedWaitSeconds = Math.Clamp(configuration.GetValue("Qualification:DiagnosticGateSeconds", 60), 1, 60)
        };
        await File.WriteAllTextAsync(ready + ".tmp", JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(ready + ".tmp", ready);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(ready, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(record.boundedWaitSeconds));
        try {
            while (!File.Exists(release)) await Task.Delay(100, timeout.Token);
        } catch (OperationCanceledException) when (timeout.IsCancellationRequested) {
            await File.WriteAllTextAsync(Path.Combine(directory, stage + ".timed-out"), DateTimeOffset.UtcNow.ToString("O"));
        }
    }
}
