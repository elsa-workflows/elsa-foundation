using System.Text.Json;
using Elsa.Cli.Worker;
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
    public async Task Accepted_import_and_workspace_edits_reach_real_host_preview_without_generation(bool workspace, bool fileEfEnabled)
    {
        if (OperatingSystem.IsWindows())
            return; // The existing PTY helper exercises these journeys on supported Unix hosts.
        using var fixture = new CandidateInspectionFixture();
        var profile = await PrepareAcceptedEditAsync(fixture, workspace, fileEfEnabled);

        var inspection = DotnetElsa.Run(fixture.SentinelEnvironment, fixture.InspectionArguments("json", profile));

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

        var text = DotnetElsa.Run(fixture.SentinelEnvironment, fixture.InspectionArguments("text", profile));
        Assert.Equal(ToolExitCode.Success, text.ExitCode);
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
        fixture.AssertSourcesUnchanged();
        fixture.AssertInputsUnchanged();
        Assert.False(Directory.Exists(Path.Join(fixture.SourceDirectory, "candidate")));
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

        var refusal = DotnetElsa.Run(fixture.SentinelEnvironment, fixture.InspectionArguments("json", profile, trust: false));

        Assert.Equal(ToolExitCode.Refusal, refusal.ExitCode);
        Assert.Equal(string.Empty, refusal.Output);
        Assert.Contains("candidate-trust-required", refusal.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(CandidateInspectionFixture.PrivateCanary, refusal.Text, StringComparison.Ordinal);
        fixture.AssertSourcesUnchanged();
        fixture.AssertInputsUnchanged();
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

    private static async Task<string?> PrepareAcceptedEditAsync(CandidateInspectionFixture fixture, bool workspace, bool fileEfEnabled = true)
    {
        fixture.UseDiagnosticsSource(fileEfEnabled);
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

    private static string[] PlanArguments(CandidateInspectionFixture fixture, string? profile)
    {
        var arguments = new List<string> { "composition", "plan", "--composition", fixture.InputPath("accepted.json"), "--format", "json" };
        if (profile is not null)
            arguments.AddRange(["--workspace-profile", profile]);
        return [.. arguments];
    }

    private static string[] Strings(JsonElement array) => array.EnumerateArray().Select(value => value.GetString()!).ToArray();
}
