using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Cli.Worker;
using Xunit;

namespace Elsa.Cli.Tests;

public sealed class WorkerProtocolTests
{
    private const string CandidateInvocationId = "11111111111111111111111111111111";
    private const string CandidateCaptureId = "22222222222222222222222222222222";
    private const int CandidateHostResponseMaxBytes = 4 * 1024 * 1024;
    private const string CandidateRequestJson = """
        {"version":2,"command":"inspect-candidate","hostDirectory":"/compiled-host","hostName":"FixtureHost","depsFile":"/compiled-host/FixtureHost.deps.json","packageRoots":[],"restore":false,"candidate":{"version":1,"source":"captured-workbench-json-v1","invocationId":"11111111111111111111111111111111","captureId":"22222222222222222222222222222222","shell":"default","environment":"Production","acceptedFeatureIds":["ResourceProbe"],"removedFeatureIds":[],"files":[{"name":"appsettings.json","captureId":"22222222222222222222222222222222","content":"e30="},{"name":"shells.json","captureId":"22222222222222222222222222222222","content":"e30="},{"name":"shells.Production.json","captureId":"22222222222222222222222222222222","content":"e30="},{"name":"appsettings.Production.json","captureId":"22222222222222222222222222222222","content":"e30="}]}}
        """;
    private static IEnumerable<JsonNode?> EnvironmentInputVariants =>
    [
        null,
        JsonNode.Parse(CandidateInspectionFixture.EnvironmentDocument(
            ("Unknown", CandidateInspectionFixture.SafePrivateEnvironmentCanary)))
    ];
    private static string CandidateHostSuccessJson => CandidateHostResponseFixtures.SuccessJson(CandidateInvocationId, CandidateCaptureId);
    private const string CandidateHostRefusalJson = """
        {"version":1,"invocationId":"11111111111111111111111111111111","captureId":"22222222222222222222222222222222","status":"refused","exitCode":2,"error":{"code":"candidate-selection-conflict","reason":"required-disabled","feature":"ResourceProbe","resource":"primary"}}
        """;

    [Theory]
    [InlineData("{\"version\":2,\"command\":\"list\",\"future\":true}")]
    [InlineData("{\"version\":2,\"command\":\"list\",\"command\":\"plan\"}")]
    [InlineData("{\"version\":2,\"command\":\"list\",\"selection\":{\"kind\":\"all\",\"kind\":\"modules\"}}")]
    [InlineData("{\"command\":\"list\"}")]
    public async Task Private_request_refuses_unknown_and_duplicate_fields(string json)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(json));

        await Assert.ThrowsAsync<JsonException>(() => WorkerContract.ReadRequestAsync(input, CancellationToken.None));
    }

    [Fact]
    public async Task Candidate_reader_accepts_closed_v1_payload_inside_the_existing_v2_envelope()
    {
        var request = await ReadCandidateRequestAsync(CandidateRequestJson);

        Assert.NotNull(request);
        Assert.Equal(2, request.Version);
        Assert.Equal(WorkerCommands.InspectCandidate, request.Command);
        var candidate = Assert.IsType<WorkerCandidatePayload>(request.Candidate);
        Assert.Equal(1, candidate.Version);
        Assert.Equal("captured-workbench-json-v1", candidate.Source);
        Assert.Equal(4, candidate.Files?.Count);
    }

    [Fact]
    public async Task Environment_reader_accepts_a_closed_separately_typed_envelope_and_preserves_raw_bytes()
    {
        var raw = CandidateInspectionFixture.EnvironmentDocument(("ConnectionStrings__Primary", ""));
        var request = await ReadEnvironmentRequestAsync(EnvironmentRequest(raw).ToJsonString());

        Assert.Equal(2, request.Version);
        Assert.Equal(WorkerCommands.InspectCandidateEnvironment, request.Command);
        Assert.Equal(CandidateCaptureId, request.EnvironmentInput!.CaptureId);
        Assert.Equal(raw, Convert.FromBase64String(request.EnvironmentInput.Content!));
        Assert.Equal("captured-workbench-json-v1", request.Candidate!.Source);
    }

    [Theory]
    [InlineData("outer-extra")]
    [InlineData("outer-version")]
    [InlineData("old-command")]
    [InlineData("input-absent")]
    [InlineData("input-null")]
    [InlineData("input-version")]
    [InlineData("input-capture")]
    [InlineData("input-extra")]
    [InlineData("candidate-extra")]
    [InlineData("file-extra")]
    public async Task Environment_reader_refuses_shape_and_correlation_skew(string mutation)
    {
        var request = EnvironmentRequest(CandidateInspectionFixture.EnvironmentDocument());
        Assert.NotNull(await ReadEnvironmentRequestAsync(request.ToJsonString()));
        switch (mutation)
        {
            case "outer-extra": request["restore"] = false; break;
            case "outer-version": request["version"] = 1; break;
            case "old-command": request["command"] = WorkerCommands.InspectCandidate; break;
            case "input-absent": request.Remove("environmentInput"); break;
            case "input-null": request["environmentInput"] = null; break;
            case "input-version": request["environmentInput"]!["version"] = 2; break;
            case "input-capture": request["environmentInput"]!["captureId"] = CandidateInvocationId; break;
            case "input-extra": request["environmentInput"]!["sourcePath"] = "private-path-canary"; break;
            case "candidate-extra": request["candidate"]!["environmentInput"] = null; break;
            case "file-extra": request["candidate"]!["files"]![0]!["environmentInput"] = null; break;
        }
        var refusal = await Assert.ThrowsAsync<JsonException>(() => ReadEnvironmentRequestAsync(request.ToJsonString()));
        Assert.Equal("The candidate worker request is invalid.", refusal.Message);
    }

    [Theory]
    [InlineData("outer")]
    [InlineData("input")]
    public async Task Environment_reader_refuses_duplicate_properties_at_each_new_boundary(string boundary)
    {
        var json = EnvironmentRequest(CandidateInspectionFixture.EnvironmentDocument()).ToJsonString();
        Assert.NotNull(await ReadEnvironmentRequestAsync(json));
        json = boundary == "outer"
            ? json.Insert(1, "\"version\":2,")
            : json.Replace("\"environmentInput\":{", "\"environmentInput\":{\"version\":1,", StringComparison.Ordinal);
        await Assert.ThrowsAsync<JsonException>(() => ReadEnvironmentRequestAsync(json));
    }

    [Theory]
    [InlineData("noncanonical")]
    [InlineData("oversized")]
    [InlineData("raw-invalid")]
    public async Task Environment_reader_independently_validates_the_original_document(string mutation)
    {
        var request = EnvironmentRequest(CandidateInspectionFixture.EnvironmentDocument());
        Assert.NotNull(await ReadEnvironmentRequestAsync(request.ToJsonString()));
        request["environmentInput"]!["content"] = mutation switch
        {
            "noncanonical" => "e31=",
            "oversized" => Convert.ToBase64String(new byte[1024 * 1024 + 1]),
            _ => Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"version\":1,\"entries\":[{\"key\":\"Unknown\",\"value\":null}]}"))
        };
        var refusal = await Assert.ThrowsAsync<WorkerRefusal>(() => ReadEnvironmentRequestAsync(request.ToJsonString()));
        Assert.Equal(mutation == "oversized" ? "candidate-environment-input-too-large" :
            "candidate-environment-input-invalid", refusal.Code);
        Assert.Equal(2, refusal.ExitCode);
        Assert.DoesNotContain("Unknown", refusal.Message);
    }

    [Fact]
    public void Environment_response_lane_preserves_correlation_and_rejects_old_source_and_wrong_mode()
    {
        var request = EnvironmentRequest(CandidateInspectionFixture.EnvironmentDocument())
            .Deserialize<CandidateEnvironmentWorkerRequestV2>(WorkerContract.Json)!;
        var response = CandidateHostResponseFixtures.Success(request.Candidate!);
        response["configurationResolution"]!["source"] = "captured-workbench-json-explicit-environment-v1";
        response["configurationResolution"]!["externalInputs"] = "supplied-intended";
        var valid = JsonSerializer.SerializeToElement(response);
        WorkerContract.ValidateCandidateEnvironmentHostResponse(valid, request.Candidate!, 0);
        Assert.Throws<WorkerRefusal>(() => WorkerContract.ValidateCandidateHostResponse(valid, request.Candidate!, 0));
        foreach (var field in new[] { "source", "externalInputs", "runtimeParity", "targetVerification" })
        {
            var changed = response.DeepClone();
            changed["configurationResolution"]![field] = field switch
            {
                "source" => "captured-workbench-json-v1", "externalInputs" => "unverified",
                "runtimeParity" => "observed", _ => "verified"
            };
            var refusal = Assert.Throws<WorkerRefusal>(() => WorkerContract.ValidateCandidateEnvironmentHostResponse(
                JsonSerializer.SerializeToElement(changed), request.Candidate!, 0));
            Assert.Equal("candidate-response-invalid", refusal.Code);
        }
    }

    [Fact]
    public void Environment_response_lane_admits_only_a_closed_correlated_unenrolled_exit_three()
    {
        var request = EnvironmentRequest(CandidateInspectionFixture.EnvironmentDocument())
            .Deserialize<CandidateEnvironmentWorkerRequestV2>(WorkerContract.Json)!;
        var response = JsonNode.Parse(CandidateHostRefusalJson)!;
        response["exitCode"] = 3;
        response["error"] = new JsonObject { ["code"] = "candidate-environment-host-unenrolled" };
        var valid = JsonSerializer.SerializeToElement(response);
        WorkerContract.ValidateCandidateEnvironmentHostResponse(valid, request.Candidate!, 3);
        Assert.Throws<WorkerRefusal>(() => WorkerContract.ValidateCandidateHostResponse(valid, request.Candidate!, 3));
        var wrongExit = response.DeepClone();
        wrongExit["exitCode"] = 2;
        Assert.Throws<WorkerRefusal>(() => WorkerContract.ValidateCandidateEnvironmentHostResponse(
            JsonSerializer.SerializeToElement(wrongExit), request.Candidate!, 2));
        foreach (var mutation in new[] { "code", "identity", "capture", "exit" })
        {
            var changed = response.DeepClone();
            switch (mutation)
            {
                case "code": changed["error"]!["code"] = "resource-not-found"; break;
                case "identity": changed["error"]!["resource"] = CandidateInspectionFixture.SafePrivateEnvironmentCanary; break;
                case "capture": changed["captureId"] = CandidateInvocationId; break;
                case "exit": changed["exitCode"] = 2; break;
            }
            Assert.Throws<WorkerRefusal>(() => WorkerContract.ValidateCandidateEnvironmentHostResponse(
                JsonSerializer.SerializeToElement(changed), request.Candidate!, 3));
        }
    }

    [Theory]
    [InlineData("candidate-environment-input-invalid", 2)]
    [InlineData("candidate-environment-input-too-large", 2)]
    [InlineData("candidate-environment-key-collision", 2)]
    [InlineData("candidate-environment-prefix-unsupported", 2)]
    [InlineData("candidate-environment-host-unenrolled", 3)]
    public void Environment_worker_response_uses_fixed_local_messages_and_keeps_the_old_lane_closed(string code, int exit)
    {
        var request = EnvironmentRequest(CandidateInspectionFixture.EnvironmentDocument())
            .Deserialize<CandidateEnvironmentWorkerRequestV2>(WorkerContract.Json)!;
        var response = JsonSerializer.SerializeToUtf8Bytes(new WorkerResponse
        {
            ExitCode = exit, Error = new WorkerError { Code = code, Message = CandidateInspectionFixture.PrivateEnvironmentCanary }
        }, WorkerContract.Json);
        var accepted = WorkerContract.ParseCandidateEnvironmentWorkerResponse(response, request.Candidate!, exit);
        Assert.Equal(code, accepted.Error!.Code);
        Assert.DoesNotContain(CandidateInspectionFixture.PrivateEnvironmentCanary, accepted.Error.Message);
        Assert.Empty(accepted.Error.Details);
        Assert.Throws<WorkerRefusal>(() => WorkerContract.ParseCandidateWorkerResponse(response, request.Candidate!, exit));
        Assert.Throws<WorkerRefusal>(() => WorkerContract.ParseCandidateEnvironmentWorkerResponse(response, request.Candidate!, exit == 2 ? 3 : 2));
    }

    [Fact]
    public async Task Environment_reader_accepts_exact_raw_and_serialized_bounds_then_refuses_one_more_byte()
    {
        var request = EnvironmentRequest(CandidateInspectionFixture.EnvironmentDocumentOfSize(1024 * 1024));
        var json = request.ToJsonString();
        Assert.NotNull(await ReadEnvironmentRequestAsync(json));
        var exact = json + new string(' ', 8 * 1024 * 1024 - Encoding.UTF8.GetByteCount(json));
        Assert.NotNull(await ReadEnvironmentRequestAsync(exact));
        var refusal = await Assert.ThrowsAsync<WorkerRefusal>(() => ReadEnvironmentRequestAsync(exact + " "));
        Assert.Equal("candidate-request-too-large", refusal.Code);
        Assert.Equal(2, refusal.ExitCode);
    }

    [Fact]
    public async Task Environment_host_response_reader_bounds_the_actual_serialized_response()
    {
        var candidate = EnvironmentRequest(CandidateInspectionFixture.EnvironmentDocument())
            .Deserialize<CandidateEnvironmentWorkerRequestV2>(WorkerContract.Json)!.Candidate!;
        var response = CandidateHostResponseFixtures.Success(candidate);
        response["configurationResolution"]!["source"] = "captured-workbench-json-explicit-environment-v1";
        response["configurationResolution"]!["externalInputs"] = "supplied-intended";
        var json = response.ToJsonString();
        var exact = Encoding.UTF8.GetBytes(json + new string(' ', CandidateHostResponseMaxBytes - Encoding.UTF8.GetByteCount(json)));
        using (var input = new MemoryStream(exact))
            WorkerContract.ValidateCandidateEnvironmentHostResponse(await WorkerContract.ReadCandidateEnvironmentHostResponseAsync(
                input, CandidateInvocationId, CandidateCaptureId, 0, CancellationToken.None), candidate, 0);
        using var oversized = new MemoryStream([.. exact, (byte)' ']);
        var refusal = await Assert.ThrowsAsync<WorkerRefusal>(() => WorkerContract.ReadCandidateEnvironmentHostResponseAsync(
            oversized, CandidateInvocationId, CandidateCaptureId, 0, CancellationToken.None));
        Assert.Equal("candidate-response-too-large", refusal.Code);
    }

    private static JsonObject EnvironmentRequest(byte[] raw)
    {
        var request = JsonNode.Parse(CandidateRequestJson)!.AsObject();
        request.Remove("restore");
        request["command"] = WorkerCommands.InspectCandidateEnvironment;
        request["environmentInput"] = JsonSerializer.SerializeToNode(new WorkerEnvironmentInput
        {
            Version = 1, CaptureId = CandidateCaptureId, Content = Convert.ToBase64String(raw)
        }, WorkerContract.Json);
        return request;
    }

    private static async Task<CandidateEnvironmentWorkerRequestV2> ReadEnvironmentRequestAsync(string json)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return await WorkerContract.ReadCandidateEnvironmentRequestAsync(input, CancellationToken.None);
    }

    [Fact]
    public async Task Legacy_reader_does_not_admit_the_candidate_command_or_payload()
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(CandidateRequestJson));

        await Assert.ThrowsAsync<JsonException>(() => WorkerContract.ReadRequestAsync(input, CancellationToken.None));
    }

    [Fact]
    public async Task Legacy_reader_rejects_candidate_field_even_when_null()
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("{\"version\":2,\"command\":\"list\",\"candidate\":null}"));

        await Assert.ThrowsAsync<JsonException>(() => WorkerContract.ReadRequestAsync(input, CancellationToken.None));
    }

    [Theory]
    [InlineData("list")]
    [InlineData("plan")]
    [InlineData("script")]
    [InlineData("apply")]
    [InlineData("validate")]
    [InlineData("post-migrate")]
    [InlineData("hold")]
    [InlineData("release")]
    [InlineData("status")]
    public async Task Old_command_reader_rejects_environment_input_even_when_null(string command)
    {
        var baseline = new JsonObject { ["version"] = 2, ["command"] = command };
        using (var input = new MemoryStream(Encoding.UTF8.GetBytes(baseline.ToJsonString())))
            Assert.NotNull(await WorkerContract.ReadRequestAsync(input, CancellationToken.None));

        foreach (var value in EnvironmentInputVariants)
        {
            var request = (JsonObject)baseline.DeepClone();
            request["environmentInput"] = value;
            using var input = new MemoryStream(Encoding.UTF8.GetBytes(request.ToJsonString()));
            var refusal = await Assert.ThrowsAsync<JsonException>(() =>
                WorkerContract.ReadRequestAsync(input, CancellationToken.None));
            Assert.DoesNotContain(CandidateInspectionFixture.SafePrivateEnvironmentCanary, refusal.Message);
        }
    }

    [Theory]
    [InlineData("outer")]
    [InlineData("candidate")]
    [InlineData("file")]
    public async Task Old_candidate_reader_rejects_environment_input_at_every_object_boundary(string boundary)
    {
        Assert.NotNull(await ReadCandidateRequestAsync(CandidateRequestJson));
        foreach (var value in EnvironmentInputVariants)
        {
            var request = JsonNode.Parse(CandidateRequestJson)!;
            var target = boundary switch
            {
                "candidate" => request["candidate"]!,
                "file" => request["candidate"]!["files"]![0]!,
                _ => request
            };
            target["environmentInput"] = value;
            var refusal = await Assert.ThrowsAsync<JsonException>(() =>
                ReadCandidateRequestAsync(request.ToJsonString()));
            Assert.Equal("The candidate worker request is invalid.", refusal.Message);
        }
    }

    [Fact]
    public async Task Candidate_reader_rejects_candidate_payload_on_a_legacy_command()
    {
        var request = JsonNode.Parse(CandidateRequestJson)!.AsObject();
        request["command"] = "list";

        await Assert.ThrowsAsync<JsonException>(() => ReadCandidateRequestAsync(request.ToJsonString()));
    }

    [Theory]
    [InlineData("selection", "{\"kind\":\"all\"}")]
    [InlineData("provider", "\"Sqlite\"")]
    [InlineData("schema", "\"public\"")]
    [InlineData("output", "\"/tmp/script.sql\"")]
    [InlineData("environment", "\"Production\"")]
    [InlineData("shell", "\"default\"")]
    [InlineData("contextSource", "\"workbench-json-v1\"")]
    [InlineData("contextVersion", "1")]
    [InlineData("resource", "\"primary\"")]
    [InlineData("shells", "[]")]
    [InlineData("connectionEnv", "\"CONNECTION\"")]
    [InlineData("connection", "\"private-value\"")]
    [InlineData("finalization", "{}")]
    [InlineData("skewAllowance", "\"00:00:00\"")]
    [InlineData("sqliteMigrationLockStaleAfter", "\"00:01:00\"")]
    public async Task Candidate_reader_rejects_non_null_legacy_live_fields(string field, string jsonValue)
    {
        var request = JsonNode.Parse(CandidateRequestJson)!.AsObject();
        request[field] = JsonNode.Parse(jsonValue);

        var refusal = await Assert.ThrowsAsync<JsonException>(() => ReadCandidateRequestAsync(request.ToJsonString()));
        Assert.Equal("The candidate worker request is invalid.", refusal.Message);
    }

    [Fact]
    public async Task Candidate_reader_allows_null_legacy_fields_without_granting_them_authority()
    {
        var request = JsonNode.Parse(CandidateRequestJson)!.AsObject();
        foreach (var field in new[]
                 {
                     "selection", "provider", "schema", "output", "environment", "shell", "contextSource",
                     "contextVersion", "resource", "shells", "connectionEnv", "connection", "finalization", "skewAllowance",
                     "sqliteMigrationLockStaleAfter"
                 })
            request[field] = null;

        Assert.NotNull(await ReadCandidateRequestAsync(request.ToJsonString()));
    }

    [Fact]
    public async Task Candidate_reader_accepts_omitted_or_false_restore_and_rejects_true_restore()
    {
        var withoutRestore = JsonNode.Parse(CandidateRequestJson)!.AsObject();
        withoutRestore.Remove("restore");
        Assert.NotNull(await ReadCandidateRequestAsync(withoutRestore.ToJsonString()));

        var withRestore = JsonNode.Parse(CandidateRequestJson)!.AsObject();
        withRestore["restore"] = true;
        await Assert.ThrowsAsync<JsonException>(() => ReadCandidateRequestAsync(withRestore.ToJsonString()));
    }

    [Fact]
    public async Task Candidate_reader_bounds_actual_request_bytes_and_honors_cancellation()
    {
        const int maxRequestBytes = 8 * 1024 * 1024;
        var validBytes = Encoding.UTF8.GetByteCount(CandidateRequestJson);
        using var exactLimit = new CountingMemoryStream(Encoding.UTF8.GetBytes(
            CandidateRequestJson + new string(' ', maxRequestBytes - validBytes)));
        Assert.NotNull(await WorkerContract.ReadCandidateRequestAsync(exactLimit, CancellationToken.None));
        Assert.Equal(maxRequestBytes, exactLimit.BytesRead);

        using var oversized = new CountingMemoryStream(new byte[8 * 1024 * 1024 + 2]);
        var refusal = await Assert.ThrowsAsync<WorkerRefusal>(() => WorkerContract.ReadCandidateRequestAsync(oversized, CancellationToken.None));
        Assert.Equal("candidate-request-too-large", refusal.Code);
        Assert.Equal("The candidate request exceeds the supported size limit.", refusal.Message);
        Assert.Equal(maxRequestBytes + 1, oversized.BytesRead);

        using var input = new MemoryStream(Encoding.UTF8.GetBytes(CandidateRequestJson));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            WorkerContract.ReadCandidateRequestAsync(input, cancellation.Token));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task Candidate_reader_accepts_exact_per_file_and_aggregate_decoded_limits(int filesAtLimit)
    {
        const int fileLimit = 1024 * 1024;
        // Four selected layers are the complete file set, so a valid aggregate can reach 4 MiB but cannot exceed it.
        var fileSizes = Enumerable.Repeat(2, 4).ToArray();
        for (var index = 0; index < filesAtLimit; index++)
            fileSizes[index] = fileLimit;

        var json = CandidateRequestWithFileSizes(fileSizes).ToJsonString();
        Assert.True(Encoding.UTF8.GetByteCount(json) < 8 * 1024 * 1024);

        var request = await ReadCandidateRequestAsync(json);

        Assert.NotNull(request);
        Assert.Equal(4, request.Candidate!.Files!.Count);
        Assert.Equal(fileSizes.Sum(), request.Candidate.Files.Sum(file => Convert.FromBase64String(file.Content!).Length));
    }

    [Fact]
    public async Task Candidate_reader_rejects_one_byte_over_the_decoded_per_file_limit()
    {
        const int fileLimit = 1024 * 1024;
        var json = CandidateRequestWithFileSizes([fileLimit + 1, 2, 2, 2]).ToJsonString();

        var refusal = await Assert.ThrowsAsync<JsonException>(() => ReadCandidateRequestAsync(json));

        Assert.Equal("The candidate worker request is invalid.", refusal.Message);
    }

    [Fact]
    public async Task Candidate_reader_accepts_the_three_required_layers_without_the_optional_appsettings_overlay()
    {
        var request = JsonNode.Parse(CandidateRequestJson)!.AsObject();
        var files = request["candidate"]!["files"]!.AsArray();
        var optional = files.Single(file => file!["name"]!.GetValue<string>() == "appsettings.Production.json");
        files.Remove(optional);

        var parsed = await ReadCandidateRequestAsync(request.ToJsonString());

        Assert.Equal(
            ["appsettings.json", "shells.Production.json", "shells.json"],
            parsed!.Candidate!.Files!.Select(file => file.Name).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("shells.json")]
    [InlineData("shells.Production.json")]
    public async Task Candidate_reader_requires_each_base_and_selected_environment_layer(string missingName)
    {
        var request = JsonNode.Parse(CandidateRequestJson)!.AsObject();
        var files = request["candidate"]!["files"]!.AsArray();
        var missing = files.Single(file => file!["name"]!.GetValue<string>() == missingName);
        files.Remove(missing);

        var refusal = await Assert.ThrowsAsync<JsonException>(() => ReadCandidateRequestAsync(request.ToJsonString()));

        Assert.Equal("The candidate worker request is invalid.", refusal.Message);
    }

    [Fact]
    public async Task Candidate_reader_rejects_noncanonical_base64_even_when_it_decodes_to_the_same_bytes()
    {
        Assert.Equal(Convert.FromBase64String("e30="), Convert.FromBase64String("e31="));
        var request = JsonNode.Parse(CandidateRequestJson)!.AsObject();
        request["candidate"]!["files"]![0]!["content"] = "e31=";

        var refusal = await Assert.ThrowsAsync<JsonException>(() => ReadCandidateRequestAsync(request.ToJsonString()));

        Assert.Equal("The candidate worker request is invalid.", refusal.Message);
    }

    [Fact]
    public async Task Candidate_reader_refuses_an_overdeep_unknown_outer_field_without_echoing_content()
    {
        var nested = new string('[', 65) + "0" + new string(']', 65);
        var json = CandidateRequestJson[..^1] + ",\"nested\":" + nested + "}";

        var refusal = await Assert.ThrowsAsync<JsonException>(() => ReadCandidateRequestAsync(json));

        Assert.Equal("The candidate worker request is invalid.", refusal.Message);
    }

    [Theory]
    [InlineData("outer-unknown")]
    [InlineData("candidate-unknown")]
    [InlineData("candidate-missing-required")]
    [InlineData("candidate-wrong-case")]
    [InlineData("outer-missing-version")]
    [InlineData("candidate-missing-version")]
    [InlineData("candidate-wrong-version")]
    [InlineData("candidate-wrong-source")]
    [InlineData("candidate-invalid-token")]
    [InlineData("candidate-equal-tokens")]
    [InlineData("candidate-unsafe-shell")]
    [InlineData("candidate-unsafe-environment")]
    [InlineData("candidate-unsorted-ids")]
    [InlineData("candidate-case-collision")]
    [InlineData("candidate-null-accepted")]
    [InlineData("candidate-null-removed")]
    [InlineData("candidate-null-id")]
    [InlineData("candidate-unsafe-removed")]
    [InlineData("candidate-excessive-accepted")]
    [InlineData("candidate-excessive-removed")]
    [InlineData("candidate-overlap")]
    [InlineData("file-unknown")]
    [InlineData("file-missing-required")]
    [InlineData("file-capture-mismatch")]
    [InlineData("file-unexpected-name")]
    [InlineData("file-invalid-base64")]
    [InlineData("file-count")]
    [InlineData("outer-duplicate")]
    [InlineData("candidate-duplicate")]
    [InlineData("file-duplicate")]
    public async Task Candidate_reader_requires_closed_case_sensitive_duplicate_free_nested_shapes(string mutation)
    {
        var request = JsonNode.Parse(CandidateRequestJson)!.AsObject();
        switch (mutation)
        {
            case "outer-unknown": request["future"] = true; break;
            case "candidate-unknown": request["candidate"]!["future"] = true; break;
            case "candidate-missing-required": request["candidate"]!.AsObject().Remove("shell"); break;
            case "candidate-wrong-case": request["candidate"]!["Shell"] = "default"; break;
            case "outer-missing-version": request.Remove("version"); break;
            case "candidate-missing-version": request["candidate"]!.AsObject().Remove("version"); break;
            case "candidate-wrong-version": request["candidate"]!["version"] = 2; break;
            case "candidate-wrong-source": request["candidate"]!["source"] = "workbench-json-v1"; break;
            case "candidate-invalid-token": request["candidate"]!["invocationId"] = new string('g', 32); break;
            case "candidate-equal-tokens": request["candidate"]!["captureId"] = "11111111111111111111111111111111"; break;
            case "candidate-unsafe-shell": request["candidate"]!["shell"] = "not a shell"; break;
            case "candidate-unsafe-environment": request["candidate"]!["environment"] = "Production/Other"; break;
            case "candidate-unsorted-ids": request["candidate"]!["acceptedFeatureIds"] = new JsonArray("Z", "A"); break;
            case "candidate-case-collision": request["candidate"]!["acceptedFeatureIds"] = new JsonArray("ResourceProbe", "resourceprobe"); break;
            case "candidate-null-accepted": request["candidate"]!["acceptedFeatureIds"] = null; break;
            case "candidate-null-removed": request["candidate"]!["removedFeatureIds"] = null; break;
            case "candidate-null-id": request["candidate"]!["acceptedFeatureIds"] = new JsonArray((JsonNode?)null); break;
            case "candidate-unsafe-removed": request["candidate"]!["removedFeatureIds"] = new JsonArray("not a feature"); break;
            case "candidate-excessive-accepted": request["candidate"]!["acceptedFeatureIds"] = SelectionIds(4097); break;
            case "candidate-excessive-removed": request["candidate"]!["removedFeatureIds"] = SelectionIds(4097); break;
            case "candidate-overlap": request["candidate"]!["removedFeatureIds"] = new JsonArray("ResourceProbe"); break;
            case "file-unknown": request["candidate"]!["files"]![0]!["future"] = true; break;
            case "file-missing-required": request["candidate"]!["files"]![0]!.AsObject().Remove("content"); break;
            case "file-capture-mismatch": request["candidate"]!["files"]![0]!["captureId"] = "33333333333333333333333333333333"; break;
            case "file-unexpected-name": request["candidate"]!["files"]![0]!["name"] = "shells.Staging.json"; break;
            case "file-invalid-base64": request["candidate"]!["files"]![0]!["content"] = "not-base64"; break;
            case "file-count": request["candidate"]!["files"]!.AsArray().Clear(); break;
            case "outer-duplicate":
                await Assert.ThrowsAsync<JsonException>(() => ReadCandidateRequestAsync(
                    CandidateRequestJson.Replace("\"command\":\"inspect-candidate\"", "\"command\":\"inspect-candidate\",\"command\":\"inspect-candidate\"", StringComparison.Ordinal)));
                return;
            case "candidate-duplicate":
                await Assert.ThrowsAsync<JsonException>(() => ReadCandidateRequestAsync(
                    CandidateRequestJson.Replace("\"version\":1", "\"version\":1,\"version\":1", StringComparison.Ordinal)));
                return;
            case "file-duplicate":
                await Assert.ThrowsAsync<JsonException>(() => ReadCandidateRequestAsync(
                    CandidateRequestJson.Replace("\"name\":\"appsettings.json\"", "\"name\":\"appsettings.json\",\"name\":\"appsettings.json\"", StringComparison.Ordinal)));
                return;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        await Assert.ThrowsAsync<JsonException>(() => ReadCandidateRequestAsync(request.ToJsonString()));
    }

    [Fact]
    public async Task Candidate_host_response_reader_accepts_correlated_success_and_refusal_envelopes()
    {
        using var success = await ReadCandidateHostResponseAsync(CandidateHostSuccessJson, processExitCode: 0);
        Assert.Equal("ok", success.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, success.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("captured-workbench-json-v1", success.RootElement.GetProperty("configurationResolution").GetProperty("source").GetString());

        using var refused = await ReadCandidateHostResponseAsync(CandidateHostRefusalJson, processExitCode: 2);
        Assert.Equal("refused", refused.RootElement.GetProperty("status").GetString());
        Assert.Equal("candidate-selection-conflict", refused.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("required-disabled", refused.RootElement.GetProperty("error").GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData("candidate-request-invalid")]
    [InlineData("candidate-request-too-large")]
    [InlineData("candidate-capture-invalid")]
    public async Task Candidate_host_response_reader_admits_correlated_input_refusals(string code)
    {
        var response = JsonNode.Parse(CandidateHostRefusalJson)!.AsObject();
        response["error"] = new JsonObject { ["code"] = code };

        using var refused = await ReadCandidateHostResponseAsync(response.ToJsonString(), processExitCode: 2);

        Assert.Equal(code, refused.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.False(refused.RootElement.TryGetProperty("configurationResolution", out _));
    }

    [Theory]
    [InlineData("candidate-request-invalid", "feature")]
    [InlineData("candidate-request-invalid", "resource")]
    [InlineData("candidate-request-too-large", "feature")]
    [InlineData("candidate-request-too-large", "resource")]
    [InlineData("candidate-capture-invalid", "feature")]
    [InlineData("candidate-capture-invalid", "resource")]
    public async Task Candidate_input_refusals_cannot_carry_target_identities(string code, string field)
    {
        var response = JsonNode.Parse(CandidateHostRefusalJson)!.AsObject();
        response["error"] = new JsonObject { ["code"] = code, [field] = "SafeTarget" };

        await AssertCandidateHostResponseRefusesAsync(response.ToJsonString(), processExitCode: 2);
    }

    [Fact]
    public async Task Candidate_host_response_reader_bounds_actual_bytes_and_refuses_oversize_with_a_safe_code()
    {
        var exactLimitJson = CandidateHostSuccessJson + new string(' ', CandidateHostResponseMaxBytes - Encoding.UTF8.GetByteCount(CandidateHostSuccessJson));
        using var exactLimit = new CountingMemoryStream(Encoding.UTF8.GetBytes(exactLimitJson));
        var response = await ReadCandidateHostResponseElementAsync(exactLimit, processExitCode: 0);
        Assert.Equal("ok", response.GetProperty("status").GetString());
        Assert.Equal(CandidateHostResponseMaxBytes, exactLimit.BytesRead);

        using var oversized = new CountingMemoryStream(new byte[CandidateHostResponseMaxBytes + 2]);
        var refusal = await Assert.ThrowsAsync<WorkerRefusal>(() =>
            ReadCandidateHostResponseElementAsync(oversized, processExitCode: 0));
        Assert.Equal("candidate-response-too-large", refusal.Code);
        Assert.Equal(ToolExitCode.ResolutionFailure, refusal.ExitCode);
        Assert.Equal("The candidate host response exceeds the supported size limit.", refusal.Message);
        Assert.Equal(CandidateHostResponseMaxBytes + 1, oversized.BytesRead);
    }

    [Theory]
    [InlineData("missing-version")]
    [InlineData("wrong-version")]
    [InlineData("missing-invocation")]
    [InlineData("wrong-invocation")]
    [InlineData("wrong-capture")]
    [InlineData("missing-capture")]
    [InlineData("unknown-status")]
    [InlineData("exit-mismatch")]
    [InlineData("ok-with-error")]
    [InlineData("ok-without-resolution")]
    [InlineData("ok-null-resolution")]
    [InlineData("refusal-with-resolution")]
    [InlineData("refusal-without-error")]
    [InlineData("unknown-top-field")]
    [InlineData("wrong-case-top-field")]
    [InlineData("duplicate-top-field")]
    public async Task Candidate_host_response_reader_rejects_invalid_closed_framing(string mutation)
    {
        var response = JsonNode.Parse(CandidateHostSuccessJson)!.AsObject();
        var processExitCode = 0;
        switch (mutation)
        {
            case "missing-version": response.Remove("version"); break;
            case "wrong-version": response["version"] = 2; break;
            case "missing-invocation": response.Remove("invocationId"); break;
            case "wrong-invocation": response["invocationId"] = "33333333333333333333333333333333"; break;
            case "wrong-capture": response["captureId"] = "33333333333333333333333333333333"; break;
            case "missing-capture": response.Remove("captureId"); break;
            case "unknown-status": response["status"] = "partial"; break;
            case "exit-mismatch": processExitCode = 1; break;
            case "ok-with-error": response["error"] = new JsonObject { ["code"] = "resource-context-conflict" }; break;
            case "ok-without-resolution": response.Remove("configurationResolution"); break;
            case "ok-null-resolution": response["configurationResolution"] = null; break;
            case "refusal-with-resolution":
                response["status"] = "refused";
                response["exitCode"] = 2;
                response["error"] = new JsonObject { ["code"] = "resource-context-conflict" };
                processExitCode = 2;
                break;
            case "refusal-without-error":
                response["status"] = "refused";
                response["exitCode"] = 2;
                response.Remove("configurationResolution");
                processExitCode = 2;
                break;
            case "unknown-top-field": response["future"] = true; break;
            case "wrong-case-top-field": response["InvocationID"] = CandidateInvocationId; break;
            case "duplicate-top-field":
                await AssertCandidateHostResponseRefusesAsync(
                    CandidateHostSuccessJson.Replace("\"status\":\"ok\"", "\"status\":\"ok\",\"status\":\"ok\"", StringComparison.Ordinal), processExitCode);
                return;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        await AssertCandidateHostResponseRefusesAsync(response.ToJsonString(), processExitCode);
    }

    [Theory]
    [InlineData("error-unknown-field")]
    [InlineData("error-missing-code")]
    [InlineData("error-unknown-code")]
    [InlineData("error-unknown-reason")]
    [InlineData("error-wrong-case")]
    [InlineData("error-duplicate-field")]
    [InlineData("error-reason-on-other-code")]
    [InlineData("error-unsafe-feature")]
    [InlineData("error-unsafe-resource")]
    public async Task Candidate_host_response_reader_rejects_unclosed_or_unsafe_error_details(string mutation)
    {
        var response = JsonNode.Parse(CandidateHostRefusalJson)!.AsObject();
        var error = response["error"]!.AsObject();
        switch (mutation)
        {
            case "error-unknown-field": error["message"] = "private-canary"; break;
            case "error-missing-code": error.Remove("code"); break;
            case "error-unknown-code": error["code"] = "private-canary"; break;
            case "error-unknown-reason": error["reason"] = "private-canary"; break;
            case "error-wrong-case": error["Code"] = "resource-context-conflict"; break;
            case "error-duplicate-field":
                await AssertCandidateHostResponseRefusesAsync(
                    CandidateHostRefusalJson.Replace("\"code\":\"candidate-selection-conflict\"", "\"code\":\"candidate-selection-conflict\",\"code\":\"candidate-selection-conflict\"", StringComparison.Ordinal), 2);
                return;
            case "error-reason-on-other-code": error["code"] = "resource-context-conflict"; break;
            case "error-unsafe-feature": error["feature"] = "not a feature id"; break;
            case "error-unsafe-resource": error["resource"] = "false"; break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        await AssertCandidateHostResponseRefusesAsync(response.ToJsonString(), processExitCode: 2);
    }

    private static async Task AssertCandidateHostResponseRefusesAsync(string json, int processExitCode)
    {
        var refusal = await Assert.ThrowsAsync<WorkerRefusal>(() => ReadCandidateHostResponseAsync(json, processExitCode));
        Assert.Equal("candidate-response-invalid", refusal.Code);
        Assert.Equal(ToolExitCode.ResolutionFailure, refusal.ExitCode);
        Assert.Equal("The candidate host response is invalid.", refusal.Message);
    }

    private static async Task<JsonDocument> ReadCandidateHostResponseAsync(string json, int processExitCode)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var response = await ReadCandidateHostResponseElementAsync(input, processExitCode);
        return JsonDocument.Parse(response.GetRawText());
    }

    private static async Task<JsonElement> ReadCandidateHostResponseElementAsync(Stream input, int processExitCode)
    {
        return await WorkerContract.ReadCandidateHostResponseAsync(input, CandidateInvocationId,
            CandidateCaptureId, processExitCode, CancellationToken.None);
    }

    private static async Task<WorkerRequest?> ReadCandidateRequestAsync(string json)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return await WorkerContract.ReadCandidateRequestAsync(input, CancellationToken.None);
    }

    private static JsonObject CandidateRequestWithFileSizes(IReadOnlyList<int> fileSizes)
    {
        var request = JsonNode.Parse(CandidateRequestJson)!.AsObject();
        var files = request["candidate"]!["files"]!.AsArray();
        Assert.Equal(files.Count, fileSizes.Count);
        for (var index = 0; index < fileSizes.Count; index++)
            files[index]!["content"] = Convert.ToBase64String(PadJsonObjectToSize(fileSizes[index]));
        return request;
    }

    private static byte[] PadJsonObjectToSize(int size)
    {
        var source = Encoding.UTF8.GetBytes("{}");
        Assert.True(source.Length <= size);
        var result = new byte[size];
        source.CopyTo(result, 0);
        Array.Fill(result, (byte)' ', source.Length, size - source.Length);
        return result;
    }

    private static JsonArray SelectionIds(int count) => new(Enumerable.Range(0, count)
        .Select(index => (JsonNode?)JsonValue.Create($"Feature{index:D4}")).ToArray());

    private sealed class CountingMemoryStream(byte[] buffer) : MemoryStream(buffer)
    {
        public long BytesRead { get; private set; }

        public override async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(destination, cancellationToken);
            BytesRead += read;
            return read;
        }
    }

    [Fact]
    public void Private_version_two_transports_only_context_metadata()
    {
        Assert.Equal(2, WorkerContract.Version);
        var request = new WorkerRequest
        {
            Command = WorkerCommands.List,
            ContextVersion = 1,
            ContextSource = WorkerContextSources.WorkbenchJson,
            Resource = "primary",
            Shell = "default"
        };

        var json = JsonSerializer.Serialize(request, WorkerContract.Json);

        Assert.Contains("\"contextSource\":\"workbench-json-v1\"", json, StringComparison.Ordinal);
        Assert.Contains("\"resource\":\"primary\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("connectionString", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Context_request_rejects_a_legacy_feature_projection_before_host_access()
    {
        var response = await WorkerRunner.RunAsync(new WorkerRequest
        {
            Command = WorkerCommands.List,
            HostDirectory = "/not-a-host",
            HostName = "NotAHost",
            DepsFile = "/not-a-host/NotAHost.deps.json",
            Environment = "Production",
            ContextVersion = 1,
            ContextSource = WorkerContextSources.WorkbenchJson,
            Shell = "default",
            Shells = []
        }, CancellationToken.None);

        Assert.Equal(ToolExitCode.Refusal, response.ExitCode);
        Assert.Equal("invalid-request", response.Error?.Code);
    }

    [Theory]
    [InlineData("{\"version\":1,\"exitCode\":0,\"tooling\":{}}", 0)]
    [InlineData("{\"exitCode\":0,\"tooling\":{}}", 0)]
    [InlineData("{\"version\":2,\"tooling\":{}}", 0)]
    [InlineData("{\"version\":2,\"exitCode\":0,\"tooling\":{},\"tooling\":{}}", 0)]
    [InlineData("{\"version\":2,\"exitCode\":0,\"error\":{\"code\":\"x\",\"message\":\"y\"},\"tooling\":{}}", 0)]
    [InlineData("secret-canary-not-json", 3)]
    public void Front_end_refuses_invalid_worker_responses_without_echoing_them(string response, int exitCode)
    {
        var parse = typeof(WorkerProcess).GetMethod("Parse", BindingFlags.NonPublic | BindingFlags.Static)!;
        var invocation = Assert.Throws<TargetInvocationException>(() => parse.Invoke(null, [response, exitCode]));
        var refusal = Assert.IsType<CliRefusal>(invocation.InnerException);

        Assert.Equal("worker-response-invalid", refusal.Code);
        Assert.DoesNotContain("secret-canary", refusal.ToString(), StringComparison.Ordinal);
    }
}
