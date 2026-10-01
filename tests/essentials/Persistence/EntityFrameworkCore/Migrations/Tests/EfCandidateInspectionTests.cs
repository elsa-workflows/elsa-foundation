using System.Reflection;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CShells;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Modularity.EntityFramework;
using Elsa.Modularity.Planning.Bridge;
using Elsa.Modularity.Planning.Catalog;
using Elsa.Modularity.Planning.Models;
using Elsa.Persistence.EntityFramework.ResourceResolution;
using Elsa.Persistence.EntityFramework.Tooling;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// Exercises the candidate host capability using the real test-host assembly identity, declared defaults,
/// and loaded feature closure. Inputs are streams only; no feature is activated and no database is opened.
/// </summary>
public sealed class EfCandidateInspectionTests : IDisposable
{
    private const string Shell = "candidate-probe";
    private const string Environment = "Production";
    private const string Runtime = "WorkflowsRuntimeEntityFrameworkCore";
    private const string RuntimeWorkflowExecution = "WorkflowsRuntimeWorkflowExecutionEntityFrameworkCorePersistence";
    private const string StructuredLogs = "DiagnosticsStructuredLogsEntityFrameworkCore";
    private const string OpenTelemetry = "DiagnosticsOpenTelemetryEntityFrameworkCore";
    private const string ConnectionCanary = "candidate-connection-private-2177";
    private const string UnknownSettingCanary = "candidate-unknown-private-2177";
    private const string MalformedFileCanary = "candidate-malformed-private-2177";
    private const string DifferentConnectionCanary = "candidate-different-connection-private-2177";
    private const int MaximumCandidateFileBytes = 1024 * 1024;
    private const string InvocationId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string CaptureId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private readonly string temporaryDirectory = Directory.CreateTempSubdirectory("elsa-candidate-inspection-").FullName;
    private string DatabasePath => Path.Combine(temporaryDirectory, "candidate-probe.db");

    public void Dispose() => Directory.Delete(temporaryDirectory, recursive: true);

    [Fact]
    public async Task Real_host_candidate_returns_a_closed_safe_projection_without_opening_a_database()
    {
        var candidate = CreateRuntimeCandidate();
        using var response = new MemoryStream();

        var exitCode = await RunCandidateAsync(candidate.Request, response, CancellationToken.None);
        var responseJson = Encoding.UTF8.GetString(response.ToArray());
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;

        Assert.Equal(0, exitCode);
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal(InvocationId, root.GetProperty("invocationId").GetString());
        Assert.Equal(CaptureId, root.GetProperty("captureId").GetString());
        Assert.Equal("ok", root.GetProperty("status").GetString());
        Assert.Equal(0, root.GetProperty("exitCode").GetInt32());
        Assert.Equal(
            ["captureId", "configurationResolution", "exitCode", "invocationId", "status", "version"],
            root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));

        var resolution = root.GetProperty("configurationResolution");
        Assert.Equal("captured-workbench-json-v1", resolution.GetProperty("source").GetString());
        Assert.Equal(Shell, resolution.GetProperty("shell").GetString());
        Assert.Equal(Environment, resolution.GetProperty("environment").GetString());
        Assert.Equal("resolved", resolution.GetProperty("resolution").GetString());
        Assert.Equal(
            ["activation", "configuredValueAffinity", "connectivity", "environment", "externalInputs", "migrationReadiness",
             "packageReachability", "participants", "resolution", "runtimeParity", "schemaReadiness", "selection", "shell",
             "source", "targetVerification", "unresolved"],
            resolution.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));

        var selection = resolution.GetProperty("selection");
        Assert.Equal(candidate.AcceptedFeatureIds, StringValues(selection.GetProperty("acceptedFeatureIds")));
        Assert.Equal(candidate.AcceptedFeatureIds, StringValues(selection.GetProperty("requestedFeatureIds")));
        Assert.Equal(candidate.AcceptedFeatureIds, StringValues(selection.GetProperty("effectiveFeatureIds")));
        Assert.Empty(StringValues(selection.GetProperty("disabledFeatureIds")));
        Assert.Empty(StringValues(selection.GetProperty("implicitFeatureIds")));

        var participant = Assert.Single(resolution.GetProperty("participants").EnumerateArray(),
            row => row.GetProperty("feature").GetString() == Runtime);
        Assert.Equal("Workflows.Runtime", participant.GetProperty("module").GetString());
        Assert.Equal("primary", participant.GetProperty("resource").GetString());
        Assert.Equal("Sqlite", participant.GetProperty("provider").GetString());
        Assert.Equal("Primary", participant.GetProperty("connectionReference").GetString());
        Assert.Equal("RootDefault", participant.GetProperty("selection").GetString());
        Assert.Equal("root", participant.GetProperty("selectorScope").GetString());
        Assert.Equal("root", participant.GetProperty("resourceScope").GetString());
        Assert.Equal("unavailable", participant.GetProperty("exactFileProvenance").GetString());
        Assert.Equal(
            ["connectionReference", "exactFileProvenance", "feature", "module", "provider", "resource", "resourceScope", "selection", "selectorScope"],
            participant.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));

        Assert.Equal("checked", resolution.GetProperty("configuredValueAffinity").GetString());
        Assert.Equal("not-performed", resolution.GetProperty("targetVerification").GetString());
        Assert.Equal("unobserved", resolution.GetProperty("runtimeParity").GetString());
        Assert.Equal("unverified", resolution.GetProperty("packageReachability").GetString());
        Assert.Equal("unverified", resolution.GetProperty("connectivity").GetString());
        Assert.Equal("unverified", resolution.GetProperty("schemaReadiness").GetString());
        Assert.Equal("unverified", resolution.GetProperty("migrationReadiness").GetString());
        Assert.Equal("unobserved", resolution.GetProperty("activation").GetString());
        Assert.Equal("unverified", resolution.GetProperty("externalInputs").GetString());
        Assert.Equal(["exact-file-provenance-unavailable"], StringValues(resolution.GetProperty("unresolved")));

        var responseContainsPrivateConfiguration = new[]
        {
            ConnectionCanary, UnknownSettingCanary, DatabasePath, "Data Source="
        }.Any(value => responseJson.Contains(value, StringComparison.Ordinal));
        Assert.False(responseContainsPrivateConfiguration);
        Assert.False(File.Exists(DatabasePath));
        Assert.Empty(Directory.EnumerateFiles(temporaryDirectory));
    }

    [Fact]
    public async Task Host_refuses_a_malformed_selected_source_without_echoing_or_opening_a_database()
    {
        var candidate = CreateRuntimeCandidate(malformedEnvironmentOverlay: true);
        using var response = new MemoryStream();

        var exitCode = await RunCandidateAsync(candidate.Request, response, CancellationToken.None);
        var responseJson = Encoding.UTF8.GetString(response.ToArray());
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;

        Assert.Equal(2, exitCode);
        Assert.Equal("refused", root.GetProperty("status").GetString());
        Assert.Equal(2, root.GetProperty("exitCode").GetInt32());
        Assert.Equal("candidate-capture-invalid", root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal(InvocationId, root.GetProperty("invocationId").GetString());
        Assert.Equal(CaptureId, root.GetProperty("captureId").GetString());
        Assert.Equal(
            ["captureId", "error", "exitCode", "invocationId", "status", "version"],
            root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.False(root.TryGetProperty("configurationResolution", out _));
        var error = root.GetProperty("error");
        Assert.True(error.TryGetProperty("code", out var code) && !string.IsNullOrWhiteSpace(code.GetString()));
        Assert.DoesNotContain(error.EnumerateObject(), property => property.Name is "reason" or "feature" or "resource");
        Assert.All(error.EnumerateObject(), property =>
            Assert.Contains(property.Name, new[] { "code", "reason", "feature", "resource" }));

        var responseContainsPrivateConfiguration = new[]
        {
            ConnectionCanary, UnknownSettingCanary, MalformedFileCanary, DatabasePath, "Data Source="
        }.Any(value => responseJson.Contains(value, StringComparison.Ordinal));
        Assert.False(responseContainsPrivateConfiguration);
        Assert.False(File.Exists(DatabasePath));
        Assert.Empty(Directory.EnumerateFiles(temporaryDirectory));
    }

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("shells.json")]
    [InlineData("shells.Production.json")]
    public async Task Host_refuses_when_a_required_source_layer_is_missing(string missingLayer)
    {
        var candidate = CreateRuntimeCandidate();
        var files = (JsonArray)candidate.Request["candidate"]!["files"]!;
        var selectedLayer = files.Single(file => file!["name"]!.GetValue<string>() == missingLayer);
        files.Remove(selectedLayer);
        var discoveryCalls = 0;
        var operation = CreateTrackingOperation(() => discoveryCalls++);
        using var response = new MemoryStream();

        var exitCode = await RunOperationAsync(operation, candidate.Request, response, CancellationToken.None);
        using var document = JsonDocument.Parse(response.ToArray());

        Assert.Equal(2, exitCode);
        Assert.Equal("candidate-request-invalid", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.False(document.RootElement.TryGetProperty("configurationResolution", out _));
        Assert.Equal(0, discoveryCalls);
        Assert.False(File.Exists(DatabasePath));
        Assert.Empty(Directory.EnumerateFiles(temporaryDirectory));
    }

    [Fact]
    public async Task Host_refuses_an_unselected_environment_layer_before_assembly_discovery()
    {
        var candidate = CreateRuntimeCandidate();
        var files = (JsonArray)candidate.Request["candidate"]!["files"]!;
        var layer = files.Single(file => file!["name"]!.GetValue<string>() == "appsettings.Production.json");
        layer!["name"] = "appsettings.Staging.json";
        var discoveryCalls = 0;
        var operation = CreateTrackingOperation(() => discoveryCalls++);
        using var response = new MemoryStream();

        var exitCode = await RunOperationAsync(operation, candidate.Request, response, CancellationToken.None);
        var responseJson = Encoding.UTF8.GetString(response.ToArray());
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;

        Assert.Equal(2, exitCode);
        Assert.Equal("candidate-request-invalid", root.GetProperty("error").GetProperty("code").GetString());
        Assert.False(root.TryGetProperty("configurationResolution", out _));
        Assert.Equal(0, discoveryCalls);
        AssertNoPrivateCandidateValues(responseJson);
        Assert.False(File.Exists(DatabasePath));
        Assert.Empty(Directory.EnumerateFiles(temporaryDirectory));
    }

    [Fact]
    public async Task Host_accepts_the_three_required_layers_without_the_optional_appsettings_overlay()
    {
        var candidate = CreateRuntimeCandidate();
        var files = (JsonArray)candidate.Request["candidate"]!["files"]!;
        var optionalLayer = files.Single(file => file!["name"]!.GetValue<string>() == "appsettings.Production.json");
        files.Remove(optionalLayer);
        var discoveryCalls = 0;
        var operation = CreateTrackingOperation(() => discoveryCalls++);
        using var response = new MemoryStream();

        var exitCode = await RunOperationAsync(operation, candidate.Request, response, CancellationToken.None);
        var responseJson = Encoding.UTF8.GetString(response.ToArray());
        using var document = JsonDocument.Parse(responseJson);

        Assert.Equal(0, exitCode);
        Assert.Equal("ok", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, discoveryCalls);
        AssertNoPrivateCandidateValues(responseJson);
        Assert.False(File.Exists(DatabasePath));
        Assert.Empty(Directory.EnumerateFiles(temporaryDirectory));
    }

    [Fact]
    public async Task Host_accepts_four_one_mebibyte_sources_at_the_exact_per_file_and_aggregate_limits()
    {
        var candidate = CreateRuntimeCandidate();
        var files = (JsonArray)candidate.Request["candidate"]!["files"]!;
        foreach (var fileNode in files)
        {
            var file = Assert.IsType<JsonObject>(fileNode);
            var bytes = Convert.FromBase64String(file["content"]!.GetValue<string>());
            file["content"] = Convert.ToBase64String(PadWithSpaces(bytes, MaximumCandidateFileBytes));
        }

        var requestJson = candidate.Request.ToJsonString();
        Assert.True(Encoding.UTF8.GetByteCount(requestJson) < 8 * 1024 * 1024);
        var discoveryCalls = 0;
        var operation = CreateTrackingOperation(() => discoveryCalls++);
        using var response = new MemoryStream();

        var exitCode = await RunOperationAsync(operation, candidate.Request, response, CancellationToken.None);
        var responseJson = Encoding.UTF8.GetString(response.ToArray());
        using var document = JsonDocument.Parse(responseJson);

        Assert.Equal(0, exitCode);
        Assert.Equal("ok", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, discoveryCalls);
        AssertNoPrivateCandidateValues(responseJson);
        Assert.False(File.Exists(DatabasePath));
        Assert.Empty(Directory.EnumerateFiles(temporaryDirectory));
    }

    [Fact]
    public async Task Host_refuses_one_byte_over_the_decoded_file_limit_before_assembly_discovery()
    {
        var candidate = CreateRuntimeCandidate();
        ReplaceCandidateFile(candidate.Request, "appsettings.Production.json",
            PadWithSpaces(Encoding.UTF8.GetBytes("{}"), MaximumCandidateFileBytes + 1));
        var discoveryCalls = 0;
        var operation = CreateTrackingOperation(() => discoveryCalls++);
        using var response = new MemoryStream();

        var exitCode = await RunOperationAsync(operation, candidate.Request, response, CancellationToken.None);
        var responseJson = Encoding.UTF8.GetString(response.ToArray());
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;

        Assert.Equal(2, exitCode);
        Assert.Equal("candidate-request-invalid", root.GetProperty("error").GetProperty("code").GetString());
        Assert.False(root.TryGetProperty("configurationResolution", out _));
        Assert.Equal(0, discoveryCalls);
        AssertNoPrivateCandidateValues(responseJson);
        Assert.False(File.Exists(DatabasePath));
        Assert.Empty(Directory.EnumerateFiles(temporaryDirectory));
    }

    [Theory]
    [InlineData(64, EfToolingExitCode.Success)]
    [InlineData(65, EfToolingExitCode.Refusal)]
    public async Task Host_enforces_decoded_source_json_depth_at_sixty_four_nested_objects(int depth, int expectedExitCode)
    {
        var candidate = CreateRuntimeCandidate();
        ReplaceCandidateFile(candidate.Request, "appsettings.Production.json",
            NestedObjectJson(depth, depth == 65 ? JsonSerializer.Serialize(MalformedFileCanary) : "0"));
        var discoveryCalls = 0;
        var operation = CreateTrackingOperation(() => discoveryCalls++);
        using var response = new MemoryStream();

        var exitCode = await RunOperationAsync(operation, candidate.Request, response, CancellationToken.None);
        var responseJson = Encoding.UTF8.GetString(response.ToArray());
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;

        Assert.Equal(expectedExitCode, exitCode);
        if (depth == 64)
        {
            Assert.Equal("ok", root.GetProperty("status").GetString());
            Assert.True(root.TryGetProperty("configurationResolution", out _));
            Assert.Equal(1, discoveryCalls);
        }
        else
        {
            Assert.Equal("candidate-capture-invalid", root.GetProperty("error").GetProperty("code").GetString());
            Assert.False(root.TryGetProperty("configurationResolution", out _));
            Assert.Equal(0, discoveryCalls);
        }

        AssertNoPrivateCandidateValues(responseJson);
        Assert.False(File.Exists(DatabasePath));
        Assert.Empty(Directory.EnumerateFiles(temporaryDirectory));
    }

    [Theory]
    [InlineData("array")]
    [InlineData("case-duplicate")]
    public async Task Host_rejects_unsupported_decoded_source_shapes_before_assembly_discovery(string shape)
    {
        var sourceJson = shape == "array"
            ? "[\"candidate-source-private-2177\"]"
            : "{\"Private\":\"candidate-source-private-2177\",\"private\":\"second-value\"}";
        var candidate = CreateRuntimeCandidate();
        ReplaceCandidateFile(candidate.Request, "appsettings.Production.json", Encoding.UTF8.GetBytes(sourceJson));
        var discoveryCalls = 0;
        var operation = CreateTrackingOperation(() => discoveryCalls++);
        using var response = new MemoryStream();

        var exitCode = await RunOperationAsync(operation, candidate.Request, response, CancellationToken.None);
        var responseJson = Encoding.UTF8.GetString(response.ToArray());
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;

        Assert.Equal(2, exitCode);
        Assert.Equal("candidate-capture-invalid", root.GetProperty("error").GetProperty("code").GetString());
        Assert.False(root.TryGetProperty("configurationResolution", out _));
        Assert.Equal(0, discoveryCalls);
        AssertNoPrivateCandidateValues(responseJson, "candidate-source-private-2177", "second-value");
        Assert.False(File.Exists(DatabasePath));
        Assert.Empty(Directory.EnumerateFiles(temporaryDirectory));
    }

    [Fact]
    public async Task Host_rejects_noncanonical_base64_before_assembly_discovery()
    {
        Assert.Equal(Convert.FromBase64String("e30="), Convert.FromBase64String("e31="));
        var candidate = CreateRuntimeCandidate();
        var file = ((JsonArray)candidate.Request["candidate"]!["files"]!).Single(node =>
            node!["name"]!.GetValue<string>() == "appsettings.json");
        file!["content"] = "e31=";
        var discoveryCalls = 0;
        var operation = CreateTrackingOperation(() => discoveryCalls++);
        using var response = new MemoryStream();

        var exitCode = await RunOperationAsync(operation, candidate.Request, response, CancellationToken.None);
        var responseJson = Encoding.UTF8.GetString(response.ToArray());
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;

        Assert.Equal(2, exitCode);
        Assert.Equal("candidate-capture-invalid", root.GetProperty("error").GetProperty("code").GetString());
        Assert.False(root.TryGetProperty("configurationResolution", out _));
        Assert.Equal(0, discoveryCalls);
        AssertNoPrivateCandidateValues(responseJson);
        Assert.False(File.Exists(DatabasePath));
        Assert.Empty(Directory.EnumerateFiles(temporaryDirectory));
    }

    [Fact]
    public async Task Host_does_not_correlate_requests_that_reuse_the_same_token()
    {
        var candidate = CreateRuntimeCandidate();
        var payload = candidate.Request["candidate"]!.AsObject();
        payload["captureId"] = InvocationId;
        foreach (var file in payload["files"]!.AsArray())
            file!["captureId"] = InvocationId;
        using var response = new MemoryStream();

        var exitCode = await RunCandidateAsync(candidate.Request, response, CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Empty(response.ToArray());
    }

    [Fact]
    public async Task Host_reads_at_most_one_byte_beyond_the_exact_request_limit()
    {
        const int maximumRequestBytes = 8 * 1024 * 1024;
        var candidate = CreateRuntimeCandidate();
        var json = Encoding.UTF8.GetBytes(candidate.Request.ToJsonString());
        var exact = PadWithSpaces(json, maximumRequestBytes);
        using var exactInput = new CountingStream(exact);
        using var exactResponse = new MemoryStream();

        Assert.Equal(0, await RunCandidateStreamAsync(exactInput, exactResponse, CancellationToken.None));
        Assert.Equal(maximumRequestBytes, exactInput.BytesRead);

        var oversized = PadWithSpaces(json, maximumRequestBytes + 4096);
        using var oversizedInput = new CountingStream(oversized);
        using var oversizedResponse = new MemoryStream();

        Assert.Equal(2, await RunCandidateStreamAsync(oversizedInput, oversizedResponse, CancellationToken.None));
        Assert.Equal(maximumRequestBytes + 1, oversizedInput.BytesRead);
        using var oversizedDocument = JsonDocument.Parse(oversizedResponse.ToArray());
        var refused = oversizedDocument.RootElement;
        Assert.Equal(InvocationId, refused.GetProperty("invocationId").GetString());
        Assert.Equal(CaptureId, refused.GetProperty("captureId").GetString());
        Assert.Equal("candidate-request-too-large", refused.GetProperty("error").GetProperty("code").GetString());
        Assert.Single(refused.GetProperty("error").EnumerateObject());
    }

    [Theory]
    [InlineData("candidate")]
    [InlineData("invocationId")]
    [InlineData("captureId")]
    public async Task Ambiguous_correlation_fields_refuse_without_response_or_host_discovery(string field)
    {
        var candidate = CreateRuntimeCandidate();
        var json = candidate.Request.ToJsonString();
        var value = field == "candidate"
            ? candidate.Request["candidate"]!.ToJsonString()
            : JsonSerializer.Serialize(field == "invocationId" ? InvocationId : CaptureId);
        json = json.Replace($"\"{field}\":", $"\"{field}\":{value},\"{field}\":", StringComparison.Ordinal);
        var discoveryCalls = 0;
        var operation = new EfCandidateInspectionOperation(() =>
        {
            discoveryCalls++;
            return EfConfigurationProbeTests.HostAssemblies;
        });
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);
        using var response = new MemoryStream();

        Assert.Equal(2, await operation.RunAsync(input, response, CancellationToken.None));
        Assert.Empty(response.ToArray());
        Assert.Equal(0, discoveryCalls);
        Assert.False(File.Exists(DatabasePath));
    }

    [Fact]
    public async Task Host_refuses_when_the_composed_shell_requests_features_outside_the_accepted_graph()
    {
        var candidate = CreateRuntimeCandidate(acceptedGraphMismatch: true);
        using var response = new MemoryStream();

        var exitCode = await RunCandidateAsync(candidate.Request, response, CancellationToken.None);
        var responseJson = Encoding.UTF8.GetString(response.ToArray());
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;

        Assert.Equal(2, exitCode);
        Assert.Equal("refused", root.GetProperty("status").GetString());
        Assert.Equal("candidate-selection-conflict", root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("requested-extra", root.GetProperty("error").GetProperty("reason").GetString());
        Assert.Equal(Runtime, root.GetProperty("error").GetProperty("feature").GetString());
        Assert.False(root.TryGetProperty("configurationResolution", out _));
        Assert.False(File.Exists(DatabasePath));
    }

    [Fact]
    public async Task Host_candidate_operation_honors_cancellation_before_writing_a_response()
    {
        var candidate = CreateRuntimeCandidate();
        using var response = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => RunCandidateAsync(candidate.Request, response, cancellation.Token));

        Assert.Empty(response.ToArray());
        Assert.False(File.Exists(DatabasePath));
    }

    [Fact]
    public void Public_operation_rejects_a_null_assembly_discovery_dependency()
    {
        Assert.Throws<ArgumentNullException>(() => new EfCandidateInspectionOperation(null!));
    }

    [Fact]
    public async Task Public_operation_checks_cancellation_before_discovery_and_writes_nothing()
    {
        var discoveryCalls = 0;
        var operation = new EfCandidateInspectionOperation(() =>
        {
            discoveryCalls++;
            return EfConfigurationProbeTests.HostAssemblies;
        });
        var candidate = CreateRuntimeCandidate();
        using var response = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => RunOperationAsync(operation, candidate.Request, response, cancellation.Token));

        Assert.Equal(0, discoveryCalls);
        Assert.Empty(response.ToArray());
        Assert.False(File.Exists(DatabasePath));
    }

    [Fact]
    public async Task Public_operation_resolves_the_supplied_host_closure_once_for_a_valid_correlated_request()
    {
        var discoveryCalls = 0;
        var operation = new EfCandidateInspectionOperation(() =>
        {
            discoveryCalls++;
            return EfConfigurationProbeTests.HostAssemblies;
        });
        var candidate = CreateRuntimeCandidate();
        using var response = new MemoryStream();

        var exitCode = await RunOperationAsync(operation, candidate.Request, response, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, discoveryCalls);
        using var document = JsonDocument.Parse(response.ToArray());
        Assert.Equal(InvocationId, document.RootElement.GetProperty("invocationId").GetString());
        Assert.Equal(CaptureId, document.RootElement.GetProperty("captureId").GetString());
        Assert.False(File.Exists(DatabasePath));
    }

    [Theory]
    [InlineData(1022, EfToolingExitCode.Success)]
    [InlineData(1023, EfToolingExitCode.ResolutionFailure)]
    [InlineData(1024, EfToolingExitCode.ResolutionFailure)]
    [InlineData(1025, EfToolingExitCode.ResolutionFailure)]
    public async Task Public_operation_bounds_participant_and_finding_rows_together(int moduleCount, int expectedExitCode)
    {
        var featureId = $"SyntheticCandidateParticipant{moduleCount}";
        var syntheticAssembly = SyntheticEfModules.BuildParticipantFeature(
            $"Elsa.Candidate.Participants.{Guid.NewGuid():N}", featureId, moduleCount);
        var candidate = CreateCandidate([featureId], BuildFiles([featureId], includeResourceConfiguration: false));
        var discoveryCalls = 0;
        var operation = new EfCandidateInspectionOperation(() =>
        {
            discoveryCalls++;
            return [.. EfConfigurationProbeTests.HostAssemblies, syntheticAssembly];
        });
        using var response = new MemoryStream();

        if (expectedExitCode == EfToolingExitCode.ResolutionFailure)
        {
            // Host availability failures are scoped exceptions; the worker maps them to its fixed
            // outer refusal. They are not a new exit-3 host-response shape in the v1 contract.
            var refusal = await Assert.ThrowsAsync<EfToolingRefusal>(() =>
                RunOperationAsync(operation, candidate.Request, response, CancellationToken.None));
            Assert.Equal("candidate-host-unavailable", refusal.Code);
            Assert.Equal(expectedExitCode, refusal.ExitCode);
            Assert.Equal(1, discoveryCalls);
            Assert.Empty(response.ToArray());
            Assert.False(File.Exists(DatabasePath));
            return;
        }

        var exitCode = await RunOperationAsync(operation, candidate.Request, response, CancellationToken.None);

        Assert.Equal(expectedExitCode, exitCode);
        Assert.Equal(1, discoveryCalls);
        using var document = JsonDocument.Parse(response.ToArray());
        var root = document.RootElement;
        Assert.Equal(InvocationId, root.GetProperty("invocationId").GetString());
        Assert.Equal(CaptureId, root.GetProperty("captureId").GetString());
        Assert.Equal(expectedExitCode, root.GetProperty("exitCode").GetInt32());
        Assert.Equal("ok", root.GetProperty("status").GetString());
        var resolution = root.GetProperty("configurationResolution");
        var participants = resolution.GetProperty("participants").EnumerateArray().ToArray();
        var findings = StringValues(resolution.GetProperty("unresolved"));
        Assert.Equal(moduleCount, participants.Length);
        Assert.Equal(["exact-file-provenance-unavailable", "legacy-target-unprojected"], findings);
        Assert.Equal(1024, participants.Length + findings.Length);
        Assert.All(participants, participant =>
        {
            Assert.Equal(featureId, participant.GetProperty("feature").GetString());
            Assert.Equal("Legacy", participant.GetProperty("selection").GetString());
            Assert.Equal(JsonValueKind.Null, participant.GetProperty("resource").ValueKind);
            Assert.Equal(JsonValueKind.Null, participant.GetProperty("provider").ValueKind);
            Assert.Equal(JsonValueKind.Null, participant.GetProperty("connectionReference").ValueKind);
        });
        Assert.False(File.Exists(DatabasePath));
    }

    [Theory]
    [InlineData("root-default-distinct-equal")]
    [InlineData("root-default-same-reference")]
    [InlineData("environment-resource-override")]
    [InlineData("shell-default-explicit-binding")]
    [InlineData("feature-disabled-removed")]
    public async Task Runtime_and_candidate_resolve_the_same_targets_from_one_file_capture(string scenario)
    {
        var run = await RunSameCaptureAsync(scenario);

        Assert.Null(run.RuntimeFailure);
        Assert.Empty(run.RuntimeDetails.RefusalCodes);
        Assert.Equal(EfToolingExitCode.Success, run.CandidateExitCode);
        Assert.Equal("ok", run.CandidateResponse.GetProperty("status").GetString());
        var resolution = run.CandidateResponse.GetProperty("configurationResolution");
        Assert.Equal(Shell, resolution.GetProperty("shell").GetString());
        Assert.Equal(Environment, resolution.GetProperty("environment").GetString());
        Assert.Equal("checked", resolution.GetProperty("configuredValueAffinity").GetString());
        Assert.Equal("not-performed", resolution.GetProperty("targetVerification").GetString());
        Assert.Equal("unobserved", resolution.GetProperty("runtimeParity").GetString());
        Assert.Equal(["exact-file-provenance-unavailable"], StringValues(resolution.GetProperty("unresolved")));
        Assert.Equal(run.AcceptedFeatureIds, StringValues(resolution.GetProperty("selection").GetProperty("acceptedFeatureIds")));
        Assert.Equal(run.AcceptedFeatureIds, StringValues(resolution.GetProperty("selection").GetProperty("requestedFeatureIds")));
        Assert.Equal(run.AcceptedFeatureIds, StringValues(resolution.GetProperty("selection").GetProperty("effectiveFeatureIds")));
        AssertRuntimeAndCandidateTargetsAgree(run, resolution);

        if (scenario is "root-default-distinct-equal" or "root-default-same-reference")
            Assert.True(AreConnectionValuesEqual(run.FileBytes, "Logs", "Telemetry"));

        if (scenario == "shell-default-explicit-binding")
            AssertTarget(run, resolution, Runtime, "shell", "Sqlite", "Shell", "ShellDefault", "shell-composed");
        else
            AssertTarget(run, resolution, Runtime, "primary", "Sqlite", "Primary", "RootDefault", "root");

        if (scenario is "root-default-distinct-equal" or "root-default-same-reference")
        {
            var telemetryResource = scenario == "root-default-same-reference" ? "logs" : "telemetry";
            AssertTarget(run, resolution, StructuredLogs, "logs", "Sqlite", "Logs", "ShellBinding", "shell-composed");
            AssertTarget(run, resolution, OpenTelemetry, telemetryResource, "Sqlite",
                scenario == "root-default-same-reference" ? "Logs" : "Telemetry", "ShellBinding", "shell-composed");
        }
        else if (scenario == "shell-default-explicit-binding")
        {
            AssertTarget(run, resolution, StructuredLogs, "logs", "Sqlite", "Logs", "ShellBinding", "shell-composed");
            AssertTarget(run, resolution, OpenTelemetry, "shell", "Sqlite", "Shell", "ShellDefault", "shell-composed");
        }
        else if (scenario is "feature-disabled-removed" or "environment-resource-override")
        {
            AssertTarget(run, resolution, StructuredLogs, "primary", "Sqlite", "Primary", "RootDefault", "root");
            if (scenario == "environment-resource-override")
                AssertTarget(run, resolution, OpenTelemetry, "primary", "Sqlite", "Primary", "RootDefault", "root");
        }

        if (scenario == "feature-disabled-removed")
        {
            Assert.DoesNotContain(OpenTelemetry, run.RuntimeDetails.ResolvedParticipants
                .Select(participant => participant.Participant.FeatureId));
            Assert.DoesNotContain(resolution.GetProperty("participants").EnumerateArray(), participant =>
                participant.GetProperty("feature").GetString() == OpenTelemetry);
            Assert.Contains(OpenTelemetry, StringValues(resolution.GetProperty("selection").GetProperty("disabledFeatureIds")));
        }

        AssertPrivateInputsRemainPrivate(run);
    }

    [Fact]
    public async Task All_legacy_partial_targets_remain_null_in_runtime_and_candidate_evidence()
    {
        var run = await RunSameCaptureAsync("all-legacy-partial");

        Assert.Null(run.RuntimeFailure);
        Assert.Empty(run.RuntimeDetails.RefusalCodes);
        Assert.False(run.RuntimeDetails.HasApplicableResource);
        Assert.Empty(run.RuntimePatch!.ConfigurationData);
        Assert.Equal(EfToolingExitCode.Success, run.CandidateExitCode);
        var resolution = run.CandidateResponse.GetProperty("configurationResolution");
        Assert.Equal("partial", resolution.GetProperty("resolution").GetString());
        Assert.Equal(["exact-file-provenance-unavailable", "legacy-target-unprojected"],
            StringValues(resolution.GetProperty("unresolved")));
        var expectedParticipants = new[] { Runtime, StructuredLogs, OpenTelemetry }.Order(StringComparer.Ordinal);
        Assert.Equal(expectedParticipants, run.RuntimeDetails.ResolvedParticipants
            .Select(participant => participant.Participant.FeatureId).Order(StringComparer.Ordinal));
        Assert.Equal(expectedParticipants, resolution.GetProperty("participants").EnumerateArray()
            .Select(participant => participant.GetProperty("feature").GetString()!).Order(StringComparer.Ordinal));
        AssertRuntimeAndCandidateTargetsAgree(run, resolution);
        Assert.Equal("not-applicable", resolution.GetProperty("configuredValueAffinity").GetString());
        Assert.All(run.RuntimeDetails.ResolvedParticipants,
            participant => Assert.Equal(PersistenceSelectionKind.Legacy, participant.Selection));
        Assert.All(resolution.GetProperty("participants").EnumerateArray(), participant =>
        {
            Assert.Equal("Legacy", participant.GetProperty("selection").GetString());
            Assert.Equal(JsonValueKind.Null, participant.GetProperty("resource").ValueKind);
            Assert.Equal(JsonValueKind.Null, participant.GetProperty("provider").ValueKind);
            Assert.Equal(JsonValueKind.Null, participant.GetProperty("connectionReference").ValueKind);
            Assert.Equal(JsonValueKind.Null, participant.GetProperty("resourceScope").ValueKind);
        });
        AssertPrivateInputsRemainPrivate(run);
    }

    [Theory]
    [InlineData("missing-root-resource", "resource-not-found")]
    [InlineData("missing-binding-resource", "resource-not-found")]
    [InlineData("null-root-selection", "resource-selection-invalid")]
    [InlineData("blank-root-selection", "resource-selection-invalid")]
    [InlineData("null-binding-selection", "resource-selection-invalid")]
    [InlineData("blank-binding-selection", "resource-selection-invalid")]
    [InlineData("null-provider", "resource-definition-invalid")]
    [InlineData("blank-provider", "resource-definition-invalid")]
    [InlineData("null-connection-reference", "resource-definition-invalid")]
    [InlineData("blank-connection-reference", "resource-definition-invalid")]
    [InlineData("authored-legacy-conflict", "resource-legacy-conflict")]
    [InlineData("mixed-diagnostic-ownership", "resource-ownership-unresolved")]
    [InlineData("unequal-diagnostic-values", "resource-context-conflict")]
    [InlineData("shared-context-provider-split", "resource-context-conflict")]
    [InlineData("shared-context-schema-split", "resource-context-conflict")]
    [InlineData("opaque-composer-configurator", "resource-configurator-unsupported")]
    [InlineData("inline-resource-identity", "resource-selection-invalid")]
    [InlineData("inline-connection-identity", "resource-definition-invalid")]
    public async Task Runtime_and_candidate_refuse_the_same_captured_configuration(string scenario, string refusalCode)
    {
        var run = await RunSameCaptureAsync(scenario);

        Assert.Equal($"EF persistence preparation refused: {refusalCode}", run.RuntimeFailure);
        Assert.Null(run.RuntimePatch);
        Assert.Equal([refusalCode], run.RuntimeDetails.RefusalCodes.Order(StringComparer.Ordinal));
        Assert.Equal(EfToolingExitCode.Refusal, run.CandidateExitCode);
        Assert.Equal("refused", run.CandidateResponse.GetProperty("status").GetString());
        Assert.Equal(refusalCode, run.CandidateResponse.GetProperty("error").GetProperty("code").GetString());
        Assert.False(run.CandidateResponse.TryGetProperty("configurationResolution", out _));
        AssertPrivateInputsRemainPrivate(run);

        if (scenario is "inline-resource-identity" or "inline-connection-identity")
            Assert.Contains(InlineIdentityCanary(),
                string.Join('\n', run.FileBytes.Values.Select(bytes => Encoding.UTF8.GetString(bytes))),
                StringComparison.Ordinal);

        if (scenario == "unequal-diagnostic-values")
        {
            Assert.False(AreConnectionValuesEqual(run.FileBytes, "Logs", "Telemetry"));
            Assert.Equal(EfToolingExitCode.Success, run.LegacyV2ExitCode);
            var offline = run.LegacyV2Response!.Value.GetProperty("configurationContext");
            Assert.Equal(["expected-connection-unchecked", "target-affinity-unverified"],
                StringValues(offline.GetProperty("unresolved")));
        }

        if (scenario == "opaque-composer-configurator")
            Assert.Equal(0, EfToolingHostTestDefaults.OpaqueConfiguratorExecutionCount);
    }

    [Fact]
    public async Task Explicit_removal_refuses_when_the_declared_host_default_readds_a_feature_absent_from_files()
    {
        var run = await RunSameCaptureAsync("host-default-readds-removed-runtime");

        Assert.Null(run.RuntimeFailure);
        Assert.Contains(run.RuntimeDetails.ResolvedParticipants,
            participant => participant.Participant.FeatureId == Runtime);
        Assert.Equal(EfToolingExitCode.Refusal, run.CandidateExitCode);
        Assert.Equal("candidate-selection-conflict", run.CandidateResponse.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("required-disabled", run.CandidateResponse.GetProperty("error").GetProperty("reason").GetString());
        Assert.Equal(Runtime, run.CandidateResponse.GetProperty("error").GetProperty("feature").GetString());
        Assert.False(run.CandidateResponse.TryGetProperty("configurationResolution", out _));
        AssertPrivateInputsRemainPrivate(run);
    }

    [Fact]
    public async Task Candidate_refuses_an_authored_removal_that_disables_a_real_required_descriptor_edge()
    {
        var files = BuildSameCaptureFiles("root-default-distinct-equal");
        using var originalConfiguration = ReadConfiguration(files);
        var originalContext = EfConfigurationProbeTests.ComposeRuntimeContext(originalConfiguration, Shell);
        var descriptors = FeatureDiscovery.DiscoverFeatures(EfConfigurationProbeTests.HostAssemblies)
            .ToDictionary(feature => feature.Id, StringComparer.OrdinalIgnoreCase);
        var originalRequested = originalContext.RequestedFeatureIds.ToHashSet(StringComparer.Ordinal);
        var edge = originalContext.OrderedFeatures
            .Where(feature => originalRequested.Contains(feature.Id))
            .SelectMany(feature => feature.Dependencies.Select(dependency => (Parent: feature.Id, Dependency: dependency)))
            .First(edge => originalRequested.Contains(edge.Dependency) && descriptors.ContainsKey(edge.Dependency));

        Assert.Contains(edge.Dependency, descriptors[edge.Parent].Dependencies, StringComparer.Ordinal);
        Assert.Contains(edge.Parent, originalRequested);
        Assert.Contains(edge.Dependency, originalRequested);

        DisableShellFeature(files, edge.Dependency);
        using var disabledConfiguration = ReadConfiguration(files);
        var disabledContext = EfConfigurationProbeTests.ComposeRuntimeContext(disabledConfiguration, Shell);
        Assert.Contains(edge.Parent, disabledContext.RequestedFeatureIds);
        Assert.DoesNotContain(edge.Dependency, disabledContext.RequestedFeatureIds);
        Assert.Contains(edge.Dependency, disabledContext.DisabledFeatureIds);
        Assert.Contains(edge.Dependency, disabledContext.EnabledFeatureIds);

        var acceptedFeatureIds = disabledContext.RequestedFeatureIds.Order(StringComparer.Ordinal).ToArray();
        Assert.DoesNotContain(edge.Dependency, acceptedFeatureIds);
        var candidate = CreateCandidate(acceptedFeatureIds, files, [edge.Dependency]);
        using var response = new MemoryStream();
        var operation = new EfCandidateInspectionOperation(() => EfConfigurationProbeTests.HostAssemblies);

        var exitCode = await RunOperationAsync(operation, candidate.Request, response, CancellationToken.None);
        var responseJson = Encoding.UTF8.GetString(response.ToArray());
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;

        Assert.Equal(EfToolingExitCode.Refusal, exitCode);
        Assert.Equal("candidate-selection-conflict", root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("required-disabled", root.GetProperty("error").GetProperty("reason").GetString());
        Assert.Equal(edge.Dependency, root.GetProperty("error").GetProperty("feature").GetString());
        Assert.False(root.TryGetProperty("configurationResolution", out _));
        AssertNoPrivateCandidateValues(responseJson);
        Assert.False(File.Exists(DatabasePath));
        Assert.Empty(Directory.EnumerateFiles(temporaryDirectory));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("case-alias")]
    public async Task Candidate_operation_rejects_an_unknown_or_case_aliased_accepted_id_from_real_descriptors(string mutation)
    {
        var files = BuildSameCaptureFiles("root-default-distinct-equal");
        using var configuration = ReadConfiguration(files);
        var context = EfConfigurationProbeTests.ComposeRuntimeContext(configuration, Shell);
        var descriptors = FeatureDiscovery.DiscoverFeatures(EfConfigurationProbeTests.HostAssemblies)
            .ToDictionary(feature => feature.Id, StringComparer.OrdinalIgnoreCase);
        var accepted = context.RequestedFeatureIds.ToArray();
        string invalidId;
        string expectedReason;

        if (mutation == "unknown")
        {
            invalidId = "CandidateBoundaryUnknownFeature";
            Assert.False(descriptors.ContainsKey(invalidId));
            accepted = [.. accepted, invalidId];
            expectedReason = "unknown";
        }
        else
        {
            var actualId = descriptors[Runtime].Id;
            Assert.Contains(actualId, accepted);
            invalidId = actualId.ToLowerInvariant();
            Assert.NotEqual(actualId, invalidId);
            accepted = accepted.Select(id => StringComparer.Ordinal.Equals(id, actualId) ? invalidId : id).ToArray();
            expectedReason = "case-collision";
        }

        accepted = accepted.Order(StringComparer.Ordinal).ToArray();
        var candidate = CreateCandidate(accepted, files);
        using var response = new MemoryStream();
        var operation = new EfCandidateInspectionOperation(() => EfConfigurationProbeTests.HostAssemblies);

        var exitCode = await RunOperationAsync(operation, candidate.Request, response, CancellationToken.None);
        var responseJson = Encoding.UTF8.GetString(response.ToArray());
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;

        Assert.Equal(EfToolingExitCode.Refusal, exitCode);
        Assert.Equal("candidate-selection-conflict", root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(expectedReason, root.GetProperty("error").GetProperty("reason").GetString());
        Assert.Equal(invalidId, root.GetProperty("error").GetProperty("feature").GetString());
        Assert.False(root.TryGetProperty("configurationResolution", out _));
        AssertNoPrivateCandidateValues(responseJson);
        Assert.False(File.Exists(DatabasePath));
        Assert.Empty(Directory.EnumerateFiles(temporaryDirectory));
    }

    [Theory]
    [InlineData("unknown-disabled", "unknown")]
    [InlineData("disabled-case-alias", "case-collision")]
    public async Task Candidate_refuses_unknown_or_case_aliased_disabled_ids_while_runtime_preparation_uses_the_same_files(
        string mutation, string expectedReason)
    {
        var sourceCandidate = CreateRuntimeCandidate();
        var files = sourceCandidate.Request["candidate"]!["files"]!.AsArray()
            .ToDictionary(
                file => file!["name"]!.GetValue<string>(),
                file => Convert.FromBase64String(file!["content"]!.GetValue<string>()),
                StringComparer.Ordinal);
        var descriptors = FeatureDiscovery.DiscoverFeatures(EfConfigurationProbeTests.HostAssemblies)
            .ToDictionary(feature => feature.Id, StringComparer.OrdinalIgnoreCase);
        string disabledId;

        if (mutation == "unknown-disabled")
        {
            disabledId = "CandidateUnknownDisabledFeature";
            Assert.False(descriptors.ContainsKey(disabledId));
        }
        else
        {
            var knownUnselectedId = descriptors.Keys.First(id =>
                !sourceCandidate.AcceptedFeatureIds.Contains(id, StringComparer.OrdinalIgnoreCase) &&
                id.Any(char.IsUpper));
            disabledId = knownUnselectedId.ToLowerInvariant();
            Assert.NotEqual(knownUnselectedId, disabledId);
        }

        var shellSettings = JsonNode.Parse(Encoding.UTF8.GetString(files["shells.json"]))!.AsObject();
        var features = shellSettings["CShells"]!["Shells"]![Shell]!["Features"]!.AsObject();
        Assert.False(features.ContainsKey(disabledId));
        features[disabledId] = false;
        files["shells.json"] = Encoding.UTF8.GetBytes(shellSettings.ToJsonString());
        using var configuration = ReadConfiguration(files);
        var runtimeContext = EfConfigurationProbeTests.ComposeRuntimeContext(configuration, Shell);
        Assert.Contains(disabledId, runtimeContext.DisabledFeatureIds);
        Assert.DoesNotContain(disabledId, runtimeContext.EnabledFeatureIds);
        var runtimePatch = await new EfPersistenceShellSettingsPreparer(configuration)
            .PrepareAsync(runtimeContext, CancellationToken.None);
        Assert.NotEmpty(runtimePatch.ConfigurationData);
        var runtimePatchJson = JsonSerializer.Serialize(runtimePatch);
        Assert.DoesNotContain(ConnectionCanary, runtimePatchJson, StringComparison.Ordinal);
        Assert.DoesNotContain(UnknownSettingCanary, runtimePatchJson, StringComparison.Ordinal);

        var candidate = CreateCandidate(sourceCandidate.AcceptedFeatureIds, files);
        using var response = new MemoryStream();
        var operation = new EfCandidateInspectionOperation(() => EfConfigurationProbeTests.HostAssemblies);
        var exitCode = await RunOperationAsync(operation, candidate.Request, response, CancellationToken.None);
        var responseJson = Encoding.UTF8.GetString(response.ToArray());
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;

        Assert.Equal(EfToolingExitCode.Refusal, exitCode);
        Assert.Equal("candidate-selection-conflict", root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(expectedReason, root.GetProperty("error").GetProperty("reason").GetString());
        Assert.False(root.TryGetProperty("configurationResolution", out _));
        AssertNoPrivateCandidateValues(responseJson);
        Assert.False(File.Exists(DatabasePath));
        Assert.Empty(Directory.EnumerateFiles(temporaryDirectory));
    }

    [Fact]
    public async Task Deleting_an_explicit_diagnostic_binding_changes_runtime_and_candidate_targets_from_the_same_edited_bytes()
    {
        var before = await RunSameCaptureAsync("shell-default-explicit-binding");
        Assert.Null(before.RuntimeFailure);
        Assert.Equal(EfToolingExitCode.Success, before.CandidateExitCode);
        var beforeResolution = before.CandidateResponse.GetProperty("configurationResolution");
        AssertRuntimeAndCandidateTargetsAgree(before, beforeResolution);
        AssertTarget(before, beforeResolution, StructuredLogs, "logs", "Sqlite", "Logs", "ShellBinding", "shell-composed");
        AssertTarget(before, beforeResolution, OpenTelemetry, "shell", "Sqlite", "Shell", "ShellDefault", "shell-composed");
        Assert.Equal("logs", ExplicitBinding(before.FileBytes, StructuredLogs));

        var afterFiles = CloneFileBytes(before.FileBytes);
        DeleteExplicitBinding(afterFiles, StructuredLogs);
        Assert.Null(ExplicitBinding(afterFiles, StructuredLogs));
        Assert.False(before.FileBytes["shells.Production.json"].AsSpan()
            .SequenceEqual(afterFiles["shells.Production.json"]));
        foreach (var name in before.FileBytes.Keys.Where(name => name != "shells.Production.json"))
            Assert.True(before.FileBytes[name].AsSpan().SequenceEqual(afterFiles[name]));

        var after = await RunSameCaptureAsync("shell-default-explicit-binding", afterFiles);
        Assert.Null(after.RuntimeFailure);
        Assert.Equal(EfToolingExitCode.Success, after.CandidateExitCode);
        Assert.Equal(before.AcceptedFeatureIds, after.AcceptedFeatureIds);
        var afterResolution = after.CandidateResponse.GetProperty("configurationResolution");
        AssertRuntimeAndCandidateTargetsAgree(after, afterResolution);
        AssertTarget(after, afterResolution, StructuredLogs, "shell", "Sqlite", "Shell", "ShellDefault", "shell-composed");
        AssertTarget(after, afterResolution, OpenTelemetry, "shell", "Sqlite", "Shell", "ShellDefault", "shell-composed");

        AssertPrivateInputsRemainPrivate(before);
        AssertPrivateInputsRemainPrivate(after);
        Assert.False(File.Exists(DatabasePath));
        Assert.Empty(Directory.EnumerateFiles(temporaryDirectory));
    }

    [Fact]
    public async Task Built_candidate_runtime_and_v1_agree_while_original_host_v2_retains_both_diagnostic_defaults()
    {
        var originalFiles = BuildSameCaptureFiles("root-default-distinct-equal");
        var application = JsonNode.Parse(originalFiles["appsettings.json"])!.AsObject();
        application["ProbeDefaults"]!["AddDiagnosticEfFeatures"] = true;
        originalFiles["appsettings.json"] = Encoding.UTF8.GetBytes(application.ToJsonString());
        var shells = JsonNode.Parse(originalFiles["shells.json"])!.AsObject();
        var originalFeatures = shells["CShells"]!["Shells"]![Shell]!["Features"]!.AsObject();
        Assert.True(originalFeatures.Remove(StructuredLogs));
        Assert.True(originalFeatures.Remove(OpenTelemetry));
        originalFiles["shells.json"] = Encoding.UTF8.GetBytes(shells.ToJsonString());
        var snapshot = SourceSnapshot.Freeze(
            new SourceSelection(Shell, Environment, "shells.Production.json", "appsettings.Production.json"), originalFiles);
        using var originalConfiguration = ReadConfiguration(originalFiles);
        var originalContext = EfConfigurationProbeTests.ComposeRuntimeContext(originalConfiguration, Shell);
        Assert.Contains(StructuredLogs, originalContext.RequestedFeatureIds);
        Assert.Contains(OpenTelemetry, originalContext.RequestedFeatureIds);
        var accepted = originalContext.EnabledFeatureIds.Where(id => id != OpenTelemetry).Order(StringComparer.Ordinal).ToArray();
        var catalog = FoundationSelectionCatalog.Load();
        var authored = new AuthoredComposition("1", new CatalogPin(catalog.Id, catalog.Version, catalog.Digest),
            null, [], accepted.ToImmutableArray(), [OpenTelemetry],
            new AcceptedSelection(catalog.Digest, accepted.ToImmutableArray(), []), null, null);
        var built = CompositionCandidateBuilder.Build(snapshot, catalog, authored, review: null);
        var editedFiles = built.Files.ToDictionary(file => file.Key, file => file.Value, StringComparer.Ordinal);
        var editedSelection = CshellsSourceReader.Read(Encoding.UTF8.GetString(editedFiles["shells.json"]),
            Encoding.UTF8.GetString(editedFiles["shells.Production.json"]), Shell);
        Assert.Contains(StructuredLogs, editedSelection.EnabledFeatureIds);
        Assert.Contains(OpenTelemetry, editedSelection.DisabledFeatureIds);
        Assert.DoesNotContain(OpenTelemetry, editedSelection.EnabledFeatureIds);

        var edited = await RunSameCaptureAsync("feature-disabled-removed", editedFiles);
        Assert.Null(edited.RuntimeFailure);
        Assert.Equal(EfToolingExitCode.Success, edited.CandidateExitCode);
        var resolution = edited.CandidateResponse.GetProperty("configurationResolution");
        AssertRuntimeAndCandidateTargetsAgree(edited, resolution);
        Assert.Equal(accepted, edited.AcceptedFeatureIds);
        Assert.Equal(new[] { StructuredLogs, Runtime }, resolution.GetProperty("participants").EnumerateArray()
            .Select(row => row.GetProperty("feature").GetString()!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        AssertTarget(edited, resolution, StructuredLogs, "logs", "Sqlite", "Logs", "ShellBinding", "shell-composed");
        var (originalExit, originalV2) = await RunLegacyV2Async(originalConfiguration);
        Assert.Equal(EfToolingExitCode.Success, originalExit);
        Assert.Contains(originalV2.GetProperty("configurationContext").GetProperty("participants").EnumerateArray(),
            row => row.GetProperty("feature").GetString() == OpenTelemetry);
        Assert.Contains(originalV2.GetProperty("configurationContext").GetProperty("participants").EnumerateArray(),
            row => row.GetProperty("feature").GetString() == StructuredLogs);
        Assert.DoesNotContain(resolution.GetProperty("participants").EnumerateArray(),
            row => row.GetProperty("feature").GetString() == OpenTelemetry);
        foreach (var file in originalFiles)
            Assert.True(snapshot.ContentMatches(file.Key, file.Value), "Building and inspecting must retain original captured bytes.");
        AssertNoPrivateCandidateValues(originalV2.GetRawText());
        AssertPrivateInputsRemainPrivate(edited);
    }

    private async Task<SameCaptureRun> RunSameCaptureAsync(string scenario, Dictionary<string, byte[]>? capturedFiles = null)
    {
        if (scenario == "opaque-composer-configurator")
            Interlocked.Exchange(ref EfToolingHostTestDefaults.OpaqueConfiguratorExecutionCount, 0);
        var files = capturedFiles ?? BuildSameCaptureFiles(scenario);
        using var configuration = ReadConfiguration(files);
        var runtimeContext = EfConfigurationProbeTests.ComposeRuntimeContext(configuration, Shell);

        ShellSettingsPreparationResult? patch = null;
        string? runtimeFailure = null;
        try
        {
            patch = await new EfPersistenceShellSettingsPreparer(configuration)
                .PrepareAsync(runtimeContext, CancellationToken.None);
        }
        catch (InvalidOperationException failure)
        {
            runtimeFailure = failure.Message;
        }

        // This detached projection is inspected only after the production runtime preparer ran above.
        var details = EfPersistencePreparation.Prepare(runtimeContext, configuration,
            EfConfigurationProbeTests.HostAssemblies, verifyConnectionValues: true);
        var accepted = runtimeContext.EnabledFeatureIds.Order(StringComparer.Ordinal).ToArray();
        string[] removed = scenario switch
        {
            "feature-disabled-removed" => [OpenTelemetry],
            "host-default-readds-removed-runtime" => [Runtime],
            _ => []
        };
        if (scenario == "host-default-readds-removed-runtime")
            accepted = accepted.Where(id => !StringComparer.Ordinal.Equals(id, Runtime)).ToArray();

        var candidate = CreateCandidate(accepted, files, removed);
        var candidateFiles = candidate.Request["candidate"]!["files"]!.AsArray();
        Assert.Equal(4, candidateFiles.Count);
        foreach (var file in candidateFiles)
        {
            var fileObject = file!.AsObject();
            var name = fileObject["name"]!.GetValue<string>();
            var decoded = Convert.FromBase64String(fileObject["content"]!.GetValue<string>());
            Assert.True(files[name].AsSpan().SequenceEqual(decoded),
                "The candidate payload must retain each captured file byte-for-byte.");
        }
        using var candidateResponse = new MemoryStream();
        var operation = new EfCandidateInspectionOperation(() => EfConfigurationProbeTests.HostAssemblies);
        var candidateExitCode = await RunOperationAsync(operation, candidate.Request, candidateResponse, CancellationToken.None);
        var candidateJson = Encoding.UTF8.GetString(candidateResponse.ToArray());
        using var candidateDocument = JsonDocument.Parse(candidateJson);

        int? legacyV2ExitCode = null;
        JsonElement? legacyV2Response = null;
        if (scenario == "unequal-diagnostic-values")
            (legacyV2ExitCode, legacyV2Response) = await RunLegacyV2Async(configuration);

        return new SameCaptureRun(files, runtimeContext, patch, runtimeFailure, details,
            candidateExitCode, candidateJson, candidateDocument.RootElement.Clone(), accepted,
            legacyV2ExitCode, legacyV2Response);
    }

    private static async Task<(int ExitCode, JsonElement Response)> RunLegacyV2Async(IConfigurationRoot configuration)
    {
        var assembly = typeof(EfToolingHostTests).Assembly;
        using var context = new EfToolingConfigurationContext(
            EfToolingConfigurationContext.WorkbenchJson,
            Path.GetDirectoryName(assembly.Location)!, assembly.GetName().Name!, Environment, Shell,
            explicitSelection: true, configuration);
        using var request = new MemoryStream(
            Encoding.UTF8.GetBytes("""{"version":2,"command":"list","selection":{"kind":"from-host"}}"""));
        using var response = new MemoryStream();
        var exitCode = await EfToolingContextOperation.RunAsync(
            request, response, context, EfConfigurationProbeTests.HostAssemblies, CancellationToken.None);
        using var document = JsonDocument.Parse(response.ToArray());
        return (exitCode, document.RootElement.Clone());
    }

    private static string InlineIdentityCanary() => "Data Source=fixture.db;Password=inline-reference-private-2177";

    private Dictionary<string, byte[]> BuildSameCaptureFiles(string scenario)
    {
        var featureDescriptors = FeatureDiscovery.DiscoverFeatures(EfConfigurationProbeTests.HostAssemblies)
            .ToDictionary(feature => feature.Id, StringComparer.OrdinalIgnoreCase);
        var isContextSplit = scenario is "shared-context-provider-split" or "shared-context-schema-split";
        var isDefaultRemoval = scenario == "host-default-readds-removed-runtime";
        var isDisabledRemoval = scenario == "feature-disabled-removed";
        var directFeatureIds = new List<string> { StructuredLogs, OpenTelemetry };
        if (!isDefaultRemoval)
            directFeatureIds.Insert(0, Runtime);
        if (isDisabledRemoval)
            directFeatureIds.Remove(OpenTelemetry);
        if (isContextSplit)
            directFeatureIds.Add(RuntimeWorkflowExecution);

        var orderedFeatureIds = new FeatureDependencyResolver()
            .GetOrderedFeatures(directFeatureIds, featureDescriptors).ToArray();
        if (isDefaultRemoval)
            orderedFeatureIds = orderedFeatureIds.Where(id => !StringComparer.Ordinal.Equals(id, Runtime)).ToArray();
        var features = new JsonObject();
        foreach (var id in orderedFeatureIds)
        {
            var setting = new JsonObject { ["UnknownCandidateSetting"] = UnknownSettingCanary };
            if (scenario == "all-legacy-partial" && id == Runtime)
                setting["Provider"] = "Sqlite";
            if (scenario == "mixed-diagnostic-ownership" && id == OpenTelemetry)
                AddLegacyTarget(setting, "LegacyTelemetry");
            if (scenario == "authored-legacy-conflict" && id == Runtime)
                setting["ConnectionString"] = $"Data Source={DatabasePath};Password={ConnectionCanary}";
            if (scenario == "shared-context-schema-split" && id is Runtime or RuntimeWorkflowExecution)
                setting["Schema"] = id == Runtime ? "schema-runtime" : "schema-execution";
            features[id] = setting;
        }

        if (isDisabledRemoval)
            features[OpenTelemetry] = false;

        var rootPersistence = new JsonObject { ["UnmodeledSetting"] = UnknownSettingCanary };
        var applicationSettings = new JsonObject();
        if (scenario != "all-legacy-partial")
        {
            var primaryProvider = isContextSplit && scenario != "shared-context-provider-split" ? "PostgreSql" : "Sqlite";
            var runtimeProvider = scenario == "shared-context-provider-split" ? "PostgreSql" : primaryProvider;
            var resources = new JsonObject
            {
                ["primary"] = new JsonObject { ["Provider"] = primaryProvider, ["ConnectionName"] = "Primary" },
                ["shell"] = new JsonObject { ["Provider"] = "Sqlite", ["ConnectionName"] = "Shell" },
                ["logs"] = new JsonObject { ["Provider"] = "Sqlite", ["ConnectionName"] = "Logs" },
                ["telemetry"] = new JsonObject { ["Provider"] = "Sqlite", ["ConnectionName"] = "Telemetry" },
                ["runtime-other"] = new JsonObject { ["Provider"] = runtimeProvider, ["ConnectionName"] = "RuntimeShared" }
            };

            switch (scenario)
            {
                case "environment-resource-override":
                    rootPersistence["DefaultResource"] = "root-base";
                    resources["root-base"] = new JsonObject { ["Provider"] = "Sqlite", ["ConnectionName"] = "PrimaryBase" };
                    resources["primary"]!["Provider"] = "PostgreSql";
                    resources["primary"]!["ConnectionName"] = "PrimaryBase";
                    break;
                case "missing-root-resource":
                    rootPersistence["DefaultResource"] = "missing";
                    break;
                case "inline-resource-identity":
                    rootPersistence["DefaultResource"] = InlineIdentityCanary();
                    resources.Remove("primary");
                    resources[InlineIdentityCanary()] = new JsonObject
                    {
                        ["Provider"] = "Sqlite",
                        ["ConnectionName"] = "Primary"
                    };
                    break;
                case "null-root-selection":
                    rootPersistence["DefaultResource"] = null;
                    break;
                case "blank-root-selection":
                    rootPersistence["DefaultResource"] = "";
                    break;
                default:
                    if (scenario is not "mixed-diagnostic-ownership")
                        rootPersistence["DefaultResource"] = "primary";
                    break;
            }

            if (scenario == "inline-connection-identity")
                resources["primary"]!["ConnectionName"] = InlineIdentityCanary();

            if (scenario is "null-provider" or "blank-provider")
                resources["primary"]!["Provider"] = scenario == "null-provider" ? null : "";
            if (scenario is "null-connection-reference" or "blank-connection-reference")
                resources["primary"]!["ConnectionName"] = scenario == "null-connection-reference" ? null : "";
            rootPersistence["Resources"] = resources;
            applicationSettings["Elsa"] = new JsonObject { ["Persistence"] = rootPersistence };
        }
        else
        {
            // A partial legacy provider value proves the host must leave all target identities unprojected.
            applicationSettings["Elsa"] = new JsonObject { ["Persistence"] = rootPersistence };
        }

        applicationSettings["ProbeDefaults"] = new JsonObject
        {
            ["AddRuntimeEfFeature"] = isDefaultRemoval,
            ["AddOpaqueRuntimeConfigurator"] = scenario == "opaque-composer-configurator"
        };
        applicationSettings["Elsa"]!["Persistence"]!["UnmodeledSetting"] = UnknownSettingCanary;

        var shellPersistence = new JsonObject();
        if (scenario == "shell-default-explicit-binding")
            shellPersistence["DefaultResource"] = "shell";
        if (scenario == "missing-binding-resource")
            shellPersistence["Bindings"] = new JsonObject { [StructuredLogs] = "missing" };
        else if (scenario is "null-binding-selection" or "blank-binding-selection")
            shellPersistence["Bindings"] = new JsonObject
            {
                [StructuredLogs] = scenario == "null-binding-selection" ? null : ""
            };
        else if (scenario == "shell-default-explicit-binding")
            shellPersistence["Bindings"] = new JsonObject { [StructuredLogs] = "logs" };
        else if (scenario == "mixed-diagnostic-ownership")
            shellPersistence["Bindings"] = new JsonObject { [StructuredLogs] = "logs" };
        else if (scenario is "root-default-distinct-equal" or "unequal-diagnostic-values")
            shellPersistence["Bindings"] = new JsonObject
            {
                [StructuredLogs] = "logs",
                [OpenTelemetry] = "telemetry"
            };
        else if (scenario == "root-default-same-reference")
            shellPersistence["Bindings"] = new JsonObject
            {
                [StructuredLogs] = "logs",
                [OpenTelemetry] = "logs"
            };
        else if (isContextSplit)
            shellPersistence["Bindings"] = new JsonObject { [RuntimeWorkflowExecution] = "runtime-other" };

        var shellSettings = new JsonObject
        {
            ["CShells"] = new JsonObject
            {
                ["Shells"] = new JsonObject
                {
                    [Shell] = new JsonObject
                    {
                        ["Name"] = Shell,
                        ["Features"] = features,
                        ["Configuration"] = new JsonObject()
                    }
                }
            }
        };
        if (scenario == "root-default-distinct-equal")
            shellSettings["CShells"]!["Shells"]![Shell]!["Configuration"] = new JsonObject
            {
                ["Elsa"] = new JsonObject
                {
                    ["Persistence"] = new JsonObject
                    {
                        ["Bindings"] = new JsonObject { [StructuredLogs] = "primary", [OpenTelemetry] = "primary" }
                    }
                }
            };
        var shellEnvironmentSettings = new JsonObject
        {
            ["CShells"] = new JsonObject
            {
                ["Shells"] = new JsonObject
                {
                    [Shell] = new JsonObject
                    {
                        ["Configuration"] = new JsonObject { ["Elsa"] = new JsonObject { ["Persistence"] = shellPersistence } }
                    }
                }
            }
        };

        var telemetryValue = scenario == "unequal-diagnostic-values" ? DifferentConnectionCanary : ConnectionCanary;
        var connections = new JsonObject
        {
            ["Primary"] = $"Data Source={DatabasePath};Password={ConnectionCanary}",
            ["Shell"] = $"Data Source={DatabasePath};Password={ConnectionCanary}",
            ["Logs"] = $"Data Source={DatabasePath};Password={ConnectionCanary}",
            ["Telemetry"] = $"Data Source={DatabasePath};Password={telemetryValue}",
            ["RuntimeShared"] = $"Data Source={DatabasePath};Password={ConnectionCanary}"
        };
        if (scenario == "inline-connection-identity")
            connections[InlineIdentityCanary()] = $"Data Source={DatabasePath};Password={ConnectionCanary}";
        // The environment must override an existing private named value, not merely add a new key.
        applicationSettings["ConnectionStrings"] = new JsonObject
        {
            ["Logs"] = $"Data Source={DatabasePath};Password={DifferentConnectionCanary}",
            ["PrimaryBase"] = $"Data Source={DatabasePath};Password={DifferentConnectionCanary}"
        };
        var applicationEnvironmentSettings = new JsonObject { ["ConnectionStrings"] = connections };
        if (scenario == "environment-resource-override")
            applicationEnvironmentSettings["Elsa"] = new JsonObject
            {
                ["Persistence"] = new JsonObject
                {
                    ["DefaultResource"] = "primary",
                    ["Resources"] = new JsonObject
                    {
                        ["primary"] = new JsonObject { ["Provider"] = "Sqlite", ["ConnectionName"] = "Primary" }
                    }
                }
            };

        // These four byte arrays are the only file capture. Runtime and candidate input below both consume them.
        return new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["appsettings.json"] = Encoding.UTF8.GetBytes(applicationSettings.ToJsonString()),
            ["appsettings.Production.json"] = Encoding.UTF8.GetBytes(applicationEnvironmentSettings.ToJsonString()),
            ["shells.json"] = Encoding.UTF8.GetBytes(shellSettings.ToJsonString()),
            ["shells.Production.json"] = Encoding.UTF8.GetBytes(shellEnvironmentSettings.ToJsonString())
        };
    }

    private void AssertRuntimeAndCandidateTargetsAgree(SameCaptureRun run, JsonElement resolution)
    {
        Assert.NotNull(run.RuntimePatch);
        Assert.Equal(run.RuntimeDetails.Patch.ConfigurationData.OrderBy(item => item.Key, StringComparer.Ordinal),
            run.RuntimePatch.ConfigurationData.OrderBy(item => item.Key, StringComparer.Ordinal));
        var runtimeByFeature = run.RuntimeDetails.ResolvedParticipants.ToDictionary(
            participant => participant.Participant.FeatureId, StringComparer.Ordinal);
        var candidateRows = resolution.GetProperty("participants").EnumerateArray().ToArray();
        var candidateFeatures = candidateRows.Select(row => row.GetProperty("feature").GetString()!)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var runtimeFeatures = runtimeByFeature.Values
            .Where(participant => participant.Participant.ModuleNames.Count != 0)
            .Select(participant => participant.Participant.FeatureId).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(runtimeFeatures, candidateFeatures);
        foreach (var group in candidateRows.GroupBy(row => row.GetProperty("feature").GetString()!, StringComparer.Ordinal))
            Assert.Equal(runtimeByFeature[group.Key].Participant.ModuleNames.Order(StringComparer.Ordinal),
                group.Select(row => row.GetProperty("module").GetString()!).Order(StringComparer.Ordinal));

        foreach (var row in candidateRows)
        {
            var feature = row.GetProperty("feature").GetString()!;
            var runtime = runtimeByFeature[feature];
            Assert.Equal(runtime.ResourceName, NullableString(row.GetProperty("resource")));
            Assert.Equal(runtime.Provider, NullableString(row.GetProperty("provider")));
            Assert.Equal(runtime.ConnectionName, NullableString(row.GetProperty("connectionReference")));
            Assert.Equal(runtime.Selection.ToString(), row.GetProperty("selection").GetString());
            Assert.Equal(runtime.Source.Scope, NullableString(row.GetProperty("selectorScope")));
            if (runtime.Selection == PersistenceSelectionKind.Legacy)
            {
                Assert.Equal(JsonValueKind.Null, row.GetProperty("resourceScope").ValueKind);
                Assert.Equal("unavailable", row.GetProperty("exactFileProvenance").GetString());
            }
            else
            {
                var definition = run.RuntimeDetails.ResourceDefinitions[runtime.ResourceName!];
                Assert.Equal(definition.Source.Scope, NullableString(row.GetProperty("resourceScope")));
                Assert.Equal("unavailable", row.GetProperty("exactFileProvenance").GetString());
            }
        }
    }

    private static void AssertTarget(SameCaptureRun run, JsonElement resolution, string feature,
        string resource, string provider, string connectionReference, string selection, string selectorScope)
    {
        var runtime = Assert.Single(run.RuntimeDetails.ResolvedParticipants,
            participant => participant.Participant.FeatureId == feature);
        Assert.Equal(resource, runtime.ResourceName);
        Assert.Equal(provider, runtime.Provider);
        Assert.Equal(connectionReference, runtime.ConnectionName);
        Assert.Equal(selection, runtime.Selection.ToString());
        Assert.Equal(selectorScope, runtime.Source.Scope);
        Assert.Equal(provider, run.RuntimePatch!.ConfigurationData[$"{feature}:Provider"]);
        Assert.Equal(connectionReference, run.RuntimePatch.ConfigurationData[$"{feature}:ConnectionName"]);
        var rows = resolution.GetProperty("participants").EnumerateArray()
            .Where(row => row.GetProperty("feature").GetString() == feature).ToArray();
        Assert.NotEmpty(rows);
        Assert.All(rows, row =>
        {
            Assert.Equal(resource, row.GetProperty("resource").GetString());
            Assert.Equal(provider, row.GetProperty("provider").GetString());
            Assert.Equal(connectionReference, row.GetProperty("connectionReference").GetString());
            Assert.Equal(selection, row.GetProperty("selection").GetString());
            Assert.Equal(selectorScope, row.GetProperty("selectorScope").GetString());
            Assert.Equal("root", row.GetProperty("resourceScope").GetString());
            Assert.Equal("unavailable", row.GetProperty("exactFileProvenance").GetString());
        });
    }

    private void AssertPrivateInputsRemainPrivate(SameCaptureRun run)
    {
        var patchJson = run.RuntimePatch is null ? string.Empty : JsonSerializer.Serialize(run.RuntimePatch);
        var v2Json = run.LegacyV2Response?.GetRawText() ?? string.Empty;
        var visible = string.Join('\n', patchJson, run.RuntimeFailure, run.CandidateResponseJson, v2Json);
        var capturedFiles = string.Join('\n', run.FileBytes.Values.Select(bytes => Encoding.UTF8.GetString(bytes)));
        Assert.True(capturedFiles.Contains(ConnectionCanary, StringComparison.Ordinal),
            "The private candidate files should include the connection-value control.");
        foreach (var canary in new[]
                 {
                     ConnectionCanary, DifferentConnectionCanary, UnknownSettingCanary, InlineIdentityCanary(), DatabasePath
                 })
            Assert.False(visible.Contains(canary, StringComparison.Ordinal),
                "A public patch, refusal diagnostic, or tooling response exposed private candidate content.");
        Assert.Empty(Directory.EnumerateFiles(temporaryDirectory));
    }

    private static string? NullableString(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null ? null : value.GetString();

    private static bool AreConnectionValuesEqual(IReadOnlyDictionary<string, byte[]> files, string first, string second)
    {
        using var document = JsonDocument.Parse(files["appsettings.Production.json"]);
        var connections = document.RootElement.GetProperty("ConnectionStrings");
        return StringComparer.Ordinal.Equals(connections.GetProperty(first).GetString(), connections.GetProperty(second).GetString());
    }

    private static void AddLegacyTarget(JsonObject settings, string connectionName)
    {
        settings["Provider"] = "Sqlite";
        settings["ConnectionName"] = connectionName;
        settings["ConnectionString"] = $"Host=localhost;Password={ConnectionCanary}";
    }

    private CandidateFixture CreateRuntimeCandidate(
        bool malformedEnvironmentOverlay = false,
        bool acceptedGraphMismatch = false)
    {
        var initialFiles = BuildFiles([Runtime]);
        using var initialConfiguration = ReadConfiguration(initialFiles);
        var closure = EfConfigurationProbeTests.ComposeRuntimeContext(initialConfiguration, Shell)
            .OrderedFeatures.Select(feature => feature.Id).Order(StringComparer.Ordinal).ToArray();
        Assert.Contains(Runtime, closure);

        string[] selectedFeatures = acceptedGraphMismatch ? [Runtime] : closure;
        var files = BuildFiles(selectedFeatures, malformedEnvironmentOverlay);
        if (!acceptedGraphMismatch && !malformedEnvironmentOverlay)
        {
            using var finalConfiguration = ReadConfiguration(files);
            var effective = EfConfigurationProbeTests.ComposeRuntimeContext(finalConfiguration, Shell);
            Assert.Equal(closure, effective.OrderedFeatures.Select(feature => feature.Id).Order(StringComparer.Ordinal));
            Assert.Empty(effective.ImplicitFeatureIds);
        }

        string[] acceptedFeatureIds = acceptedGraphMismatch ? [] : closure;
        return CreateCandidate(acceptedFeatureIds, files);
    }

    private CandidateFixture CreateCandidate(string[] acceptedFeatureIds, Dictionary<string, byte[]> files,
        IReadOnlyList<string>? removedFeatureIds = null)
    {
        var hostAssembly = typeof(EfToolingHostTests).Assembly;
        var candidate = new JsonObject
        {
            ["version"] = 1,
            ["source"] = "captured-workbench-json-v1",
            ["invocationId"] = InvocationId,
            ["captureId"] = CaptureId,
            ["shell"] = Shell,
            ["environment"] = Environment,
            ["acceptedFeatureIds"] = StringArray(acceptedFeatureIds),
            ["removedFeatureIds"] = StringArray(removedFeatureIds ?? Array.Empty<string>()),
            ["files"] = FileArray(files)
        };
        var request = new JsonObject
        {
            ["version"] = 1,
            ["host"] = new JsonObject
            {
                ["name"] = hostAssembly.GetName().Name,
                ["directory"] = Path.GetDirectoryName(hostAssembly.Location)
            },
            ["candidate"] = candidate
        };

        return new CandidateFixture(request, acceptedFeatureIds);
    }

    private Dictionary<string, byte[]> BuildFiles(
        IReadOnlyList<string> featureIds,
        bool malformedEnvironmentOverlay = false,
        bool includeResourceConfiguration = true)
    {
        var applicationSettings = new JsonObject();
        if (includeResourceConfiguration)
        {
            applicationSettings["ProbeDefaults"] = new JsonObject { ["AddRuntimeEfFeature"] = false };
            applicationSettings["Elsa"] = new JsonObject
            {
                ["Persistence"] = new JsonObject
                {
                    ["DefaultResource"] = "primary",
                    ["Resources"] = new JsonObject
                    {
                        ["primary"] = new JsonObject { ["Provider"] = "Sqlite", ["ConnectionName"] = "Primary" }
                    },
                    ["UnknownCandidateSetting"] = UnknownSettingCanary
                }
            };
            applicationSettings["ConnectionStrings"] = new JsonObject
            {
                ["Primary"] = $"Data Source={DatabasePath};Password={ConnectionCanary}"
            };
        }
        var features = new JsonObject();
        foreach (var featureId in featureIds)
            features[featureId] = new JsonObject { ["UnknownProbeSetting"] = UnknownSettingCanary };

        var shellFiles = new JsonObject
        {
            ["CShells"] = new JsonObject
            {
                ["Shells"] = new JsonObject
                {
                    [Shell] = new JsonObject
                    {
                        ["Name"] = Shell,
                        ["Features"] = features,
                        ["Configuration"] = new JsonObject()
                    }
                }
            }
        };
        var environmentShellFiles = new JsonObject
        {
            ["CShells"] = new JsonObject
            {
                ["Shells"] = new JsonObject
                {
                    [Shell] = new JsonObject { ["Configuration"] = new JsonObject() }
                }
            }
        };

        var environmentAppSettings = malformedEnvironmentOverlay
            ? Encoding.UTF8.GetBytes(
                $"{{\"Elsa\":{{\"Persistence\":{{\"DefaultResource\":\"primary\"}},\"Private\":\"{MalformedFileCanary}")
            : Encoding.UTF8.GetBytes("{}");

        return new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["appsettings.json"] = Encoding.UTF8.GetBytes(applicationSettings.ToJsonString()),
            ["appsettings.Production.json"] = environmentAppSettings,
            ["shells.json"] = Encoding.UTF8.GetBytes(shellFiles.ToJsonString()),
            ["shells.Production.json"] = Encoding.UTF8.GetBytes(environmentShellFiles.ToJsonString())
        };
    }

    private static ConfigurationRoot ReadConfiguration(IReadOnlyDictionary<string, byte[]> files)
    {
        var builder = new ConfigurationBuilder();
        var streams = new[] { "appsettings.json", "appsettings.Production.json", "shells.json", "shells.Production.json" }
            .Select(name => new MemoryStream(files[name], writable: false)).ToArray();
        try
        {
            foreach (var stream in streams)
                builder.AddJsonStream(stream);
            return (ConfigurationRoot)builder.Build();
        }
        finally
        {
            foreach (var stream in streams)
                stream.Dispose();
        }
    }

    private static void DisableShellFeature(Dictionary<string, byte[]> files, string featureId)
    {
        var shellSettings = JsonNode.Parse(Encoding.UTF8.GetString(files["shells.json"]))!.AsObject();
        var features = shellSettings["CShells"]!["Shells"]![Shell]!["Features"]!.AsObject();
        Assert.True(features.ContainsKey(featureId));
        features[featureId] = false;
        files["shells.json"] = Encoding.UTF8.GetBytes(shellSettings.ToJsonString());
    }

    private static Dictionary<string, byte[]> CloneFileBytes(IReadOnlyDictionary<string, byte[]> files) =>
        files.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);

    private static string? ExplicitBinding(IReadOnlyDictionary<string, byte[]> files, string featureId)
    {
        var shellOverlay = JsonNode.Parse(Encoding.UTF8.GetString(files["shells.Production.json"]))!.AsObject();
        return shellOverlay["CShells"]?["Shells"]?[Shell]?["Configuration"]?["Elsa"]?["Persistence"]?
            ["Bindings"]?[featureId]?.GetValue<string>();
    }

    private static void DeleteExplicitBinding(Dictionary<string, byte[]> files, string featureId)
    {
        var shellOverlay = JsonNode.Parse(Encoding.UTF8.GetString(files["shells.Production.json"]))!.AsObject();
        var persistence = shellOverlay["CShells"]!["Shells"]![Shell]!["Configuration"]!["Elsa"]!["Persistence"]!.AsObject();
        var bindings = persistence["Bindings"]!.AsObject();
        Assert.True(bindings.Remove(featureId));
        if (bindings.Count == 0)
            persistence.Remove("Bindings");
        files["shells.Production.json"] = Encoding.UTF8.GetBytes(shellOverlay.ToJsonString());
    }

    private static JsonArray FileArray(IReadOnlyDictionary<string, byte[]> files)
    {
        var result = new JsonArray();
        foreach (var name in new[] { "appsettings.json", "shells.json", "shells.Production.json", "appsettings.Production.json" })
            result.Add(new JsonObject
            {
                ["name"] = name,
                ["captureId"] = CaptureId,
                ["content"] = Convert.ToBase64String(files[name])
            });
        return result;
    }

    private static JsonArray StringArray(IEnumerable<string> values)
    {
        var result = new JsonArray();
        foreach (var value in values)
            result.Add(value);
        return result;
    }

    private static string[] StringValues(JsonElement array) =>
        [.. array.EnumerateArray().Select(value => value.GetString()!)];

    private static async Task<int> RunCandidateAsync(JsonObject request, Stream response, CancellationToken cancellationToken)
    {
        // This exact public method is the host capability the worker negotiates reflectively.
        var method = typeof(EfToolingHost).GetMethod("RunCandidateInspectionAsync",
            BindingFlags.Public | BindingFlags.Static, binder: null,
            types: [typeof(Stream), typeof(Stream), typeof(CancellationToken)], modifiers: null);
        Assert.NotNull(method);
        var capability = typeof(EfToolingHost).Assembly.GetType(
            "Elsa.Persistence.EntityFramework.Tooling.EfCandidateInspectionContract", throwOnError: false);
        Assert.NotNull(capability);
        var version = capability.GetField("Version", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(version);
        Assert.True(version.IsLiteral);
        Assert.Equal(1, Assert.IsType<int>(version.GetRawConstantValue()));

        using var input = new MemoryStream(Encoding.UTF8.GetBytes(request.ToJsonString()), writable: false);
        return await EfToolingHost.RunCandidateInspectionAsync(input, response, cancellationToken);
    }

    private static async Task<int> RunCandidateStreamAsync(Stream input, Stream response, CancellationToken cancellationToken) =>
        await EfToolingHost.RunCandidateInspectionAsync(input, response, cancellationToken);

    private static byte[] PadWithSpaces(byte[] json, int size)
    {
        Assert.True(json.Length <= size);
        var result = new byte[size];
        json.CopyTo(result, 0);
        Array.Fill(result, (byte)' ', json.Length, size - json.Length);
        return result;
    }

    private static byte[] NestedObjectJson(int objectDepth, string leafJson)
    {
        var json = new StringBuilder();
        for (var depth = 0; depth < objectDepth; depth++)
            json.Append("{\"nested\":");
        json.Append(leafJson);
        json.Append('}', objectDepth);
        return Encoding.UTF8.GetBytes(json.ToString());
    }

    private static void ReplaceCandidateFile(JsonObject request, string name, byte[] bytes)
    {
        var file = ((JsonArray)request["candidate"]!["files"]!).Single(node =>
            node!["name"]!.GetValue<string>() == name);
        file!["content"] = Convert.ToBase64String(bytes);
    }

    private static EfCandidateInspectionOperation CreateTrackingOperation(Action onDiscovery) =>
        new(() =>
        {
            onDiscovery();
            return EfConfigurationProbeTests.HostAssemblies;
        });

    private void AssertNoPrivateCandidateValues(string responseJson, params string[] additionalValues)
    {
        foreach (var value in new[]
                 {
                     ConnectionCanary, UnknownSettingCanary, MalformedFileCanary, DifferentConnectionCanary,
                     InlineIdentityCanary(),
                     DatabasePath, "Data Source="
                 }.Concat(additionalValues))
            Assert.DoesNotContain(value, responseJson, StringComparison.Ordinal);
    }

    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public int BytesRead { get; private set; }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken);
            BytesRead += read;
            return read;
        }
    }

    private static async Task<int> RunOperationAsync(
        EfCandidateInspectionOperation operation,
        JsonObject request,
        Stream response,
        CancellationToken cancellationToken)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(request.ToJsonString()), writable: false);
        return await operation.RunAsync(input, response, cancellationToken);
    }

    private sealed record CandidateFixture(JsonObject Request, string[] AcceptedFeatureIds);

    private sealed record SameCaptureRun(
        IReadOnlyDictionary<string, byte[]> FileBytes,
        ShellSettingsPreparationContext RuntimeContext,
        ShellSettingsPreparationResult? RuntimePatch,
        string? RuntimeFailure,
        EfPersistencePreparationResult RuntimeDetails,
        int CandidateExitCode,
        string CandidateResponseJson,
        JsonElement CandidateResponse,
        string[] AcceptedFeatureIds,
        int? LegacyV2ExitCode,
        JsonElement? LegacyV2Response);
}
