using System.Text.Json;

namespace Elsa.Cli;

/// <summary>One module's entry in a committed <c>migration-plan.json</c>, as far as <c>script-check</c> reads it.</summary>
public sealed record PlanModule(string Module, string File, IReadOnlyList<string> Ids);

/// <summary>
/// The committed <c>migration-plan.json</c>, read as <c>script-check</c>'s own source of truth for what
/// should be regenerated (FR-044): the provider, the schema, and the modules the artifact claims to
/// describe — never a selection typed on the command line, which would let a check pass by checking
/// something else.
/// </summary>
public sealed record MigrationPlan(
    string Provider,
    string? Schema,
    string HostName,
    string? HostShell,
    string HostEnvironment,
    IReadOnlyList<PlanModule> Modules)
{
    public const string FileName = "migration-plan.json";

    /// <summary>The one manifest schema version this build reads.</summary>
    public const int SupportedSchemaVersion = 1;

    public static MigrationPlan Read(string directory)
    {
        var path = Path.Join(directory, FileName);
        if (!File.Exists(path))
        {
            throw CliRefusal.Resolution(
                "plan-missing",
                $"'{directory}' has no {FileName}, so there is nothing stating what it should contain. Run `dotnet elsa persistence script` to produce one.");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var plan = document.RootElement;
            var version = plan.GetProperty("schemaVersion").GetInt32();
            if (version != SupportedSchemaVersion)
            {
                throw CliRefusal.Resolution(
                    "plan-unsupported",
                    $"'{path}' declares manifest schemaVersion {version} and this build reads {SupportedSchemaVersion}.");
            }

            var host = plan.GetProperty("host");
            return new(
                plan.GetProperty("provider").GetString()!,
                plan.GetProperty("schema").GetString(),
                host.GetProperty("name").GetString() ?? "",
                host.GetProperty("shell").GetString(),
                host.GetProperty("environment").GetString() ?? "",
                [
                    .. plan.GetProperty("modules").EnumerateArray().Select(module => new PlanModule(
                        module.GetProperty("module").GetString()!,
                        module.GetProperty("file").GetString()!,
                        [.. module.GetProperty("migrations").GetProperty("ids").EnumerateArray().Select(id => id.GetString()!)]))
                ]);
        }
        catch (Exception failure) when (failure is JsonException or KeyNotFoundException or InvalidOperationException or IOException)
        {
            throw CliRefusal.Resolution("plan-unreadable", $"'{path}' is not a migration plan this build can read: {failure.Message}");
        }
    }
}
