using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workbench.Tests;
using Xunit;

namespace Elsa.Etl.Hosting.Tests;

/// <summary>
/// Composes the production Workbench with the ETL features enabled in its own copied <c>shells.json</c>, the way the
/// ETL e2e fixture does, plus the native result delivery test hold. Everything else is the stock Development shell.
/// </summary>
internal sealed class EtlWorkbenchFixture : IAsyncDisposable
{
    private const string HoldVariable = "ELSA_HOLD_NATIVE_RESULT_DELIVERY";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _ownedDirectory;
    private readonly List<WorkbenchProcess> _killedHosts = [];

    private EtlWorkbenchFixture(string ownedDirectory, string holdPath, WorkbenchProcess host)
    {
        _ownedDirectory = ownedDirectory;
        HoldPath = holdPath;
        Host = host;
    }

    public WorkbenchProcess Host { get; private set; }
    public string HoldPath { get; }
    public string EngagedMarkerPath => HoldPath + ".engaged";

    /// <summary>The process ids and exit codes of every host this fixture killed, oldest first.</summary>
    public IReadOnlyList<(int ProcessId, int ExitCode)> KilledHosts => _killedHosts.Select(host => (host.ProcessId, host.ExitCode)).ToArray();

    public static async Task<EtlWorkbenchFixture> StartAsync(double recoverySweepSeconds = 0.2)
    {
        var owned = Directory.CreateTempSubdirectory("elsa-etl-hosting-").FullName;
        var holdPath = Path.Combine(owned, "hold-native-result-delivery");
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
        var fixture = new EtlWorkbenchFixture(owned, holdPath, host);
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

    /// <summary>Freezes the next real result delivery after its lease is claimed and before the parent is resumed.</summary>
    public async Task HoldClaimedResultDeliveryAsync()
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

    /// <summary>Publishes the finite file pipeline over the fixture's own CSV inputs and returns the immutable version selection token.</summary>
    public async Task<string> PublishPipelineAsync()
    {
        var definitionId = "finite-file-" + Guid.NewGuid().ToString("N")[..12];
        var pipeline = new
        {
            bindings = new { source = "source", lookup = "lookup", output = "output", errors = "errors" },
            sourceSchema = new object[] { new { name = "id", type = "int64", required = true }, new { name = "name", type = "string", required = true } },
            lookupSchema = new object[] { new { name = "id", type = "int64", required = true }, new { name = "region", type = "string", required = true } },
            sourceLookupField = "id", lookupKeyField = "id",
            mapping = new object[]
            {
                new { name = "id", origin = "source", field = "id" },
                new { name = "customerName", origin = "source", field = "name" },
                new { name = "region", origin = "lookup", field = "region" }
            },
            filters = Array.Empty<object>(),
            limits = new
            {
                partitionCount = 2, maxRecordBytes = 65536, maxFields = 256, maxBatchRecords = 100, maxBatchBytes = 4194304,
                maxOutstandingRecords = 100, maxOutstandingBytes = 4194304, maxLookupRecords = 100, maxLookupBytes = 4194304,
                maxOutputBatchBytes = 4194304, maxSnapshotBytes = 4194304, maxArtifactBytes = 262144
            }
        };
        var draft = await PostAsync("/etl/definitions", new { id = definitionId, pipeline });
        var validation = await PostAsync($"/etl/definitions/{definitionId}/validate", new { expectedRevision = draft["revision"]!.GetValue<long>() });
        Assert.Empty(validation["diagnostics"]!.AsArray());
        var publication = await PostAsync($"/etl/definitions/{definitionId}/publish", new
        {
            expectedRevision = validation["revision"]!.GetValue<long>(),
            definitionVersion = validation["definitionVersion"]!.GetValue<long>(),
            contentHash = validation["contentHash"]!.GetValue<string>()
        });
        return publication["version"]!["selection"]!.GetValue<string>();
    }

    private static void PrepareContentRoot(string contentRoot, double recoverySweepSeconds)
    {
        var etlRoot = Path.Combine(contentRoot, "etl");
        var inputs = Path.Combine(etlRoot, "inputs");
        var mainRoot = Path.Combine(etlRoot, "main-output");
        var errorRoot = Path.Combine(etlRoot, "error-output");
        var snapshotRoot = Path.Combine(etlRoot, "snapshots");
        foreach (var directory in new[] { inputs, mainRoot, errorRoot, snapshotRoot })
            Directory.CreateDirectory(directory);
        var utf8 = new UTF8Encoding(false);
        var sourcePath = Path.Combine(inputs, "source.csv");
        var lookupPath = Path.Combine(inputs, "lookup.csv");
        File.WriteAllText(sourcePath, "id,name\n1,Ada\n2,Grace\n3,Linus\n", utf8);
        File.WriteAllText(lookupPath, "id,region\n1,Europe\n2,US\n3,Europe\n", utf8);

        var shellPath = Path.Combine(contentRoot, "shells.json");
        var shells = JsonNode.Parse(File.ReadAllText(shellPath))!;
        var features = shells["CShells"]!["Shells"]!["default"]!["Features"]!.AsObject();
        features["WorkflowsRuntimeResumption"] = new JsonObject { ["SweepIntervalSeconds"] = recoverySweepSeconds };
        features["EtlDesign"] = new JsonObject();
        features["EtlDesignEntityFrameworkCore"] = new JsonObject
        {
            ["Provider"] = "Sqlite", ["ConnectionString"] = $"Data Source={Path.Combine(etlRoot, "etl-design.db")};Pooling=False"
        };
        features["EtlDesignApi"] = new JsonObject { ["CursorSigningKey"] = "finite-file-etl-design-cursor-key-for-hosting-tests" };
        features["EtlRuntime"] = new JsonObject
        {
            ["SnapshotRoot"] = snapshotRoot,
            ["FileBindings"] = new JsonArray(
                Binding("source", "Source", "source-v1", sourcePath, immutableInput: true, atomicPublication: false, Sha256(sourcePath)),
                Binding("lookup", "Lookup", "lookup-v1", lookupPath, immutableInput: true, atomicPublication: false, Sha256(lookupPath)),
                Binding("output", "MainOutput", "output-v1", mainRoot, immutableInput: false, atomicPublication: true, null),
                Binding("errors", "ErrorOutput", "errors-v1", errorRoot, immutableInput: false, atomicPublication: true, null))
        };
        // The delivery lease has no heartbeat while a delivery is in flight, so the restarted host can reclaim the
        // frozen claim only after it lapses; the five-second floor keeps each row short without changing the proof.
        features["EtlRuntimeEntityFrameworkCore"] = new JsonObject
        {
            ["Provider"] = "Sqlite", ["ConnectionString"] = $"Data Source={Path.Combine(etlRoot, "etl-runtime.db")};Pooling=False",
            ["DeliveryLeaseSeconds"] = 5
        };
        features["EtlRuntimeApi"] = new JsonObject { ["CursorSigningKey"] = "finite-file-etl-runtime-cursor-key-for-hosting-tests" };
        features["EtlWorkflowsDesign"] = new JsonObject();
        features["EtlWorkflowsRuntime"] = new JsonObject();
        File.WriteAllText(shellPath, shells.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), utf8);
    }

    private static JsonObject Binding(string alias, string role, string revision, string path, bool immutableInput, bool atomicPublication, string? sha256)
    {
        var binding = new JsonObject
        {
            ["TenantId"] = "default", ["PersistencePartition"] = "default", ["Alias"] = alias, ["Role"] = role, ["Revision"] = revision,
            ["Path"] = path, ["IsCurrent"] = true, ["ImmutableInput"] = immutableInput, ["AtomicPublication"] = atomicPublication
        };
        if (sha256 is not null)
            binding["ExpectedSha256"] = sha256;
        return binding;
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
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
