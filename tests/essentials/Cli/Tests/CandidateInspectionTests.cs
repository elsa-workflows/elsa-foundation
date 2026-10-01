using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using Elsa.Cli.Worker;
using Elsa.Modularity.Planning.Bridge;
using Xunit;

namespace Elsa.Cli.Tests;

/// <summary>Actual file-authoring actors through the built CLI, worker and installed host closure.</summary>
public sealed class CandidateInspectionTests
{
    private static readonly string[] ExpectedSelection =
    [
        CandidateInspectionFixture.OpenTelemetryFeatureId,
        CandidateInspectionFixture.StructuredLogsFeatureId,
        CandidateInspectionFixture.StructuredLogsEfFeatureId,
        CandidateInspectionFixture.ResourceProbeFeatureId
    ];

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Accepted_import_and_workspace_edits_reach_real_host_preview_and_file_absent_generation(bool workspace, bool fileEfEnabled)
    {
        if (OperatingSystem.IsWindows())
            return; // The existing PTY helper exercises these journeys on supported Unix hosts.
        using var fixture = new CandidateInspectionFixture();
        var generateAbsentComposerDefault = workspace && !fileEfEnabled;
        var profile = await PrepareAcceptedEditAsync(fixture, workspace, fileEfEnabled,
            includeReviewedSetting: generateAbsentComposerDefault);
        var settingReviewPath = generateAbsentComposerDefault ? fixture.SettingReviewPath : null;
        if (generateAbsentComposerDefault)
        {
            var source = fixture.CaptureSource().Snapshot;
            var fileSelection = CshellsSourceReader.Read(source.ReadText("shells.json"),
                source.ReadText(source.Selection.ShellOverlayFileName), fixture.ShellId);
            Assert.DoesNotContain(CandidateInspectionFixture.OpenTelemetryEfFeatureId, fileSelection.EnabledFeatureIds);
        }

        var inspection = DotnetElsa.Run(fixture.SentinelEnvironment, fixture.InspectionArguments(
            "json", profile is null ? null : [profile], settingReviewPath: settingReviewPath));

        Assert.Equal(ToolExitCode.Success, inspection.ExitCode);
        using var document = JsonDocument.Parse(inspection.Output);
        var root = document.RootElement;
        Assert.Equal(new[] { "plan", "configurationResolution" }, root.EnumerateObject().Select(property => property.Name));
        var plan = root.GetProperty("plan");
        Assert.Equal(ExpectedSelection, Strings(plan.GetProperty("candidate").GetProperty("featureIds")));
        Assert.Equal(ExpectedSelection, Strings(plan.GetProperty("accepted").GetProperty("featureIds")));
        Assert.Equal("unchecked", plan.GetProperty("persistence").GetProperty("status").GetString());
        var resolution = root.GetProperty("configurationResolution");
        var selection = resolution.GetProperty("selection");
        foreach (var name in new[] { "acceptedFeatureIds", "requestedFeatureIds", "effectiveFeatureIds" })
            Assert.Equal(ExpectedSelection, Strings(selection.GetProperty(name)));
        Assert.Empty(Strings(selection.GetProperty("implicitFeatureIds")));
        Assert.Contains(CandidateInspectionFixture.OpenTelemetryEfFeatureId, Strings(selection.GetProperty("disabledFeatureIds")));
        var participants = resolution.GetProperty("participants").EnumerateArray().ToArray();
        Assert.Equal(new[] { CandidateInspectionFixture.StructuredLogsEfFeatureId, CandidateInspectionFixture.ResourceProbeFeatureId },
            participants.Select(row => row.GetProperty("feature").GetString()));
        Assert.All(participants, row =>
        {
            Assert.Equal("primary", row.GetProperty("resource").GetString());
            Assert.Equal("Sqlite", row.GetProperty("provider").GetString());
            Assert.Equal("Probe", row.GetProperty("connectionReference").GetString());
            Assert.Equal("unavailable", row.GetProperty("exactFileProvenance").GetString());
        });
        Assert.Equal("not-performed", resolution.GetProperty("targetVerification").GetString());
        Assert.Equal("unobserved", resolution.GetProperty("runtimeParity").GetString());
        Assert.Equal("unobserved", resolution.GetProperty("activation").GetString());

        var text = DotnetElsa.Run(fixture.SentinelEnvironment, fixture.InspectionArguments(
            workspaceProfiles: profile is null ? null : [profile], timeoutSeconds: 300,
            settingReviewPath: settingReviewPath));
        Assert.Equal(ToolExitCode.Success, text.ExitCode);
        Assert.StartsWith("Candidate: ", text.Output, StringComparison.Ordinal);
        Assert.Contains("primary", text.Output, StringComparison.Ordinal);
        Assert.Contains("Sqlite", text.Output, StringComparison.Ordinal);
        Assert.Contains("Probe", text.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(CandidateInspectionFixture.OpenTelemetryEfFeatureId + " ->", text.Output, StringComparison.Ordinal);
        foreach (var output in new[] { inspection.Text, text.Text })
        {
            Assert.DoesNotContain(CandidateInspectionFixture.PrivateCanary, output, StringComparison.Ordinal);
            Assert.DoesNotContain(fixture.DatabasePath, output, StringComparison.Ordinal);
            Assert.DoesNotContain("invocationId", output, StringComparison.Ordinal);
            Assert.DoesNotContain("captureId", output, StringComparison.Ordinal);
        }
        var subsequentPlan = DotnetElsa.Run(fixture.SentinelEnvironment, PlanArguments(fixture, profile));
        Assert.Equal(ToolExitCode.Success, subsequentPlan.ExitCode);
        using var planned = JsonDocument.Parse(subsequentPlan.Output);
        Assert.Equal(ExpectedSelection, Strings(planned.RootElement.GetProperty("candidate").GetProperty("featureIds")));
        Assert.Equal("unchecked", planned.RootElement.GetProperty("persistence").GetProperty("status").GetString());
        Assert.False(Directory.Exists(fixture.CandidateOutputDirectory));

        if (generateAbsentComposerDefault)
        {
            var generated = await fixture.RunGenerateInteractiveAsync(profile!, settingReviewPath!);
            Assert.Equal(ToolExitCode.Success, generated.ExitCode);
            Assert.True(generated.ResponseSent);
            Assert.False(generated.TimedOut);
            Assert.Contains("Candidate host files generated.", generated.Output, StringComparison.Ordinal);
            var generatedText = generated.Output + generated.Error;
            Assert.DoesNotContain(CandidateInspectionFixture.PrivateCanary, generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain(fixture.DatabasePath, generatedText, StringComparison.Ordinal);

            var candidate = CompositionFileSource.Open(fixture.CandidateOutputDirectory, fixture.ShellId, fixture.Environment).Snapshot;
            var overlayJson = candidate.ReadText(candidate.Selection.ShellOverlayFileName);
            using var overlayDocument = JsonDocument.Parse(overlayJson);
            var featureOverlay = overlayDocument.RootElement.GetProperty("CShells").GetProperty("Shells")
                .GetProperty(fixture.ShellId).GetProperty("Features");
            Assert.Equal(JsonValueKind.False, featureOverlay.GetProperty(CandidateInspectionFixture.OpenTelemetryEfFeatureId).ValueKind);

            var merged = CshellsSourceReader.Read(candidate.ReadText("shells.json"), overlayJson, fixture.ShellId);
            Assert.DoesNotContain(CandidateInspectionFixture.OpenTelemetryEfFeatureId, merged.EnabledFeatureIds);
            Assert.Contains(CandidateInspectionFixture.OpenTelemetryEfFeatureId, merged.DisabledFeatureIds);
            Assert.Contains(CandidateInspectionFixture.PrivateCanary,
                candidate.ReadText("appsettings.json"), StringComparison.Ordinal);
            using var generatedSettings = JsonDocument.Parse(candidate.ReadText("appsettings.json"));
            Assert.Equal(CandidateInspectionFixture.PrivateCanary,
                generatedSettings.RootElement.GetProperty("UnknownLocal").GetProperty("Nested").GetString());
        }
        else
            Assert.False(Directory.Exists(fixture.CandidateOutputDirectory));

        fixture.AssertSourcesUnchanged();
        fixture.AssertInputsUnchanged();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Inspection_requires_explicit_host_trust_without_a_preview(bool workspace)
    {
        if (OperatingSystem.IsWindows())
            return;
        using var fixture = new CandidateInspectionFixture();
        var profile = await PrepareAcceptedEditAsync(fixture, workspace);

        var refusal = DotnetElsa.Run(fixture.SentinelEnvironment, fixture.InspectionArguments(
            "json", profile is null ? null : [profile], trust: false));

        Assert.Equal(ToolExitCode.Refusal, refusal.ExitCode);
        Assert.Equal(string.Empty, refusal.Output);
        Assert.Contains("candidate-trust-required", refusal.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(CandidateInspectionFixture.PrivateCanary, refusal.Text, StringComparison.Ordinal);
        fixture.AssertSourcesUnchanged();
        fixture.AssertInputsUnchanged();
    }

    [Fact]
    public void Inspection_refuses_a_missing_trusted_host_without_echoing_its_path()
    {
        if (OperatingSystem.IsWindows())
            return;
        using var fixture = new CandidateInspectionFixture();
        var missingHost = fixture.InputPath($"{CandidateInspectionFixture.PrivateCanary}-missing-host");
        Assert.False(Directory.Exists(missingHost));

        var refusal = DotnetElsa.Run(fixture.SentinelEnvironment, fixture.InspectionArguments(
            "json", hostDirectory: missingHost));

        Assert.Equal(ToolExitCode.ResolutionFailure, refusal.ExitCode);
        Assert.Empty(refusal.Output);
        Assert.Contains("error [candidate-host-unavailable]:", refusal.Error, StringComparison.Ordinal);
        Assert.Contains("selected installed host layout could not be inspected", refusal.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(CandidateInspectionFixture.PrivateCanary, refusal.Text, StringComparison.Ordinal);
        fixture.AssertSourcesUnchanged();
    }

    [Fact]
    public async Task Inspection_closes_a_missing_package_root_refusal_without_echoing_its_path()
    {
        if (OperatingSystem.IsWindows())
            return;
        using var fixture = new CandidateInspectionFixture();
        await PrepareAcceptedEditAsync(fixture, workspace: false);
        var missingPackages = fixture.InputPath($"{CandidateInspectionFixture.PrivateCanary}-missing-packages");
        Assert.False(Directory.Exists(missingPackages));

        var refusal = DotnetElsa.Run(fixture.SentinelEnvironment, fixture.InspectionArguments(
            "json", packageRoots: [missingPackages]));

        Assert.Equal(ToolExitCode.ResolutionFailure, refusal.ExitCode);
        Assert.Empty(refusal.Output);
        Assert.Contains("error [candidate-host-unavailable]:", refusal.Error, StringComparison.Ordinal);
        Assert.Contains("selected installed host closure could not be inspected", refusal.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(CandidateInspectionFixture.PrivateCanary, refusal.Text, StringComparison.Ordinal);
        fixture.AssertSourcesUnchanged();
        fixture.AssertInputsUnchanged();
    }

    [Fact]
    public async Task Inspection_forwards_review_and_repeated_workspace_profiles_without_changing_inputs()
    {
        if (OperatingSystem.IsWindows())
            return;
        using var fixture = new CandidateInspectionFixture();
        var selectedProfile = await PrepareAcceptedEditAsync(fixture, workspace: true, includeReviewedSetting: true)
            ?? throw new InvalidOperationException("Workspace preparation did not return its selected profile.");
        var unusedProfile = fixture.WriteWorkspaceProfile("unused-profile.json", "unused-candidate", "1",
            [CandidateInspectionFixture.StructuredLogsFeatureId]).Path;

        var missingReview = DotnetElsa.Run(fixture.SentinelEnvironment, fixture.InspectionArguments(
            "json", [selectedProfile, unusedProfile]));
        Assert.Equal(ToolExitCode.Refusal, missingReview.ExitCode);
        Assert.Empty(missingReview.Output);
        Assert.Contains("bridge-portable-unsafe", missingReview.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(CandidateInspectionFixture.PrivateCanary, missingReview.Text, StringComparison.Ordinal);

        var inspection = DotnetElsa.Run(fixture.SentinelEnvironment, fixture.InspectionArguments(
            "json", [selectedProfile, unusedProfile], settingReviewPath: fixture.SettingReviewPath));

        Assert.Equal(ToolExitCode.Success, inspection.ExitCode);
        using var document = JsonDocument.Parse(inspection.Output);
        var root = document.RootElement;
        Assert.Equal(ExpectedSelection, Strings(root.GetProperty("plan").GetProperty("accepted").GetProperty("featureIds")));
        Assert.Equal("unchecked", root.GetProperty("plan").GetProperty("persistence").GetProperty("status").GetString());
        var selection = root.GetProperty("configurationResolution").GetProperty("selection");
        foreach (var name in new[] { "acceptedFeatureIds", "requestedFeatureIds", "effectiveFeatureIds" })
            Assert.Equal(ExpectedSelection, Strings(selection.GetProperty(name)));
        Assert.DoesNotContain(CandidateInspectionFixture.PrivateCanary, inspection.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.DatabasePath, inspection.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("invocationId", inspection.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("captureId", inspection.Text, StringComparison.Ordinal);
        fixture.AssertSourcesUnchanged();
        fixture.AssertInputsUnchanged();
        Assert.False(Directory.Exists(Path.Join(fixture.SourceDirectory, "candidate")));
    }

    [Theory]
    [InlineData("trust", "candidate-trust-required")]
    [InlineData("format", "composition-format-invalid")]
    [InlineData("timeout-low", "candidate-request-invalid")]
    [InlineData("timeout-high", "candidate-request-invalid")]
    public void Invalid_inspection_options_refuse_before_reading_unavailable_private_inputs(string mutation, string code)
    {
        var arguments = new List<string>
        {
            "composition", "inspect", "--host", "/private-input-canary/missing-host",
            "--host-dir", "/private-input-canary/missing-source", "--shell", "default",
            "--environment", "Production", "--composition", "/private-input-canary/missing-composition"
        };
        if (mutation != "trust") arguments.Add("--trust-host-code");
        if (mutation == "format") arguments.AddRange(["--format", "xml"]);
        if (mutation == "timeout-low") arguments.AddRange(["--timeout-seconds", "0"]);
        if (mutation == "timeout-high") arguments.AddRange(["--timeout-seconds", "301"]);
        var refusal = DotnetElsa.Run([.. arguments]);
        Assert.Equal(ToolExitCode.Refusal, refusal.ExitCode);
        Assert.Empty(refusal.Output);
        Assert.Contains(code, refusal.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("private-input-canary", refusal.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Explicit_environment_input_uses_the_actual_workbench_closure_without_ambient_values_or_side_effects()
    {
        if (OperatingSystem.IsWindows())
            return;
        using var fixture = new CandidateInspectionFixture();
        await PrepareWorkbenchAcceptedAsync(fixture);
        var environmentPath = fixture.WriteEnvironmentInput(
            ("UnrelatedBlank", ""),
            ("ConnectionStrings__DeclaredConnection", CandidateInspectionFixture.PrivateEnvironmentCanary));
        var environmentBytes = File.ReadAllBytes(environmentPath);
        var environmentBase64 = Convert.ToBase64String(environmentBytes);
        var environmentDigest = Convert.ToHexString(SHA256.HashData(environmentBytes));
        var ambientConnection = fixture.InputPath("ambient-connection-canary.db");
        var ambient = new Dictionary<string, string>
        {
            ["ConnectionStrings__DeclaredConnection"] = $"Data Source={ambientConnection}",
            ["CShells__Shells__default__Features__RuntimeFaultStackTrace"] = "false"
        };
        var arguments = fixture.InspectionArguments("json", hostDirectory: DotnetElsa.Workbench(),
            environmentInputPath: environmentPath);

        var explicitRun = DotnetElsa.Run(ambient, arguments);
        AssertExpectedExit(explicitRun, ToolExitCode.Success, "explicit Workbench environment inspection");
        AssertWorkbenchResolution(explicitRun.Output, externalInputs: "supplied-intended");
        Assert.DoesNotContain(CandidateInspectionFixture.PrivateEnvironmentCanary, explicitRun.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(environmentPath, explicitRun.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.DatabasePath, explicitRun.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(ambientConnection, explicitRun.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(environmentBase64, explicitRun.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(environmentDigest, explicitRun.Text, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(fixture.DatabasePath));
        Assert.False(File.Exists(ambientConnection));
        Assert.Equal(environmentBytes, File.ReadAllBytes(environmentPath));

        var noOptionRun = DotnetElsa.Run(ambient, fixture.InspectionArguments(
            "json", hostDirectory: DotnetElsa.Workbench()));
        Assert.Equal(ToolExitCode.Success, noOptionRun.ExitCode);
        AssertWorkbenchResolution(noOptionRun.Output, externalInputs: "unverified",
            source: "captured-workbench-json-v1");
        Assert.DoesNotContain(CandidateInspectionFixture.PrivateEnvironmentCanary, noOptionRun.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(ambientConnection, noOptionRun.Text, StringComparison.Ordinal);
        fixture.AssertSourcesUnchanged();
        fixture.AssertInputsUnchanged();
    }

    [Fact]
    public async Task Explicit_environment_input_refuses_an_unenrolled_existing_fixture_host_without_private_echo()
    {
        if (OperatingSystem.IsWindows())
            return;
        using var fixture = new CandidateInspectionFixture();
        await PrepareAcceptedEditAsync(fixture, workspace: false);
        var environmentPath = fixture.WriteEnvironmentInput(("UnrelatedBlank", ""));

        var refusal = DotnetElsa.Run(fixture.SentinelEnvironment, fixture.InspectionArguments(
            "json", hostDirectory: fixture.HostAssemblyDirectory, environmentInputPath: environmentPath));

        Assert.Equal(ToolExitCode.ResolutionFailure, refusal.ExitCode);
        Assert.Empty(refusal.Output);
        Assert.Contains("candidate-environment-host-unenrolled", refusal.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(CandidateInspectionFixture.PrivateEnvironmentCanary, refusal.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(environmentPath, refusal.Text, StringComparison.Ordinal);
        fixture.AssertSourcesUnchanged();
        fixture.AssertInputsUnchanged();
    }

    [Fact]
    public async Task Explicit_environment_input_refuses_the_actual_unenrolled_foundation_host_without_private_echo()
    {
        if (OperatingSystem.IsWindows())
            return;
        using var fixture = new CandidateInspectionFixture();
        await PrepareWorkbenchAcceptedAsync(fixture);
        var environmentPath = fixture.WriteEnvironmentInput(("UnrelatedBlank", ""));

        var refusal = DotnetElsa.Run(fixture.SentinelEnvironment, fixture.InspectionArguments(
            "json", hostDirectory: DotnetElsa.FoundationHost(), environmentInputPath: environmentPath));

        Assert.Equal(ToolExitCode.ResolutionFailure, refusal.ExitCode);
        Assert.Empty(refusal.Output);
        Assert.Contains("candidate-environment-host-unenrolled", refusal.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(CandidateInspectionFixture.PrivateEnvironmentCanary, refusal.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(environmentPath, refusal.Text, StringComparison.Ordinal);
        fixture.AssertSourcesUnchanged();
        fixture.AssertInputsUnchanged();
    }

    [Theory]
    [InlineData("MYSQLCONNSTR_Declared")]
    [InlineData("SQLAZURECONNSTR_Declared")]
    [InlineData("SQLCONNSTR_Declared")]
    [InlineData("CUSTOMCONNSTR_Declared")]
    [InlineData("POSTGRESQLCONNSTR_Declared")]
    [InlineData("APIHUBCONNSTR_Declared")]
    [InlineData("DOCDBCONNSTR_Declared")]
    [InlineData("EVENTHUBCONNSTR_Declared")]
    [InlineData("NOTIFICATIONHUBCONNSTR_Declared")]
    [InlineData("REDISCACHECONNSTR_Declared")]
    [InlineData("SERVICEBUSCONNSTR_Declared")]
    public async Task Explicit_environment_input_refuses_each_standard_service_prefix_through_the_public_command(string key)
    {
        if (OperatingSystem.IsWindows())
            return;
        using var fixture = new CandidateInspectionFixture();
        await PrepareWorkbenchAcceptedAsync(fixture);
        var environmentPath = fixture.WriteEnvironmentInput((key, CandidateInspectionFixture.PrivateEnvironmentCanary));

        var refusal = DotnetElsa.Run(fixture.SentinelEnvironment, fixture.InspectionArguments(
            "json", hostDirectory: DotnetElsa.Workbench(), environmentInputPath: environmentPath));

        Assert.Equal(ToolExitCode.Refusal, refusal.ExitCode);
        Assert.Empty(refusal.Output);
        Assert.Contains("candidate-environment-prefix-unsupported", refusal.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(CandidateInspectionFixture.PrivateEnvironmentCanary, refusal.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(environmentPath, refusal.Text, StringComparison.Ordinal);
        fixture.AssertSourcesUnchanged();
        fixture.AssertInputsUnchanged();
    }

    [Theory]
    [InlineData("malformed", "candidate-environment-input-invalid")]
    [InlineData("collision", "candidate-environment-key-collision")]
    public async Task Explicit_environment_input_refuses_malformed_or_colliding_documents_without_private_echo(string kind, string code)
    {
        if (OperatingSystem.IsWindows())
            return;
        using var fixture = new CandidateInspectionFixture();
        await PrepareWorkbenchAcceptedAsync(fixture);
        var environmentPath = fixture.InputPath("environment-input.json");
        var bytes = kind == "malformed"
            ? "{"u8.ToArray()
            : CandidateInspectionFixture.EnvironmentDocument(("A__B", "one"), ("A:B", "two"));
        File.WriteAllBytes(environmentPath, bytes);
        var environmentBytes = File.ReadAllBytes(environmentPath);

        var refusal = DotnetElsa.Run(fixture.SentinelEnvironment, fixture.InspectionArguments(
            "json", hostDirectory: DotnetElsa.Workbench(), environmentInputPath: environmentPath));

        Assert.Equal(ToolExitCode.Refusal, refusal.ExitCode);
        Assert.Empty(refusal.Output);
        Assert.Contains(code, refusal.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(CandidateInspectionFixture.PrivateEnvironmentCanary, refusal.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(environmentPath, refusal.Text, StringComparison.Ordinal);
        Assert.Equal(environmentBytes, File.ReadAllBytes(environmentPath));
        fixture.AssertSourcesUnchanged();
        fixture.AssertInputsUnchanged();
    }

    [Fact]
    public async Task Fresh_public_invocations_repeat_the_same_safe_supported_projection_and_invalid_classification()
    {
        if (OperatingSystem.IsWindows())
            return;
        using var fixture = new CandidateInspectionFixture();
        await PrepareWorkbenchAcceptedAsync(fixture);
        var environmentPath = fixture.WriteEnvironmentInput(
            ("UnrelatedBlank", ""),
            ("ConnectionStrings__DeclaredConnection", CandidateInspectionFixture.PrivateEnvironmentCanary));
        var arguments = fixture.InspectionArguments("json", hostDirectory: DotnetElsa.Workbench(),
            environmentInputPath: environmentPath);

        var first = DotnetElsa.Run(fixture.SentinelEnvironment, arguments);
        var second = DotnetElsa.Run(fixture.SentinelEnvironment, arguments);
        AssertExpectedExit(first, ToolExitCode.Success, "first Workbench environment inspection");
        AssertExpectedExit(second, ToolExitCode.Success, "second Workbench environment inspection");
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(first.Output), JsonNode.Parse(second.Output)));
        Assert.DoesNotContain(CandidateInspectionFixture.PrivateEnvironmentCanary, first.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(CandidateInspectionFixture.PrivateEnvironmentCanary, second.Text, StringComparison.Ordinal);

        var invalidPath = fixture.InputPath("repeatable-invalid-environment.json");
        var invalidBytes = CandidateInspectionFixture.EnvironmentDocument(("A__B", "one"), ("A:B", "two"));
        File.WriteAllBytes(invalidPath, invalidBytes);
        var invalidArguments = fixture.InspectionArguments("json", hostDirectory: DotnetElsa.Workbench(),
            environmentInputPath: invalidPath);
        var invalidFirst = DotnetElsa.Run(fixture.SentinelEnvironment, invalidArguments);
        var invalidSecond = DotnetElsa.Run(fixture.SentinelEnvironment, invalidArguments);
        Assert.Equal(ToolExitCode.Refusal, invalidFirst.ExitCode);
        Assert.Equal(invalidFirst.ExitCode, invalidSecond.ExitCode);
        Assert.Equal(invalidFirst.Error, invalidSecond.Error);
        Assert.Contains("candidate-environment-key-collision", invalidFirst.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(CandidateInspectionFixture.PrivateEnvironmentCanary, invalidFirst.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(invalidPath, invalidFirst.Text, StringComparison.Ordinal);
        Assert.Equal(invalidBytes, File.ReadAllBytes(invalidPath));
        fixture.AssertSourcesUnchanged();
        fixture.AssertInputsUnchanged();
    }

    [Fact]
    public async Task Selection_divergence_requires_existing_accept_recovery_and_required_edge_removal_still_refuses()
    {
        if (OperatingSystem.IsWindows())
            return;
        using var fixture = new CandidateInspectionFixture();
        await PrepareWorkbenchAcceptedAsync(fixture);
        var environmentPath = fixture.WriteEnvironmentInput(
            ("CShells__Shells__default__Features__RuntimeFaultStackTrace", "false"));
        var originalAcceptedPath = fixture.InputPath("accepted.json");
        var originalAcceptedBytes = File.ReadAllBytes(originalAcceptedPath);
        var authoredBeforeEdit = File.ReadAllBytes(fixture.InputPath("authored.json"));

        var divergence = DotnetElsa.Run(fixture.SentinelEnvironment, fixture.InspectionArguments(
            "json", hostDirectory: DotnetElsa.Workbench(), environmentInputPath: environmentPath));
        AssertExpectedExit(divergence, ToolExitCode.Refusal, "Workbench selection-divergence inspection");
        Assert.Empty(divergence.Output);
        Assert.Contains("candidate-selection-conflict", divergence.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(CandidateInspectionFixture.PrivateEnvironmentCanary, divergence.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(environmentPath, divergence.Text, StringComparison.Ordinal);
        Assert.Equal(originalAcceptedBytes, File.ReadAllBytes(originalAcceptedPath));
        Assert.Equal(authoredBeforeEdit, File.ReadAllBytes(fixture.InputPath("authored.json")));
        Assert.False(Directory.Exists(fixture.CandidateOutputDirectory));
        fixture.AssertSourcesUnchanged();
        fixture.AssertInputsUnchanged();

        var authored = JsonNode.Parse(File.ReadAllText(fixture.InputPath("authored.json")))!.AsObject();
        authored["remove"]!.AsArray().Add(CandidateInspectionFixture.WorkbenchRuntimeFaultStackTraceFeatureId);
        File.WriteAllText(fixture.InputPath("authored.json"), authored.ToJsonString());
        Assert.False(authoredBeforeEdit.AsSpan().SequenceEqual(File.ReadAllBytes(fixture.InputPath("authored.json"))));
        var recoveredAcceptedPath = fixture.InputPath("accepted-recovered.json");
        var accepted = await AcceptWorkbenchAsync(fixture, "accepted-recovered.json");
        Assert.Equal(ToolExitCode.Success, accepted.ExitCode);
        Assert.True(File.Exists(recoveredAcceptedPath));
        Assert.Equal(originalAcceptedBytes, File.ReadAllBytes(originalAcceptedPath));
        Assert.DoesNotContain(CandidateInspectionFixture.WorkbenchRuntimeFaultStackTraceFeatureId,
            AcceptedFeatureIds(fixture, recoveredAcceptedPath));
        fixture.TrackAcceptedInput(recoveredAcceptedPath);

        var recovered = DotnetElsa.Run(fixture.SentinelEnvironment, fixture.InspectionArguments(
            "json", hostDirectory: DotnetElsa.Workbench(), environmentInputPath: environmentPath,
            compositionPath: recoveredAcceptedPath));
        Assert.Equal(ToolExitCode.Success, recovered.ExitCode);
        AssertWorkbenchResolution(recovered.Output, externalInputs: "supplied-intended", expectRuntimeFault: false);
        Assert.DoesNotContain(CandidateInspectionFixture.PrivateEnvironmentCanary, recovered.Text, StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.CandidateOutputDirectory));

        var requiredEdgeEnvironmentPath = fixture.WriteEnvironmentInput("required-edge-environment.json",
            ("CShells__Shells__default__Features__RuntimeFaultStackTrace", "false"),
            ("CShells__Shells__default__Features__DiagnosticsStructuredLogs", "false"));
        var requiredEdgeEnvironmentBytes = File.ReadAllBytes(requiredEdgeEnvironmentPath);
        var authoredForRequiredEdge = JsonNode.Parse(File.ReadAllText(fixture.InputPath("authored.json")))!.AsObject();
        authoredForRequiredEdge["remove"]!.AsArray().Add(CandidateInspectionFixture.StructuredLogsFeatureId);
        File.WriteAllText(fixture.InputPath("authored.json"), authoredForRequiredEdge.ToJsonString());
        var requiredEdgeAcceptedPath = fixture.InputPath("accepted-required-edge.json");
        var recoveredAcceptedBytes = File.ReadAllBytes(recoveredAcceptedPath);
        var edgeAccepted = await AcceptWorkbenchAsync(fixture, "accepted-required-edge.json");
        Assert.Equal(ToolExitCode.Success, edgeAccepted.ExitCode);
        Assert.True(File.Exists(requiredEdgeAcceptedPath));
        Assert.Equal(originalAcceptedBytes, File.ReadAllBytes(originalAcceptedPath));
        Assert.Equal(recoveredAcceptedBytes, File.ReadAllBytes(recoveredAcceptedPath));
        Assert.DoesNotContain(CandidateInspectionFixture.StructuredLogsFeatureId,
            AcceptedFeatureIds(fixture, requiredEdgeAcceptedPath));
        Assert.Contains(CandidateInspectionFixture.StructuredLogsEfFeatureId,
            AcceptedFeatureIds(fixture, requiredEdgeAcceptedPath));
        fixture.TrackAcceptedInput(requiredEdgeAcceptedPath);
        var acceptedBeforeInspection = File.ReadAllBytes(requiredEdgeAcceptedPath);
        var authoredBeforeInspection = File.ReadAllBytes(fixture.InputPath("authored.json"));

        var edgeRefusal = DotnetElsa.Run(fixture.SentinelEnvironment, fixture.InspectionArguments(
            "json", hostDirectory: DotnetElsa.Workbench(), environmentInputPath: requiredEdgeEnvironmentPath,
            compositionPath: requiredEdgeAcceptedPath));
        Assert.Equal(ToolExitCode.Refusal, edgeRefusal.ExitCode);
        Assert.Empty(edgeRefusal.Output);
        Assert.Contains("candidate-selection-conflict", edgeRefusal.Error, StringComparison.Ordinal);
        Assert.Contains("A required feature is explicitly disabled.", edgeRefusal.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(CandidateInspectionFixture.PrivateEnvironmentCanary, edgeRefusal.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(requiredEdgeEnvironmentPath, edgeRefusal.Text, StringComparison.Ordinal);
        Assert.Equal(originalAcceptedBytes, File.ReadAllBytes(originalAcceptedPath));
        Assert.Equal(recoveredAcceptedBytes, File.ReadAllBytes(recoveredAcceptedPath));
        Assert.Equal(acceptedBeforeInspection, File.ReadAllBytes(requiredEdgeAcceptedPath));
        Assert.Equal(authoredBeforeInspection, File.ReadAllBytes(fixture.InputPath("authored.json")));
        Assert.Equal(requiredEdgeEnvironmentBytes, File.ReadAllBytes(requiredEdgeEnvironmentPath));
        Assert.False(Directory.Exists(fixture.CandidateOutputDirectory));
        fixture.AssertSourcesUnchanged();
        fixture.AssertInputsUnchanged();
    }

    internal static async Task<string?> PrepareAcceptedEditAsync(CandidateInspectionFixture fixture, bool workspace,
        bool fileEfEnabled = true, bool includeReviewedSetting = false)
    {
        fixture.UseDiagnosticsSource(fileEfEnabled);
        if (includeReviewedSetting)
            fixture.AddReviewedInspectionSetting();
        fixture.WriteBundledCatalog();
        string? profile = null;
        if (workspace)
            profile = fixture.WriteWorkspaceStart();
        else
        {
            var imported = await PseudoTerminalCli.RunElsaAsync("Type accept to write the authored composition: ", "accept",
                ["composition", "import", "--host-dir", fixture.SourceDirectory, "--shell", fixture.ShellId,
                    "--environment", fixture.Environment, "--catalog", fixture.InputPath("catalog.json"),
                    "--output", fixture.InputPath("authored.json")]);
            Assert.Equal(ToolExitCode.Success, imported.ExitCode);
            Assert.True(imported.ResponseSent);
            Assert.False(imported.TimedOut);
            Assert.DoesNotContain(CandidateInspectionFixture.PrivateCanary, imported.Output + imported.Error, StringComparison.Ordinal);
        }
        fixture.EditDiagnosticSelection();
        if (includeReviewedSetting)
            fixture.EditReviewedInspectionSetting();
        var arguments = new List<string>
        {
            "composition", "accept", "--composition", fixture.InputPath("authored.json"),
            "--output", fixture.InputPath("accepted.json")
        };
        if (profile is not null)
            arguments.AddRange(["--workspace-profile", profile]);
        var accepted = await PseudoTerminalCli.RunElsaAsync("Type accept to write the accepted composition: ", "accept", [.. arguments]);
        Assert.Equal(ToolExitCode.Success, accepted.ExitCode);
        Assert.True(accepted.ResponseSent);
        Assert.False(accepted.TimedOut);
        Assert.DoesNotContain(CandidateInspectionFixture.PrivateCanary, accepted.Output + accepted.Error, StringComparison.Ordinal);
        fixture.TrackAcceptedInput();
        return profile;
    }

    private static async Task PrepareWorkbenchAcceptedAsync(CandidateInspectionFixture fixture)
    {
        fixture.UseWorkbenchFiles();
        fixture.WriteBundledCatalog();
        var imported = await PseudoTerminalCli.RunElsaAsync("Type accept to write the authored composition: ", "accept",
            ["composition", "import", "--host-dir", fixture.SourceDirectory, "--shell", fixture.ShellId,
                "--environment", fixture.Environment, "--catalog", fixture.InputPath("catalog.json"),
                "--output", fixture.InputPath("authored.json")]);
        Assert.Equal(ToolExitCode.Success, imported.ExitCode);
        Assert.True(imported.ResponseSent);
        Assert.False(imported.TimedOut);
        var accepted = await AcceptWorkbenchAsync(fixture);
        Assert.Equal(ToolExitCode.Success, accepted.ExitCode);
        Assert.True(accepted.ResponseSent);
        Assert.False(accepted.TimedOut);
        fixture.TrackAcceptedInput();

        using var acceptedDocument = JsonDocument.Parse(File.ReadAllText(fixture.InputPath("accepted.json")));
        var acceptedIds = acceptedDocument.RootElement.GetProperty("accepted").GetProperty("featureIds")
            .EnumerateArray().Select(value => value.GetString()!).ToArray();
        Assert.Equal(
            new[]
            {
                CandidateInspectionFixture.WorkbenchModularityApiFeatureId,
                CandidateInspectionFixture.WorkbenchRuntimeFaultStackTraceFeatureId,
                CandidateInspectionFixture.StructuredLogsFeatureId,
                CandidateInspectionFixture.StructuredLogsEfFeatureId
            }.Order(StringComparer.Ordinal),
            acceptedIds.Order(StringComparer.Ordinal));
    }

    private static Task<PseudoTerminalCliRun> AcceptWorkbenchAsync(CandidateInspectionFixture fixture,
        string outputName = "accepted.json") =>
        PseudoTerminalCli.RunElsaAsync("Type accept to write the accepted composition: ", "accept",
            ["composition", "accept", "--composition", fixture.InputPath("authored.json"),
                "--output", fixture.InputPath(outputName)]);

    private static void AssertWorkbenchResolution(string output, string externalInputs,
        string source = "captured-workbench-json-explicit-environment-v1", bool expectRuntimeFault = true)
    {
        using var document = JsonDocument.Parse(output);
        var resolution = document.RootElement.GetProperty("configurationResolution");
        Assert.Equal(source, resolution.GetProperty("source").GetString());
        Assert.Equal(externalInputs, resolution.GetProperty("externalInputs").GetString());
        Assert.Equal("not-performed", resolution.GetProperty("targetVerification").GetString());
        Assert.Equal("unobserved", resolution.GetProperty("runtimeParity").GetString());
        Assert.Equal("unverified", resolution.GetProperty("connectivity").GetString());
        Assert.Equal("unverified", resolution.GetProperty("schemaReadiness").GetString());
        Assert.Equal("unverified", resolution.GetProperty("migrationReadiness").GetString());
        Assert.Equal("unobserved", resolution.GetProperty("activation").GetString());
        var selection = resolution.GetProperty("selection");
        var effective = Strings(selection.GetProperty("effectiveFeatureIds"));
        Assert.Contains(CandidateInspectionFixture.WorkbenchModularityApiFeatureId, effective);
        if (expectRuntimeFault)
            Assert.Contains(CandidateInspectionFixture.WorkbenchRuntimeFaultStackTraceFeatureId, effective);
        else
            Assert.DoesNotContain(CandidateInspectionFixture.WorkbenchRuntimeFaultStackTraceFeatureId, effective);
        Assert.Contains(CandidateInspectionFixture.StructuredLogsFeatureId, effective);
        Assert.Contains(CandidateInspectionFixture.StructuredLogsEfFeatureId, effective);
        var participants = resolution.GetProperty("participants").EnumerateArray().ToArray();
        Assert.NotEmpty(participants);
        Assert.Contains(participants, participant =>
            participant.GetProperty("resource").GetString() == CandidateInspectionFixture.PublicEnvironmentResource &&
            participant.GetProperty("connectionReference").GetString() == CandidateInspectionFixture.PublicEnvironmentConnection);
    }

    private static string[] AcceptedFeatureIds(CandidateInspectionFixture fixture, string? path = null)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path ?? fixture.InputPath("accepted.json")));
        return Strings(document.RootElement.GetProperty("accepted").GetProperty("featureIds"));
    }

    private static void AssertExpectedExit(CliRun run, int expected, string operation)
    {
        Assert.True(run.ExitCode == expected,
            $"{operation} expected exit {expected}, got {run.ExitCode}; fixed diagnostic code: {FixedDiagnosticCode(run)}.");
    }

    private static string FixedDiagnosticCode(CliRun run)
    {
        const string prefix = "error [";
        var start = run.Error.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
            return "none";
        start += prefix.Length;
        var end = run.Error.IndexOf(']', start);
        if (end <= start)
            return "malformed";
        var code = run.Error[start..end];
        return code.Length <= 96 && code.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
            ? code
            : "invalid";
    }

    private static string[] PlanArguments(CandidateInspectionFixture fixture, string? profile)
    {
        var arguments = new List<string> { "composition", "plan", "--composition", fixture.InputPath("accepted.json"), "--format", "json" };
        if (profile is not null)
            arguments.AddRange(["--workspace-profile", profile]);
        return [.. arguments];
    }

    private static string[] Strings(JsonElement array) => array.EnumerateArray().Select(value => value.GetString()!).ToArray();
}
