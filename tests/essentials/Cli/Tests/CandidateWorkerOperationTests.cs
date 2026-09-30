using System.Diagnostics;
using System.Text.Json;
using Elsa.Cli.Worker;
using Xunit;

namespace Elsa.Cli.Tests;

public sealed class CandidateWorkerOperationTests
{
    private const string PrivateCanaryRootPrefix = "candidate-private-canary-2177-";

    [Theory]
    [InlineData("restore")]
    [InlineData("connection")]
    [InlineData("environment")]
    [InlineData("candidate")]
    public async Task Invalid_or_live_requests_refuse_before_any_closure_call(string field)
    {
        var calls = 0;
        var request = Request();
        request = field switch
        {
            "restore" => request with { Restore = true },
            "connection" => request with { Connection = "secret-input-canary" },
            "environment" => request with { Environment = "Production" },
            _ => request with { Candidate = null }
        };
        var result = await Run(request, (_, _) => { calls++; return Task.FromResult(new WorkerResponse()); });
        Assert.Equal(0, calls);
        Assert.Equal(2, result.ExitCode);
        Assert.Equal("candidate-request-invalid", result.Error?.Code);
        Assert.DoesNotContain("canary", JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task Valid_request_reaches_only_the_supplied_candidate_closure_once()
    {
        var calls = 0;
        var expected = new WorkerResponse { ExitCode = 0 };
        var result = await Run(Request(), (request, token) =>
        {
            calls++;
            Assert.Equal("inspect-candidate", request.Command);
            Assert.False(request.Restore);
            return Task.FromResult(expected);
        });
        Assert.Same(expected, result);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("legacy-refusal")]
    [InlineData("json")]
    public async Task Loader_and_legacy_refusal_details_never_escape(string kind)
    {
        var result = await Run(Request(), (_, _) => throw (kind switch
        {
            "ordinary" => new IOException("private-path-canary"),
            "json" => new JsonException("private-path-canary"),
            _ => WorkerRefusal.Resolution("packages-empty", "private-path-canary", ["secret-detail-canary"])
        }));
        Assert.Equal(3, result.ExitCode);
        Assert.Equal("candidate-host-unavailable", result.Error?.Code);
        Assert.DoesNotContain("canary", JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData("candidate-closure-changed")]
    [InlineData("candidate-capability-unavailable")]
    [InlineData("candidate-response-invalid")]
    public async Task Known_candidate_refusals_keep_the_code_but_replace_untrusted_messages(string code)
    {
        var result = await Run(Request(), (_, _) => throw WorkerRefusal.Resolution(code, "private-canary", ["canary"]));
        Assert.Equal(code, result.Error?.Code);
        Assert.Equal(3, result.ExitCode);
        Assert.DoesNotContain("canary", JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task Precancelled_execution_never_calls_the_loader()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(Request(), (_, _) =>
        { calls++; return Task.FromResult(new WorkerResponse()); }, cancellation.Token));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Built_candidate_worker_reaches_the_real_producer_and_suppresses_both_host_console_streams()
    {
        var host = HostLayout.Resolve(DotnetElsa.Host("ResourceAwareLiveHost"));
        using var sentinels = new TempDirectory("elsa-candidate-worker-sentinels-");
        var database = sentinels.File("must-not-create.db");
        var context = sentinels.File("context-constructed.txt");
        var action = sentinels.File("action-constructed.txt");
        var request = ForHost(host, database);
        var run = await RunChild(host, request, new Dictionary<string, string>
        {
            ["ELSA_RESOURCE_PROBE_CONTEXT_MARKER"] = context,
            ["ELSA_RESOURCE_PROBE_ACTION_MARKER"] = action
        });
        Assert.Equal(0, run.ExitCode);
        Assert.Empty(run.Error);
        Assert.DoesNotContain("private-console-canary", run.Text);
        var response = JsonSerializer.Deserialize<WorkerResponse>(run.Output, WorkerContract.Json)!;
        Assert.Equal(0, response.ExitCode);
        var resolution = response.Tooling!.Value.GetProperty("configurationResolution");
        Assert.Equal("default", resolution.GetProperty("shell").GetString());
        var participant = Assert.Single(resolution.GetProperty("participants").EnumerateArray());
        Assert.Equal("ResourceProbe", participant.GetProperty("feature").GetString());
        Assert.Equal("RootDefault", participant.GetProperty("selection").GetString());
        Assert.Equal("primary", participant.GetProperty("resource").GetString());
        Assert.Equal("Probe", participant.GetProperty("connectionReference").GetString());
        Assert.DoesNotContain("private-connection-canary", run.Text);
        Assert.False(File.Exists(database));
        Assert.False(File.Exists(context));
        Assert.False(File.Exists(action));
    }

    [Theory]
    [InlineData("state")]
    [InlineData("probe")]
    [InlineData("negative")]
    public async Task Built_candidate_worker_uses_the_observed_package_root_route_without_mutating_it(string route)
    {
        var host = HostLayout.Resolve(DotnetElsa.Host("ResourceAwareLiveHost"));
        using var packages = new TempDirectory($"{PrivateCanaryRootPrefix}packages-");
        using var sentinels = new TempDirectory($"{PrivateCanaryRootPrefix}sentinels-");
        const string package = "Acme.Widgets";
        const string version = "1.4.2";
        var installPath = NuplanePackageRootFixture.InstallInto(packages.Path, package, version, complete: route == "probe");
        var stateFile = Path.Join(packages.Path, NuplaneInstallRoot.StateFileName);
        var markerFile = Path.Join(installPath, NuplaneInstallRoot.ReadyMarker);
        if (route == "state")
            NuplanePackageRootFixture.WriteStateFile(packages.Path, package, version, installPath);

        var filesBefore = SnapshotPackageFiles(packages.Path);
        DateTime? markerTimestampBefore = File.Exists(markerFile) ? File.GetLastWriteTimeUtc(markerFile) : null;
        var database = sentinels.File("must-not-create.db");
        var context = sentinels.File("context-constructed.txt");
        var action = sentinels.File("action-constructed.txt");
        var request = ForHost(host, database) with { PackageRoots = [packages.Path] };
        var run = await RunChild(host, request, new Dictionary<string, string>
        {
            ["ELSA_RESOURCE_PROBE_CONTEXT_MARKER"] = context,
            ["ELSA_RESOURCE_PROBE_ACTION_MARKER"] = action
        });

        Assert.Empty(run.Error);
        Assert.DoesNotContain(PrivateCanaryRootPrefix, run.Text);
        Assert.DoesNotContain("private-console-canary", run.Text);
        Assert.DoesNotContain("private-connection-canary", run.Text);
        var response = JsonSerializer.Deserialize<WorkerResponse>(run.Output, WorkerContract.Json)!;
        Assert.Equal(route == "negative" ? 3 : 0, run.ExitCode);
        Assert.Equal(run.ExitCode, response.ExitCode);
        if (route == "negative")
        {
            Assert.Equal("candidate-host-unavailable", response.Error?.Code);
            Assert.Null(response.Tooling);
        }
        else
        {
            Assert.Null(response.Error);
            var resolution = response.Tooling!.Value.GetProperty("configurationResolution");
            var participant = Assert.Single(resolution.GetProperty("participants").EnumerateArray());
            Assert.Equal("ResourceProbe", participant.GetProperty("feature").GetString());
            Assert.Equal("RootDefault", participant.GetProperty("selection").GetString());
            Assert.Equal("primary", participant.GetProperty("resource").GetString());
            Assert.Equal("Sqlite", participant.GetProperty("provider").GetString());
            Assert.Equal("Probe", participant.GetProperty("connectionReference").GetString());
        }

        Assert.False(File.Exists(database));
        Assert.False(File.Exists(context));
        Assert.False(File.Exists(action));
        Assert.Equal(route == "state", File.Exists(stateFile));
        Assert.Equal(route == "probe", File.Exists(markerFile));
        var markerTimestampAfter = File.Exists(markerFile) ? File.GetLastWriteTimeUtc(markerFile) : (DateTime?)null;
        Assert.Equal(markerTimestampBefore, markerTimestampAfter);
        var filesAfter = SnapshotPackageFiles(packages.Path);
        Assert.Equal(filesBefore.Keys.Order(StringComparer.Ordinal).ToArray(), filesAfter.Keys.Order(StringComparer.Ordinal).ToArray());
        foreach (var (path, bytes) in filesBefore)
            Assert.True(bytes.AsSpan().SequenceEqual(filesAfter[path]), $"Package-root file changed: {path}.");
        Assert.DoesNotContain(
            Directory.EnumerateDirectories(packages.Path, "*", SearchOption.AllDirectories).Select(Path.GetFileName),
            NuplaneInstallRoot.StagingDirectory);
    }

    [Fact]
    public async Task Built_candidate_worker_refuses_an_old_host_without_legacy_fallback()
    {
        var host = HostLayout.Resolve(DotnetElsa.Host("LegacyHost"));
        var run = await RunChild(host, ForHost(host));
        Assert.Equal(3, run.ExitCode);
        var response = JsonSerializer.Deserialize<WorkerResponse>(run.Output, WorkerContract.Json)!;
        Assert.Equal("candidate-capability-unavailable", response.Error?.Code);
        Assert.Empty(run.Error);
    }

    [Fact]
    public async Task Built_candidate_worker_refuses_restore_before_accessing_request_dependency_paths()
    {
        var host = HostLayout.Resolve(DotnetElsa.Host("ResourceAwareLiveHost"));
        var run = await RunChild(host, ForHost(host) with { Restore = true, DepsFile = "/private-missing-deps-canary" });
        Assert.Equal(2, run.ExitCode);
        var response = JsonSerializer.Deserialize<WorkerResponse>(run.Output, WorkerContract.Json)!;
        Assert.Equal("candidate-request-invalid", response.Error?.Code);
        Assert.Empty(run.Error);
        Assert.DoesNotContain("canary", run.Text);
    }

    private static WorkerRequest ForHost(HostLayout host, string? databasePath = null) => Request() with
    {
        HostDirectory = host.Directory, HostName = host.Name, DepsFile = host.DepsFile,
        Candidate = Request().Candidate! with
        {
            AcceptedFeatureIds = ["ResourceProbe"],
            Files = new[]
            {
                ("appsettings.json", JsonSerializer.Serialize(new
                {
                    ProbeDefaults = new { WriteConsoleCanary = true },
                    ConnectionStrings = new { Probe = $"Data Source={databasePath ?? ":memory:"};Password=private-connection-canary" },
                    Elsa = new { Persistence = new { DefaultResource = "primary", Resources = new { primary = new { Provider = "Sqlite", ConnectionName = "Probe" } } } }
                })),
                ("shells.json", """{"CShells":{"Shells":{"default":{"Name":"default","Features":{"ResourceProbe":{}}}}}}"""),
                ("shells.Production.json", "{}")
            }.Select(file => new WorkerCandidateFile
            { Name = file.Item1, Content = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(file.Item2)), CaptureId = new('2', 32) }).ToArray()
        }
    };

    private static async Task<CliRun> RunChild(HostLayout host, WorkerRequest request, IReadOnlyDictionary<string, string>? environment = null)
    {
        var worker = Path.Join(Path.GetDirectoryName(DotnetElsa.ToolAssembly), WorkerProcess.WorkerAssemblyFileName);
        var start = new ProcessStartInfo(DotnetMuxer.Path())
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var variable in environment ?? new Dictionary<string, string>())
            start.Environment[variable.Key] = variable.Value;
        foreach (var argument in WorkerProcess.Arguments(host, worker).Append("--candidate-inspection"))
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var error = process.StandardError.ReadToEndAsync(deadline.Token);
            await JsonSerializer.SerializeAsync(process.StandardInput.BaseStream, request, WorkerContract.Json, deadline.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
            return new(process.ExitCode, await output, await error);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(cleanup.Token);
            }
        }
    }

    private static Task<WorkerResponse> Run(WorkerRequest request,
        Func<WorkerRequest, CancellationToken, Task<WorkerResponse>> closure, CancellationToken token = default)
    {
        return new CandidateWorkerOperation(closure).RunAsync(request, token);
    }

    private static Dictionary<string, byte[]> SnapshotPackageFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(root, path), File.ReadAllBytes, StringComparer.Ordinal);

    private static WorkerRequest Request() => new()
    {
        Command = WorkerCommands.InspectCandidate, HostDirectory = "/host", HostName = "Host", DepsFile = "/host/Host.deps.json",
        Candidate = new()
        {
            Version = 1, Source = "captured-workbench-json-v1", InvocationId = new('1', 32), CaptureId = new('2', 32),
            Shell = "default", Environment = "Production", AcceptedFeatureIds = [], RemovedFeatureIds = [],
            Files = new[] { "appsettings.json", "shells.json", "shells.Production.json" }
                .Select(name => new WorkerCandidateFile { Name = name, CaptureId = new('2', 32), Content = "e30=" }).ToArray()
        }
    };
}
