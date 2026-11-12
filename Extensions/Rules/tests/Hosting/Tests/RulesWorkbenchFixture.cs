using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workbench.Tests;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Elsa.Rules.Hosting.Tests;

/// <summary>
/// Composes the production Workbench with the Rules features enabled in its own copied <c>shells.json</c> over SQLite
/// files, under the opt-in source-build identity (a source-built Workbench has no packaged deployment receipt), plus the
/// Rules native delivery test hold. Everything else is the stock Development shell.
/// </summary>
internal sealed class RulesWorkbenchFixture : IAsyncDisposable
{
    private const string HoldVariable = "ELSA_HOLD_RULES_NATIVE_DELIVERY";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _ownedDirectory;
    private readonly List<WorkbenchProcess> _killedHosts = [];

    private RulesWorkbenchFixture(string ownedDirectory, string holdPath, WorkbenchProcess host)
    {
        _ownedDirectory = ownedDirectory;
        HoldPath = holdPath;
        Host = host;
    }

    public WorkbenchProcess Host { get; private set; }
    public string HoldPath { get; }
    public string EngagedMarkerPath => HoldPath + ".engaged";

    /// <summary>The console output of every host this fixture killed, oldest first.</summary>
    public IReadOnlyList<string> KilledHostOutputs => _killedHosts.Select(host => host.Output).ToArray();

    /// <summary>The process ids and exit codes of every host this fixture killed, oldest first.</summary>
    public IReadOnlyList<(int ProcessId, int ExitCode)> KilledHosts => _killedHosts.Select(host => (host.ProcessId, host.ExitCode)).ToArray();

    public static async Task<RulesWorkbenchFixture> StartAsync(double recoverySweepSeconds = 0.2)
    {
        var owned = Directory.CreateTempSubdirectory("elsa-rules-hosting-").FullName;
        var holdPath = Path.Combine(owned, "hold-rules-native-delivery");
        var shell = WorkbenchShell.Development with
        {
            Settings = new Dictionary<string, string>(WorkbenchShell.Development.Settings) { [HoldVariable] = holdPath }
        };
        WorkbenchProcess host;
        try
        {
            host = await WorkbenchProcess.StartAsync(shell, contentRoot => PrepareContentRoot(contentRoot, recoverySweepSeconds));
        }
        catch
        {
            Directory.Delete(owned, recursive: true);
            throw;
        }
        var fixture = new RulesWorkbenchFixture(owned, holdPath, host);
        try
        {
            await fixture.LoginAsync();
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    /// <summary>Freezes the next real terminal delivery after its lease is claimed and before the parent is resumed.</summary>
    public async Task HoldClaimedDeliveryAsync()
    {
        // Written whole and moved into place, so a worker poll overlapping the write never reads an empty hold.
        var staged = HoldPath + ".staged";
        await File.WriteAllTextAsync(staged, "hold-claim");
        File.Move(staged, HoldPath, overwrite: true);
    }

    public void ReleaseHold()
    {
        if (File.Exists(HoldPath)) File.Delete(HoldPath);
        if (File.Exists(EngagedMarkerPath)) File.Delete(EngagedMarkerPath);
    }

    /// <summary>Kills the Workbench process without cleanup and boots a fresh one over the same content root and SQLite files.</summary>
    public async Task CrashAndRestartAsync()
    {
        var killed = Host;
        Host = await killed.CrashAndRestartAsync(_ => ReleaseHold());
        _killedHosts.Add(killed);
        await LoginAsync();
    }

    public async Task LoginAsync()
    {
        using var response = await Host.Client.PostAsJsonAsync("/_elsa/identity/login", new { username = "admin", password = "Password123!" });
        Assert.True(response.IsSuccessStatusCode, $"Test-account login returned HTTP {(int)response.StatusCode}.{Environment.NewLine}{Host.Output}");
    }

    public async Task<JsonNode> GetAsync(string path)
    {
        using var response = await Host.Client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"GET {path} returned HTTP {(int)response.StatusCode}: {body}");
        return JsonNode.Parse(body)!;
    }

    public async Task<JsonNode> PostAsync(string path, object value)
    {
        using var response = await Host.Client.PostAsJsonAsync(path, value, Json);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} returned HTTP {(int)response.StatusCode}: {body}");
        return string.IsNullOrWhiteSpace(body) ? new JsonObject() : JsonNode.Parse(body)!;
    }

    public async Task<string> ActivityVersionIdAsync(string activityTypeKey)
    {
        var catalog = await GetAsync("/design/activities/definitions?search=" + Uri.EscapeDataString(activityTypeKey));
        var item = Assert.Single(catalog["items"]!.AsArray(), item => item!["definition"]!["activityTypeKey"]!.GetValue<string>() == activityTypeKey);
        return item!["definition"]!["headVersionId"]!.GetValue<string>();
    }

    /// <summary>One row per Rules run in the Workbench's Rules SQLite file: status, delivery owner and acknowledgement.</summary>
    public IReadOnlyList<RulesRunRow> ReadRuns()
    {
        var rows = new List<RulesRunRow>();
        using var connection = new SqliteConnection($"Data Source={RulesDatabasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Status, DeliveryOwner, DeliveryAcknowledged, TerminalId64 FROM elsa_rules_runtime_runs";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            rows.Add(new(reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetBoolean(2), !reader.IsDBNull(3)));
        return rows;
    }

    private string RulesDatabasePath => Path.Combine(Host.ContentRoot, "rules", "rules-runtime.db");

    private static void PrepareContentRoot(string contentRoot, double recoverySweepSeconds)
    {
        var rulesRoot = Path.Combine(contentRoot, "rules");
        Directory.CreateDirectory(rulesRoot);
        var shellPath = Path.Combine(contentRoot, "shells.json");
        var shells = JsonNode.Parse(File.ReadAllText(shellPath))!;
        var features = shells["CShells"]!["Shells"]!["default"]!["Features"]!.AsObject();
        features["WorkflowsRuntimeResumption"] = new JsonObject { ["SweepIntervalSeconds"] = recoverySweepSeconds };
        // A source-built Workbench has no packaged deployment receipt; the opt-in pins the loaded Rules assemblies instead.
        features["RulesRuntime"] = new JsonObject { ["AllowSourceBuildIdentity"] = true, ["PollIntervalMilliseconds"] = 50 };
        // The delivery lease has no heartbeat while a delivery is in flight, so the restarted host can reclaim the frozen
        // claim only after it lapses (database time); a short lease keeps each row short without changing the proof.
        features["RulesRuntimeEntityFrameworkCore"] = new JsonObject
        {
            ["Provider"] = "Sqlite", ["ConnectionString"] = $"Data Source={Path.Combine(rulesRoot, "rules-runtime.db")};Pooling=False",
            ["DeliveryLeaseSeconds"] = 3
        };
        features["RulesWorkflowsRuntime"] = new JsonObject { ["PollIntervalMilliseconds"] = 50 };
        features["RulesWorkflowsDesign"] = new JsonObject();
        File.WriteAllText(shellPath, shells.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            foreach (var killed in _killedHosts)
                await killed.DisposeAsync();
            await Host.DisposeAsync();
        }
        finally
        {
            if (Directory.Exists(_ownedDirectory))
                Directory.Delete(_ownedDirectory, recursive: true);
        }
    }
}

/// <summary>A Rules run row as the Workbench's SQLite file holds it; <see cref="Status"/> follows <c>RuleRunStatus</c> (1 is Succeeded).</summary>
internal sealed record RulesRunRow(int Status, string? DeliveryOwner, bool DeliveryAcknowledged, bool HasTerminal);
