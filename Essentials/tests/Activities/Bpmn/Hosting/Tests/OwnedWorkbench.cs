using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

/// <summary>Owns only the production Workbench child it starts and its isolated configuration/stores.</summary>
internal sealed class OwnedWorkbench : IAsyncDisposable
{
    private static readonly TimeSpan StartupLimit = TimeSpan.FromMinutes(3);
    private readonly ConcurrentQueue<string> output = new();
    private HttpClient client = new(new HttpClientHandler { CookieContainer = new CookieContainer() });
    private Process? process;
    private bool designSharesRuntime;

    private OwnedWorkbench(string root) => ContentRoot = root;

    public string ContentRoot { get; }
    public int ProcessId => process?.Id ?? throw new InvalidOperationException("The owned Workbench is stopped.");
    public string RuntimeDatabase => Path.Combine(ContentRoot, "runtime.db");
    public string DesignDatabase => designSharesRuntime ? RuntimeDatabase : Path.Combine(ContentRoot, "design.db");
    internal string AssemblyPath => Metadata("BpmnHostingWorkbenchAssembly");
    internal string ReaderAssemblyPath => Metadata("BpmnHostingReaderAssembly");
    internal int? LastStoppedExitCode { get; private set; }

    public static async Task<OwnedWorkbench> StartAsync(bool directBpmn = false, bool runtimeOnly = false,
        string? legacySnapshotDirectory = null, string? runtimeSnapshotPath = null, double? recoverySweepSeconds = null,
        bool legacyCombinedStore = false, bool runtimeFlowchart = false, bool authoredDirectPublication = false)
    {
        if (legacySnapshotDirectory is not null && runtimeSnapshotPath is not null)
            throw new ArgumentException("Select one actual legacy snapshot source.");
        if (legacyCombinedStore && (runtimeSnapshotPath is null || runtimeOnly))
            throw new ArgumentException("A combined legacy capture requires its whole Runtime/Design store and a combined host profile.");
        var host = new OwnedWorkbench(Directory.CreateTempSubdirectory("elsa-bpmn-hosting-").FullName) { designSharesRuntime = legacyCombinedStore };
        try
        {
            var source = Metadata("BpmnHostingWorkbenchSource");
            foreach (var name in new[] { "appsettings.json", "appsettings.Development.json", "shells.json" })
                File.Copy(Path.Combine(source, name), Path.Combine(host.ContentRoot, name));
            if (legacySnapshotDirectory is not null)
                foreach (var name in new[] { "runtime", "design" })
                    File.Copy(Path.Combine(legacySnapshotDirectory, name + ".sqlite"), Path.Combine(host.ContentRoot, name + ".db"));
            if (runtimeSnapshotPath is not null)
                File.Copy(runtimeSnapshotPath, host.RuntimeDatabase);
            Directory.CreateDirectory(Path.Combine(host.ContentRoot, "packages"));
            var shellPath = Path.Combine(host.ContentRoot, "shells.json");
            var shells = JsonNode.Parse(await File.ReadAllTextAsync(shellPath))!;
            var features = shells["CShells"]!["Shells"]!["default"]!["Features"]!;
            if (recoverySweepSeconds is { } interval)
                features["WorkflowsRuntimeResumption"] = new JsonObject { ["SweepIntervalSeconds"] = interval };
            if (directBpmn)
            {
                features["ActivitiesBpmnHosting"] = new JsonObject { ["AdmitNewExecutions"] = true };
                features["ActivitiesBpmnHostingApi"] = new JsonObject();
                // Qualify the built-in single-node profile first. Clustered model placement remains a separate gate.
                foreach (var name in new[] { "WorkflowsRuntimeDistributed", "WorkflowsRuntimeDistributedEntityFrameworkCorePersistence", "WorkflowsRuntimeDistributedCommandTransportEntityFrameworkCorePersistence" })
                    features.AsObject().Remove(name);
            }
            if (authoredDirectPublication)
            {
                if (!directBpmn || runtimeOnly)
                    throw new ArgumentException("Authored direct publication requires the composed Design and direct BPMN host.");
                features["ActivitiesBpmnDirectPublishing"] = new JsonObject();
            }
            if (runtimeOnly)
            {
                var allowed = new HashSet<string>(StringComparer.Ordinal)
                {
                    "ActivitiesBpmnHosting", "ActivitiesBpmnHostingApi", "ActivitiesRuntime", "Events", "Expressions", "FileSystemDistributedLocking", "Primitives", "Serialization", "Tasks",
                    "WorkflowsRuntimeEntityFrameworkCore", "WorkflowsRuntimeResumption",
                    "FoundationIdentityAbstractions", "FoundationIdentityApi", "FoundationIdentityAspNetCoreIdentity",
                    "FoundationIdentityAspNetCoreIdentityEntityFrameworkCore", "IdentityIamEntityFrameworkCore", "IdentityProviderConfigurationEntityFrameworkCore"
                };
                if (runtimeFlowchart)
                {
                    allowed.Add("ActivitiesFlowchart");
                    allowed.Add("ActivitiesPrimitives");
                    allowed.Add("WorkflowsRuntimeCheckpointPersistence");
                    features["ActivitiesFlowchart"] = new JsonObject();
                    features["ActivitiesPrimitives"] = new JsonObject();
                }
                foreach (var name in features.AsObject().Select(item => item.Key).Where(name => !allowed.Contains(name)).ToArray())
                    features.AsObject().Remove(name);
            }
            features["WorkflowsRuntimeEntityFrameworkCore"]!["ConnectionString"] = "Data Source=runtime.db";
            foreach (var name in new[] { "ActivitiesDesignEntityFrameworkCore", "WorkflowsDesignEntityFrameworkCore", "WorkflowsPublishingEntityFrameworkCore" })
                if (features[name] is { } selected) selected["ConnectionString"] = legacyCombinedStore ? "Data Source=runtime.db" : "Data Source=design.db";
            if ((legacySnapshotDirectory is not null || legacyCombinedStore) && features["ClrActivityReconciliation"] is JsonObject clr)
                await host.RestoreClrSourceIdentityAsync(clr);
            await File.WriteAllTextAsync(shellPath, shells.ToJsonString());
            await host.LaunchAsync();
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    private async Task RestoreClrSourceIdentityAsync(JsonObject feature)
    {
        if (!File.Exists(DesignDatabase)) return;
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in new[] { ReaderAssemblyPath, "--read-clr-source-identities", DesignDatabase }) info.ArgumentList.Add(arg);
        using var reader = Process.Start(info) ?? throw Failure("The captured CLR source identity reader did not start.");
        var output = reader.StandardOutput.ReadToEndAsync(); var error = reader.StandardError.ReadToEndAsync();
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await reader.WaitForExitAsync(limit.Token); }
        catch { if (!reader.HasExited) { reader.Kill(entireProcessTree: true); await reader.WaitForExitAsync(); } throw; }
        if (reader.ExitCode != 0) throw Failure("The captured CLR source identity could not be read: " + await error);
        var identities = JsonNode.Parse(await output)!["sourceIds"]!.AsArray().Select(id => id!.GetValue<string>()).ToArray();
        if (identities.Length == 0) return;
        if (identities.Length != 1) throw Failure("Multiple captured CLR sources require an explicit source-to-folder binding.");
        if (feature["Options"] is not JsonObject options)
        {
            options = new JsonObject(); feature["Options"] = options;
        }
        if (options["SourceId"] is { } configured && configured.GetValue<string>() != identities[0])
            throw Failure("The configured CLR source identity differs from the captured catalog authority.");
        // Preserve the logical authority without treating its path-shaped value as a filesystem location.
        // FolderPath is independent and retains this host's normal current-build scan location.
        options["SourceId"] = identities[0];
    }

    public async Task LoginAsync()
    {
        using var response = await client.PostAsJsonAsync("/_elsa/identity/login", new { username = "admin", password = "Password123!" });
        if (!response.IsSuccessStatusCode)
            throw Failure($"Test-account login returned HTTP {(int)response.StatusCode}");
    }

    public async Task<JsonNode> GetAsync(string path, HttpStatusCode? expected = null)
    {
        using var response = await client.GetAsync(path);
        return await ReadAsync(response, path, expected);
    }

    public async Task<JsonNode?> FindAsync(string path)
    {
        using var response = await client.GetAsync(path);
        return response.StatusCode == HttpStatusCode.NotFound ? null : await ReadAsync(response, path, null);
    }

    public async Task<JsonNode> PostAsync(string path, object value, HttpStatusCode? expected = null, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(value) };
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        using var response = await client.SendAsync(request);
        return await ReadAsync(response, path, expected);
    }

    public async Task RestartAsync(double? recoverySweepSeconds = null)
    {
        await StopAsync();
        if (recoverySweepSeconds is { } interval)
        {
            var path = Path.Combine(ContentRoot, "shells.json");
            var shells = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            shells["CShells"]!["Shells"]!["default"]!["Features"]!["WorkflowsRuntimeResumption"] = new JsonObject { ["SweepIntervalSeconds"] = interval };
            await File.WriteAllTextAsync(path, shells.ToJsonString());
        }
        await LaunchAsync();
    }

    internal object[] DescribeBinaries(string? sourceRevision, bool includePublishing = false, bool includeBpmnAuthoring = false)
    {
        var binaries = new[] { "Elsa.Workbench", "Elsa.Workflows.Runtime.Core", "Elsa.Workflows.Runtime", "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore",
            "Elsa.Activities.Bpmn.Core", "Elsa.Activities.Bpmn", "Elsa.Activities.Bpmn.Api", "Bpmn.Model", "Bpmn.Semantics",
            "Elsa.Workflows.Design.Core", "Elsa.Workflows.Design.Api", "Elsa.Workflows.Design.Persistence.EntityFrameworkCore", "Elsa.Persistence.EntityFramework" }
            .Concat(includePublishing ? new[] { "Elsa.Workflows.Publishing.Core", "Elsa.Workflows.Publishing" } : [])
            .Concat(includeBpmnAuthoring ? new[] { "Elsa.Activities.Bpmn.Interchange", "Elsa.Api.Capabilities", "Elsa.Api.Capabilities.Core" } : [])
            .Select(name => Path.Combine(Path.GetDirectoryName(AssemblyPath)!, name + ".dll"))
            .Select(path => new { file = Path.GetFileName(path), productVersion = FileVersionInfo.GetVersionInfo(path).ProductVersion,
                sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) }).ToArray();
        if (sourceRevision is not null && binaries.Single(binary => binary.file == "Elsa.Workbench.dll").productVersion?.Contains(sourceRevision, StringComparison.Ordinal) != true)
            throw Failure("The owned Workbench product revision does not match its expected evidence source.");
        return binaries.Cast<object>().ToArray();
    }

    internal async Task<JsonNode> ReadEmbeddingAsync(string model, string parent, string? directCounterpart = null, string? database = null)
    {
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { ReaderAssemblyPath, "--read-bpmn-embedding", database ?? RuntimeDatabase, model, parent }) info.ArgumentList.Add(argument);
        if (directCounterpart is not null) info.ArgumentList.Add(directCounterpart);
        using var reader = Process.Start(info) ?? throw Failure("The owned embedding reader did not start");
        var output = reader.StandardOutput.ReadToEndAsync(); var error = reader.StandardError.ReadToEndAsync();
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await reader.WaitForExitAsync(limit.Token); }
        catch { if (!reader.HasExited) { reader.Kill(entireProcessTree: true); await reader.WaitForExitAsync(); } throw; }
        if (reader.ExitCode != 0) throw Failure("The read-only embedding observer failed: " + await error);
        return JsonNode.Parse(await output) ?? throw Failure("The read-only embedding observer returned no document");
    }

    internal async Task<object?> SnapshotAsync(string name, string? subdirectory = null, bool design = false)
    {
        if (process is not null) throw Failure("Stop the owned Workbench before capturing its database");
        var directory = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY");
        if (directory is null) return null;
        if (subdirectory is not null) directory = Path.Combine(directory, subdirectory);
        Directory.CreateDirectory(directory);
        var backup = Path.Combine(ContentRoot, "evidence-backup"); File.Delete(backup);
        await using (var source = new SqliteConnection($"Data Source={(design ? DesignDatabase : RuntimeDatabase)};Mode=ReadOnly;Pooling=False"))
        await using (var target = new SqliteConnection($"Data Source={backup};Pooling=False"))
        {
            await source.OpenAsync(); await target.OpenAsync(); source.BackupDatabase(target);
            await using var check = target.CreateCommand(); check.CommandText = "PRAGMA integrity_check";
            if ((string)(await check.ExecuteScalarAsync())! != "ok") throw Failure("The owned Runtime backup failed its integrity check");
        }
        var data = await File.ReadAllBytesAsync(backup);
        var path = Path.Combine(directory, name);
        await using (var file = File.Create(path))
        await using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize)) await gzip.WriteAsync(data);
        return new { file = name, databaseSha256 = Convert.ToHexStringLower(SHA256.HashData(data)),
            gzipSha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path))) };
    }

    public async Task StopAsync()
    {
        var owned = process;
        if (owned is null) return;
        process = null;
        try
        {
            if (!owned.HasExited)
            {
                owned.Kill(entireProcessTree: true);
                using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await owned.WaitForExitAsync(limit.Token);
            }
            LastStoppedExitCode = owned.ExitCode;
        }
        finally { owned.Dispose(); }
    }

    private async Task LaunchAsync()
    {
        if (process is not null) throw new InvalidOperationException("An owned Workbench is already running.");
        client.Dispose();
        client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() });
        var assembly = Metadata("BpmnHostingWorkbenchAssembly");
        if (!File.Exists(assembly)) throw new FileNotFoundException("Build the production Workbench before running the hosting tests.", assembly);
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = ContentRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[] { assembly, "--contentRoot", ContentRoot, "--urls", "http://127.0.0.1:0" }) info.ArgumentList.Add(argument);
        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        var listening = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        var owned = new Process { StartInfo = info, EnableRaisingEvents = true };
        owned.Exited += (_, _) => listening.TrySetException(Failure("The owned Workbench exited before listening"));
        void Observe(string? line)
        {
            if (line is null) return;
            output.Enqueue(line);
            while (output.Count > 1000) output.TryDequeue(out _);
            const string marker = "Now listening on: ";
            var offset = line.IndexOf(marker, StringComparison.Ordinal);
            if (offset >= 0 && Uri.TryCreate(line[(offset + marker.Length)..].Trim(), UriKind.Absolute, out var address) &&
                address.IsLoopback && address.Port > 0) listening.TrySetResult(address);
        }
        owned.OutputDataReceived += (_, args) => Observe(args.Data);
        owned.ErrorDataReceived += (_, args) => Observe(args.Data);
        process = owned;
        try
        {
            if (!owned.Start()) throw Failure("The owned Workbench did not start");
        }
        catch { process = null; owned.Dispose(); throw; }
        owned.BeginOutputReadLine(); owned.BeginErrorReadLine();
        using var deadline = new CancellationTokenSource(StartupLimit);
        string? lastReadiness = null;
        try
        {
            client.BaseAddress = await listening.Task.WaitAsync(deadline.Token);
            while (true)
            {
                if (owned.HasExited) throw Failure("The owned Workbench exited during shell activation");
                using var ready = await client.GetAsync("/health/ready", deadline.Token);
                if (ready.IsSuccessStatusCode) return;
                lastReadiness = await ready.Content.ReadAsStringAsync(deadline.Token);
                if (JsonNode.Parse(lastReadiness) is { } readiness && readiness["status"]?.GetValue<string>() == "failed" &&
                    readiness["code"]?.GetValue<string>() == "shell_activation_failed")
                    throw Failure("The owned Workbench reported a failed shell activation");
                await Task.Delay(TimeSpan.FromMilliseconds(200), deadline.Token);
            }
        }
        catch (OperationCanceledException exception) when (deadline.IsCancellationRequested)
        { throw Failure($"The owned Workbench did not become ready. Last readiness response: {lastReadiness}", exception); }
    }

    private static string Metadata(string key) => typeof(OwnedWorkbench).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == key).Value ?? throw new InvalidOperationException($"Missing host build metadata '{key}'.");

    private async Task<JsonNode> ReadAsync(HttpResponseMessage response, string path, HttpStatusCode? expected)
    {
        if (expected is { } status ? response.StatusCode != status : !response.IsSuccessStatusCode)
            throw Failure($"Owned-host request {path} returned HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        if (expected is not null && response.Content.Headers.ContentLength == 0) return new JsonObject();
        return await response.Content.ReadFromJsonAsync<JsonNode>() ?? throw Failure("An owned-host API returned no document");
    }

    internal InvalidOperationException Failure(string message, Exception? cause = null) => new($"{message}.{Environment.NewLine}{string.Join(Environment.NewLine, output)}", cause);

    public async ValueTask DisposeAsync()
    {
        try { await StopAsync(); }
        finally
        {
            client.Dispose();
        }
        Directory.Delete(ContentRoot, recursive: true);
    }
}
