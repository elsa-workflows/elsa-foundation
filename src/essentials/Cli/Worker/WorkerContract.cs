using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Cli.Worker;

/// <summary>
/// The versioned JSON <c>dotnet-elsa</c> exchanges with its worker over stdin/stdout. Two processes, one
/// package: the front end resolves nothing about persistence itself, and the worker answers nothing the
/// front end did not ask for.
/// </summary>
/// <remarks>
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

    /// <summary>Reads and validates the private candidate host envelope while preserving its original JSON value.</summary>
    public static async Task<JsonElement> ReadCandidateHostResponseAsync(
        Stream stream,
        string expectedInvocationId,
        string expectedCaptureId,
        int processExitCode,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ReadBoundedCandidateJsonAsync(stream, CandidateHostResponseMaxBytes,
                () => WorkerRefusal.Resolution("candidate-response-too-large", "The candidate host response exceeds the supported size limit."),
                root =>
                {
                    ValidateCandidateHostResponse(root, expectedInvocationId, expectedCaptureId, processExitCode);
                    return root.Clone();
                },
                cancellationToken);
        }
        catch (JsonException)
        {
            throw InvalidCandidateHostResponse();
        }
    }

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

    private static void ValidateCandidateHostResponse(
        JsonElement root,
        string expectedInvocationId,
        string expectedCaptureId,
        int processExitCode)
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
            ValidateCandidateConfigurationResolution(resolution);
            return;
        }

        if (statusValue != "refused" || processExitCode != ToolExitCode.Refusal ||
            hasResolution || !hasError || error.ValueKind != JsonValueKind.Object)
            throw InvalidCandidateHostResponse();

        ValidateCandidateHostError(error);
    }

    private static void ValidateCandidateConfigurationResolution(JsonElement resolution)
    {
        if (!HasExactlyFields(resolution, CandidateConfigurationResolutionFields) ||
            !HasStringValue(resolution, "source", "captured-workbench-json-v1") ||
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
            !HasStringValue(resolution, "externalInputs", "unverified") ||
            !resolution.TryGetProperty("selection", out var selection) ||
            !TryValidateCandidateResolutionSelection(selection, out var acceptedFeatureIds) ||
            !resolution.TryGetProperty("participants", out var participants) || participants.ValueKind != JsonValueKind.Array ||
            !resolution.TryGetProperty("unresolved", out var unresolvedElement) ||
            !TryReadSortedUnresolvedCodes(unresolvedElement, out var unresolvedCodes) ||
            participants.GetArrayLength() > 1024 - unresolvedCodes.Length)
            throw InvalidCandidateHostResponse();

        var accepted = new HashSet<string>(acceptedFeatureIds, StringComparer.Ordinal);
        var hasLegacyParticipant = false;
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
            hasUnavailableResourceScope |= unavailableResourceScope;
        }

        var unresolved = new HashSet<string>(unresolvedCodes, StringComparer.Ordinal);
        var hasExactFileProvenanceUnavailable = unresolved.Contains("exact-file-provenance-unavailable");
        if (participants.GetArrayLength() > 0 && !hasExactFileProvenanceUnavailable ||
            hasLegacyParticipant && !unresolved.Contains("legacy-target-unprojected") ||
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
            if (resource is not null || provider is not null || connectionReference is not null)
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

    private static void ValidateCandidateHostError(JsonElement error)
    {
        if (HasDuplicateFields(error) ||
            error.EnumerateObject().Any(property => !CandidateHostErrorFields.Contains(property.Name)) ||
            !error.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.String ||
            !CandidateHostErrorCodes.Contains(code.GetString() ?? string.Empty))
            throw InvalidCandidateHostResponse();

        var codeValue = code.GetString();
        if (codeValue is "candidate-request-invalid" or "candidate-request-too-large" or "candidate-capture-invalid" &&
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
            request.Finalization is not null || request.SkewAllowance is not null)
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
    /// The connection string itself, read by this front end from its own stdin when <c>--connection-stdin</c>
    /// was given (D7) — the one case where the value has nowhere to travel but this request. Never populated
    /// from <c>--connection-env</c>, and never logged, echoed, or included in a refusal.
    /// </summary>
    public string? Connection { get; init; }

    /// <summary>Versioned, file-only input accepted exclusively by <see cref="WorkerCommands.InspectCandidate"/>.</summary>
    public WorkerCandidatePayload? Candidate { get; init; }
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
