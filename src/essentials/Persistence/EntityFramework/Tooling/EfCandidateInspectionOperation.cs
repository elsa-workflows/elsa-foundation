using System.Buffers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using CShells;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Persistence.EntityFramework.ResourceResolution;
using Microsoft.Extensions.Configuration;

namespace Elsa.Persistence.EntityFramework.Tooling;

/// <summary>Inspects one captured composition candidate inside its selected host closure.</summary>
public sealed class EfCandidateInspectionOperation
{
    internal const int MaximumRequestBytes = 8 * 1024 * 1024;
    internal const int MaximumFileBytes = 1024 * 1024;
    internal const int MaximumFilesBytes = 4 * 1024 * 1024;
    internal const int MaximumResponseBytes = 4 * 1024 * 1024;
    internal const int MaximumJsonDepth = 64;
    internal const int MaximumSelectionIds = 4096;
    internal const int MaximumRows = 1024;
    internal const int MaximumIdentityLength = 128;
    internal const string Source = "captured-workbench-json-v1";

    internal static readonly HashSet<string> ResourceRefusalCodes = new(StringComparer.Ordinal)
    {
        "resource-selection-invalid",
        "resource-not-found",
        "resource-definition-invalid",
        "resource-configurator-unsupported",
        "resource-required-feature-disabled",
        "resource-legacy-conflict",
        "resource-ownership-unresolved",
        "resource-context-conflict"
    };

    internal static readonly HashSet<string> UnresolvedCodes = new(StringComparer.Ordinal)
    {
        "legacy-target-unprojected",
        "exact-file-provenance-unavailable",
        "resource-participant-unenrolled",
        "resource-scope-unsupported"
    };

    private static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = MaximumJsonDepth };
    private readonly Func<IEnumerable<Assembly>> assemblyDiscovery;

    public EfCandidateInspectionOperation(Func<IEnumerable<Assembly>> assemblyDiscovery)
    {
        this.assemblyDiscovery = assemblyDiscovery ?? throw new ArgumentNullException(nameof(assemblyDiscovery));
    }

    /// <summary>Reads one bounded private request and writes one correlated, redacted host response.</summary>
    /// <exception cref="EfToolingRefusal">The selected host cannot inspect the candidate within the supported projection bounds.</exception>
    public async Task<int> RunAsync(Stream request, Stream response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);
        cancellationToken.ThrowIfCancellationRequested();

        var requestBuffer = ArrayPool<byte>.Shared.Rent(MaximumRequestBytes + 1);
        try
        {
            var length = await ReadBoundedAsync(request, requestBuffer, cancellationToken);
            CandidateRequest candidate;
            using (var document = TryParseDocument(requestBuffer.AsMemory(0, length)))
            {
                if (document is null || !TryReadCorrelation(document.RootElement, out var correlation))
                    return EfToolingExitCode.Refusal;
                if (length > MaximumRequestBytes)
                {
                    await WriteErrorAsync(response, correlation, "candidate-request-too-large", null, null, cancellationToken);
                    return EfToolingExitCode.Refusal;
                }

                try
                {
                    candidate = ParseRequest(document.RootElement, correlation);
                }
                catch (CandidateInputRefusal refusal)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await WriteErrorAsync(response, correlation, refusal.Code, null, null, cancellationToken);
                    return EfToolingExitCode.Refusal;
                }
            }

            using (candidate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ConfigurationRoot configuration;
                try
                {
                    configuration = BuildConfiguration(candidate);
                }
                catch (CandidateInputRefusal refusal)
                {
                    await WriteErrorAsync(response, candidate.Correlation, refusal.Code, null, null, cancellationToken);
                    return EfToolingExitCode.Refusal;
                }

                using (configuration)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = Inspect(candidate, configuration, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (result.Conflict is { } conflict)
                    {
                        await WriteErrorAsync(response, candidate.Correlation, "candidate-selection-conflict",
                            conflict.Reason, conflict.Feature, cancellationToken);
                        return EfToolingExitCode.Refusal;
                    }

                    if (result.RefusalCode is { } refusalCode)
                    {
                        await WriteErrorAsync(response, candidate.Correlation, refusalCode, null, null, cancellationToken);
                        return EfToolingExitCode.Refusal;
                    }

                    var payload = WriteResolution(candidate.Correlation, result.Resolution!);
                    if (payload.Length > MaximumResponseBytes)
                        throw HostUnavailable();
                    await response.WriteAsync(payload, cancellationToken);
                    await response.FlushAsync(cancellationToken);
                    return EfToolingExitCode.Success;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(requestBuffer, clearArray: true);
        }
    }

    private InspectionResult Inspect(CandidateRequest candidate, IConfigurationRoot configuration, CancellationToken cancellationToken)
    {
        Assembly[] closure;
        try
        {
            closure = assemblyDiscovery().Where(assembly => !assembly.IsDynamic).Distinct().ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure) when (EfToolingHost.IsNonFatal(failure))
        {
            throw HostUnavailable();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var matchingHostAssemblies = closure.Where(assembly =>
            string.Equals(assembly.GetName().Name, candidate.HostName, StringComparison.Ordinal) &&
            EfToolingConfigurationContext.SameHostAssemblyPath(assembly.Location, candidate.HostDirectory, candidate.HostName)).ToArray();
        if (matchingHostAssemblies.Length != 1)
            throw HostUnavailable();
        var hostAssembly = matchingHostAssemblies[0];

        return InspectComposition(candidate.Shell, candidate.Environment, candidate.AcceptedFeatureIds,
            candidate.RemovedFeatureIds, configuration, hostAssembly, closure,
            _ => SafeIdentityOrNull, cancellationToken);
    }

    /// <summary>Runs the shared host composition, reconciliation and EF preparation core for either candidate lane.</summary>
    internal static InspectionResult InspectComposition(
        string shell,
        string environment,
        IReadOnlyList<string> acceptedFeatureIds,
        IReadOnlyList<string> removedFeatureIds,
        IConfigurationRoot configuration,
        Assembly hostAssembly,
        Assembly[] closure,
        Func<IReadOnlyDictionary<string, ShellFeatureDescriptor>, Func<string, string?>?>? publicFeatureFactory,
        CancellationToken cancellationToken)
    {
        IEfToolingShellDefaults defaults;
        IReadOnlyDictionary<string, ShellFeatureDescriptor> descriptors;
        ShellSettings settings;
        try
        {
            var composerType = EfToolingShellDefaultsDeclaration.ResolveComposerType(hostAssembly, required: true)
                ?? throw HostUnavailable();
            defaults = EfToolingShellDefaultsDeclaration.Construct(composerType);

            var discovered = FeatureDiscovery.DiscoverFeatures(closure).ToArray();
            if (discovered.Any(feature => !IsSafeIdentity(feature.Id)) ||
                discovered.GroupBy(feature => feature.Id, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() != 1))
                throw HostUnavailable();
            descriptors = discovered.ToDictionary(feature => feature.Id, StringComparer.OrdinalIgnoreCase);
            settings = EfToolingConfigurationContext.ComposeShell(defaults, configuration, shell);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (EfToolingRefusal refusal) when (ResourceRefusalCodes.Contains(refusal.Code))
        {
            return InspectionResult.Refused(refusal.Code);
        }
        catch (Exception failure) when (EfToolingHost.IsNonFatal(failure))
        {
            throw HostUnavailable();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var requested = settings.EnabledFeatures.ToArray();
        var disabled = settings.DisabledFeatures.ToArray();
        string[] effective;
        try
        {
            effective = new FeatureDependencyResolver().GetOrderedFeatures(
                requested.Where(descriptors.ContainsKey), descriptors).ToArray();
        }
        catch (Exception failure) when (EfToolingHost.IsNonFatal(failure))
        {
            return InspectionResult.Conflicted(new SelectionConflict("unavailable", null));
        }

        var conflict = Reconcile(acceptedFeatureIds, removedFeatureIds, requested, effective, disabled, descriptors,
            publicFeatureFactory?.Invoke(descriptors));
        if (conflict is not null)
            return InspectionResult.Conflicted(conflict);

        cancellationToken.ThrowIfCancellationRequested();
        EfPersistencePreparationResult prepared;
        try
        {
            prepared = EfPersistencePreparation.Prepare(settings, descriptors, configuration, closure,
                verifyConnectionValues: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (EfToolingRefusal refusal) when (ResourceRefusalCodes.Contains(refusal.Code))
        {
            return InspectionResult.Refused(refusal.Code);
        }
        catch (Exception failure) when (EfToolingHost.IsNonFatal(failure))
        {
            throw HostUnavailable();
        }

        if (prepared.RefusalCodes.Count != 0)
        {
            var code = prepared.RefusalCodes.Order(StringComparer.Ordinal).First();
            if (!ResourceRefusalCodes.Contains(code))
                throw HostUnavailable();
            return InspectionResult.Refused(code);
        }

        try
        {
            return InspectionResult.Resolved(BuildResolution(shell, environment,
                acceptedFeatureIds, settings, prepared));
        }
        catch (CandidateInputRefusal refusal) when (ResourceRefusalCodes.Contains(refusal.Code))
        {
            return InspectionResult.Refused(refusal.Code);
        }
    }

    internal static SelectionConflict? Reconcile(
        IReadOnlyList<string> accepted,
        IReadOnlyList<string> removed,
        IReadOnlyList<string> requested,
        IReadOnlyList<string> effective,
        IReadOnlyList<string> disabled,
        IReadOnlyDictionary<string, ShellFeatureDescriptor> descriptors,
        Func<string, string?>? publicFeature = null)
    {
        publicFeature ??= SafeIdentityOrNull;
        foreach (var id in accepted.Concat(removed).Concat(disabled))
        {
            if (!descriptors.ContainsKey(id))
                return new SelectionConflict("unknown", publicFeature(id));
            if (!string.Equals(descriptors.Keys.First(key => StringComparer.OrdinalIgnoreCase.Equals(key, id)), id,
                    StringComparison.Ordinal))
                return new SelectionConflict("case-collision", publicFeature(id));
        }

        if (requested.Any(id => !IsSafeIdentity(id)) || effective.Any(id => !IsSafeIdentity(id)) || disabled.Any(id => !IsSafeIdentity(id)))
            return new SelectionConflict("unknown", null);

        var unavailableRequested = requested.FirstOrDefault(id => !descriptors.ContainsKey(id));
        if (unavailableRequested is not null)
            return new SelectionConflict("unknown", publicFeature(unavailableRequested));

        var requestedCollision = FindCaseCollision(requested);
        var effectiveCollision = FindCaseCollision(effective);
        var disabledCollision = FindCaseCollision(disabled);
        if (requestedCollision is not null || effectiveCollision is not null || disabledCollision is not null)
            return new SelectionConflict("case-collision", publicFeature(requestedCollision ?? effectiveCollision ?? disabledCollision));

        var removedActive = removed.Intersect(requested.Concat(effective), StringComparer.Ordinal).FirstOrDefault();
        if (removedActive is not null)
            return new SelectionConflict("required-disabled", publicFeature(removedActive));

        var disabledActive = disabled.Intersect(effective, StringComparer.Ordinal).FirstOrDefault();
        if (disabledActive is not null)
            return new SelectionConflict("required-disabled", publicFeature(disabledActive));

        foreach (var removedId in removed)
            if (!disabled.Contains(removedId, StringComparer.Ordinal))
                return new SelectionConflict("required-disabled", publicFeature(removedId));

        var requestedMismatch = CompareIds(accepted, requested, publicFeature);
        if (requestedMismatch is { } requestedConflict)
            return requestedConflict;
        var effectiveMismatch = CompareIds(accepted, effective, publicFeature);
        if (effectiveMismatch is { } effectiveConflict)
            return effectiveConflict with { Reason = effectiveConflict.Reason == "requested-extra" ? "expanded-extra" : effectiveConflict.Reason };

        return null;
    }

    private static SelectionConflict? CompareIds(
        IReadOnlyList<string> accepted,
        IReadOnlyList<string> observed,
        Func<string, string?> publicFeature)
    {
        var acceptedSet = accepted.ToHashSet(StringComparer.Ordinal);
        var observedSet = observed.ToHashSet(StringComparer.Ordinal);
        var caseAlias = accepted.FirstOrDefault(id => observed.Any(value =>
            StringComparer.OrdinalIgnoreCase.Equals(id, value) && !StringComparer.Ordinal.Equals(id, value)));
        if (caseAlias is not null)
            return new SelectionConflict("case-collision", publicFeature(caseAlias));

        var extra = observed.Where(id => !acceptedSet.Contains(id)).Order(StringComparer.Ordinal).FirstOrDefault();
        if (extra is not null)
            return new SelectionConflict("requested-extra", publicFeature(extra));
        var missing = accepted.Where(id => !observedSet.Contains(id)).Order(StringComparer.Ordinal).FirstOrDefault();
        return missing is null ? null : new SelectionConflict("requested-missing", publicFeature(missing));
    }

    private static CandidateRequest ParseRequest(JsonElement root, Correlation correlation)
    {
        if (root.ValueKind != JsonValueKind.Object || HasDuplicateProperties(root) ||
            !HasOnlyProperties(root, "version", "host", "candidate") ||
            !TryInt(root, "version", out var hostVersion) || hostVersion != EfCandidateInspectionContract.Version)
            throw CandidateInputRefusal.RequestInvalid();

        var host = Property(root, "host");
        var candidate = Property(root, "candidate");
        if (host.ValueKind != JsonValueKind.Object || HasDuplicateProperties(host) ||
            !HasOnlyProperties(host, "name", "directory") ||
            !TryString(host, "name", out var hostName) || !IsHostAssemblyName(hostName) ||
            !TryString(host, "directory", out var hostDirectory) || !IsCanonicalDirectory(hostDirectory))
            throw CandidateInputRefusal.RequestInvalid();

        if (candidate.ValueKind != JsonValueKind.Object || HasDuplicateProperties(candidate) ||
            !HasOnlyProperties(candidate, "version", "source", "invocationId", "captureId", "shell", "environment",
                "acceptedFeatureIds", "removedFeatureIds", "files") ||
            !TryInt(candidate, "version", out var candidateVersion) || candidateVersion != EfCandidateInspectionContract.Version ||
            !TryString(candidate, "source", out var source) || source != Source ||
            !TryString(candidate, "invocationId", out var invocationId) || invocationId != correlation.InvocationId ||
            !TryString(candidate, "captureId", out var captureId) || captureId != correlation.CaptureId ||
            !TryString(candidate, "shell", out var shell) || !IsSafeIdentity(shell) ||
            !TryString(candidate, "environment", out var environment) || !IsSafeEnvironment(environment) ||
            !TryStringArray(candidate, "acceptedFeatureIds", out var accepted) ||
            !TryStringArray(candidate, "removedFeatureIds", out var removed) ||
            !ValidSortedIds(accepted) || !ValidSortedIds(removed) ||
            accepted.Intersect(removed, StringComparer.OrdinalIgnoreCase).Any())
            throw CandidateInputRefusal.RequestInvalid();

        var filesElement = Property(candidate, "files");
        if (filesElement.ValueKind != JsonValueKind.Array || filesElement.GetArrayLength() is < 3 or > 4)
            throw CandidateInputRefusal.RequestInvalid();

        var encodedFiles = new Dictionary<string, (string CaptureId, string Content)>(StringComparer.Ordinal);
        var aggregateLength = 0;
        foreach (var file in filesElement.EnumerateArray())
        {
            if (file.ValueKind != JsonValueKind.Object || HasDuplicateProperties(file) ||
                !HasOnlyProperties(file, "name", "captureId", "content") ||
                !TryString(file, "name", out var name) || !AllowedFileName(name, environment) ||
                !TryString(file, "captureId", out var fileCaptureId) || fileCaptureId != captureId ||
                !TryString(file, "content", out var content) || !TryBase64Length(content, out var decodedLength) ||
                decodedLength > MaximumFileBytes || aggregateLength + decodedLength > MaximumFilesBytes ||
                !encodedFiles.TryAdd(name, (fileCaptureId, content)))
                throw CandidateInputRefusal.RequestInvalid();
            aggregateLength += decodedLength;
        }

        if (!encodedFiles.ContainsKey("appsettings.json") || !encodedFiles.ContainsKey("shells.json") ||
            !encodedFiles.ContainsKey($"shells.{environment}.json"))
            throw CandidateInputRefusal.RequestInvalid();

        var decoded = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        try
        {
            foreach (var (name, entry) in encodedFiles)
            {
                var bytes = Convert.FromBase64String(entry.Content);
                if (Convert.ToBase64String(bytes) != entry.Content || !IsValidSourceJson(bytes))
                {
                    CryptographicOperations.ZeroMemory(bytes);
                    throw CandidateInputRefusal.CaptureInvalid();
                }
                decoded.Add(name, bytes);
            }
        }
        catch (FormatException)
        {
            Clear(decoded.Values);
            throw CandidateInputRefusal.RequestInvalid();
        }
        catch
        {
            Clear(decoded.Values);
            throw;
        }

        return new CandidateRequest(correlation, hostName, hostDirectory, shell, environment, accepted, removed, decoded);
    }

    private static ConfigurationRoot BuildConfiguration(CandidateRequest candidate) =>
        BuildConfiguration(candidate.Files, candidate.Environment);

    internal static ConfigurationRoot BuildConfiguration(
        IReadOnlyDictionary<string, byte[]> files,
        string environment,
        IReadOnlyDictionary<string, string>? explicitOverlay = null)
    {
        var builder = new ConfigurationBuilder();
        var sourceNames = new[]
        {
            "appsettings.json",
            $"appsettings.{environment}.json",
            "shells.json",
            $"shells.{environment}.json"
        };
        var streams = sourceNames.Where(files.ContainsKey)
            .Select(name => new MemoryStream(files[name], writable: false)).ToArray();
        try
        {
            foreach (var stream in streams)
                builder.AddJsonStream(stream);
            if (explicitOverlay is not null)
                builder.AddInMemoryCollection(explicitOverlay.Select(entry =>
                    new KeyValuePair<string, string?>(entry.Key, entry.Value)));
            return (ConfigurationRoot)builder.Build();
        }
        catch (Exception failure) when (EfToolingHost.IsNonFatal(failure))
        {
            throw CandidateInputRefusal.CaptureInvalid();
        }
        finally
        {
            foreach (var stream in streams)
                stream.Dispose();
        }
    }

    internal static CandidateResolution BuildResolution(
        string shell,
        string environment,
        IReadOnlyList<string> acceptedFeatureIds,
        ShellSettings settings,
        EfPersistencePreparationResult prepared)
    {
        var unresolved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in prepared.UnresolvedCodes)
        {
            if (!UnresolvedCodes.Contains(code))
                throw HostUnavailable();
            unresolved.Add(code);
        }

        var rows = new List<ParticipantRow>();
        foreach (var selected in prepared.ResolvedParticipants)
        {
            if (selected.Participant.ModuleNames.Count == 0)
            {
                unresolved.Add("resource-participant-unenrolled");
                continue;
            }

            var legacy = selected.Selection == PersistenceSelectionKind.Legacy;
            var resource = legacy ? null : selected.ResourceName;
            var provider = legacy ? null : selected.Provider;
            var connectionReference = legacy ? null : selected.ConnectionName;
            if (!legacy && (!IsLogicalIdentity(resource) || !IsLogicalIdentity(connectionReference) || !IsProvider(provider)))
                return CandidateResolution.Refused("resource-definition-invalid");

            var selectorScope = Scope(selected.Source.Scope);
            var resourceScope = legacy ? null : Scope(prepared.ResourceDefinitions.TryGetValue(selected.ResourceName!, out var definition)
                ? definition.Source.Scope : null);
            if (!legacy && resourceScope == "unavailable")
                unresolved.Add("resource-scope-unsupported");
            if (legacy)
                unresolved.Add("legacy-target-unprojected");

            foreach (var module in selected.Participant.ModuleNames.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                if (!IsSafeIdentity(module) || !IsSafeIdentity(selected.Participant.FeatureId))
                    return CandidateResolution.Refused("resource-definition-invalid");
                if (rows.Count >= MaximumRows)
                    throw HostUnavailable();
                rows.Add(new ParticipantRow(selected.Participant.FeatureId, module, selected.Selection.ToString(),
                    resource, provider, connectionReference, selectorScope, resourceScope, "unavailable"));
            }
        }

        if (rows.Count != 0)
            unresolved.Add("exact-file-provenance-unavailable");

        if (rows.Count > MaximumRows - unresolved.Count)
            throw HostUnavailable();

        var orderedUnresolved = unresolved.Order(StringComparer.Ordinal).ToArray();
        var partial = unresolved.Any(code => code != "exact-file-provenance-unavailable");
        var requested = settings.EnabledFeatures.Order(StringComparer.Ordinal).ToArray();
        var effective = prepared.ActiveFeatureIds.Order(StringComparer.Ordinal).ToArray();
        var disabled = settings.DisabledFeatures.Order(StringComparer.Ordinal).ToArray();
        if (acceptedFeatureIds.Count > MaximumSelectionIds || requested.Length > MaximumSelectionIds ||
            effective.Length > MaximumSelectionIds || disabled.Length > MaximumSelectionIds)
            throw HostUnavailable();

        return new CandidateResolution(
            shell,
            environment,
            partial ? "partial" : "resolved",
            prepared.HasApplicableResource ? "checked" : "not-applicable",
            acceptedFeatureIds.ToArray(),
            requested,
            effective,
            disabled,
            effective.Except(requested, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            rows.OrderBy(row => row.Feature, StringComparer.Ordinal).ThenBy(row => row.Module, StringComparer.Ordinal).ToArray(),
            orderedUnresolved);
    }

    private static byte[] WriteResolution(Correlation correlation, CandidateResolution resolution) =>
        WriteResolution(correlation.InvocationId, correlation.CaptureId, resolution, Source, "unverified");

    internal static byte[] WriteResolution(
        string invocationId,
        string captureId,
        CandidateResolution resolution,
        string source,
        string externalInputs)
    {
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", EfCandidateInspectionContract.Version);
            writer.WriteString("invocationId", invocationId);
            writer.WriteString("captureId", captureId);
            writer.WriteString("status", "ok");
            writer.WriteNumber("exitCode", EfToolingExitCode.Success);
            writer.WritePropertyName("configurationResolution");
            writer.WriteStartObject();
            writer.WriteString("source", source);
            writer.WriteString("shell", resolution.Shell);
            writer.WriteString("environment", resolution.Environment);
            writer.WriteString("resolution", resolution.Resolution);
            writer.WritePropertyName("selection");
            writer.WriteStartObject();
            WriteIds(writer, "acceptedFeatureIds", resolution.Accepted);
            WriteIds(writer, "requestedFeatureIds", resolution.Requested);
            WriteIds(writer, "effectiveFeatureIds", resolution.Effective);
            WriteIds(writer, "disabledFeatureIds", resolution.Disabled);
            WriteIds(writer, "implicitFeatureIds", resolution.Implicit);
            writer.WriteEndObject();
            writer.WriteString("configuredValueAffinity", resolution.ConfiguredValueAffinity);
            writer.WriteString("targetVerification", "not-performed");
            writer.WriteString("runtimeParity", "unobserved");
            writer.WriteString("packageReachability", "unverified");
            writer.WriteString("connectivity", "unverified");
            writer.WriteString("schemaReadiness", "unverified");
            writer.WriteString("migrationReadiness", "unverified");
            writer.WriteString("activation", "unobserved");
            writer.WriteString("externalInputs", externalInputs);
            writer.WritePropertyName("participants");
            writer.WriteStartArray();
            foreach (var row in resolution.Participants)
            {
                writer.WriteStartObject();
                writer.WriteString("feature", row.Feature);
                writer.WriteString("module", row.Module);
                writer.WriteString("selection", row.Selection);
                if (row.Resource is null) writer.WriteNull("resource"); else writer.WriteString("resource", row.Resource);
                if (row.Provider is null) writer.WriteNull("provider"); else writer.WriteString("provider", row.Provider);
                if (row.ConnectionReference is null) writer.WriteNull("connectionReference"); else writer.WriteString("connectionReference", row.ConnectionReference);
                if (row.SelectorScope is null) writer.WriteNull("selectorScope"); else writer.WriteString("selectorScope", row.SelectorScope);
                if (row.ResourceScope is null) writer.WriteNull("resourceScope"); else writer.WriteString("resourceScope", row.ResourceScope);
                writer.WriteString("exactFileProvenance", row.ExactFileProvenance);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            WriteIds(writer, "unresolved", resolution.Unresolved);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return output.ToArray();
    }

    private static async Task WriteErrorAsync(
        Stream response,
        Correlation correlation,
        string code,
        string? reason,
        string? feature,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", EfCandidateInspectionContract.Version);
            writer.WriteString("invocationId", correlation.InvocationId);
            writer.WriteString("captureId", correlation.CaptureId);
            writer.WriteString("status", "refused");
            writer.WriteNumber("exitCode", EfToolingExitCode.Refusal);
            writer.WritePropertyName("error");
            writer.WriteStartObject();
            writer.WriteString("code", code);
            if (reason is not null) writer.WriteString("reason", reason);
            if (feature is not null && IsSafeIdentity(feature)) writer.WriteString("feature", feature);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        if (output.Length > MaximumResponseBytes)
            throw HostUnavailable();
        await response.WriteAsync(output.ToArray(), cancellationToken);
        await response.FlushAsync(cancellationToken);
    }

    private static async Task<int> ReadBoundedAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var count = 0;
        var boundedLength = Math.Min(buffer.Length, MaximumRequestBytes + 1);
        while (count < boundedLength)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(buffer.AsMemory(count, boundedLength - count), cancellationToken);
            if (read == 0)
                break;
            count += read;
        }
        return count;
    }

    private static JsonDocument? TryParseDocument(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            return JsonDocument.Parse(bytes, DocumentOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryReadCorrelation(JsonElement root, out Correlation correlation)
    {
        correlation = default!;
        if (root.ValueKind != JsonValueKind.Object || !TryGetUniqueProperty(root, "candidate", out var candidate) ||
            candidate.ValueKind != JsonValueKind.Object ||
            !TryGetUniqueProperty(candidate, "invocationId", out var invocation) || invocation.ValueKind != JsonValueKind.String ||
            !TryGetUniqueProperty(candidate, "captureId", out var capture) || capture.ValueKind != JsonValueKind.String)
            return false;
        var invocationId = invocation.GetString()!;
        var captureId = capture.GetString()!;
        if (!IsToken(invocationId) || !IsToken(captureId) || invocationId == captureId)
            return false;
        correlation = new Correlation(invocationId, captureId);
        return true;
    }

    private static bool TryGetUniqueProperty(JsonElement parent, string name, out JsonElement value)
    {
        value = default;
        var found = false;
        foreach (var property in parent.EnumerateObject())
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(property.Name, name))
                continue;
            if (found || property.Name != name)
                return false;
            value = property.Value;
            found = true;
        }
        return found;
    }

    private static bool IsValidSourceJson(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes, DocumentOptions);
            return document.RootElement.ValueKind == JsonValueKind.Object && !HasDuplicateProperties(document.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasDuplicateProperties(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in element.EnumerateObject())
                    if (!names.Add(property.Name) || HasDuplicateProperties(property.Value))
                        return true;
                return false;
            }
            case JsonValueKind.Array:
                return element.EnumerateArray().Any(HasDuplicateProperties);
            default:
                return false;
        }
    }

    private static bool HasOnlyProperties(JsonElement element, params string[] names)
    {
        var allowed = names.ToHashSet(StringComparer.Ordinal);
        return element.EnumerateObject().All(property => allowed.Contains(property.Name)) &&
               names.All(name => element.TryGetProperty(name, out _));
    }

    private static JsonElement Property(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value : default;

    private static bool TryString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
            return false;
        value = property.GetString()!;
        return true;
    }

    private static bool TryInt(JsonElement element, string name, out int value)
    {
        value = default;
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value);
    }

    private static bool TryStringArray(JsonElement element, string name, out string[] values)
    {
        values = [];
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Array ||
            property.GetArrayLength() > MaximumSelectionIds || property.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
            return false;
        values = property.EnumerateArray().Select(item => item.GetString()!).ToArray();
        return true;
    }

    private static bool ValidSortedIds(IReadOnlyList<string> values)
    {
        if (values.Count > MaximumSelectionIds || values.Any(value => !IsSafeIdentity(value)) ||
            !values.SequenceEqual(values.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            return false;
        return values.Distinct(StringComparer.OrdinalIgnoreCase).Count() == values.Count;
    }

    private static bool TryBase64Length(string encoded, out int decodedLength)
    {
        decodedLength = 0;
        if (encoded.Length == 0 || encoded.Length % 4 != 0 || encoded.Length > ((MaximumFileBytes + 2) / 3) * 4)
            return false;
        var padding = encoded.EndsWith("==", StringComparison.Ordinal) ? 2 : encoded.EndsWith('=') ? 1 : 0;
        decodedLength = encoded.Length / 4 * 3 - padding;
        return decodedLength >= 0 && decodedLength <= MaximumFileBytes;
    }

    private static bool AllowedFileName(string name, string environment) => name is "appsettings.json" or "shells.json" ||
        name == $"appsettings.{environment}.json" || name == $"shells.{environment}.json";

    private static bool IsHostAssemblyName(string name) =>
        IsSafeIdentity(name) && !name.Contains('/') && !name.Contains('\\') && !name.Contains('+') && !name.Contains(':');

    private static bool IsCanonicalDirectory(string directory)
    {
        try
        {
            return Path.IsPathFullyQualified(directory) && Path.GetFullPath(directory) == directory && Directory.Exists(directory);
        }
        catch (Exception failure) when (EfToolingHost.IsNonFatal(failure))
        {
            return false;
        }
    }

    private static bool IsSafeEnvironment(string value) =>
        value.Length is > 0 and <= MaximumIdentityLength && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private static bool IsToken(string value) => value.Length == 32 && value.All(character =>
        character is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static bool IsSafeIdentity(string? value) =>
        value is { Length: > 0 and <= MaximumIdentityLength } && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' or '/' or '+');

    internal static string? SafeIdentityOrNull(string? value) => IsSafeIdentity(value) ? value : null;

    internal static bool IsLogicalIdentity(string? value) =>
        value is { Length: > 0 and <= MaximumIdentityLength } &&
        (char.IsAsciiLetter(value[0]) || value[0] == '_') &&
        !value.Equals("true", StringComparison.OrdinalIgnoreCase) &&
        !value.Equals("false", StringComparison.OrdinalIgnoreCase) &&
        !value.Equals("null", StringComparison.OrdinalIgnoreCase) &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.');

    internal static bool IsProvider(string? value) => value is "Sqlite" or "SqlServer" or "PostgreSql" or "MySql";

    internal static string? Scope(string? scope) => scope is "root" or "shell-composed" or "shell-authored" or "feature"
        ? scope : "unavailable";

    internal static string? FindCaseCollision(IReadOnlyList<string> values)
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (seen.TryGetValue(value, out var prior) && !StringComparer.Ordinal.Equals(prior, value))
                return value;
            seen[value] = value;
        }
        return null;
    }

    internal static void WriteIds(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (var value in values)
            writer.WriteStringValue(value);
        writer.WriteEndArray();
    }

    private static void Clear(IEnumerable<byte[]> buffers)
    {
        foreach (var buffer in buffers)
            CryptographicOperations.ZeroMemory(buffer);
    }

    internal static EfToolingRefusal HostUnavailable() =>
        EfToolingRefusal.Resolution("candidate-host-unavailable", "Candidate inspection could not be completed by this host.");

    private sealed record Correlation(string InvocationId, string CaptureId);
    internal sealed record SelectionConflict(string Reason, string? Feature);
    internal sealed record ParticipantRow(
        string Feature,
        string Module,
        string Selection,
        string? Resource,
        string? Provider,
        string? ConnectionReference,
        string? SelectorScope,
        string? ResourceScope,
        string ExactFileProvenance);
    internal sealed record CandidateResolution(
        string Shell,
        string Environment,
        string Resolution,
        string ConfiguredValueAffinity,
        string[] Accepted,
        string[] Requested,
        string[] Effective,
        string[] Disabled,
        string[] Implicit,
        ParticipantRow[] Participants,
        string[] Unresolved)
    {
        public static CandidateResolution Refused(string code) => throw new CandidateInputRefusal(code);
    }

    internal sealed record InspectionResult(CandidateResolution? Resolution, string? RefusalCode, SelectionConflict? Conflict)
    {
        public static InspectionResult Resolved(CandidateResolution value) => new(value, null, null);
        public static InspectionResult Refused(string code) => new(null, code, null);
        public static InspectionResult Conflicted(SelectionConflict value) => new(null, null, value);
    }

    private sealed class CandidateRequest(
        Correlation correlation,
        string hostName,
        string hostDirectory,
        string shell,
        string environment,
        string[] acceptedFeatureIds,
        string[] removedFeatureIds,
        Dictionary<string, byte[]> files) : IDisposable
    {
        public Correlation Correlation { get; } = correlation;
        public string HostName { get; } = hostName;
        public string HostDirectory { get; } = hostDirectory;
        public string Shell { get; } = shell;
        public string Environment { get; } = environment;
        public string[] AcceptedFeatureIds { get; } = acceptedFeatureIds;
        public string[] RemovedFeatureIds { get; } = removedFeatureIds;
        public Dictionary<string, byte[]> Files { get; } = files;
        public void Dispose() => Clear(Files.Values);
    }

    internal sealed class CandidateInputRefusal(string code) : Exception
    {
        public string Code { get; } = code;
        public static CandidateInputRefusal RequestInvalid() => new("candidate-request-invalid");
        public static CandidateInputRefusal CaptureInvalid() => new("candidate-capture-invalid");
    }
}
