using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Cli.Worker;
using Xunit;

namespace Elsa.Cli.Tests;

/// <summary>Lifecycle evidence that requires the actual CLI child rather than a direct capture or parser call.</summary>
public sealed class CandidateInspectionLifecycleTests
{
    [Theory]
    [InlineData("host-refusal", true)]
    [InlineData("host-refusal", false)]
    [InlineData("process-flood", true)]
    [InlineData("process-flood", false)]
    [InlineData("process-timeout", true)]
    [InlineData("process-timeout", false)]
    public async Task Post_dispatch_refusals_recheck_inputs_and_preserve_unchanged_outcomes(string outcome, bool changeInput)
    {
        if (OperatingSystem.IsWindows())
            return;
        using var fixture = new CandidateInspectionFixture();
        var selectedProfile = await CandidateInspectionTests.PrepareAcceptedEditAsync(fixture, workspace: true);
        Assert.NotNull(selectedProfile);
        var unusedProfile = fixture.WriteWorkspaceProfile("unused-refusal.json", "unused-refusal", "1",
            [CandidateInspectionFixture.StructuredLogsFeatureId]).Path;
        var startedMarker = fixture.InputPath("refusal-composer-started.txt");
        ConfigureProbe(fixture, probe =>
        {
            probe["StartedMarker"] = startedMarker;
            probe["HoldMilliseconds"] = outcome == "process-timeout" ? 30_000 : 4_000;
            if (outcome == "process-flood")
                probe["StandardOutputBytes"] = 8 * 1024 * 1024;
        });
        if (outcome == "host-refusal")
        {
            var path = Path.Join(fixture.SourceDirectory, "appsettings.json");
            var root = JsonNode.Parse(File.ReadAllText(path))!;
            root["Elsa"]!["Persistence"]!["Resources"]!["primary"]!["Provider"] = "Unsupported";
            File.WriteAllText(path, root.ToJsonString());
        }

        var pending = Task.Run(() => DotnetElsa.Run(fixture.SentinelEnvironment, fixture.InspectionArguments(
            "json", [selectedProfile!, unusedProfile], timeoutSeconds: outcome == "process-timeout" ? 15 : 60)));
        try
        {
            await WaitForMarkerAsync(startedMarker, pending, TimeSpan.FromSeconds(15));
            if (changeInput)
                File.AppendAllText(unusedProfile, " ");
            var refusal = await pending.WaitAsync(TimeSpan.FromSeconds(75));
            var expectedCode = changeInput ? "composition-input-changed" : outcome switch
            {
                "host-refusal" => "resource-context-conflict",
                "process-flood" => "candidate-response-too-large",
                _ => "candidate-inspection-timeout"
            };

            Assert.Equal(!changeInput && outcome == "host-refusal" ? ToolExitCode.Refusal : ToolExitCode.ResolutionFailure,
                refusal.ExitCode);
            Assert.Empty(refusal.Output);
            Assert.Contains(expectedCode, refusal.Error, StringComparison.Ordinal);
            Assert.DoesNotContain(CandidateInspectionFixture.PrivateCanary, refusal.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(fixture.SourceDirectory, refusal.Text, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Join(fixture.SourceDirectory, "candidate")));
            AssertNoLiveArtifacts(fixture);
        }
        finally
        {
            if (!pending.IsCompleted)
                _ = await pending.WaitAsync(TimeSpan.FromSeconds(75));
        }
    }

    [Fact]
    public async Task Changed_unused_profile_after_host_dispatch_refuses_before_final_stdout()
    {
        if (OperatingSystem.IsWindows())
            return;
        using var fixture = new CandidateInspectionFixture();
        var selectedProfile = await CandidateInspectionTests.PrepareAcceptedEditAsync(fixture, workspace: true);
        Assert.NotNull(selectedProfile);
        var unusedProfile = fixture.WriteWorkspaceProfile("unused-lifecycle.json", "unused-lifecycle", "1",
            [CandidateInspectionFixture.StructuredLogsFeatureId]).Path;
        var startedMarker = fixture.InputPath("composer-started.txt");
        ConfigureProbe(fixture, probe =>
        {
            probe["StartedMarker"] = startedMarker;
            probe["HoldMilliseconds"] = 4_000;
        });

        var pending = Task.Run(() => DotnetElsa.Run(fixture.SentinelEnvironment, fixture.InspectionArguments(
            "json", [selectedProfile!, unusedProfile], timeoutSeconds: 15)));
        try
        {
            await WaitForMarkerAsync(startedMarker, pending, TimeSpan.FromSeconds(10));
            File.AppendAllText(unusedProfile, " ");

            var refusal = await pending.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(ToolExitCode.ResolutionFailure, refusal.ExitCode);
            Assert.Empty(refusal.Output);
            Assert.Contains("composition-input-changed", refusal.Error, StringComparison.Ordinal);
            Assert.DoesNotContain(CandidateInspectionFixture.PrivateCanary, refusal.Text, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Join(fixture.SourceDirectory, "candidate")));
            AssertNoLiveArtifacts(fixture);
        }
        finally
        {
            if (!pending.IsCompleted)
                _ = await pending.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    [Fact]
    public async Task Stale_accepted_selection_refuses_through_the_actual_command_without_preview()
    {
        if (OperatingSystem.IsWindows())
            return;
        using var fixture = new CandidateInspectionFixture();
        await CandidateInspectionTests.PrepareAcceptedEditAsync(fixture, workspace: false);
        var acceptedPath = fixture.InputPath("accepted.json");
        var accepted = JsonNode.Parse(File.ReadAllText(acceptedPath))!;
        accepted["add"]!.AsArray().Remove(
            accepted["add"]!.AsArray().Single(value =>
                value!.GetValue<string>() == CandidateInspectionFixture.StructuredLogsFeatureId));
        File.WriteAllText(acceptedPath, accepted.ToJsonString());

        var refusal = DotnetElsa.Run(fixture.SentinelEnvironment, fixture.InspectionArguments("json"));

        Assert.Equal(ToolExitCode.Refusal, refusal.ExitCode);
        Assert.Empty(refusal.Output);
        Assert.Contains("bridge-selection-drift", refusal.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(CandidateInspectionFixture.PrivateCanary, refusal.Text, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Join(fixture.SourceDirectory, "candidate")));
        AssertNoLiveArtifacts(fixture);
    }

    [Fact]
    public async Task Unused_source_fifo_refuses_before_read_without_starting_inspection()
    {
        if (OperatingSystem.IsWindows())
            return;
        using var fixture = new CandidateInspectionFixture();
        await CandidateInspectionTests.PrepareAcceptedEditAsync(fixture, workspace: false);
        var fifo = Path.Join(fixture.SourceDirectory, "shells.Unused.json");
        using (var mkfifo = Process.Start(new ProcessStartInfo("/usr/bin/mkfifo") { ArgumentList = { fifo } }))
        {
            Assert.NotNull(mkfifo);
            Assert.True(mkfifo.WaitForExit(2_000));
            Assert.Equal(0, mkfifo.ExitCode);
        }

        var run = await PseudoTerminalCli.RunProcessAsync(
            DotnetMuxer.Path(),
            ["exec", DotnetElsa.ToolAssembly, .. fixture.InspectionArguments("json")],
            "candidate-inspection-does-not-prompt",
            string.Empty,
            TimeSpan.FromSeconds(10));

        Assert.False(run.TimedOut);
        Assert.False(run.ResponseSent);
        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("bridge-source-unreadable", run.Output + run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(CandidateInspectionFixture.PrivateCanary, run.Output + run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("configurationResolution", run.Output + run.Error, StringComparison.Ordinal);
        AssertNoLiveArtifacts(fixture);
    }

    [Fact]
    public async Task Composer_exception_canary_becomes_a_fixed_safe_actual_child_refusal()
    {
        if (OperatingSystem.IsWindows())
            return;
        using var fixture = new CandidateInspectionFixture();
        await CandidateInspectionTests.PrepareAcceptedEditAsync(fixture, workspace: false);
        ConfigureProbe(fixture, probe => probe["ThrowPrivateCanary"] = true);

        var refusal = DotnetElsa.Run(fixture.SentinelEnvironment, fixture.InspectionArguments("json"));

        Assert.Equal(ToolExitCode.ResolutionFailure, refusal.ExitCode);
        Assert.Empty(refusal.Output);
        Assert.Contains("candidate-host-unavailable", refusal.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("candidate-private-canary-2177-composer-exception", refusal.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.SourceDirectory, refusal.Text, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Join(fixture.SourceDirectory, "candidate")));
        AssertNoLiveArtifacts(fixture);
    }

    [Fact]
    public async Task One_real_worker_response_renders_equivalent_json_and_text_without_live_claims()
    {
        if (OperatingSystem.IsWindows())
            return;
        using var fixture = new CandidateInspectionFixture();
        await CandidateInspectionTests.PrepareAcceptedEditAsync(fixture, workspace: false);
        var capture = CompositionInspectionCapture.Open(
            fixture.SourceDirectory,
            fixture.ShellId,
            fixture.Environment,
            fixture.InputPath("accepted.json"),
            fixture.InputPath("catalog.json"),
            reviewPath: null,
            workspaceProfilePaths: null);
        var host = HostLayout.Resolve(fixture.HostAssemblyDirectory);
        var request = new WorkerRequest
        {
            Command = WorkerCommands.InspectCandidate,
            HostDirectory = host.Directory,
            HostName = host.Name,
            DepsFile = host.DepsFile,
            PackageRoots = [],
            Candidate = capture.Payload
        };

        capture.VerifyUnchanged();
        var worker = Path.Join(Path.GetDirectoryName(DotnetElsa.ToolAssembly), WorkerProcess.WorkerAssemblyFileName);
        var response = await new CandidateWorkerProcess(workerAssembly: worker)
            .RunAsync(host, request, 60, CancellationToken.None);
        Assert.Equal(ToolExitCode.Success, response.ExitCode);
        Assert.Null(response.Error);
        var tooling = Assert.IsType<JsonElement>(response.Tooling);
        var output = new CandidateInspectionOutput();
        var json = output.Render(capture, tooling, response.ExitCode, "json");
        var text = output.Render(capture, tooling, response.ExitCode, "text");
        capture.VerifyUnchanged();

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("unchecked", root.GetProperty("plan").GetProperty("persistence").GetProperty("status").GetString());
        var resolution = root.GetProperty("configurationResolution");
        Assert.Equal("not-performed", resolution.GetProperty("targetVerification").GetString());
        Assert.Equal("unobserved", resolution.GetProperty("runtimeParity").GetString());
        Assert.Contains("Persistence: unchecked", text, StringComparison.Ordinal);
        Assert.Contains("targetVerification: not-performed", text, StringComparison.Ordinal);
        Assert.Contains("runtimeParity: unobserved", text, StringComparison.Ordinal);
        foreach (var set in resolution.GetProperty("selection").EnumerateObject())
            Assert.Contains($"{set.Name}: {string.Join(", ", set.Value.EnumerateArray().Select(value => value.GetString()))}",
                text, StringComparison.Ordinal);
        AssertParticipantsAppearInOrder(resolution.GetProperty("participants"), text);
        foreach (var canary in new[] { CandidateInspectionFixture.PrivateCanary, fixture.DatabasePath, "invocationId", "captureId" })
        {
            Assert.DoesNotContain(canary, json, StringComparison.Ordinal);
            Assert.DoesNotContain(canary, text, StringComparison.Ordinal);
        }
        Assert.False(Directory.Exists(Path.Join(fixture.SourceDirectory, "candidate")));
        AssertNoLiveArtifacts(fixture);
    }

    private static void ConfigureProbe(CandidateInspectionFixture fixture, Action<JsonObject> configure)
    {
        var path = Path.Join(fixture.SourceDirectory, "appsettings.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var probe = root["ProbeDefaults"]?.AsObject() ?? new JsonObject();
        root["ProbeDefaults"] = probe;
        configure(probe);
        File.WriteAllText(path, root.ToJsonString());
    }

    private static void AssertNoLiveArtifacts(CandidateInspectionFixture fixture)
    {
        Assert.False(File.Exists(fixture.DatabasePath));
        Assert.False(File.Exists(fixture.ContextMarkerPath));
        Assert.False(File.Exists(fixture.ActionMarkerPath));
    }

    private static async Task WaitForMarkerAsync(string marker, Task pending, TimeSpan timeout)
    {
        var stopAt = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < stopAt)
        {
            if (File.Exists(marker) && new FileInfo(marker).Length > 0)
                return;
            Assert.False(pending.IsCompleted, "Inspection completed before entering the marked composer stage.");
            await Task.Delay(50);
        }
        Assert.Fail("Inspection did not enter the marked composer stage before the bounded wait elapsed.");
    }

    private static void AssertParticipantsAppearInOrder(JsonElement participants, string text)
    {
        var offset = 0;
        foreach (var participant in participants.EnumerateArray())
        {
            var heading = $"{participant.GetProperty("feature").GetString()} -> {participant.GetProperty("module").GetString()}";
            var index = text.IndexOf(heading, offset, StringComparison.Ordinal);
            Assert.True(index >= offset, $"The text projection omitted or reordered participant '{heading}'.");
            offset = index + heading.Length;
            foreach (var field in participant.EnumerateObject().Where(field => field.Name is not ("feature" or "module")))
                Assert.Contains($"{field.Name}: {field.Value.GetString() ?? "not projected"}", text, StringComparison.Ordinal);
        }
    }
}
