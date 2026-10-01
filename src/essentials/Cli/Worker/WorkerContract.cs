using System.Buffers;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Cli.Worker;

/// <summary>
/// The versioned JSON <c>dotnet-elsa</c> exchanges with its worker over stdin/stdout. Two processes, one
/// package: the front end resolves nothing about persistence itself, and the worker answers nothing the
/// front end did not ask for.
/// </summary>
/// <remarks>
/// Candidate-v1 stays file-only. Explicit environment inspection uses a separate closed command and host
/// capability; sharing this transport must not broaden any old command reader or response validator.
/// Deliberately separate from the frozen <c>EfToolingContract</c> the worker speaks on the other side. That
/// one is a contract with an assembly inside the host's closure, versioned independently of this tool; this
/// one is private to the two halves of one package and carries what only the front end knows (where the
/// host is) and what only the worker can see (the host's deps file and package set). The tooling response is
/// passed through verbatim rather than re-modelled, so there is one place that defines its shape.
/// </remarks>
public static class WorkerContract
{
    /// <summary>The only envelope version this build speaks in either direction.</summary>
    public const int Version = 2;

    private const int CandidateRequestMaxBytes = 8 * 1024 * 1024;
    private const int CandidateHostResponseMaxBytes = 4 * 1024 * 1024;
    private const int CandidateJsonMaxDepth = 64;
    private const string CandidateRequestInvalidMessage = "The candidate worker request is invalid.";
    private const string CandidateHostResponseInvalidMessage = "The candidate host response is invalid.";
    private const int CandidateFileMaxBytes = 1024 * 1024;
    private const int CandidateFilesMaxBytes = 4 * 1024 * 1024;
    private static readonly HashSet<string> CandidateEnvironmentRequestFields = new(StringComparer.Ordinal)
    {
        "version", "command", "hostDirectory", "hostName", "depsFile", "packageRoots", "candidate", "environmentInput"
    };
    private static readonly HashSet<string> CandidateEnvironmentInputErrorCodes = new(StringComparer.Ordinal)
    {
        "candidate-environment-input-invalid", "candidate-environment-input-too-large",
        "candidate-environment-key-collision", "candidate-environment-prefix-unsupported"
    };
    private static readonly HashSet<string> CandidateHostResponseFields = new(StringComparer.Ordinal)
    {
        "version", "invocationId", "captureId", "status", "exitCode", "configurationResolution", "error"
    };
    private static readonly HashSet<string> CandidateHostErrorFields = new(StringComparer.Ordinal)
    {
        "code", "reason", "feature", "resource"
    };
    private static readonly HashSet<string> CandidateHostErrorCodes = new(StringComparer.Ordinal)
    {
        "candidate-request-invalid", "candidate-request-too-large", "candidate-capture-invalid",
        "candidate-selection-conflict", "resource-selection-invalid", "resource-not-found",
        "resource-definition-invalid", "resource-configurator-unsupported", "resource-required-feature-disabled",
        "resource-legacy-conflict", "resource-ownership-unresolved", "resource-context-conflict"
    };
    private static readonly HashSet<string> CandidateWorkerResponseFields = new(StringComparer.Ordinal)
    {
        "version", "exitCode", "tooling", "error"
    };
    private static readonly HashSet<string> CandidateWorkerErrorFields = new(StringComparer.Ordinal)
    {
        "code", "message", "details"
    };
    private static readonly HashSet<string> CandidateSelectionConflictReasons = new(StringComparer.Ordinal)
    {
        "unknown", "unavailable", "requested-extra", "requested-missing", "expanded-extra", "required-disabled", "case-collision"
    };
    private static readonly HashSet<string> CandidateConfigurationResolutionFields = new(StringComparer.Ordinal)
    {
        "source", "shell", "environment", "resolution", "selection", "participants", "configuredValueAffinity",
        "targetVerification", "runtimeParity", "packageReachability", "connectivity", "schemaReadiness",
        "migrationReadiness", "activation", "externalInputs", "unresolved"
    };
    private static readonly HashSet<string> CandidateResolutionSelectionFields = new(StringComparer.Ordinal)
    {
        "acceptedFeatureIds", "requestedFeatureIds", "effectiveFeatureIds", "disabledFeatureIds", "implicitFeatureIds"
    };
    private static readonly HashSet<string> CandidateResolutionParticipantFields = new(StringComparer.Ordinal)
    {
        "feature", "module", "selection", "resource", "provider", "connectionReference", "selectorScope",
        "resourceScope", "exactFileProvenance"
    };
    private static readonly HashSet<string> CandidateParticipantSelections = new(StringComparer.Ordinal)
    {
        "Legacy", "ShellBinding", "ShellDefault", "RootDefault"
    };
    private static readonly HashSet<string> CandidateProviders = new(StringComparer.Ordinal)
    {
        "Sqlite", "SqlServer", "PostgreSql", "MySql"
    };
    private static readonly HashSet<string> CandidateScopes = new(StringComparer.Ordinal)
    {
        "root", "shell-composed", "shell-authored", "feature", "unavailable"
    };
    private static readonly HashSet<string> CandidateUnresolvedCodes = new(StringComparer.Ordinal)
    {
        "legacy-target-unprojected", "exact-file-provenance-unavailable", "resource-participant-unenrolled",
        "resource-scope-unsupported"
    };
    private static readonly HashSet<string> CandidatePartialUnresolvedCodes = new(StringComparer.Ordinal)
    {
        "legacy-target-unprojected", "resource-participant-unenrolled", "resource-scope-unsupported"
    };

    /// <summary>
    /// Case-sensitive camelCase with unmapped members refused, matching the tooling contract: a worker and a
    /// front end from different builds must fail loudly rather than silently ignore a field one of them
    /// meant as an instruction.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Reads the private envelope without accepting duplicate or unknown fields.</summary>
    internal static async Task<WorkerRequest?> ReadRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("version", out _) || HasDuplicateFields(document.RootElement))
            throw new JsonException("The worker request is not a closed versioned object.");

        if (document.RootElement.TryGetProperty("candidate", out _) ||
            (document.RootElement.TryGetProperty("command", out var command) &&
             command.ValueKind == JsonValueKind.String && command.GetString() == WorkerCommands.InspectCandidate))
            throw new JsonException("The worker request is not a legacy operation.");

        return document.RootElement.Deserialize<WorkerRequest>(Json);
    }

    /// <summary>Reads the additive candidate operation through its bounded, closed v1 payload parser.</summary>
    public static async Task<WorkerRequest?> ReadCandidateRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadBoundedCandidateJsonAsync(stream, CandidateRequestMaxBytes,
                () => WorkerRefusal.Usage("candidate-request-too-large", "The candidate request exceeds the supported size limit."),
                root =>
                {
                    if (root.ValueKind != JsonValueKind.Object ||
                        !root.TryGetProperty("version", out _) || HasDuplicateFields(root))
                        throw InvalidCandidateRequest();

                    var request = root.Deserialize<WorkerRequest>(Json);
                    if (request is null)
                        throw InvalidCandidateRequest();
                    ValidateCandidateRequest(root, request);
                    return request;
                },
                cancellationToken);
        }
        catch (JsonException)
        {
            throw InvalidCandidateRequest();
        }
    }

    /// <summary>Reads only the additive explicit-environment command; old DTOs remain closed.</summary>
    public static async Task<CandidateEnvironmentWorkerRequestV2> ReadCandidateEnvironmentRequestAsync(
        Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadBoundedCandidateJsonAsync(stream, CandidateRequestMaxBytes,
                () => WorkerRefusal.Usage("candidate-request-too-large", "The candidate request exceeds the supported size limit."),
                root =>
                {
                    if (!HasExactlyFields(root, CandidateEnvironmentRequestFields) || HasDuplicateFields(root))
                        throw InvalidCandidateRequest();
                    var request = root.Deserialize<CandidateEnvironmentWorkerRequestV2>(Json);
                    if (request is null)
                        throw InvalidCandidateRequest();
                    ValidateCandidateEnvironmentRequest(request);
                    return request;
                }, cancellationToken);
        }
        catch (JsonException)
        {
            throw InvalidCandidateRequest();
        }
    }

    /// <summary>
    /// Reads the two candidate commands from one bounded input stream. The command discriminator is inspected
    /// before deserialization so the legacy closed reader remains closed while the additive lane can carry its
    /// one extra envelope field.
    /// </summary>
    internal static async Task<CandidateInspectionRequest?> ReadCandidateInspectionRequestAsync(
        Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadBoundedCandidateJsonAsync(stream, CandidateRequestMaxBytes,
                () => WorkerRefusal.Usage("candidate-request-too-large", "The candidate request exceeds the supported size limit."),
                root =>
                {
                    if (root.ValueKind != JsonValueKind.Object ||
                        !root.TryGetProperty("version", out _) || HasDuplicateFields(root))
                        throw InvalidCandidateRequest();

                    if (root.TryGetProperty("command", out var command) &&
                        command.ValueKind == JsonValueKind.String &&
                        command.GetString() == WorkerCommands.InspectCandidateEnvironment)
                    {
                        var environment = ParseCandidateEnvironmentRequest(root);
                        return new CandidateInspectionRequest(null, environment);
                    }

                    var candidate = root.Deserialize<WorkerRequest>(Json);
                    if (candidate is null)
                        throw InvalidCandidateRequest();
                    ValidateCandidateRequest(root, candidate);
                    return new CandidateInspectionRequest(candidate, null);
                },
                cancellationToken);
        }
        catch (JsonException)
        {
            throw InvalidCandidateRequest();
        }
    }

    /// <summary>Independently rechecks candidate correlation and the original raw private document.</summary>
    public static void ValidateCandidateEnvironmentRequest(CandidateEnvironmentWorkerRequestV2 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Version != Version || request.Command != WorkerCommands.InspectCandidateEnvironment)
            throw InvalidCandidateRequest();
        ValidateCandidateFields(request.FileOnlyClosureRequest());
        ValidateCandidateEnvironmentInput(request.Candidate!, request.EnvironmentInput);
    }

    /// <summary>Validates the inner environment value independently before it crosses the host reflection boundary.</summary>
    internal static void ValidateCandidateEnvironmentInput(
        WorkerCandidatePayload candidate, WorkerEnvironmentInput? environmentInput)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (environmentInput is null || environmentInput.Version != 1 ||
            environmentInput.CaptureId != candidate.CaptureId || environmentInput.Content is null)
            throw InvalidCandidateRequest();

        var bytes = DecodeEnvironmentInput(environmentInput.Content);
        try
        {
            _ = ExplicitEnvironmentInput.Parse(bytes);
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    internal static bool IsCandidateEnvironmentInputErrorCode(string? code) =>
        code is not null && CandidateEnvironmentInputErrorCodes.Contains(code);

    internal static string CandidateEnvironmentInputMessage(string code) =>
        CandidateWorkerErrorMessage(code);

    private static CandidateEnvironmentWorkerRequestV2 ParseCandidateEnvironmentRequest(JsonElement root)
    {
        if (!HasExactlyFields(root, CandidateEnvironmentRequestFields) || HasDuplicateFields(root))
            throw InvalidCandidateRequest();
        var request = root.Deserialize<CandidateEnvironmentWorkerRequestV2>(Json);
        if (request is null)
            throw InvalidCandidateRequest();
        ValidateCandidateEnvironmentRequest(request);
        return request;
    }

    /// <summary>Shared frontend/worker admission of raw supplied bytes; it reads no ambient sources.</summary>
    public static IReadOnlyDictionary<string, string> ParseEnvironmentInputDocument(ReadOnlyMemory<byte> document) =>
        ExplicitEnvironmentInput.Parse(document);

    internal static WorkerRefusal EnvironmentInputRefusal(string code) =>
        WorkerRefusal.Usage(code, CandidateWorkerErrorMessage(code));

    private static byte[] DecodeEnvironmentInput(string content)
    {
        var maximumEncodedLength = ((ExplicitEnvironmentInput.MaximumBytes + 2) / 3) * 4;
        if (content.Length > maximumEncodedLength)
            throw EnvironmentInputRefusal("candidate-environment-input-too-large");
        if (content.Length % 4 != 0)
            throw EnvironmentInputRefusal("candidate-environment-input-invalid");

        // Count padding before allocation: the final base64 block can encode one or two bytes above the raw bound.
        var decodedLength = content.Length / 4 * 3 - (content.EndsWith("==", StringComparison.Ordinal) ? 2 :
            content.EndsWith('=') ? 1 : 0);
        if (decodedLength > ExplicitEnvironmentInput.MaximumBytes)
            throw EnvironmentInputRefusal("candidate-environment-input-too-large");
        var bytes = new byte[Math.Max(0, decodedLength)];
        if (!Convert.TryFromBase64String(content, bytes, out var written) || written != bytes.Length ||
            !StringComparer.Ordinal.Equals(Convert.ToBase64String(bytes), content))
        {
            Array.Clear(bytes);
            throw EnvironmentInputRefusal("candidate-environment-input-invalid");
        }
        return bytes;
    }

    /// <summary>Reads and validates the private candidate host envelope while preserving its original JSON value.</summary>
    public static Task<JsonElement> ReadCandidateHostResponseAsync(
        Stream stream, string expectedInvocationId, string expectedCaptureId, int processExitCode,
        CancellationToken cancellationToken) =>
        ReadCandidateHostResponseCoreAsync(stream, expectedInvocationId, expectedCaptureId,
            processExitCode, environmentInput: false, cancellationToken);

    /// <summary>Reads only the explicit-environment projection and its correlated fixed refusals.</summary>
    public static Task<JsonElement> ReadCandidateEnvironmentHostResponseAsync(
        Stream stream, string expectedInvocationId, string expectedCaptureId, int processExitCode,
        CancellationToken cancellationToken) =>
        ReadCandidateHostResponseCoreAsync(stream, expectedInvocationId, expectedCaptureId,
            processExitCode, environmentInput: true, cancellationToken);

    private static async Task<JsonElement> ReadCandidateHostResponseCoreAsync(
        Stream stream, string expectedInvocationId, string expectedCaptureId, int processExitCode,
        bool environmentInput, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadBoundedCandidateJsonAsync(stream, CandidateHostResponseMaxBytes,
                () => WorkerRefusal.Resolution("candidate-response-too-large", "The candidate host response exceeds the supported size limit."),
                root =>
                {
                    ValidateCandidateHostResponse(root, expectedInvocationId, expectedCaptureId, processExitCode, environmentInput);
                    return root.Clone();
                },
                cancellationToken);
        }
        catch (JsonException)
        {
            throw InvalidCandidateHostResponse();
        }
    }

    /// <summary>Parses the bounded private worker response returned by a candidate inspection process.</summary>
    public static WorkerResponse ParseCandidateWorkerResponse(
        ReadOnlyMemory<byte> utf8Response, WorkerCandidatePayload expectedCandidate, int processExitCode) =>
        ParseCandidateWorkerResponseCore(utf8Response, expectedCandidate, processExitCode, environmentInput: false);

    /// <summary>Parses the additive worker lane without widening candidate-v1 response admission.</summary>
    public static WorkerResponse ParseCandidateEnvironmentWorkerResponse(
        ReadOnlyMemory<byte> utf8Response, WorkerCandidatePayload expectedCandidate, int processExitCode) =>
        ParseCandidateWorkerResponseCore(utf8Response, expectedCandidate, processExitCode, environmentInput: true);

    private static WorkerResponse ParseCandidateWorkerResponseCore(
        ReadOnlyMemory<byte> utf8Response, WorkerCandidatePayload expectedCandidate, int processExitCode, bool environmentInput)
    {
        ArgumentNullException.ThrowIfNull(expectedCandidate);
        if (utf8Response.Length > CandidateHostResponseMaxBytes)
            throw WorkerRefusal.Resolution("candidate-response-too-large", "The candidate host response exceeds the supported bound.");

        try
        {
            ValidateCandidatePayload(expectedCandidate);
        }
        catch (JsonException)
        {
            throw InvalidCandidateHostResponse();
        }

        try
        {
            using var document = JsonDocument.Parse(utf8Response,
                new JsonDocumentOptions { MaxDepth = CandidateJsonMaxDepth });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || HasDuplicateFields(root) ||
                root.EnumerateObject().Any(property => !CandidateWorkerResponseFields.Contains(property.Name)) ||
                !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var versionValue) || versionValue != Version ||
                !root.TryGetProperty("exitCode", out var exitCode) || exitCode.ValueKind != JsonValueKind.Number ||
                !exitCode.TryGetInt32(out var exitCodeValue) || exitCodeValue != processExitCode)
                throw InvalidCandidateHostResponse();

            var hasTooling = root.TryGetProperty("tooling", out var tooling) && tooling.ValueKind != JsonValueKind.Null;
            var hasError = root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null;
            if (hasTooling == hasError)
                throw InvalidCandidateHostResponse();

            if (hasTooling)
            {
                ValidateCandidateHostResponseCore(tooling, expectedCandidate, processExitCode, environmentInput);
                return new WorkerResponse
                {
                    Version = Version,
                    ExitCode = exitCodeValue,
                    Tooling = tooling.Clone()
                };
            }

            var code = ReadCandidateWorkerErrorCode(error, exitCodeValue, environmentInput);
            return new WorkerResponse
            {
                Version = Version,
                ExitCode = exitCodeValue,
                Error = new WorkerError { Code = code, Message = CandidateWorkerErrorMessage(code) }
            };
        }
        catch (WorkerRefusal)
        {
            throw;
        }
        catch (JsonException)
        {
            throw InvalidCandidateHostResponse();
        }
    }

    private static string ReadCandidateWorkerErrorCode(JsonElement error, int exitCode, bool environmentInput)
    {
        if (error.ValueKind != JsonValueKind.Object || HasDuplicateFields(error) ||
            error.EnumerateObject().Any(property => !CandidateWorkerErrorFields.Contains(property.Name)) ||
            !error.TryGetProperty("code", out var codeElement) || codeElement.ValueKind != JsonValueKind.String)
            throw InvalidCandidateHostResponse();

        if (error.TryGetProperty("message", out var message) && message.ValueKind != JsonValueKind.String)
            throw InvalidCandidateHostResponse();
        if (error.TryGetProperty("details", out var details) &&
            (details.ValueKind != JsonValueKind.Array || details.GetArrayLength() != 0))
            throw InvalidCandidateHostResponse();

        var code = codeElement.GetString()!;
        var isUsageRefusal = code is "candidate-request-invalid" or "candidate-request-too-large" ||
            environmentInput && CandidateEnvironmentInputErrorCodes.Contains(code);
        var isResolutionRefusal = code is "candidate-host-unavailable" or "candidate-package-unavailable" or "candidate-closure-changed" or
            "candidate-capability-unavailable" or "candidate-response-invalid" or "candidate-response-too-large" ||
            environmentInput && code == "candidate-environment-host-unenrolled";
        if (isUsageRefusal && exitCode == ToolExitCode.Refusal ||
            isResolutionRefusal && exitCode == ToolExitCode.ResolutionFailure)
            return code;

        throw InvalidCandidateHostResponse();
    }

    private static string CandidateWorkerErrorMessage(string code) => code switch
    {
        "candidate-request-invalid" => CandidateRequestInvalidMessage,
        "candidate-request-too-large" => "The candidate host request exceeds the supported bound.",
        "candidate-host-unavailable" => "The selected installed host closure could not be inspected.",
        "candidate-package-unavailable" => "The selected host package closure could not be loaded.",
        "candidate-closure-changed" => "The selected installed host closure changed during inspection.",
        "candidate-capability-unavailable" => "The selected host has no complete candidate inspection capability.",
        "candidate-response-invalid" => CandidateHostResponseInvalidMessage,
        "candidate-response-too-large" => "The candidate host response exceeds the supported bound.",
        "candidate-environment-input-invalid" => "The explicit environment input is invalid.",
        "candidate-environment-input-too-large" => "The explicit environment input exceeds the supported size limit.",
        "candidate-environment-key-collision" => "The explicit environment input contains colliding keys.",
        "candidate-environment-prefix-unsupported" => "The explicit environment input contains an unsupported service prefix.",
        "candidate-environment-host-unenrolled" => "The selected host is not enrolled for explicit environment inspection.",
        _ => throw InvalidCandidateHostResponse()
    };

    private static async Task<T> ReadBoundedCandidateJsonAsync<T>(
        Stream stream,
        int maxBytes,
        Func<WorkerRefusal> oversizedRefusal,
        Func<JsonElement, T> parse,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(maxBytes + 1);
        try
        {
            var length = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await stream.ReadAsync(buffer.AsMemory(length, maxBytes + 1 - length), cancellationToken);
                if (read == 0)
                    break;
                length += read;
                if (length > maxBytes)
                    throw oversizedRefusal();
            }

            using var document = JsonDocument.Parse(buffer.AsMemory(0, length),
                new JsonDocumentOptions { MaxDepth = CandidateJsonMaxDepth });
            return parse(document.RootElement);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    /// <summary>Validates the full host exchange against the candidate whose bytes were sent.</summary>
    /// <exception cref="WorkerRefusal">The host exchange does not describe that candidate.</exception>
    public static void ValidateCandidateHostResponse(
        JsonElement root, WorkerCandidatePayload candidate, int processExitCode) =>
        ValidateCandidateHostResponseCore(root, candidate, processExitCode, environmentInput: false);

    /// <summary>Validates the new lane against the same immutable candidate/context and exact projection.</summary>
    public static void ValidateCandidateEnvironmentHostResponse(
        JsonElement root, WorkerCandidatePayload candidate, int processExitCode) =>
        ValidateCandidateHostResponseCore(root, candidate, processExitCode, environmentInput: true);

    private static void ValidateCandidateHostResponseCore(
        JsonElement root, WorkerCandidatePayload candidate, int processExitCode, bool environmentInput)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        try
        {
            ValidateCandidatePayload(candidate);
        }
        catch (JsonException)
        {
            throw InvalidCandidateHostResponse();
        }
        ValidateCandidateHostResponse(root, candidate.InvocationId ?? string.Empty,
            candidate.CaptureId ?? string.Empty, processExitCode, environmentInput);

        if (root.GetProperty("status").GetString() != "ok")
            return;

        var resolution = root.GetProperty("configurationResolution");
        var selection = resolution.GetProperty("selection");
        var accepted = selection.GetProperty("acceptedFeatureIds").EnumerateArray().Select(id => id.GetString()!);
        var disabled = selection.GetProperty("disabledFeatureIds").EnumerateArray()
            .Select(id => id.GetString()!).ToHashSet(StringComparer.Ordinal);
        if (!HasStringValue(resolution, "shell", candidate.Shell!) ||
            !HasStringValue(resolution, "environment", candidate.Environment!) ||
            !accepted.SequenceEqual(candidate.AcceptedFeatureIds!, StringComparer.Ordinal) ||
            candidate.RemovedFeatureIds!.Any(id => !disabled.Contains(id)))
            throw InvalidCandidateHostResponse();
    }

    private static void ValidateCandidateHostResponse(
        JsonElement root,
        string expectedInvocationId,
        string expectedCaptureId,
        int processExitCode,
        bool environmentInput)
    {
        if (!IsToken(expectedInvocationId) || !IsToken(expectedCaptureId) ||
            string.Equals(expectedInvocationId, expectedCaptureId, StringComparison.Ordinal) ||
            root.ValueKind != JsonValueKind.Object || HasDuplicateFields(root) ||
            root.EnumerateObject().Any(property => !CandidateHostResponseFields.Contains(property.Name)) ||
            !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number ||
            !version.TryGetInt32(out var versionValue) || versionValue != 1 ||
            !root.TryGetProperty("invocationId", out var invocationId) || invocationId.ValueKind != JsonValueKind.String ||
            invocationId.GetString() != expectedInvocationId ||
            !root.TryGetProperty("captureId", out var captureId) || captureId.ValueKind != JsonValueKind.String ||
            captureId.GetString() != expectedCaptureId ||
            !root.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("exitCode", out var exitCode) || exitCode.ValueKind != JsonValueKind.Number ||
            !exitCode.TryGetInt32(out var exitCodeValue) || exitCodeValue != processExitCode)
            throw InvalidCandidateHostResponse();

        var statusValue = status.GetString();
        var hasResolution = root.TryGetProperty("configurationResolution", out var resolution);
        var hasError = root.TryGetProperty("error", out var error);
        if (statusValue == "ok")
        {
            if (processExitCode != ToolExitCode.Success || !hasResolution || hasError ||
                resolution.ValueKind != JsonValueKind.Object)
                throw InvalidCandidateHostResponse();
            ValidateCandidateConfigurationResolution(resolution, environmentInput);
            return;
        }

        var unenrolled = environmentInput && error.ValueKind == JsonValueKind.Object &&
            HasStringValue(error, "code", "candidate-environment-host-unenrolled");
        var expectedRefusalExit = unenrolled ? ToolExitCode.ResolutionFailure : ToolExitCode.Refusal;
        if (statusValue != "refused" || processExitCode != expectedRefusalExit ||
            hasResolution || !hasError || error.ValueKind != JsonValueKind.Object)
            throw InvalidCandidateHostResponse();

        ValidateCandidateHostError(error, environmentInput);
    }

    private static void ValidateCandidateConfigurationResolution(JsonElement resolution, bool environmentInput)
    {
        if (!HasExactlyFields(resolution, CandidateConfigurationResolutionFields) ||
            !HasStringValue(resolution, "source", environmentInput ? "captured-workbench-json-explicit-environment-v1" : "captured-workbench-json-v1") ||
            !TryGetString(resolution, "shell", out var shell) || !IsSafeCandidateReference(shell) ||
            !TryGetString(resolution, "environment", out var environment) || !IsSafeEnvironment(environment) ||
            !TryGetString(resolution, "resolution", out var resolutionValue) ||
            resolutionValue is not ("resolved" or "partial") ||
            !TryGetString(resolution, "configuredValueAffinity", out var affinity) ||
            affinity is not ("checked" or "not-applicable") ||
            !HasStringValue(resolution, "targetVerification", "not-performed") ||
            !HasStringValue(resolution, "runtimeParity", "unobserved") ||
            !HasStringValue(resolution, "packageReachability", "unverified") ||
            !HasStringValue(resolution, "connectivity", "unverified") ||
            !HasStringValue(resolution, "schemaReadiness", "unverified") ||
            !HasStringValue(resolution, "migrationReadiness", "unverified") ||
            !HasStringValue(resolution, "activation", "unobserved") ||
            !HasStringValue(resolution, "externalInputs", environmentInput ? "supplied-intended" : "unverified") ||
            !resolution.TryGetProperty("selection", out var selection) ||
            !TryValidateCandidateResolutionSelection(selection, out var acceptedFeatureIds) ||
            !resolution.TryGetProperty("participants", out var participants) || participants.ValueKind != JsonValueKind.Array ||
            !resolution.TryGetProperty("unresolved", out var unresolvedElement) ||
            !TryReadSortedUnresolvedCodes(unresolvedElement, out var unresolvedCodes) ||
            participants.GetArrayLength() > 1024 - unresolvedCodes.Length)
            throw InvalidCandidateHostResponse();

        var accepted = new HashSet<string>(acceptedFeatureIds, StringComparer.Ordinal);
        var hasLegacyParticipant = false;
        var hasResourceParticipant = false;
        var hasUnavailableResourceScope = false;
        string? previousFeature = null;
        string? previousModule = null;
        foreach (var participant in participants.EnumerateArray())
        {
            if (!TryValidateCandidateParticipant(participant, accepted, out var feature, out var module,
                    out var legacy, out var unavailableResourceScope))
                throw InvalidCandidateHostResponse();

            if (previousFeature is not null)
            {
                var order = StringComparer.Ordinal.Compare(previousFeature, feature);
                if (order > 0 || (order == 0 && StringComparer.Ordinal.Compare(previousModule, module) >= 0))
                    throw InvalidCandidateHostResponse();
            }
            previousFeature = feature;
            previousModule = module;
            hasLegacyParticipant |= legacy;
            hasResourceParticipant |= !legacy;
            hasUnavailableResourceScope |= unavailableResourceScope;
        }

        var unresolved = new HashSet<string>(unresolvedCodes, StringComparer.Ordinal);
        var hasExactFileProvenanceUnavailable = unresolved.Contains("exact-file-provenance-unavailable");
        if ((participants.GetArrayLength() > 0) != hasExactFileProvenanceUnavailable ||
            hasLegacyParticipant != unresolved.Contains("legacy-target-unprojected") ||
            (affinity == "checked") != hasResourceParticipant ||
            hasUnavailableResourceScope && !unresolved.Contains("resource-scope-unsupported"))
            throw InvalidCandidateHostResponse();

        var needsPartial = unresolvedCodes.Any(CandidatePartialUnresolvedCodes.Contains);
        if ((resolutionValue == "partial") != needsPartial)
            throw InvalidCandidateHostResponse();
    }

    private static bool TryValidateCandidateResolutionSelection(JsonElement selection, out string[] acceptedFeatureIds)
    {
        acceptedFeatureIds = [];
        if (!HasExactlyFields(selection, CandidateResolutionSelectionFields) ||
            !selection.TryGetProperty("acceptedFeatureIds", out var acceptedElement) ||
            !TryReadSortedCandidateIds(acceptedElement, out var accepted) ||
            !selection.TryGetProperty("requestedFeatureIds", out var requestedElement) ||
            !TryReadSortedCandidateIds(requestedElement, out var requested) ||
            !selection.TryGetProperty("effectiveFeatureIds", out var effectiveElement) ||
            !TryReadSortedCandidateIds(effectiveElement, out var effective) ||
            !selection.TryGetProperty("disabledFeatureIds", out var disabledElement) ||
            !TryReadSortedCandidateIds(disabledElement, out var disabled) ||
            !selection.TryGetProperty("implicitFeatureIds", out var implicitElement) ||
            !TryReadSortedCandidateIds(implicitElement, out var implicitIds) ||
            !accepted.SequenceEqual(requested, StringComparer.Ordinal) ||
            !accepted.SequenceEqual(effective, StringComparer.Ordinal) || implicitIds.Length != 0 ||
            disabled.Intersect(accepted, StringComparer.OrdinalIgnoreCase).Any())
            return false;

        acceptedFeatureIds = accepted;
        return true;
    }

    private static bool TryValidateCandidateParticipant(
        JsonElement participant,
        HashSet<string> acceptedFeatureIds,
        out string feature,
        out string module,
        out bool legacy,
        out bool unavailableResourceScope)
    {
        feature = string.Empty;
        module = string.Empty;
        legacy = false;
        unavailableResourceScope = false;
        if (!HasExactlyFields(participant, CandidateResolutionParticipantFields) ||
            !TryGetString(participant, "feature", out feature) || !IsSafeCandidateReference(feature) ||
            !acceptedFeatureIds.Contains(feature) ||
            !TryGetString(participant, "module", out module) || !IsSafeCandidateReference(module) ||
            !TryGetString(participant, "selection", out var selection) || !CandidateParticipantSelections.Contains(selection) ||
            !TryGetNullableString(participant, "resource", out var resource) ||
            !TryGetNullableString(participant, "provider", out var provider) ||
            !TryGetNullableString(participant, "connectionReference", out var connectionReference) ||
            !TryGetNullableString(participant, "selectorScope", out var selectorScope) ||
            !TryGetNullableString(participant, "resourceScope", out var resourceScope) ||
            !HasStringValue(participant, "exactFileProvenance", "unavailable"))
            return false;

        if (resource is not null && !IsSafePublicResourceIdentity(resource) ||
            connectionReference is not null && !IsSafePublicResourceIdentity(connectionReference) ||
            provider is not null && !CandidateProviders.Contains(provider) ||
            selectorScope is not null && !CandidateScopes.Contains(selectorScope) ||
            resourceScope is not null && !CandidateScopes.Contains(resourceScope))
            return false;

        legacy = selection == "Legacy";
        if (legacy)
        {
            if (resource is not null || provider is not null || connectionReference is not null || resourceScope is not null)
                return false;
        }
        else if (resource is null || provider is null || connectionReference is null)
            return false;

        unavailableResourceScope = resourceScope == "unavailable";
        return true;
    }

    private static bool TryReadSortedCandidateIds(JsonElement element, out string[] values)
    {
        values = [];
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() > 4096)
            return false;

        var result = new string[element.GetArrayLength()];
        var caseInsensitive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? previous = null;
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                return false;
            var value = item.GetString();
            if (!IsSafeCandidateReference(value) || !caseInsensitive.Add(value!) ||
                previous is not null && StringComparer.Ordinal.Compare(previous, value) >= 0)
                return false;
            result[index++] = value!;
            previous = value;
        }

        values = result;
        return true;
    }

    private static bool TryReadSortedUnresolvedCodes(JsonElement element, out string[] values)
    {
        values = [];
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() > 1024)
            return false;

        var result = new string[element.GetArrayLength()];
        string? previous = null;
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                return false;
            var value = item.GetString();
            if (value is null || !CandidateUnresolvedCodes.Contains(value) ||
                previous is not null && StringComparer.Ordinal.Compare(previous, value) >= 0)
                return false;
            result[index++] = value;
            previous = value;
        }

        values = result;
        return true;
    }

    private static bool HasExactlyFields(JsonElement value, HashSet<string> expectedFields) =>
        value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Count() == expectedFields.Count &&
        value.EnumerateObject().All(property => expectedFields.Contains(property.Name));

    private static bool HasStringValue(JsonElement value, string name, string expected) =>
        TryGetString(value, name, out var actual) && actual == expected;

    private static bool TryGetString(JsonElement value, string name, out string result)
    {
        result = string.Empty;
        if (!value.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
            return false;
        result = property.GetString()!;
        return true;
    }

    private static bool TryGetNullableString(JsonElement value, string name, out string? result)
    {
        result = null;
        if (!value.TryGetProperty(name, out var property))
            return false;
        if (property.ValueKind == JsonValueKind.Null)
            return true;
        if (property.ValueKind != JsonValueKind.String)
            return false;
        result = property.GetString();
        return result is not null;
    }

    private static void ValidateCandidateHostError(JsonElement error, bool environmentInput)
    {
        if (HasDuplicateFields(error) ||
            error.EnumerateObject().Any(property => !CandidateHostErrorFields.Contains(property.Name)) ||
            !error.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.String ||
            !(CandidateHostErrorCodes.Contains(code.GetString() ?? string.Empty) ||
              environmentInput && (CandidateEnvironmentInputErrorCodes.Contains(code.GetString() ?? string.Empty) ||
                                   code.GetString() == "candidate-environment-host-unenrolled")))
            throw InvalidCandidateHostResponse();

        var codeValue = code.GetString();
        if ((codeValue is "candidate-request-invalid" or "candidate-request-too-large" or "candidate-capture-invalid" ||
             environmentInput && (CandidateEnvironmentInputErrorCodes.Contains(codeValue!) ||
                                  codeValue == "candidate-environment-host-unenrolled")) &&
            error.EnumerateObject().Count() != 1)
            throw InvalidCandidateHostResponse();
        var hasReason = error.TryGetProperty("reason", out var reason);
        if (hasReason && (codeValue != "candidate-selection-conflict" || reason.ValueKind != JsonValueKind.String ||
            !CandidateSelectionConflictReasons.Contains(reason.GetString() ?? string.Empty)))
            throw InvalidCandidateHostResponse();

        foreach (var identityName in new[] { "feature", "resource" })
        {
            if (error.TryGetProperty(identityName, out var identity) &&
                (identity.ValueKind != JsonValueKind.String ||
                 !(identityName == "feature"
                     ? IsSafeCandidateReference(identity.GetString())
                     : IsSafePublicResourceIdentity(identity.GetString()))))
                throw InvalidCandidateHostResponse();
        }
    }

    private static bool IsSafePublicResourceIdentity(string? value) => value is { Length: > 0 and <= 128 } &&
        (char.IsAsciiLetter(value[0]) || value[0] == '_') &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.') &&
        !string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(value, "null", StringComparison.OrdinalIgnoreCase);

    private static WorkerRefusal InvalidCandidateHostResponse() => WorkerRefusal.Resolution(
        "candidate-response-invalid", CandidateHostResponseInvalidMessage);

    private static void ValidateCandidateRequest(JsonElement root, WorkerRequest request)
    {
        ValidateCandidateFields(request);
        if (!root.TryGetProperty("hostDirectory", out _) || !root.TryGetProperty("hostName", out _) ||
            !root.TryGetProperty("depsFile", out _) || !root.TryGetProperty("packageRoots", out _) ||
            !root.TryGetProperty("candidate", out _))
            throw InvalidCandidateRequest();
    }

    /// <summary>Rechecks candidate-only admission before any installed-closure operation.</summary>
    /// <exception cref="WorkerRefusal">The request is not an admitted file-only candidate operation.</exception>
    public static void ValidateCandidateRequest(WorkerRequest request)
    {
        try { ValidateCandidateFields(request); }
        catch (JsonException)
        { throw WorkerRefusal.Usage("candidate-request-invalid", CandidateRequestInvalidMessage); }
    }

    private static void ValidateCandidateFields(WorkerRequest request)
    {
        if (request is null)
            throw InvalidCandidateRequest();
        var candidate = request.Candidate;
        if (request.Version != Version || request.Command != WorkerCommands.InspectCandidate ||
            string.IsNullOrWhiteSpace(request.HostDirectory) || string.IsNullOrWhiteSpace(request.HostName) ||
            string.IsNullOrWhiteSpace(request.DepsFile) || request.PackageRoots is null ||
            request.PackageRoots.Any(string.IsNullOrWhiteSpace) || candidate is null || request.Restore ||
            request.Selection is not null || request.Provider is not null || request.Schema is not null ||
            request.Output is not null || request.Environment is not null || request.Shell is not null ||
            request.ContextSource is not null || request.ContextVersion is not null || request.Resource is not null ||
            request.Shells is not null || request.ConnectionEnv is not null || request.Connection is not null ||
            request.Finalization is not null || request.SkewAllowance is not null ||
            request.SqliteMigrationLockStaleAfter is not null)
            throw InvalidCandidateRequest();

        ValidateCandidatePayload(candidate);
    }

    private static void ValidateCandidatePayload(WorkerCandidatePayload candidate)
    {
        if (candidate.Version != 1 || candidate.Source != "captured-workbench-json-v1" ||
            !IsToken(candidate.InvocationId) || !IsToken(candidate.CaptureId) ||
            string.Equals(candidate.InvocationId, candidate.CaptureId, StringComparison.Ordinal) ||
            !IsSafeCandidateReference(candidate.Shell) || !IsSafeEnvironment(candidate.Environment) ||
            candidate.Files is null ||
            !IsValidCandidateSelection(candidate.AcceptedFeatureIds, candidate.RemovedFeatureIds) ||
            !IsOrdinallySorted(candidate.AcceptedFeatureIds!) || !IsOrdinallySorted(candidate.RemovedFeatureIds!))
            throw InvalidCandidateRequest();

        var expectedNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "appsettings.json", "shells.json", $"shells.{candidate.Environment}.json"
        };
        var optionalAppsettingsOverlay = $"appsettings.{candidate.Environment}.json";
        if (candidate.Files.Count == 4)
            expectedNames.Add(optionalAppsettingsOverlay);
        if (candidate.Files.Count is not (3 or 4))
            throw InvalidCandidateRequest();

        var actualNames = new HashSet<string>(StringComparer.Ordinal);
        var totalBytes = 0;
        foreach (var file in candidate.Files)
        {
            if (file is null || file.CaptureId != candidate.CaptureId || file.Content is null ||
                !expectedNames.Contains(file.Name ?? string.Empty) || !actualNames.Add(file.Name!) ||
                !TryValidateCanonicalBase64(file.Content, ref totalBytes))
                throw InvalidCandidateRequest();
        }

        if (!actualNames.SetEquals(expectedNames))
            throw InvalidCandidateRequest();
    }

    /// <summary>Shared frontend/worker admission for finite, safe, case-unambiguous selection identities.</summary>
    /// <remarks>Authored order is unrestricted; the wire reader additionally requires ordinal sorting.</remarks>
    public static bool IsValidCandidateSelection(IReadOnlyList<string>? accepted, IReadOnlyList<string>? removed) =>
        accepted is not null && removed is not null && accepted.Count <= 4096 && removed.Count <= 4096 &&
        accepted.All(IsSafeCandidateReference) && removed.All(IsSafeCandidateReference) &&
        accepted.Distinct(StringComparer.OrdinalIgnoreCase).Count() == accepted.Count &&
        removed.Distinct(StringComparer.OrdinalIgnoreCase).Count() == removed.Count &&
        !accepted.Intersect(removed, StringComparer.OrdinalIgnoreCase).Any();

    private static bool IsOrdinallySorted(IReadOnlyList<string> values)
    {
        string? previous = null;
        foreach (var value in values)
        {
            if (previous is not null && StringComparer.Ordinal.Compare(previous, value) >= 0)
                return false;
            previous = value;
        }
        return true;
    }

    private static bool TryValidateCanonicalBase64(string value, ref int totalBytes)
    {
        var maxEncodedBytes = ((CandidateFileMaxBytes + 2) / 3) * 4;
        if (value.Length > maxEncodedBytes || value.Length % 4 != 0)
            return false;

        var decoded = new byte[Math.Min(CandidateFileMaxBytes, value.Length / 4 * 3)];
        if (!Convert.TryFromBase64String(value, decoded, out var written) ||
            written > CandidateFileMaxBytes || totalBytes > CandidateFilesMaxBytes - written ||
            !string.Equals(Convert.ToBase64String(decoded.AsSpan(0, written)), value, StringComparison.Ordinal))
            return false;
        totalBytes += written;
        return true;
    }

    private static bool IsToken(string? value) => value is { Length: 32 } &&
        value.All(ch => ch is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsSafeCandidateReference(string? value) => value is { Length: > 0 and <= 128 } &&
        value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-' or '/' or '+');

    private static bool IsSafeEnvironment(string? value) => value is { Length: > 0 and <= 128 } &&
        value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-');

    private static JsonException InvalidCandidateRequest() => new(CandidateRequestInvalidMessage);

    public static bool HasDuplicateFields(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
            return value.EnumerateArray().Any(HasDuplicateFields);
        if (value.ValueKind != JsonValueKind.Object)
            return false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in value.EnumerateObject())
            if (!names.Add(field.Name) || HasDuplicateFields(field.Value))
                return true;
        return false;
    }
}

/// <summary>Admits the one explicit private environment overlay without consulting ambient configuration.</summary>
internal static class ExplicitEnvironmentInput
{
    internal const int MaximumBytes = 1_048_576;
    internal const int MaximumEntries = 1_024;
    internal const int MaximumKeyBytes = 1_024;
    internal const int MaximumValueBytes = 65_536;

    private const int JsonMaximumDepth = 64;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        MaxDepth = JsonMaximumDepth,
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow
    };
    private static readonly string[] UnsupportedServicePrefixes =
    [
        "MYSQLCONNSTR_",
        "SQLAZURECONNSTR_",
        "SQLCONNSTR_",
        "CUSTOMCONNSTR_",
        "POSTGRESQLCONNSTR_",
        "APIHUBCONNSTR_",
        "DOCDBCONNSTR_",
        "EVENTHUBCONNSTR_",
        "NOTIFICATIONHUBCONNSTR_",
        "REDISCACHECONNSTR_",
        "SERVICEBUSCONNSTR_"
    ];

    internal static IReadOnlyDictionary<string, string> Parse(ReadOnlyMemory<byte> document)
    {
        if (document.Length > MaximumBytes)
            throw TooLarge();

        try
        {
            var utf8 = document.Span;
            if (utf8.StartsWith("\uFEFF"u8))
            {
                utf8 = utf8[3..];
                if (utf8.StartsWith("\uFEFF"u8))
                    throw Invalid();
            }

            var json = StrictUtf8.GetString(utf8);
            if (json.StartsWith('\uFEFF'))
                throw Invalid();

            using var parsed = JsonDocument.Parse(json, JsonOptions);
            return ParseRoot(parsed.RootElement);
        }
        catch (WorkerRefusal)
        {
            throw;
        }
        catch (DecoderFallbackException)
        {
            throw Invalid();
        }
        catch (JsonException)
        {
            throw Invalid();
        }
        catch (EncoderFallbackException)
        {
            throw Invalid();
        }
        catch (InvalidOperationException)
        {
            // A malformed escaped Unicode scalar may be refused by JsonElement.GetString itself.
            throw Invalid();
        }
    }

    private static IReadOnlyDictionary<string, string> ParseRoot(JsonElement root)
    {
        ReadClosedPair(root, "version", "entries", out var version, out var entries);
        if (version.ValueKind != JsonValueKind.Number || version.GetRawText() != "1" ||
            entries.ValueKind != JsonValueKind.Array)
            throw Invalid();

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var count = 0;
        foreach (var entry in entries.EnumerateArray())
        {
            if (++count > MaximumEntries)
                throw TooLarge();
            ParseEntry(entry, values);
        }

        return new ReadOnlyDictionary<string, string>(values);
    }

    private static void ParseEntry(JsonElement entry, IDictionary<string, string> values)
    {
        ReadClosedPair(entry, "key", "value", out var keyElement, out var valueElement);
        if (keyElement.ValueKind != JsonValueKind.String || valueElement.ValueKind != JsonValueKind.String)
            throw Invalid();

        var key = keyElement.GetString() ?? throw Invalid();
        var value = valueElement.GetString() ?? throw Invalid();
        ValidateString(keyElement, key, key: true);
        ValidateString(valueElement, value, key: false);

        if (StrictUtf8.GetByteCount(key) > MaximumKeyBytes || StrictUtf8.GetByteCount(value) > MaximumValueBytes)
            throw TooLarge();

        if (UnsupportedServicePrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            throw PrefixUnsupported();

        var normalized = key.Replace("__", ":", StringComparison.Ordinal);
        if (StrictUtf8.GetByteCount(normalized) > MaximumKeyBytes)
            throw TooLarge();

        if (!values.TryAdd(normalized, value))
            throw KeyCollision();
    }

    private static void ReadClosedPair(JsonElement value, string firstName, string secondName,
        out JsonElement first, out JsonElement second)
    {
        first = second = default;
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != 2 ||
            !value.TryGetProperty(firstName, out first) || !value.TryGetProperty(secondName, out second))
            throw Invalid();
    }

    private static void ValidateString(JsonElement element, string value, bool key)
    {
        if (key && value.Length == 0)
            throw Invalid();
        var raw = element.GetRawText();
        for (var index = 1; index < raw.Length - 1; index++)
        {
            if (raw[index] != '\\')
                continue;

            if (raw[index + 1] != 'u')
            {
                index++;
                continue;
            }

            if (!TryReadUnicodeEscape(raw, index, out var codeUnit))
                throw Invalid();
            if (char.IsHighSurrogate(codeUnit))
            {
                if (!TryReadUnicodeEscape(raw, index + 6, out var low) || !char.IsLowSurrogate(low))
                    throw Invalid();
                index += 11;
            }
            else if (char.IsLowSurrogate(codeUnit))
            {
                throw Invalid();
            }
            else
            {
                index += 5;
            }
        }

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                    throw Invalid();
                index++;
                continue;
            }

            if (char.IsLowSurrogate(character) || key && char.IsControl(character) || key && character == '=' ||
                !key && character == '\0')
                throw Invalid();
        }
    }

    private static bool TryReadUnicodeEscape(string raw, int slash, out char codeUnit)
    {
        codeUnit = default;
        if (slash < 0 || slash + 5 >= raw.Length || raw[slash] != '\\' || raw[slash + 1] != 'u')
            return false;

        var value = 0;
        for (var index = slash + 2; index <= slash + 5; index++)
        {
            var digit = raw[index] switch
            {
                >= '0' and <= '9' => raw[index] - '0',
                >= 'a' and <= 'f' => raw[index] - 'a' + 10,
                >= 'A' and <= 'F' => raw[index] - 'A' + 10,
                _ => -1
            };
            if (digit < 0)
                return false;
            value = (value << 4) | digit;
        }

        codeUnit = (char)value;
        return true;
    }

    private static WorkerRefusal Invalid() =>
        WorkerContract.EnvironmentInputRefusal("candidate-environment-input-invalid");

    private static WorkerRefusal TooLarge() =>
        WorkerContract.EnvironmentInputRefusal("candidate-environment-input-too-large");

    private static WorkerRefusal KeyCollision() =>
        WorkerContract.EnvironmentInputRefusal("candidate-environment-key-collision");

    private static WorkerRefusal PrefixUnsupported() =>
        WorkerContract.EnvironmentInputRefusal("candidate-environment-prefix-unsupported");
}

/// <summary>The commands this worker backs.</summary>
public static class WorkerCommands
{
    public const string List = "list";
    public const string Plan = "plan";
    public const string Script = "script";
    public const string Apply = "apply";
    public const string Validate = "validate";
    public const string PostMigrate = "post-migrate";

    /// <summary>Places an operator's hold on a schema family's finalization (spec 181, FR-019, FR-020).</summary>
    public const string Hold = "hold";

    /// <summary>Releases an operator's hold (spec 181, FR-019, FR-020).</summary>
    public const string Release = "release";

    /// <summary>Reports each schema family's finalization status (spec 181, FR-020, FR-022).</summary>
    public const string Status = "status";

    /// <summary>The private, configuration-only candidate inspection operation.</summary>
    public const string InspectCandidate = "inspect-candidate";

    /// <summary>The additive command carrying one explicit private environment document.</summary>
    public const string InspectCandidateEnvironment = "inspect-candidate-environment";

    public static readonly string[] All = [List, Plan, Script, Apply, Validate, PostMigrate, Hold, Release, Status];

    /// <summary>The commands that read or write a schema family's finalization record rather than migrations.</summary>
    public static bool IsFinalization(string command) => command is Hold or Release or Status;

    /// <summary>The commands that open the host's database and so need a connection (D7).</summary>
    public static bool OpensDatabase(string command) => command is Apply or Validate or PostMigrate || IsFinalization(command);
}

/// <summary>Source identifiers shared by the EF-free CLI front end and worker.</summary>
public static class WorkerContextSources
{
    public const string WorkbenchJson = "workbench-json-v1";
    public const string WorkbenchJsonEnvironment = "workbench-json-environment-v1";

    public static bool IsSupported(string? source) =>
        source is WorkbenchJson or WorkbenchJsonEnvironment;
}

/// <summary>
/// The exit codes spec 171 assigns the CLI, mirrored here so the front end can classify a failure of its
/// own — a missing deps file, a worker that never started — with the same vocabulary the worker and the
/// host's tooling entry point use.
/// </summary>
public static class ToolExitCode
{
    public const int Success = 0;
    public const int NegativeResult = 1;
    public const int Refusal = 2;
    public const int ResolutionFailure = 3;
    public const int DatabaseFailure = 4;
}

/// <summary>One request from the front end to the worker.</summary>
public sealed record WorkerRequest
{
    public int Version { get; init; } = WorkerContract.Version;

    /// <summary>One of <see cref="WorkerCommands.All"/>.</summary>
    public string? Command { get; init; }

    /// <summary>The <c>--host</c> directory, already validated to carry a runtimeconfig/deps pair (FR-011).</summary>
    public string? HostDirectory { get; init; }

    /// <summary>The host application's own name, as the manifest's <c>host.name</c> records it.</summary>
    public string? HostName { get; init; }

    /// <summary>The host's <c>.deps.json</c>, the one file that states what the host pins.</summary>
    public string? DepsFile { get; init; }

    /// <summary>Every <c>--packages</c> root, in the order given. Empty means "resolve from the host alone".</summary>
    public IReadOnlyList<string> PackageRoots { get; init; } = [];

    /// <summary>
    /// Whether <c>--restore</c> was given (ADR 0076 D10's opt-in exception). The only field that lets this
    /// tool write under the host's directories, and the only one that lets it touch a network. It is a bare
    /// boolean on purpose: what to restore, and from where, is read by the worker out of the host's own
    /// <c>appsettings.json</c>, so a feed's configured credential reference has nowhere to travel — not
    /// this request, not a process argument, not an environment variable this front end sets.
    /// </summary>
    public bool Restore { get; init; }

    /// <summary>Which modules to run against; <c>null</c> on <c>list</c> means every module in the closure.</summary>
    public WorkerSelection? Selection { get; init; }

    public string? Provider { get; init; }

    public string? Schema { get; init; }

    /// <summary>Where <c>script</c> writes its artifact.</summary>
    public string? Output { get; init; }

    /// <summary>The <c>--environment</c> value the manifest records (FR-038, FR-047).</summary>
    public string? Environment { get; init; }

    /// <summary>The <c>--shell</c> the provider-agreement check was narrowed to, or <c>null</c> for every configured shell.</summary>
    public string? Shell { get; init; }

    /// <summary>Host-owned source selected by --configuration-context, absent for legacy commands.</summary>
    public string? ContextSource { get; init; }

    /// <summary>Context factory protocol version 1, supplied only with an explicit source.</summary>
    public int? ContextVersion { get; init; }

    /// <summary>Optional selected named resource identity; its provider and connection stay host-owned.</summary>
    public string? Resource { get; init; }

    /// <summary>
    /// The host's enabled shell features, read from <c>shells.json</c> plus its <c>--environment</c> overlay
    /// (FR-035). Present — even empty — exactly when that configuration was found beside the host, which is
    /// what the manifest's <c>providerAgreement: checked</c> states; <c>null</c> when none was found at all.
    /// Each entry carries a feature's <c>Provider</c> setting and nothing else: no other shell setting is
    /// lifted out of the file, so no connection string can travel here.
    /// </summary>
    public IReadOnlyList<WorkerShellFeature>? Shells { get; init; }

    /// <summary>
    /// The name of the environment variable <c>--connection-env</c> names (D7). Only its name travels here;
    /// the worker reads the value from its own (inherited) environment, never from this front end's request.
    /// Required by the commands that open a database unless <see cref="Connection"/> is given instead.
    /// </summary>
    public string? ConnectionEnv { get; init; }

    /// <summary>The family, version, reason and operator <c>hold</c>, <c>release</c> and <c>status</c> act on; absent otherwise.</summary>
    public WorkerFinalization? Finalization { get; init; }

    /// <summary>
    /// The skew allowance <c>status</c> judges the cluster members' liveness with, as a <c>TimeSpan</c> in its invariant
    /// <c>c</c> format: <c>--skew-allowance</c>, else the host's <c>Elsa:Cluster:Membership:SkewAllowance</c>, else absent
    /// for the membership provider's own default. Never sent to a host whose tooling predates it, which judges nothing.
    /// </summary>
    public TimeSpan? SkewAllowance { get; init; }

    /// <summary>
    /// How long <c>apply</c> waits for a SQLite database's EF migration lock before it reports it as stale, as a <c>TimeSpan</c>:
    /// the host's <c>Elsa:Persistence:EntityFramework:Migrate:SqliteMigrationLockStaleAfter</c> from its appsettings, else absent
    /// for the persistence build's own default. Never sent to a host whose tooling predates it, which waits the default.
    /// </summary>
    public TimeSpan? SqliteMigrationLockStaleAfter { get; init; }

    /// <summary>
    /// The connection string itself, read by this front end from its own stdin when <c>--connection-stdin</c>
    /// was given (D7) — the one case where the value has nowhere to travel but this request. Never populated
    /// from <c>--connection-env</c>, and never logged, echoed, or included in a refusal.
    /// </summary>
    public string? Connection { get; init; }

    /// <summary>Versioned, file-only input accepted exclusively by <see cref="WorkerCommands.InspectCandidate"/>.</summary>
    public WorkerCandidatePayload? Candidate { get; init; }
}

/// <summary>Closed additive v2 request. It never broadens the legacy <see cref="WorkerRequest"/> reader.</summary>
public sealed record CandidateEnvironmentWorkerRequestV2
{
    [JsonRequired] public int Version { get; init; }
    [JsonRequired] public string? Command { get; init; }
    [JsonRequired] public string? HostDirectory { get; init; }
    [JsonRequired] public string? HostName { get; init; }
    [JsonRequired] public string? DepsFile { get; init; }
    [JsonRequired] public IReadOnlyList<string>? PackageRoots { get; init; }
    [JsonRequired] public WorkerCandidatePayload? Candidate { get; init; }
    [JsonRequired] public WorkerEnvironmentInput? EnvironmentInput { get; init; }

    // Reuse only existing layout/payload admission. Execution always negotiates the additive capability.
    internal WorkerRequest FileOnlyClosureRequest() => new()
    {
        Version = Version, Command = WorkerCommands.InspectCandidate, HostDirectory = HostDirectory,
        HostName = HostName, DepsFile = DepsFile, PackageRoots = PackageRoots!, Candidate = Candidate
    };
}

/// <summary>Discriminated candidate request selected after the command field is read from the bounded envelope.</summary>
internal sealed record CandidateInspectionRequest(
    WorkerRequest? FileOnly,
    CandidateEnvironmentWorkerRequestV2? Environment)
{
    public bool IsEnvironment => Environment is not null;
}

/// <summary>Private raw captured document, bound to its candidate; no source path or public fingerprint.</summary>
public sealed record WorkerEnvironmentInput
{
    [JsonRequired] public int Version { get; init; }
    [JsonRequired] public string? CaptureId { get; init; }
    [JsonRequired] public string? Content { get; init; }
}

/// <summary>Closed v1 candidate payload for the configuration-only worker operation.</summary>
public sealed record WorkerCandidatePayload
{
    [JsonRequired]
    public int Version { get; init; }

    [JsonRequired]
    public string? Source { get; init; }

    [JsonRequired]
    public string? InvocationId { get; init; }

    [JsonRequired]
    public string? CaptureId { get; init; }

    [JsonRequired]
    public string? Shell { get; init; }

    [JsonRequired]
    public string? Environment { get; init; }

    [JsonRequired]
    public IReadOnlyList<string>? AcceptedFeatureIds { get; init; }

    [JsonRequired]
    public IReadOnlyList<string>? RemovedFeatureIds { get; init; }

    [JsonRequired]
    public IReadOnlyList<WorkerCandidateFile>? Files { get; init; }
}

/// <summary>One captured source layer in a candidate request.</summary>
public sealed record WorkerCandidateFile
{
    [JsonRequired]
    public string? Name { get; init; }

    [JsonRequired]
    public string? CaptureId { get; init; }

    [JsonRequired]
    public string? Content { get; init; }
}

/// <summary>What a finalization command names (spec 181, FR-019): the family, an optional version, the reason and the operator.</summary>
public sealed record WorkerFinalization
{
    public string? Family { get; init; }

    public string? Version { get; init; }

    public string? Reason { get; init; }

    public string? Operator { get; init; }
}

/// <summary>Which modules a command runs against, discriminated the same way the tooling contract discriminates it.</summary>
public sealed record WorkerSelection
{
    public const string AllKind = "all";
    public const string ModulesKind = "modules";

    /// <summary>Every module the host's own enabled shell features map to through <c>[UsesEfModule]</c> (FR-028).</summary>
    public const string FromHostKind = "from-host";

    public string? Kind { get; init; }

    public IReadOnlyList<string>? Modules { get; init; }
}

/// <summary>
/// One feature a shell enables, reduced to what the provider-agreement check needs: the shell, the CShells
/// feature name, and that feature's configured <c>Provider</c> setting (<c>null</c> when the shell sets
/// none). A feature a shell disables never appears here — an unenabled feature is ignored (FR-036).
/// </summary>
public sealed record WorkerShellFeature
{
    public string? Shell { get; init; }

    public string? Feature { get; init; }

    public string? Provider { get; init; }
}

/// <summary>One response from the worker to the front end.</summary>
public sealed record WorkerResponse
{
    public int Version { get; init; } = WorkerContract.Version;

    /// <summary>The code the front end exits with, whether it came from the worker or from the host's tooling entry point.</summary>
    public int ExitCode { get; init; }

    /// <summary>The tooling entry point's own response, verbatim, when one was produced.</summary>
    public JsonElement? Tooling { get; init; }

    /// <summary>The worker's own refusal, for everything that fails before or around the tooling call.</summary>
    public WorkerError? Error { get; init; }
}

/// <summary>A refusal the worker itself made: resolving the host, its packages, or its tooling entry point.</summary>
public sealed record WorkerError
{
    public string Code { get; init; } = "";

    public string Message { get; init; } = "";

    /// <summary>One line per offender, for the refusals that must name every one rather than the first.</summary>
    public IReadOnlyList<string> Details { get; init; } = [];
}
