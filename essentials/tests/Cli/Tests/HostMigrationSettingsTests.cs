using System.Reflection;
using Elsa.Persistence.EntityFramework;
using Xunit;

namespace Elsa.Cli.Tests;

/// <summary>
/// The tool reads the SQLite migration lock bound from a host's appsettings by key. It cannot reference the options that host binds
/// it with, so this project, which can see both, holds the two to each other, and holds the reading to what the host would bind.
/// </summary>
public sealed class HostMigrationSettingsTests
{
    private static readonly Type Settings = Assembly.Load("Elsa.Cli").GetType("Elsa.Cli.HostMigrationSettings", throwOnError: true)!;

    [Fact]
    public void The_key_the_tool_reads_is_the_one_the_hosts_migrate_options_bind()
    {
        // Elsa.Cli keeps its internals to itself, so the constant is read by reflection.
        var key = Settings.GetField("SqliteMigrationLockStaleAfterKey", BindingFlags.Public | BindingFlags.Static)!.GetRawConstantValue();

        Assert.Equal($"{EfMigrateOptions.SectionName}:{nameof(EfMigrateOptions.SqliteMigrationLockStaleAfter)}", key);
    }

    [Fact]
    public void The_bound_is_read_from_the_hosts_appsettings_and_an_overlay_wins()
    {
        using var host = new TempHost("""{ "Elsa": { "Persistence": { "EntityFramework": { "Migrate": { "SqliteMigrationLockStaleAfter": "00:30:00" } } } } }""",
            """{ "Elsa": { "Persistence": { "EntityFramework": { "Migrate": { "SqliteMigrationLockStaleAfter": "00:01:00" } } } } }""");

        Assert.Equal(TimeSpan.FromMinutes(1), Read(host.Directory, "Staging"));
        Assert.Equal(TimeSpan.FromMinutes(30), Read(host.Directory, "Other"));
    }

    [Fact]
    public void A_host_that_configures_no_bound_leaves_it_to_the_persistence_default()
    {
        using var host = new TempHost("""{ "Logging": {} }""", null);

        Assert.Null(Read(host.Directory, "Staging"));
    }

    [Theory]
    [InlineData("soon")]
    [InlineData("00:00:00")]
    [InlineData("-00:01:00")]
    public void A_bound_that_is_not_a_positive_time_span_is_refused(string value)
    {
        using var host = new TempHost($$"""{ "Elsa": { "Persistence": { "EntityFramework": { "Migrate": { "SqliteMigrationLockStaleAfter": "{{value}}" } } } } }""", null);

        var failure = Assert.Throws<TargetInvocationException>(() => Read(host.Directory, "Staging"));

        Assert.Contains("positive time span", failure.InnerException!.Message, StringComparison.Ordinal);
    }

    private static TimeSpan? Read(string directory, string environment) =>
        (TimeSpan?)Settings.GetMethod("SqliteMigrationLockStaleAfter", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [directory, environment]);

    private sealed class TempHost : IDisposable
    {
        public TempHost(string appsettings, string? overlay)
        {
            Directory = System.IO.Directory.CreateTempSubdirectory("elsa-cli-migration-settings-").FullName;
            File.WriteAllText(Path.Join(Directory, "appsettings.json"), appsettings);
            if (overlay is not null)
                File.WriteAllText(Path.Join(Directory, "appsettings.Staging.json"), overlay);
        }

        public string Directory { get; }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
