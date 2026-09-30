using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Persistence.EntityFramework.Tooling;
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
    private const string ConnectionCanary = "candidate-connection-private-2177";
    private const string UnknownSettingCanary = "candidate-unknown-private-2177";
    private const string MalformedFileCanary = "candidate-malformed-private-2177";
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

    [Fact]
    public async Task Host_refuses_when_the_selected_shell_environment_layer_is_missing()
    {
        var candidate = CreateRuntimeCandidate();
        var files = (JsonArray)candidate.Request["candidate"]!["files"]!;
        var selectedLayer = files.Single(file => file!["name"]!.GetValue<string>() == "shells.Production.json");
        files.Remove(selectedLayer);
        using var response = new MemoryStream();

        var exitCode = await RunCandidateAsync(candidate.Request, response, CancellationToken.None);
        using var document = JsonDocument.Parse(response.ToArray());

        Assert.Equal(2, exitCode);
        Assert.Equal("candidate-request-invalid", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.False(File.Exists(DatabasePath));
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

        var exitCode = await RunOperationAsync(operation, candidate.Request, response, CancellationToken.None);

        Assert.Equal(expectedExitCode, exitCode);
        Assert.Equal(1, discoveryCalls);
        using var document = JsonDocument.Parse(response.ToArray());
        var root = document.RootElement;
        Assert.Equal(InvocationId, root.GetProperty("invocationId").GetString());
        Assert.Equal(CaptureId, root.GetProperty("captureId").GetString());
        Assert.Equal(expectedExitCode, root.GetProperty("exitCode").GetInt32());
        if (expectedExitCode == EfToolingExitCode.Success)
        {
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
        }
        else
        {
            Assert.Equal("refused", root.GetProperty("status").GetString());
            Assert.Equal("candidate-host-unavailable", root.GetProperty("error").GetProperty("code").GetString());
            Assert.False(root.TryGetProperty("configurationResolution", out _));
            Assert.Equal(["code"], root.GetProperty("error").EnumerateObject().Select(property => property.Name));
        }
        Assert.False(File.Exists(DatabasePath));
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

    private CandidateFixture CreateCandidate(string[] acceptedFeatureIds, Dictionary<string, byte[]> files)
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
            ["removedFeatureIds"] = new JsonArray(),
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
}
