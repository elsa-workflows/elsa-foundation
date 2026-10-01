using System.Diagnostics;
using System.IO.Pipes;
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
    [InlineData("sqlite-migration-lock")]
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
            "sqlite-migration-lock" => request with { SqliteMigrationLockStaleAfter = TimeSpan.FromMinutes(1) },
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
    [InlineData("corrupt-state")]
    [InlineData("corrupt-probe")]
    public async Task Built_candidate_worker_uses_the_observed_package_root_route_without_mutating_it(string route)
    {
        var host = HostLayout.Resolve(DotnetElsa.Host("ResourceAwareLiveHost"));
        using var packages = new TempDirectory($"{PrivateCanaryRootPrefix}packages-");
        using var sentinels = new TempDirectory($"{PrivateCanaryRootPrefix}sentinels-");
        const string package = "Acme.Widgets";
        const string version = "1.4.2";
        var stateRoute = route is "state" or "corrupt-state";
        var probeRoute = route is "probe" or "corrupt-probe";
        var shouldRefuse = route is "negative" or "corrupt-state" or "corrupt-probe";
        var installPath = NuplanePackageRootFixture.InstallInto(packages.Path, package, version, complete: probeRoute);
        var stateFile = Path.Join(packages.Path, NuplaneInstallRoot.StateFileName);
        var markerFile = Path.Join(installPath, NuplaneInstallRoot.ReadyMarker);
        if (stateRoute)
            NuplanePackageRootFixture.WriteStateFile(packages.Path, package, version, installPath);
        if (route.StartsWith("corrupt-", StringComparison.Ordinal))
            File.WriteAllText(Path.Join(installPath, "lib", "net10.0", package + ".dll"), "private-invalid-assembly-canary");

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
        Assert.DoesNotContain("private-invalid-assembly-canary", run.Text);
        Assert.Equal(shouldRefuse ? 3 : 0, run.ExitCode);
        Assert.Equal(run.ExitCode, response.ExitCode);
        if (shouldRefuse)
        {
            Assert.Equal("candidate-host-unavailable", response.Error?.Code);
            Assert.Null(response.Tooling);
        }
        else
        {
            Assert.Null(response.Error);
            var tooling = Assert.IsType<JsonElement>(response.Tooling);
            Assert.Equal("ok", tooling.GetProperty("status").GetString());
            var resolution = tooling.GetProperty("configurationResolution");
            Assert.Equal("default", resolution.GetProperty("shell").GetString());
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
        Assert.Equal(stateRoute, File.Exists(stateFile));
        Assert.Equal(probeRoute, File.Exists(markerFile));
        var markerTimestampAfter = File.Exists(markerFile) ? File.GetLastWriteTimeUtc(markerFile) : (DateTime?)null;
        Assert.Equal(markerTimestampBefore, markerTimestampAfter);
        var filesAfter = SnapshotPackageFiles(packages.Path);
        Assert.Equal(filesBefore.Keys.Order(StringComparer.Ordinal).ToArray(), filesAfter.Keys.Order(StringComparer.Ordinal).ToArray());
        foreach (var (path, bytes) in filesBefore)
            Assert.True(bytes.AsSpan().SequenceEqual(filesAfter[path]), $"Package-root file changed: {path}.");
        Assert.DoesNotContain(NuplaneInstallRoot.StagingDirectory,
            Directory.EnumerateDirectories(packages.Path, "*", SearchOption.AllDirectories).Select(Path.GetFileName));
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

    [Fact]
    public Task Candidate_worker_process_times_out_and_reaps_the_composer_child() =>
        AssertDefaultProcessRefusal("candidate-inspection-timeout", timeoutSeconds: 15, holdComposer: true);

    [Fact]
    public Task Candidate_worker_process_cancels_and_reaps_the_composer_child() =>
        AssertDefaultProcessRefusal("candidate-inspection-cancelled", timeoutSeconds: 120, holdComposer: true, cancelAfterStart: true);

    [Fact]
    public Task Candidate_worker_process_bounds_a_child_that_floods_stderr_then_stdout() =>
        AssertDefaultProcessRefusal("candidate-response-too-large", timeoutSeconds: 60,
            standardErrorBytes: 256 * 1024, standardOutputBytes: 4 * 1024 * 1024 + 1);

    [Fact]
    public Task Candidate_worker_process_timeout_reaps_a_descendant_of_the_composer_child() =>
        AssertDefaultProcessRefusal("candidate-inspection-timeout", timeoutSeconds: 15,
            holdComposer: true, expectDescendant: true);

    [Fact]
    public Task Candidate_worker_process_timeout_reaps_a_descendant_after_the_worker_exits() =>
        AssertDefaultProcessRefusal("candidate-inspection-timeout", timeoutSeconds: 15,
            expectDescendant: true, expectRootExited: true);

    [Fact]
    public Task Candidate_worker_owner_reaps_payload_and_descendant_when_frontend_lease_is_lost() =>
        AssertUnixOwnerCompletion(killOwnerOnly: false);

    [Fact]
    public Task Unix_group_completion_waits_for_live_payload_after_its_leader_exits() =>
        AssertUnixOwnerCompletion(killOwnerOnly: true);

    private static async Task AssertUnixOwnerCompletion(bool killOwnerOnly)
    {
        // The manual owner handshake exercises the Unix session/process-group path. The Windows job
        // runtime is outside this manual Unix control.
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return;

        var host = HostLayout.Resolve(DotnetElsa.Host("ResourceAwareLiveHost"));
        using var sentinels = new TempDirectory($"{PrivateCanaryRootPrefix}owner-lease-");
        var started = sentinels.File("worker-started.txt");
        var descendant = sentinels.File("descendant-started.txt");
        var database = sentinels.File("must-not-create.db");
        var context = sentinels.File("context-constructed.txt");
        var action = sentinels.File("action-constructed.txt");
        var request = ForHost(host, database, new Dictionary<string, object>
        {
            ["WriteConsoleCanary"] = false,
            ["StartedMarker"] = started,
            ["HoldMilliseconds"] = 60_000,
            ["DescendantMarker"] = descendant,
            ["ContextMarker"] = context,
            ["ActionMarker"] = action
        });

        var workerAssembly = Path.Join(Path.GetDirectoryName(DotnetElsa.ToolAssembly), WorkerProcess.WorkerAssemblyFileName);
        var correlation = Guid.NewGuid();
        var pipeName = "ec" + correlation.ToString("N");
        using var control = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var start = new ProcessStartInfo(DotnetMuxer.Path())
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in WorkerProcess.Arguments(host, workerAssembly).Append("--candidate-inspection"))
            start.ArgumentList.Add(argument);
        start.ArgumentList[5] = workerAssembly;
        start.ArgumentList.Add("--candidate-owner");
        start.ArgumentList.Add(pipeName);
        start.ArgumentList.Add(correlation.ToString("N"));
        start.ArgumentList.Add(start.ArgumentList[2]);
        start.ArgumentList.Add(start.ArgumentList[4]);
        start.ArgumentList.Add(workerAssembly);

        Process? ownerProcess = null;
        (int Pid, long StartToken) ownerIdentity = default;
        Task? ownerExit = null;
        Task<string>? standardOutput = null;
        Task<string>? standardError = null;
        Task? groupCompletion = null;
        using var groupDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            ownerProcess = Process.Start(start) ?? throw new InvalidOperationException("The candidate owner did not start.");
            ownerIdentity = (ownerProcess.Id, ProcessIdentityReader.Read(ownerProcess.Id).StartToken);
            ownerExit = ownerProcess.WaitForExitAsync();
            standardOutput = ownerProcess.StandardOutput.ReadToEndAsync();
            standardError = ownerProcess.StandardError.ReadToEndAsync();

            using var handshakeDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await control.WaitForConnectionAsync(handshakeDeadline.Token);
            var ready = new byte[CandidateProcessOwner.ReadyFrameLength];
            await control.ReadExactlyAsync(ready, handshakeDeadline.Token);
            Assert.Equal(CandidateProcessOwner.Ready, ready[0]);
            Assert.Equal(correlation, new Guid(ready.AsSpan(1)));

            await control.WriteAsync(new byte[] { CandidateProcessOwner.Go }, handshakeDeadline.Token);
            await control.FlushAsync(handshakeDeadline.Token);
            await JsonSerializer.SerializeAsync(ownerProcess.StandardInput.BaseStream, request, WorkerContract.Json,
                handshakeDeadline.Token);
            await ownerProcess.StandardInput.BaseStream.FlushAsync(handshakeDeadline.Token);
            ownerProcess.StandardInput.Close();

            Assert.True(await WaitForMarkerOrCompletion(ownerExit!, started, TimeSpan.FromSeconds(20)),
                "The payload composer did not record its process identity.");
            Assert.True(await WaitForMarkerOrCompletion(ownerExit!, descendant, TimeSpan.FromSeconds(20)),
                "The payload descendant did not record its process identity.");
            Assert.True(TryReadProcessIdentity(started, out var payloadIdentity));
            Assert.True(TryReadProcessIdentity(descendant, out var descendantIdentity));
            Assert.NotEqual(ownerIdentity.Pid, payloadIdentity.Pid);
            Assert.NotEqual(payloadIdentity.Pid, descendantIdentity.Pid);
            Assert.True(IsMarkedProcessRunning(started), "The payload composer must be live before lease loss.");
            Assert.True(IsMarkedProcessRunning(descendant), "The payload descendant must be live before lease loss.");
            Assert.False(ownerExit!.IsCompleted, "The owner must retain the lease before the frontend closes control.");

            if (killOwnerOnly)
            {
                // Deliberately remove only this test's leader. Its original group remains anchored by the
                // exact marked children: native completion observation must not mistake leader exit for exit.
                ownerProcess.Kill();
                await ownerExit!.WaitAsync(TimeSpan.FromSeconds(20));
                Assert.True(ownerProcess.HasExited);
                Assert.True(IsMarkedProcessRunning(started));
                Assert.True(IsMarkedProcessRunning(descendant));
                var liveGroupObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var monitor = new CandidateUnixProcessGroup((groupId, token) =>
                {
                    var members = CandidateUnixProcessGroup.ReadMembers(groupId, token).ToArray();
                    if (members.Any(member => member.GroupId == groupId && member.IsExecuting))
                        liveGroupObserved.TrySetResult();
                    return members;
                });
                groupCompletion = monitor.WaitForExitAsync(ownerProcess.Id, groupDeadline.Token);
                await liveGroupObserved.Task.WaitAsync(TimeSpan.FromSeconds(20));
                Assert.False(groupCompletion.IsCompleted,
                    "Native group completion returned after leader exit while a matching marked child remained live.");
                Assert.True(IsMarkedProcessRunning(started));
                Assert.True(IsMarkedProcessRunning(descendant));
                await KillMarkedProcessIfStillRunning(descendant);
                await KillMarkedProcessIfStillRunning(started);
            }
            else
            {
                // Simulate abrupt frontend loss. EOF must terminate the entire owned scope, with observed
                // group completion rather than treating the supervisor's asynchronous exit as sufficient.
                control.Dispose();
                await ownerExit!.WaitAsync(TimeSpan.FromSeconds(20));
                groupCompletion = new CandidateUnixProcessGroup().WaitForExitAsync(ownerProcess.Id, groupDeadline.Token);
            }
            await groupCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.WhenAll(standardOutput!, standardError!).WaitAsync(TimeSpan.FromSeconds(20));
            Assert.False(IsMarkedProcessRunning(started), "The payload composer survived owner lease loss.");
            Assert.False(IsMarkedProcessRunning(descendant), "The payload descendant survived owner lease loss.");
            Assert.False(File.Exists(database));
            Assert.False(File.Exists(context));
            Assert.False(File.Exists(action));
        }
        finally
        {
            groupDeadline.Cancel();
            try
            {
                control.Dispose();
            }
            finally
            {
                try
                {
                    if (ownerProcess is not null)
                        await StopOwnedProcessIfStillRunning(ownerProcess, ownerIdentity);
                }
                finally
                {
                    try
                    {
                        await KillMarkedProcessIfStillRunning(descendant);
                    }
                    finally
                    {
                        try
                        {
                            await KillMarkedProcessIfStillRunning(started);
                        }
                        finally
                        {
                            try
                            {
                                if (ownerExit is not null)
                                    await ObserveOwnedTask(ownerExit);
                            }
                            finally
                            {
                                try
                                {
                                    if (standardOutput is not null)
                                        await ObserveOwnedTask(standardOutput);
                                }
                                finally
                                {
                                    try
                                    {
                                        if (standardError is not null)
                                            await ObserveOwnedTask(standardError);
                                    }
                                    finally
                                    {
                                        try
                                        {
                                            if (groupCompletion is not null)
                                                await ObserveOwnedTask(groupCompletion);
                                        }
                                        finally
                                        {
                                            ownerProcess?.Dispose();
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public async Task Candidate_worker_process_cancels_a_real_child_while_its_stdin_write_is_blocked()
    {
        var host = HostLayout.Resolve(DotnetElsa.Host("ResourceAwareLiveHost"));
        using var sentinels = new TempDirectory($"{PrivateCanaryRootPrefix}stdin-child-");
        var worker = sentinels.File("stdin-child.dll");
        File.Copy(Path.Join(host.Directory, host.Name + ".dll"), worker);
        var marker = sentinels.File("candidate-stdin-started.txt");
        var database = sentinels.File("must-not-create.db");
        var request = ForHost(host, database);
        // Four admitted 1 MiB sources yield more than 5 MiB of private stdin. The real child
        // acknowledges a bounded 4 KiB header, then stops reading, leaving the pipe write pending.
        var content = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
            "{\"Padding\":\"" + new string('x', 1024 * 1024 - 14) + "\"}"));
        request = request with
        {
            HostDirectory = sentinels.Path,
            Candidate = request.Candidate! with
            {
                Files = new[]
                {
                    "appsettings.json", "appsettings.Production.json", "shells.json", "shells.Production.json"
                }.Select(name => new WorkerCandidateFile
                {
                    Name = name, CaptureId = request.Candidate.CaptureId, Content = content
                }).ToArray()
            }
        };
        Assert.Equal(1024 * 1024, Convert.FromBase64String(content).Length);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var run = new CandidateWorkerProcess(workerAssembly: worker).RunAsync(host, request, 60, cancellation.Token);
        try
        {
            Assert.True(await WaitForMarkerOrCompletion(run, marker, TimeSpan.FromSeconds(30)),
                "The real child must acknowledge stdin before write-stage cancellation.");
            Assert.True(TryReadProcessIdentity(marker, out _));
            Assert.False(run.IsCompleted);
            cancellation.Cancel();
            var refusal = await Assert.ThrowsAsync<CliRefusal>(() => run.WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.Equal("candidate-inspection-cancelled", refusal.Code);
            Assert.Equal(ToolExitCode.Refusal, refusal.ExitCode);
            Assert.DoesNotContain(PrivateCanaryRootPrefix, refusal.ToString(), StringComparison.Ordinal);
            Assert.False(IsMarkedProcessRunning(marker), "The stdin-blocked child must be reaped before refusal.");
            Assert.False(File.Exists(database));
        }
        finally
        {
            cancellation.Cancel();
            try { await run.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (CliRefusal) { }
            catch (OperationCanceledException) { }
            catch (TimeoutException) { }
            await KillMarkedProcessIfStillRunning(marker);
        }
    }

    private static async Task AssertDefaultProcessRefusal(string expectedCode, int timeoutSeconds,
        bool holdComposer = false, bool cancelAfterStart = false, int standardErrorBytes = 0, int standardOutputBytes = 0,
        bool expectDescendant = false, bool expectRootExited = false)
    {
        var host = HostLayout.Resolve(DotnetElsa.Host("ResourceAwareLiveHost"));
        using var sentinels = new TempDirectory($"{PrivateCanaryRootPrefix}adverse-child-");
        var started = sentinels.File("worker-started.txt");
        var descendant = expectDescendant ? sentinels.File("descendant-started.txt") : null;
        var database = sentinels.File("must-not-create.db");
        var context = sentinels.File("context-constructed.txt");
        var action = sentinels.File("action-constructed.txt");
        var probeDefaults = new Dictionary<string, object>
        {
            ["WriteConsoleCanary"] = false,
            ["StartedMarker"] = started,
            ["HoldMilliseconds"] = holdComposer ? 60_000 : 0,
            ["StandardErrorBytes"] = standardErrorBytes,
            ["StandardOutputBytes"] = standardOutputBytes,
            ["DescendantMarker"] = descendant ?? string.Empty,
            ["ContextMarker"] = context,
            ["ActionMarker"] = action
        };
        var request = ForHost(host, database, probeDefaults);
        var worker = Path.Join(Path.GetDirectoryName(DotnetElsa.ToolAssembly), WorkerProcess.WorkerAssemblyFileName);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(75));
        var run = new CandidateWorkerProcess(workerAssembly: worker).RunAsync(host, request, timeoutSeconds, cancellation.Token);

        try
        {
            var parentStarted = await WaitForMarkerOrCompletion(run, started, TimeSpan.FromSeconds(60));
            var descendantStarted = descendant is null ||
                await WaitForMarkerOrCompletion(run, descendant, TimeSpan.FromSeconds(60));
            Assert.True(parentStarted && TryReadProcessIdentity(started, out _) && descendantStarted &&
                        (descendant is null || TryReadProcessIdentity(descendant, out _)),
                "The real worker completed or failed to record the required private process identity markers.");

            if (expectRootExited)
            {
                Assert.NotNull(descendant);
                Assert.True(await WaitForRootExitWithLiveDescendant(run, started, descendant!, TimeSpan.FromSeconds(30)),
                    "The worker must exit while its marked descendant remains live and holds the worker pipes open.");
                Assert.False(run.IsCompleted, "The worker exchange must remain pending until the inherited pipes are closed.");
            }

            if (cancelAfterStart)
                cancellation.Cancel();

            var refusal = await Assert.ThrowsAsync<CliRefusal>(() => run.WaitAsync(TimeSpan.FromSeconds(30)));
            Assert.Equal(expectedCode, refusal.Code);
            Assert.Equal(expectedCode == "candidate-inspection-cancelled" ? ToolExitCode.Refusal : ToolExitCode.ResolutionFailure,
                refusal.ExitCode);
            Assert.DoesNotContain(PrivateCanaryRootPrefix, refusal.Message);
            Assert.DoesNotContain(PrivateCanaryRootPrefix, string.Join("\n", refusal.Details));
            var childStillRunning = IsMarkedProcessRunning(started);
            var observedState = childStillRunning && OperatingSystem.IsLinux() && TryReadProcessIdentity(started, out var childIdentity)
                ? $" Linux PID {childIdentity.Pid}, state {ProcessIdentityReader.ReadLinux(childIdentity.Pid).State}."
                : string.Empty;
            Assert.False(childStillRunning, "The candidate worker returned while its marked child was still alive." + observedState);
            if (descendant is not null)
                Assert.False(IsMarkedProcessRunning(descendant), "The candidate worker returned while its marked descendant was still alive.");
            Assert.False(File.Exists(database));
            Assert.False(File.Exists(context));
            Assert.False(File.Exists(action));
        }
        finally
        {
            cancellation.Cancel();
            try { await run.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (CliRefusal) { }
            catch (OperationCanceledException) { }
            catch (TimeoutException) { }
            if (descendant is not null)
                await KillMarkedProcessIfStillRunning(descendant);
            await KillMarkedProcessIfStillRunning(started);
        }
    }

    private static async Task<bool> WaitForRootExitWithLiveDescendant(Task run, string rootMarker,
        string descendantMarker, TimeSpan maximumWait)
    {
        var stopAt = Stopwatch.GetTimestamp() + (long)(maximumWait.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < stopAt)
        {
            if (run.IsCompleted)
                return false;
            if (!IsMarkedProcessRunning(rootMarker) && IsMarkedProcessRunning(descendantMarker))
                return true;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
        return false;
    }

    private static async Task<bool> WaitForMarkerOrCompletion(Task run, string marker, TimeSpan maximumWait)
    {
        var stopAt = Stopwatch.GetTimestamp() + (long)(maximumWait.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < stopAt)
        {
            if (TryReadProcessIdentity(marker, out _))
                return true;
            if (run.IsCompleted)
                return false;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
        return false;
    }

    private static bool TryReadProcessIdentity(string marker, out (int Pid, long StartToken) identity)
    {
        identity = default;
        if (!File.Exists(marker))
            return false;
        var parts = File.ReadAllText(marker).Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out var pid) || !long.TryParse(parts[1], out var startedAtTicks))
            return false;
        identity = (pid, startedAtTicks);
        return true;
    }

    private static bool IsMarkedProcessRunning(string marker)
    {
        if (!TryReadProcessIdentity(marker, out var identity))
            return false;
        try
        {
            if (OperatingSystem.IsLinux())
            {
                var observed = ProcessIdentityReader.ReadLinux(identity.Pid);
                return observed.Identity.StartToken == identity.StartToken && observed.State is not ('Z' or 'X' or 'x');
            }
            using var process = Process.GetProcessById(identity.Pid);
            if (process.HasExited || ProcessIdentityReader.Read(process.Id).StartToken != identity.StartToken)
                return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (FileNotFoundException) when (OperatingSystem.IsLinux())
        {
            return false; // The kernel process entry disappeared.
        }
        catch (DirectoryNotFoundException) when (OperatingSystem.IsLinux())
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception) when (!OperatingSystem.IsWindows())
        {
            if (!IsUnixProcessRunning(identity.Pid))
                return false;
            throw; // A live PID without an established identity is unknown, not a passing observation.
        }
        // Observer failures must propagate rather than being caught as identity-probe exit races.
        return OperatingSystem.IsWindows() || IsUnixProcessRunning(identity.Pid);
    }

    private static bool IsUnixProcessRunning(int pid)
    {
        // macOS zombies can retain a readable StartTime and HasExited=false. Always inspect state,
        // not just when StartTime throws. Only an absent PID or explicit zombie proves non-execution.
        var start = new ProcessStartInfo("/bin/ps")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-p");
        start.ArgumentList.Add(pid.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.ArgumentList.Add("-o");
        start.ArgumentList.Add("stat=");
        using var observer = Process.Start(start) ?? throw new InvalidOperationException("Process observation failed.");
        if (!observer.WaitForExit(5000))
        {
            observer.Kill(entireProcessTree: true);
            Assert.True(observer.WaitForExit(5000), "The owned process observer did not terminate.");
            throw new TimeoutException("Process observation did not complete.");
        }
        var state = observer.StandardOutput.ReadToEnd().Trim();
        Assert.Contains(observer.ExitCode, new[] { 0, 1 });
        if (state.Length == 0 && observer.ExitCode == 1 || state.StartsWith('Z'))
            return false;
        Assert.True(observer.ExitCode == 0 && state.Length > 0, "Process state could not be established.");
        return true;
    }

    private static async Task KillMarkedProcessIfStillRunning(string marker)
    {
        if (!TryReadProcessIdentity(marker, out var identity))
            return;
        try
        {
            using var process = Process.GetProcessById(identity.Pid);
            if (ProcessIdentityReader.Read(process.Id).StartToken != identity.StartToken || process.HasExited)
                return;
            process.Kill(entireProcessTree: true);
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(cleanup.Token);
        }
        catch (ArgumentException)
        {
            // The marked child has already exited.
        }
        catch (InvalidOperationException)
        {
            // The marked child has already exited.
        }
        catch (FileNotFoundException) when (OperatingSystem.IsLinux())
        {
            // The kernel process entry disappeared during identity observation.
        }
        catch (DirectoryNotFoundException) when (OperatingSystem.IsLinux())
        {
            // The marked process has already exited.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The marked child has already exited or is no longer owned by this test.
        }
        catch (OperationCanceledException)
        {
            // Cleanup was bounded; do not wait indefinitely on a broken test child.
        }
    }

    private static async Task StopOwnedProcessIfStillRunning(Process process, (int Pid, long StartToken) identity)
    {
        try
        {
            if (process.Id != identity.Pid || ProcessIdentityReader.Read(process.Id).StartToken != identity.StartToken ||
                process.HasExited)
                return;
            process.Kill();
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(cleanup.Token);
        }
        catch (ArgumentException)
        {
            // The owned supervisor has already exited.
        }
        catch (InvalidOperationException)
        {
            // The owned supervisor has already exited.
        }
        catch (FileNotFoundException) when (OperatingSystem.IsLinux())
        {
            // The kernel process entry disappeared during identity observation.
        }
        catch (DirectoryNotFoundException) when (OperatingSystem.IsLinux())
        {
            // The marked process has already exited.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The owned supervisor has already exited or is no longer owned by this test.
        }
        catch (OperationCanceledException)
        {
            // Cleanup was bounded; do not wait indefinitely on a broken test supervisor.
        }
    }

    private static async Task ObserveOwnedTask(Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (IOException)
        {
            // A bounded stream observer may fault when its owned process is terminated during cleanup.
        }
        catch (ObjectDisposedException)
        {
            // A bounded stream observer may be disposed with its owned process during cleanup.
        }
        catch (InvalidOperationException)
        {
            // The owned process may have been disposed while its bounded observer completed.
        }
        catch (OperationCanceledException)
        {
            // Cleanup was bounded; do not wait indefinitely on a broken test process.
        }
    }

    private static WorkerRequest ForHost(HostLayout host, string? databasePath = null, object? probeDefaults = null) => Request() with
    {
        HostDirectory = host.Directory, HostName = host.Name, DepsFile = host.DepsFile,
        Candidate = Request().Candidate! with
        {
            AcceptedFeatureIds = ["ResourceProbe"],
            Files = new[]
            {
                ("appsettings.json", JsonSerializer.Serialize(new
                {
                    ProbeDefaults = probeDefaults ?? new { WriteConsoleCanary = true },
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
