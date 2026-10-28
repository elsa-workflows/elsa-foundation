using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Nodes;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

/// <summary>Owns only the production Workbench child it starts and its isolated configuration/stores.</summary>
internal sealed class OwnedWorkbench : IAsyncDisposable
{
    private static readonly TimeSpan StartupLimit = TimeSpan.FromMinutes(3);
    private readonly ConcurrentQueue<string> output = new();
    private HttpClient client = new(new HttpClientHandler { CookieContainer = new CookieContainer() });
    private Process? process;

    private OwnedWorkbench(string root) => ContentRoot = root;

    public string ContentRoot { get; }
    public int ProcessId => process?.Id ?? throw new InvalidOperationException("The owned Workbench is stopped.");
    public string RuntimeDatabase => Path.Combine(ContentRoot, "runtime.db");
    public string DesignDatabase => Path.Combine(ContentRoot, "design.db");

    public static async Task<OwnedWorkbench> StartAsync()
    {
        var host = new OwnedWorkbench(Directory.CreateTempSubdirectory("elsa-bpmn-hosting-").FullName);
        try
        {
            var source = Metadata("BpmnHostingWorkbenchSource");
            foreach (var name in new[] { "appsettings.json", "appsettings.Development.json", "shells.json" })
                File.Copy(Path.Combine(source, name), Path.Combine(host.ContentRoot, name));
            Directory.CreateDirectory(Path.Combine(host.ContentRoot, "packages"));
            var shellPath = Path.Combine(host.ContentRoot, "shells.json");
            var shells = JsonNode.Parse(await File.ReadAllTextAsync(shellPath))!;
            var features = shells["CShells"]!["Shells"]!["default"]!["Features"]!;
            features["WorkflowsRuntimeEntityFrameworkCore"]!["ConnectionString"] = "Data Source=runtime.db";
            foreach (var name in new[] { "ActivitiesDesignEntityFrameworkCore", "WorkflowsDesignEntityFrameworkCore", "WorkflowsPublishingEntityFrameworkCore" })
                features[name]!["ConnectionString"] = "Data Source=design.db";
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

    public async Task LoginAsync()
    {
        using var response = await client.PostAsJsonAsync("/_elsa/identity/login", new { username = "admin", password = "Password123!" });
        if (!response.IsSuccessStatusCode)
            throw Failure($"Test-account login returned HTTP {(int)response.StatusCode}");
    }

    public async Task<JsonNode> GetAsync(string path)
    {
        using var response = await client.GetAsync(path);
        return await ReadAsync(response, path);
    }

    public async Task<JsonNode> PostAsync(string path, object value)
    {
        using var response = await client.PostAsJsonAsync(path, value);
        return await ReadAsync(response, path);
    }

    public async Task RestartAsync()
    {
        await StopAsync();
        await LaunchAsync();
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
        client.BaseAddress = await listening.Task.WaitAsync(deadline.Token);
        while (true)
        {
            if (owned.HasExited) throw Failure("The owned Workbench exited during shell activation");
            using var ready = await client.GetAsync("/health/ready", deadline.Token);
            if (ready.IsSuccessStatusCode) return;
            await Task.Delay(TimeSpan.FromMilliseconds(200), deadline.Token);
        }
    }

    private static string Metadata(string key) => typeof(OwnedWorkbench).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == key).Value ?? throw new InvalidOperationException($"Missing host build metadata '{key}'.");

    private async Task<JsonNode> ReadAsync(HttpResponseMessage response, string path)
    {
        if (!response.IsSuccessStatusCode) throw Failure($"Owned-host request {path} returned HTTP {(int)response.StatusCode}");
        return await response.Content.ReadFromJsonAsync<JsonNode>() ?? throw Failure("An owned-host API returned no document");
    }

    private InvalidOperationException Failure(string message) => new($"{message}.{Environment.NewLine}{string.Join(Environment.NewLine, output)}");

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
