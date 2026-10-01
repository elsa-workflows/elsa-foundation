using System.Buffers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Elsa.Persistence.EntityFramework.Tooling;

/// <summary>Inspects one captured candidate with one explicitly supplied environment document.</summary>
public sealed class EfCandidateEnvironmentInspectionOperation
{
    private const int MaximumRequestBytes = EfCandidateInspectionOperation.MaximumRequestBytes;
    private const int MaximumFileBytes = EfCandidateInspectionOperation.MaximumFileBytes;
    private const int MaximumFilesBytes = EfCandidateInspectionOperation.MaximumFilesBytes;
    private const int MaximumResponseBytes = EfCandidateInspectionOperation.MaximumResponseBytes;
    private const int MaximumEnvironmentBytes = 1024 * 1024;
    private const string Source = "captured-workbench-json-explicit-environment-v1";
    private const string EnvironmentPolicy = "workbench-json-explicit-environment-v1";
    private const string EnvironmentInputsAttributeName =
        "Elsa.Persistence.EntityFramework.Tooling.EfCandidateEnvironmentInputsAttribute";
    private const string EnvironmentHostUnenrolled = "candidate-environment-host-unenrolled";
    private const string CapabilityUnavailable = "candidate-capability-unavailable";

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        MaxDepth = EfCandidateInspectionOperation.MaximumJsonDepth,
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow
    };

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly HashSet<string> ErrorReasons = new(StringComparer.Ordinal)
    {
        "unknown", "unavailable", "requested-extra", "requested-missing", "expanded-extra", "required-disabled", "case-collision"
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

    private readonly Func<IEnumerable<Assembly>> assemblyDiscovery;

    public EfCandidateEnvironmentInspectionOperation(Func<IEnumerable<Assembly>> assemblyDiscovery)
    {
        this.assemblyDiscovery = assemblyDiscovery ?? throw new ArgumentNullException(nameof(assemblyDiscovery));
    }

    /// <summary>Reads one bounded additive request and writes one correlated, redacted response.</summary>
    public async Task<int> RunAsync(Stream request, Stream response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);
        cancellationToken.ThrowIfCancellationRequested();

        var requestBuffer = ArrayPool<byte>.Shared.Rent(MaximumRequestBytes + 1);
        try
        {
            var length = await ReadBoundedAsync(request, requestBuffer, cancellationToken);
            using var document = TryParseDocument(requestBuffer.AsMemory(0, length));
            if (document is null || !TryReadCorrelation(document.RootElement, out var correlation))
                return EfToolingExitCode.Refusal;

            if (length > MaximumRequestBytes)
            {
                await WriteErrorAsync(response, correlation, "candidate-request-too-large", EfToolingExitCode.Refusal,
                    null, null, cancellationToken);
                return EfToolingExitCode.Refusal;
            }

            CandidateEnvironmentRequest candidate;
            try
            {
                candidate = ParseRequest(document.RootElement, correlation);
            }
            catch (CandidateInputRefusal refusal)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WriteErrorAsync(response, correlation, refusal.Code, EfToolingExitCode.Refusal,
                    null, null, cancellationToken);
                return EfToolingExitCode.Refusal;
            }
            catch (Exception failure) when (failure is JsonException or InvalidOperationException or ArgumentException or FormatException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WriteErrorAsync(response, correlation, "candidate-request-invalid", EfToolingExitCode.Refusal,
                    null, null, cancellationToken);
                return EfToolingExitCode.Refusal;
            }

            using (candidate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var closure = DiscoverAssemblies(cancellationToken);
                var hostAssembly = closure.Where(assembly =>
                        string.Equals(assembly.GetName().Name, candidate.HostName, StringComparison.Ordinal) &&
                        EfToolingConfigurationContext.SameHostAssemblyPath(
                            assembly.Location, candidate.HostDirectory, candidate.HostName))
                    .ToArray();
                if (hostAssembly.Length != 1)
                    throw EfCandidateInspectionOperation.HostUnavailable();

                try
                {
                    // This is the host-side repeat of worker admission. It must run before either
                    // captured configuration construction or composer construction.
                    ValidateCapabilityAndEnrollment(hostAssembly[0]);
                }
                catch (HostAdmissionRefusal refusal)
                {
                    await WriteErrorAsync(response, candidate.Correlation, refusal.Code, refusal.ExitCode,
                        null, null, cancellationToken);
                    return refusal.ExitCode;
                }

                cancellationToken.ThrowIfCancellationRequested();
                ConfigurationRoot capturedConfiguration;
                try
                {
                    capturedConfiguration = EfCandidateInspectionOperation.BuildConfiguration(
                        candidate.Files, candidate.Environment);
                }
                catch (EfCandidateInspectionOperation.CandidateInputRefusal refusal)
                {
                    await WriteErrorAsync(response, candidate.Correlation, refusal.Code, EfToolingExitCode.Refusal,
                        null, null, cancellationToken);
                    return EfToolingExitCode.Refusal;
                }

                using (capturedConfiguration)
                {
                    var publicIdentities = EfCandidateInspectionOperation.ReadCapturedPublicIdentities(capturedConfiguration);
                    using var configuration = EfCandidateInspectionOperation.BuildConfiguration(
                        capturedConfiguration, candidate.EnvironmentEntries);
                    cancellationToken.ThrowIfCancellationRequested();
                    var inspection = Inspect(candidate, hostAssembly[0], closure, configuration, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();

                    if (inspection.Conflict is { } conflict)
                    {
                        await WriteErrorAsync(response, candidate.Correlation, "candidate-selection-conflict",
                            EfToolingExitCode.Refusal, conflict.Reason, conflict.Feature, cancellationToken);
                        return EfToolingExitCode.Refusal;
                    }

                    if (inspection.RefusalCode is { } refusalCode)
                    {
                        await WriteErrorAsync(response, candidate.Correlation, refusalCode, EfToolingExitCode.Refusal,
                            null, null, cancellationToken);
                        return EfToolingExitCode.Refusal;
                    }

                    if (!EfCandidateInspectionOperation.HasOnlyCapturedPublicIdentities(
                            inspection.Resolution!, publicIdentities))
                    {
                        await WriteErrorAsync(response, candidate.Correlation, "resource-definition-invalid",
                            EfToolingExitCode.Refusal, null, null, cancellationToken);
                        return EfToolingExitCode.Refusal;
                    }

                    var payload = EfCandidateInspectionOperation.WriteResolution(
                        candidate.Correlation.InvocationId,
                        candidate.Correlation.CaptureId,
                        inspection.Resolution!,
                        Source,
                        "supplied-intended");
                    if (payload.Length > MaximumResponseBytes)
                        throw EfCandidateInspectionOperation.HostUnavailable();
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

    private EfCandidateInspectionOperation.InspectionResult Inspect(
        CandidateEnvironmentRequest candidate,
        Assembly hostAssembly,
        Assembly[] closure,
        IConfigurationRoot configuration,
        CancellationToken cancellationToken)
        => EfCandidateInspectionOperation.InspectComposition(
            candidate.Shell,
            candidate.Environment,
            candidate.AcceptedFeatureIds,
            candidate.RemovedFeatureIds,
            configuration,
            hostAssembly,
            closure,
            descriptors => PublicIdentityAllowlist.FromCandidate(candidate, descriptors.Keys).FeatureOrNull,
            cancellationToken);

    private static void ValidateCapabilityAndEnrollment(Assembly hostAssembly)
    {
        try
        {
            var persistenceAssembly = typeof(EfCandidateEnvironmentInspectionOperation).Assembly;
            var contractType = persistenceAssembly.GetType(
                typeof(EfCandidateEnvironmentInspectionContract).FullName!, throwOnError: false);
            var hostType = persistenceAssembly.GetType(typeof(EfToolingHost).FullName!, throwOnError: false);
            var version = contractType?.GetField("Version", BindingFlags.Public | BindingFlags.Static);
            var method = hostType?.GetMethod("RunCandidateEnvironmentInspectionAsync",
                BindingFlags.Public | BindingFlags.Static, [typeof(Stream), typeof(Stream), typeof(CancellationToken)]);
            if (contractType is null || contractType.Assembly != persistenceAssembly ||
                version?.DeclaringType != contractType || version?.IsLiteral != true ||
                version?.FieldType != typeof(int) || version?.GetRawConstantValue() is not 1 ||
                method is not { IsPublic: true, IsStatic: true, IsGenericMethod: false, ContainsGenericParameters: false } ||
                method.DeclaringType != hostType || method.ReturnType != typeof(Task<int>) ||
                method.GetParameters() is not { Length: 3 } parameters ||
                parameters[0].ParameterType != typeof(Stream) ||
                parameters[1].ParameterType != typeof(Stream) ||
                parameters[2].ParameterType != typeof(CancellationToken))
                throw EfToolingRefusal.Resolution(CapabilityUnavailable,
                    "The selected host has no complete candidate inspection capability.");
        }
        catch (EfToolingRefusal)
        {
            throw;
        }
        catch (Exception failure) when (EfToolingHost.IsNonFatal(failure))
        {
            throw EfToolingRefusal.Resolution(CapabilityUnavailable,
                "The selected host has no complete candidate inspection capability.");
        }

        try
        {
            var persistenceAssembly = typeof(EfCandidateEnvironmentInspectionOperation).Assembly;
            var attributeType = persistenceAssembly.GetType(EnvironmentInputsAttributeName, throwOnError: false);
            if (attributeType is null || attributeType.Assembly != persistenceAssembly ||
                !typeof(Attribute).IsAssignableFrom(attributeType) || !HasEnvironmentAttributeShape(attributeType))
                throw HostAdmissionRefusal.Enrollment();

            var declarations = hostAssembly.GetCustomAttributesData()
                .Where(attribute => attribute.AttributeType == attributeType)
                .ToArray();
            if (declarations.Length != 1 || !IsEnrollmentDeclaration(declarations[0], attributeType))
                throw HostAdmissionRefusal.Enrollment();
        }
        catch (HostAdmissionRefusal)
        {
            throw;
        }
        catch (Exception failure) when (EfToolingHost.IsNonFatal(failure))
        {
            throw HostAdmissionRefusal.Enrollment();
        }
    }

    private static bool HasEnvironmentAttributeShape(Type attributeType)
    {
        if (!attributeType.IsPublic || !attributeType.IsClass || !attributeType.IsSealed)
            return false;

        var constructor = attributeType.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null,
            [typeof(int), typeof(string)], null);
        var version = attributeType.GetProperty("Version", BindingFlags.Public | BindingFlags.Instance);
        var policy = attributeType.GetProperty("Policy", BindingFlags.Public | BindingFlags.Instance);
        if (constructor is null || constructor.DeclaringType != attributeType ||
            version?.DeclaringType != attributeType || version?.PropertyType != typeof(int) ||
            version?.GetMethod is not { IsPublic: true, IsStatic: false, IsGenericMethod: false } || version.SetMethod is not null ||
            policy?.DeclaringType != attributeType || policy?.PropertyType != typeof(string) ||
            policy?.GetMethod is not { IsPublic: true, IsStatic: false, IsGenericMethod: false } || policy.SetMethod is not null)
            return false;
        if (version.GetMethod!.DeclaringType != attributeType || policy.GetMethod!.DeclaringType != attributeType)
            return false;

        var usages = attributeType.GetCustomAttributesData()
            .Where(attribute => attribute.AttributeType == typeof(AttributeUsageAttribute))
            .ToArray();
        if (usages.Length != 1)
            return false;

        var usage = usages[0];
        if (usage.ConstructorArguments.Count != 1 ||
            usage.ConstructorArguments[0].ArgumentType != typeof(AttributeTargets) ||
            !EnumValueIs(usage.ConstructorArguments[0], AttributeTargets.Assembly))
            return false;

        var named = usage.NamedArguments.ToDictionary(argument => argument.MemberName, StringComparer.Ordinal);
        return named.Count == 2 &&
            named.TryGetValue(nameof(AttributeUsageAttribute.AllowMultiple), out var allowMultiple) &&
            allowMultiple.TypedValue.ArgumentType == typeof(bool) && allowMultiple.TypedValue.Value is true &&
            named.TryGetValue(nameof(AttributeUsageAttribute.Inherited), out var inherited) &&
            inherited.TypedValue.ArgumentType == typeof(bool) && inherited.TypedValue.Value is false;
    }

    private static bool EnumValueIs(CustomAttributeTypedArgument argument, AttributeTargets expected) =>
        argument.Value is AttributeTargets value && value == expected ||
        argument.Value is int numeric && numeric == (int)expected;

    private static bool IsEnrollmentDeclaration(CustomAttributeData declaration, Type attributeType)
    {
        var constructor = declaration.Constructor;
        if (declaration.AttributeType != attributeType || constructor.DeclaringType != attributeType ||
            !constructor.IsPublic || constructor.GetParameters() is not { Length: 2 } parameters ||
            parameters[0].ParameterType != typeof(int) || parameters[1].ParameterType != typeof(string) ||
            declaration.ConstructorArguments.Count != 2 || declaration.NamedArguments.Count != 0)
            return false;
        var version = declaration.ConstructorArguments[0];
        var policy = declaration.ConstructorArguments[1];
        return version.ArgumentType == typeof(int) && version.Value is 1 &&
            policy.ArgumentType == typeof(string) && policy.Value is string value && value == EnvironmentPolicy;
    }

    private Assembly[] DiscoverAssemblies(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return assemblyDiscovery().Where(assembly => !assembly.IsDynamic).Distinct().ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure) when (EfToolingHost.IsNonFatal(failure))
        {
            throw EfCandidateInspectionOperation.HostUnavailable();
        }
    }

    private static CandidateEnvironmentRequest ParseRequest(JsonElement root, Correlation correlation)
    {
        if (root.ValueKind != JsonValueKind.Object || HasDuplicateProperties(root) ||
            !HasOnlyProperties(root, "version", "host", "candidate", "environmentInput") ||
            !TryInt(root, "version", out var version) || version != EfCandidateEnvironmentInspectionContract.Version ||
            Property(root, "version").GetRawText() != "1")
            throw CandidateInputRefusal.RequestInvalid();

        var host = Property(root, "host");
        if (host.ValueKind != JsonValueKind.Object || HasDuplicateProperties(host) ||
            !HasOnlyProperties(host, "name", "directory") ||
            !TryString(host, "name", out var hostName) || !IsHostAssemblyName(hostName) ||
            !TryString(host, "directory", out var hostDirectory) || !IsCanonicalDirectory(hostDirectory))
            throw CandidateInputRefusal.RequestInvalid();

        var candidate = Property(root, "candidate");
        if (candidate.ValueKind != JsonValueKind.Object || HasDuplicateProperties(candidate) ||
            !HasOnlyProperties(candidate, "version", "source", "invocationId", "captureId", "shell", "environment",
                "acceptedFeatureIds", "removedFeatureIds", "files") ||
            !TryInt(candidate, "version", out var candidateVersion) || candidateVersion != EfCandidateInspectionContract.Version ||
            Property(candidate, "version").GetRawText() != "1" ||
            !TryString(candidate, "source", out var source) || source != EfCandidateInspectionOperation.Source ||
            !TryString(candidate, "invocationId", out var invocationId) || invocationId != correlation.InvocationId ||
            !TryString(candidate, "captureId", out var captureId) || captureId != correlation.CaptureId ||
            !TryString(candidate, "shell", out var shell) || !EfCandidateInspectionOperation.IsSafeIdentity(shell) ||
            !TryString(candidate, "environment", out var environment) || !IsSafeEnvironment(environment) ||
            !TryStringArray(candidate, "acceptedFeatureIds", out var accepted) ||
            !TryStringArray(candidate, "removedFeatureIds", out var removed) ||
            !ValidSortedIds(accepted) || !ValidSortedIds(removed) ||
            accepted.Intersect(removed, StringComparer.OrdinalIgnoreCase).Any())
            throw CandidateInputRefusal.RequestInvalid();

        var filesElement = Property(candidate, "files");
        if (filesElement.ValueKind != JsonValueKind.Array || filesElement.GetArrayLength() is < 3 or > 4)
            throw CandidateInputRefusal.RequestInvalid();

        var encodedFiles = new Dictionary<string, string>(StringComparer.Ordinal);
        var aggregateLength = 0;
        foreach (var file in filesElement.EnumerateArray())
        {
            if (file.ValueKind != JsonValueKind.Object || HasDuplicateProperties(file) ||
                !HasOnlyProperties(file, "name", "captureId", "content") ||
                !TryString(file, "name", out var name) || !AllowedFileName(name, environment) ||
                !TryString(file, "captureId", out var fileCaptureId) || fileCaptureId != captureId ||
                !TryString(file, "content", out var content) || !TryBase64Length(content, MaximumFileBytes, out var decodedLength) ||
                decodedLength > MaximumFileBytes || aggregateLength + decodedLength > MaximumFilesBytes ||
                !encodedFiles.TryAdd(name, content))
                throw CandidateInputRefusal.RequestInvalid();
            aggregateLength += decodedLength;
        }

        if (!encodedFiles.ContainsKey("appsettings.json") || !encodedFiles.ContainsKey("shells.json") ||
            !encodedFiles.ContainsKey($"shells.{environment}.json"))
            throw CandidateInputRefusal.RequestInvalid();

        var environmentInput = Property(root, "environmentInput");
        if (environmentInput.ValueKind != JsonValueKind.Object || HasDuplicateProperties(environmentInput) ||
            !HasOnlyProperties(environmentInput, "version", "captureId", "content") ||
            !TryInt(environmentInput, "version", out var environmentVersion) || environmentVersion != 1 ||
            Property(environmentInput, "version").GetRawText() != "1" ||
            !TryString(environmentInput, "captureId", out var environmentCaptureId) || environmentCaptureId != captureId ||
            !TryString(environmentInput, "content", out var environmentContent))
        {
            throw CandidateInputRefusal.RequestInvalid();
        }

        if (!TryBase64DecodedLength(environmentContent, out var estimatedEnvironmentLength))
            throw CandidateInputRefusal.EnvironmentInvalid();
        if (environmentContent.Length > EncodedLengthFor(MaximumEnvironmentBytes) ||
            estimatedEnvironmentLength > MaximumEnvironmentBytes)
            throw CandidateInputRefusal.EnvironmentTooLarge();

        byte[] environmentBytes;
        try
        {
            environmentBytes = DecodeCanonicalBase64(environmentContent, MaximumEnvironmentBytes);
        }
        catch (FormatException)
        {
            throw CandidateInputRefusal.EnvironmentInvalid();
        }

        IReadOnlyDictionary<string, string> environmentEntries;
        try
        {
            environmentEntries = ParseEnvironmentDocument(environmentBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(environmentBytes);
        }

        var decodedFiles = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        byte[]? currentBuffer = null;
        var ownershipTransferred = false;
        try
        {
            foreach (var (name, content) in encodedFiles)
            {
                byte[] decoded;
                try
                {
                    decoded = DecodeCanonicalBase64(content, MaximumFileBytes);
                }
                catch (FormatException)
                {
                    throw CandidateInputRefusal.CaptureInvalid();
                }
                currentBuffer = decoded;
                if (!IsValidSourceJson(decoded))
                {
                    CryptographicOperations.ZeroMemory(decoded);
                    currentBuffer = null;
                    throw CandidateInputRefusal.CaptureInvalid();
                }
                decodedFiles.Add(name, decoded);
                currentBuffer = null;
            }

            var capturedRequest = new CandidateEnvironmentRequest(correlation, hostName, hostDirectory, shell, environment,
                accepted, removed, decodedFiles, environmentEntries);
            ownershipTransferred = true;
            return capturedRequest;
        }
        catch (FormatException)
        {
            throw CandidateInputRefusal.RequestInvalid();
        }
        finally
        {
            if (!ownershipTransferred)
            {
                if (currentBuffer is not null)
                    CryptographicOperations.ZeroMemory(currentBuffer);
                Clear(decodedFiles.Values);
            }
        }
    }

    private static IReadOnlyDictionary<string, string> ParseEnvironmentDocument(byte[] bytes)
    {
        if (bytes.Length > MaximumEnvironmentBytes)
            throw CandidateInputRefusal.EnvironmentTooLarge();

        try
        {
            var utf8 = bytes.AsSpan();
            if (utf8.StartsWith("\uFEFF"u8))
            {
                utf8 = utf8[3..];
                if (utf8.StartsWith("\uFEFF"u8))
                    throw CandidateInputRefusal.EnvironmentInvalid();
            }

            var json = StrictUtf8.GetString(utf8);
            if (json.StartsWith('\uFEFF'))
                throw CandidateInputRefusal.EnvironmentInvalid();
            using var document = JsonDocument.Parse(json, DocumentOptions);
            return ParseEnvironmentRoot(document.RootElement);
        }
        catch (CandidateInputRefusal)
        {
            throw;
        }
        catch (DecoderFallbackException)
        {
            throw CandidateInputRefusal.EnvironmentInvalid();
        }
        catch (EncoderFallbackException)
        {
            throw CandidateInputRefusal.EnvironmentInvalid();
        }
        catch (JsonException)
        {
            throw CandidateInputRefusal.EnvironmentInvalid();
        }
        catch (InvalidOperationException)
        {
            throw CandidateInputRefusal.EnvironmentInvalid();
        }
    }

    private static IReadOnlyDictionary<string, string> ParseEnvironmentRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || HasDuplicateProperties(root) ||
            !HasOnlyProperties(root, "version", "entries") || !TryInt(root, "version", out var version) || version != 1 ||
            Property(root, "version").GetRawText() != "1")
            throw CandidateInputRefusal.EnvironmentInvalid();

        var entries = Property(root, "entries");
        if (entries.ValueKind != JsonValueKind.Array)
            throw CandidateInputRefusal.EnvironmentInvalid();
        if (entries.GetArrayLength() > 1024)
            throw CandidateInputRefusal.EnvironmentTooLarge();

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object || HasDuplicateProperties(entry) ||
                !HasOnlyProperties(entry, "key", "value") ||
                !TryString(entry, "key", out var key) || !TryString(entry, "value", out var value))
                throw CandidateInputRefusal.EnvironmentInvalid();
            ValidateEnvironmentString(entry.GetProperty("key"), key, key: true);
            ValidateEnvironmentString(entry.GetProperty("value"), value, key: false);
            if (StrictUtf8.GetByteCount(key) > 1024 || StrictUtf8.GetByteCount(value) > 65536)
                throw CandidateInputRefusal.EnvironmentTooLarge();
            if (UnsupportedServicePrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                throw CandidateInputRefusal.EnvironmentPrefixUnsupported();

            var normalized = key.Replace("__", ":", StringComparison.Ordinal);
            if (StrictUtf8.GetByteCount(normalized) > 1024)
                throw CandidateInputRefusal.EnvironmentTooLarge();
            if (!values.TryAdd(normalized, value))
                throw CandidateInputRefusal.EnvironmentKeyCollision();
        }

        return values;
    }

    private static void ValidateEnvironmentString(JsonElement element, string value, bool key)
    {
        if (key && value.Length == 0)
            throw CandidateInputRefusal.EnvironmentInvalid();

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
                throw CandidateInputRefusal.EnvironmentInvalid();
            if (char.IsHighSurrogate(codeUnit))
            {
                if (!TryReadUnicodeEscape(raw, index + 6, out var low) || !char.IsLowSurrogate(low))
                    throw CandidateInputRefusal.EnvironmentInvalid();
                index += 11;
            }
            else if (char.IsLowSurrogate(codeUnit))
            {
                throw CandidateInputRefusal.EnvironmentInvalid();
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
                    throw CandidateInputRefusal.EnvironmentInvalid();
                index++;
                continue;
            }
            if (char.IsLowSurrogate(character) || key && char.IsControl(character) || key && character == '=' ||
                !key && character == '\0')
                throw CandidateInputRefusal.EnvironmentInvalid();
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

    private static byte[] DecodeCanonicalBase64(string content, int maximumBytes)
    {
        if (!TryBase64Length(content, maximumBytes, out _))
            throw new FormatException();
        var bytes = Convert.FromBase64String(content);
        if (bytes.Length > maximumBytes || !Convert.ToBase64String(bytes).Equals(content, StringComparison.Ordinal))
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw new FormatException();
        }
        return bytes;
    }

    private static bool TryBase64Length(string content, int maximumBytes, out int decodedLength)
    {
        if (!TryBase64DecodedLength(content, out decodedLength) ||
            content.Length > EncodedLengthFor(maximumBytes) || decodedLength > maximumBytes)
            return false;
        return true;
    }

    private static int EncodedLengthFor(int maximumBytes) => (maximumBytes + 2) / 3 * 4;

    private static bool TryBase64DecodedLength(string content, out int decodedLength)
    {
        decodedLength = 0;
        if (content.Length == 0 || content.Length % 4 != 0)
            return false;

        var padding = content.EndsWith("==", StringComparison.Ordinal) ? 2 : content.EndsWith('=') ? 1 : 0;
        var dataLength = content.Length - padding;
        if (content.IndexOf('=') >= 0 && content.IndexOf('=') < dataLength)
            return false;
        for (var index = 0; index < dataLength; index++)
        {
            var character = content[index];
            if (!char.IsAsciiLetterOrDigit(character) && character is not '+' and not '/')
                return false;
        }

        decodedLength = content.Length / 4 * 3 - padding;
        return decodedLength >= 0;
    }

    private static async Task WriteErrorAsync(
        Stream response,
        Correlation correlation,
        string code,
        int exitCode,
        string? reason,
        string? feature,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", EfCandidateEnvironmentInspectionContract.Version);
            writer.WriteString("invocationId", correlation.InvocationId);
            writer.WriteString("captureId", correlation.CaptureId);
            writer.WriteString("status", "refused");
            writer.WriteNumber("exitCode", exitCode);
            writer.WritePropertyName("error");
            writer.WriteStartObject();
            writer.WriteString("code", code);
            if (reason is not null && ErrorReasons.Contains(reason)) writer.WriteString("reason", reason);
            if (feature is not null && EfCandidateInspectionOperation.IsSafeIdentity(feature))
                writer.WriteString("feature", feature);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        if (output.Length > MaximumResponseBytes)
            throw EfCandidateInspectionOperation.HostUnavailable();
        await response.WriteAsync(output.ToArray(), cancellationToken);
        await response.FlushAsync(cancellationToken);
    }

    private static async Task<int> ReadBoundedAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var count = 0;
        var boundedLength = MaximumRequestBytes + 1;
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
        try { return JsonDocument.Parse(bytes, DocumentOptions); }
        catch (Exception failure) when (failure is JsonException or InvalidOperationException or ArgumentException or FormatException)
        { return null; }
    }

    private static bool TryReadCorrelation(JsonElement root, out Correlation correlation)
    {
        correlation = default!;
        try
        {
            if (root.ValueKind != JsonValueKind.Object || !TryGetUniqueProperty(root, "candidate", out var candidate) ||
                candidate.ValueKind != JsonValueKind.Object ||
                !TryGetUniqueProperty(candidate, "invocationId", out var invocation) || invocation.ValueKind != JsonValueKind.String ||
                !TryGetUniqueProperty(candidate, "captureId", out var capture) || capture.ValueKind != JsonValueKind.String)
                return false;
            var invocationId = invocation.GetString();
            var captureId = capture.GetString();
            if (invocationId is null || captureId is null || !IsToken(invocationId) || !IsToken(captureId) || invocationId == captureId)
                return false;
            correlation = new Correlation(invocationId, captureId);
            return true;
        }
        catch (Exception failure) when (failure is JsonException or InvalidOperationException or ArgumentException or FormatException)
        {
            return false;
        }
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
        catch (JsonException) { return false; }
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
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out value);
    }

    private static bool TryStringArray(JsonElement element, string name, out string[] values)
    {
        values = [];
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Array ||
            property.GetArrayLength() > EfCandidateInspectionOperation.MaximumSelectionIds ||
            property.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
            return false;
        values = property.EnumerateArray().Select(item => item.GetString()!).ToArray();
        return true;
    }

    private static bool ValidSortedIds(IReadOnlyList<string> values) =>
        values.Count <= EfCandidateInspectionOperation.MaximumSelectionIds &&
        values.All(EfCandidateInspectionOperation.IsSafeIdentity) &&
        values.SequenceEqual(values.Order(StringComparer.Ordinal), StringComparer.Ordinal) &&
        values.Distinct(StringComparer.OrdinalIgnoreCase).Count() == values.Count;

    private static bool AllowedFileName(string name, string environment) => name is "appsettings.json" or "shells.json" ||
        name == $"appsettings.{environment}.json" || name == $"shells.{environment}.json";

    private static bool IsHostAssemblyName(string name) =>
        EfCandidateInspectionOperation.IsSafeIdentity(name) && !name.Contains('/') && !name.Contains('\\') &&
        !name.Contains('+') && !name.Contains(':');

    private static bool IsCanonicalDirectory(string directory)
    {
        try { return Path.IsPathFullyQualified(directory) && Path.GetFullPath(directory) == directory && Directory.Exists(directory); }
        catch (Exception failure) when (EfToolingHost.IsNonFatal(failure)) { return false; }
    }

    private static bool IsSafeEnvironment(string value) =>
        value.Length is > 0 and <= EfCandidateInspectionOperation.MaximumIdentityLength &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private static bool IsToken(string value) => value.Length == 32 && value.All(character =>
        character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void Clear(IEnumerable<byte[]> buffers)
    {
        foreach (var buffer in buffers)
            CryptographicOperations.ZeroMemory(buffer);
    }

    private sealed record Correlation(string InvocationId, string CaptureId);

    private sealed class CandidateEnvironmentRequest(
        Correlation correlation,
        string hostName,
        string hostDirectory,
        string shell,
        string environment,
        string[] acceptedFeatureIds,
        string[] removedFeatureIds,
        Dictionary<string, byte[]> files,
        IReadOnlyDictionary<string, string> environmentEntries) : IDisposable
    {
        public Correlation Correlation { get; } = correlation;
        public string HostName { get; } = hostName;
        public string HostDirectory { get; } = hostDirectory;
        public string Shell { get; } = shell;
        public string Environment { get; } = environment;
        public string[] AcceptedFeatureIds { get; } = acceptedFeatureIds;
        public string[] RemovedFeatureIds { get; } = removedFeatureIds;
        public Dictionary<string, byte[]> Files { get; } = files;
        public IReadOnlyDictionary<string, string> EnvironmentEntries { get; } = environmentEntries;
        public void Dispose() => Clear(Files.Values);
    }

    private sealed class CandidateInputRefusal(string code) : Exception
    {
        public string Code { get; } = code;
        public static CandidateInputRefusal RequestInvalid() => new("candidate-request-invalid");
        public static CandidateInputRefusal CaptureInvalid() => new("candidate-capture-invalid");
        public static CandidateInputRefusal EnvironmentInvalid() => new("candidate-environment-input-invalid");
        public static CandidateInputRefusal EnvironmentTooLarge() => new("candidate-environment-input-too-large");
        public static CandidateInputRefusal EnvironmentKeyCollision() => new("candidate-environment-key-collision");
        public static CandidateInputRefusal EnvironmentPrefixUnsupported() => new("candidate-environment-prefix-unsupported");
    }

    private sealed class HostAdmissionRefusal(string code, int exitCode) : Exception
    {
        public string Code { get; } = code;
        public int ExitCode { get; } = exitCode;
        public static HostAdmissionRefusal Enrollment() => new(EnvironmentHostUnenrolled, EfToolingExitCode.ResolutionFailure);
    }

    private sealed class PublicIdentityAllowlist(
        IEnumerable<string> accepted,
        IEnumerable<string> source,
        IEnumerable<string> actual)
    {
        private readonly HashSet<string> features = new(accepted.Concat(source).Concat(actual), StringComparer.Ordinal);

        public string? FeatureOrNull(string? value) =>
            value is not null && EfCandidateInspectionOperation.IsSafeIdentity(value) && features.Contains(value) ? value : null;

        public static PublicIdentityAllowlist FromCandidate(CandidateEnvironmentRequest candidate, IEnumerable<string> source) =>
            new(candidate.AcceptedFeatureIds.Concat(candidate.RemovedFeatureIds), source, []);

    }
}
