using System.Diagnostics;
using System.Globalization;
using System.Text;
using CShells.Configuration;
using CShells.Features;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tooling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: EfModule(
    "Acme.ResourceProbe",
    typeof(ResourceProbeDbContext),
    HistoryModule = "AcmeResourceProbe",
    Sqlite = typeof(ResourceProbeDbContext),
    PostMigration = [typeof(ResourceProbePostMigrationAction)])]
[assembly: EfToolingShellDefaults(typeof(ResourceProbeShellDefaults))]

return 0;

[ShellFeature(name: "ResourceProbe", DisplayName = "Resource probe")]
[UsesEfModule("Acme.ResourceProbe")]
[EfPersistenceResourceParticipant]
public sealed class ResourceProbeFeature : IShellFeature
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionName { get; set; }
    public string? ConnectionString { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
    }
}

public sealed class ResourceProbeDbContext : DbContext
{
    public ResourceProbeDbContext(DbContextOptions<ResourceProbeDbContext> options) : base(options)
    {
        var marker = ResourceProbeShellDefaults.ContextMarkerPath;
        if (marker is not { Length: > 0 })
            marker = Environment.GetEnvironmentVariable("ELSA_RESOURCE_PROBE_CONTEXT_MARKER");
        if (marker is { Length: > 0 } path)
            File.WriteAllText(path, "constructed");
    }
}

public sealed class ResourceProbePostMigrationAction : IEfPostMigrationAction
{
    public ResourceProbePostMigrationAction()
    {
        var marker = ResourceProbeShellDefaults.ActionMarkerPath;
        if (marker is not { Length: > 0 })
            marker = Environment.GetEnvironmentVariable("ELSA_RESOURCE_PROBE_ACTION_MARKER");
        if (marker is { Length: > 0 } path)
            File.WriteAllText(path, "constructed");
    }

    public string Id => "resource-probe";
    public string Kind => "test";
    public string RequiredWhen => "never";
    public string Audit => "none";
    public Task<bool> AuditAsync(DbContext context, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task RunAsync(DbContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class ResourceProbeShellDefaults : IEfToolingShellDefaults
{
    private const int MaximumHoldMilliseconds = 120_000;
    private const int MaximumFloodBytes = 16 * 1024 * 1024;
    private const string PrivateCanaryPrefix = "candidate-private-canary-2177-";

    internal static string? ContextMarkerPath { get; private set; }
    internal static string? ActionMarkerPath { get; private set; }

    public void Configure(ShellBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        var probeDefaults = configuration.GetSection("ProbeDefaults");
        if (probeDefaults.Exists())
        {
            ContextMarkerPath = probeDefaults["ContextMarker"];
            ActionMarkerPath = probeDefaults["ActionMarker"];
            ConfigureAdverseChild(probeDefaults);
        }

        if (configuration.GetValue<bool>("ProbeDefaults:WriteConsoleCanary"))
        {
            Console.WriteLine("private-console-canary");
            Console.Error.WriteLine("private-console-canary");
        }
        if (configuration.GetValue<bool>("ProbeDefaults:EnableDiagnostics"))
        {
            builder.WithFeature("DiagnosticsStructuredLogsEntityFrameworkCore");
            builder.WithFeature("DiagnosticsOpenTelemetryEntityFrameworkCore");
        }
    }

    private static void ConfigureAdverseChild(IConfigurationSection probeDefaults)
    {
        if (probeDefaults["StartedMarker"] is { Length: > 0 } marker)
        {
            using var process = Process.GetCurrentProcess();
            var identity = FormattableString.Invariant($"{process.Id}|{process.StartTime.ToUniversalTime().Ticks}");
            File.WriteAllText(marker, identity, Encoding.ASCII);
        }

        var holdMilliseconds = ReadBoundedValue(probeDefaults, "HoldMilliseconds", MaximumHoldMilliseconds);
        if (holdMilliseconds > 0)
            Thread.Sleep(holdMilliseconds);

        var standardErrorBytes = ReadBoundedValue(probeDefaults, "StandardErrorBytes", MaximumFloodBytes);
        if (standardErrorBytes > 0)
            WriteCanaryBytes(Console.OpenStandardError(), "stderr", standardErrorBytes);

        var standardOutputBytes = ReadBoundedValue(probeDefaults, "StandardOutputBytes", MaximumFloodBytes);
        if (standardOutputBytes > 0)
            WriteCanaryBytes(Console.OpenStandardOutput(), "stdout", standardOutputBytes);
    }

    private static int ReadBoundedValue(IConfiguration configuration, string key, int maximum) =>
        int.TryParse(configuration[key], NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? Math.Min(value, maximum)
            : 0;

    private static void WriteCanaryBytes(Stream output, string streamName, int byteCount)
    {
        var pattern = Encoding.ASCII.GetBytes($"{PrivateCanaryPrefix}{streamName}|");
        var buffer = new byte[Math.Min(64 * 1024, byteCount)];
        for (var index = 0; index < buffer.Length; index++)
            buffer[index] = pattern[index % pattern.Length];

        var remaining = byteCount;
        while (remaining > 0)
        {
            var count = Math.Min(buffer.Length, remaining);
            output.Write(buffer, 0, count);
            remaining -= count;
        }
        output.Flush();
    }
}
