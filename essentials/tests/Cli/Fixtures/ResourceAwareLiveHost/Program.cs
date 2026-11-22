using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CShells.Configuration;
using CShells.Features;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tooling;
using Elsa.Cli.Tests;
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

if (args.Length > 0 && args[0] == "--candidate-test-descendant")
    return RunCandidateTestDescendant(args);

// This fixture can also act as a finite stdin-blocked child for the real process-owner proof.
// The shipped worker still uses its own assembly; only that explicit test selects this entrypoint.
if (args.Length == 1 && args[0] == "--candidate-inspection")
{
    var header = new byte[4096];
    Console.OpenStandardInput().ReadExactly(header);
    var reader = new Utf8JsonReader(header, isFinalBlock: false, state: default);
    while (reader.Read())
    {
        if (reader.TokenType != JsonTokenType.PropertyName || !reader.ValueTextEquals("hostDirectory"u8))
            continue;
        if (!reader.Read() || reader.TokenType != JsonTokenType.String)
            return 2;
        var marker = Path.Join(reader.GetString()!, "candidate-stdin-started.txt");
        return RunCandidateTestDescendant(["--candidate-test-descendant", marker, "60000"]);
    }
    return 2;
}

return 0;

static int RunCandidateTestDescendant(string[] arguments)
{
    if (arguments.Length != 3 || arguments[1].Length == 0 ||
        !int.TryParse(arguments[2], NumberStyles.None, CultureInfo.InvariantCulture, out var requestedHold))
        return 2;

    var holdMilliseconds = Math.Clamp(requestedHold, 0, 120_000);
    File.WriteAllText(arguments[1], ProcessIdentityReader.Current().ToMarker(), Encoding.ASCII);
    if (holdMilliseconds > 0)
        Thread.Sleep(holdMilliseconds);
    return 0;
}

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
        if (probeDefaults.GetValue<bool>("ThrowPrivateCanary"))
            throw new InvalidOperationException(PrivateCanaryPrefix + "composer-exception");

        if (probeDefaults["StartedMarker"] is { Length: > 0 } marker)
        {
            File.WriteAllText(marker, ProcessIdentityReader.Current().ToMarker(), Encoding.ASCII);
        }

        if (probeDefaults["DescendantMarker"] is { Length: > 0 } descendantMarker)
        {
            // Inherited pipes also exercise closure of streams held open by the descendant.
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };
            start.ArgumentList.Add(typeof(ResourceProbeShellDefaults).Assembly.Location);
            start.ArgumentList.Add("--candidate-test-descendant");
            start.ArgumentList.Add(descendantMarker);
            start.ArgumentList.Add("60000");
            // Disposing this wrapper leaves the finite-lived child running for the cleanup proof.
            (Process.Start(start) ?? throw new InvalidOperationException("The test descendant did not start.")).Dispose();
        }

        var holdMilliseconds = ReadBoundedValue(probeDefaults, "HoldMilliseconds", MaximumHoldMilliseconds);
        if (holdMilliseconds > 0)
            Thread.Sleep(holdMilliseconds);

        var standardErrorBytes = ReadBoundedValue(probeDefaults, "StandardErrorBytes", MaximumFloodBytes);
        if (standardErrorBytes > 0)
            WriteCanaryBytes(Console.OpenStandardError(), "stderr", standardErrorBytes);

        var standardOutputBytes = ReadBoundedValue(probeDefaults, "StandardOutputBytes", MaximumFloodBytes);
        if (standardOutputBytes > 0)
        {
            WriteCanaryBytes(Console.OpenStandardOutput(), "stdout", standardOutputBytes);
            if (probeDefaults["OutputWrittenMarker"] is { Length: > 0 } outputWrittenMarker)
                File.WriteAllText(outputWrittenMarker, ProcessIdentityReader.Current().ToMarker(), Encoding.ASCII);

            var holdAfterOutput = ReadBoundedValue(probeDefaults, "HoldAfterOutput", MaximumHoldMilliseconds);
            if (holdAfterOutput > 0)
                Thread.Sleep(holdAfterOutput);
        }
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
