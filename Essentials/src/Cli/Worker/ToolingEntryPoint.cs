using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;

namespace Elsa.Cli.Worker;

/// <summary>
/// The one entry point the worker calls inside the host's closure: <c>Tooling.EfToolingHost.RunAsync(Stream,
/// Stream)</c> in the host's own <c>Elsa.Persistence.EntityFramework</c> (FR-003), reached reflectively so
/// this project references no persistence assembly of its own (FR-001, ADR 0076 D1).
/// </summary>
/// <remarks>
/// The two-argument overload is deliberate: it discovers modules across every load context in the process,
/// which is what a Nuplane package graph — loaded into a context of its own — needs. Handing over an
/// explicit assembly set instead would quietly exclude exactly those modules.
/// </remarks>
public sealed class ToolingEntryPoint
{
    private const int CandidateHostRequestMaximumBytes = 8 * 1024 * 1024;
    private const int CandidateHostResponseMaximumBytes = 4 * 1024 * 1024;
    private const string CandidateRequestTooLargeMessage = "The candidate request exceeds the supported size limit.";
    private const string CandidateResponseTooLargeMessage = "The candidate host response exceeds the supported size limit.";
    private const string CandidateHostUnavailableMessage = "The selected host candidate inspection could not be completed.";

    private const string ToolingHostTypeName = "Elsa.Persistence.EntityFramework.Tooling.EfToolingHost";
    private const string ProviderBindingTypeName = "Elsa.Persistence.EntityFramework.EfRelationalProviderBinding";
    private const string RequestTypeName = "Elsa.Persistence.EntityFramework.Tooling.EfToolingRequest";
    private const string ContextTypeName = "Elsa.Persistence.EntityFramework.Tooling.EfToolingConfigurationContext";
    private const string ContextContractTypeName = "Elsa.Persistence.EntityFramework.Tooling.EfToolingContextContract";
    private const string CandidateEnvironmentInspectionMethodName = "RunCandidateEnvironmentInspectionAsync";
    private const string CandidateEnvironmentInspectionContractTypeName =
        "Elsa.Persistence.EntityFramework.Tooling.EfCandidateEnvironmentInspectionContract";
    private const string CandidateEnvironmentInputsAttributeTypeName =
        "Elsa.Persistence.EntityFramework.Tooling.EfCandidateEnvironmentInputsAttribute";
    private const string CandidateEnvironmentInputsPolicy = "workbench-json-explicit-environment-v1";
    private const string CapabilitySelectionField = "CapabilitySelection";
    private const string SkewAllowanceField = "SkewAllowance";
    private const string SqliteMigrationLockStaleAfterField = "SqliteMigrationLockStaleAfter";

    /// <summary>Serialized the way the frozen tooling contract reads it: camelCase, and no null for a field a command would refuse.</summary>
    private static readonly JsonSerializerOptions RequestJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly MethodInfo runAsync;
    private readonly MethodInfo providerPackageId;
    private readonly MethodInfo describeBindingFailure;
    private readonly MethodInfo select;
    private readonly ToolingContextApi? contextApi;

    internal ToolingEntryPoint(
        MethodInfo runAsync,
        MethodInfo providerPackageId,
        MethodInfo describeBindingFailure,
        MethodInfo select,
        bool supportsCapabilitySelection,
        ToolingContextApi? contextApi,
        bool supportsSkewAllowance = false,
        bool supportsSqliteMigrationLockStaleAfter = false)
    {
        this.runAsync = runAsync;
        this.providerPackageId = providerPackageId;
        this.describeBindingFailure = describeBindingFailure;
        this.select = select;
        this.contextApi = contextApi;
        SupportsCapabilitySelection = supportsCapabilitySelection;
        SupportsSkewAllowance = supportsSkewAllowance;
        SupportsSqliteMigrationLockStaleAfter = supportsSqliteMigrationLockStaleAfter;
    }

    /// <summary>
    /// Whether this host's tooling contract carries the <c>capabilitySelection</c> field (spec 172 FR-004).
    /// </summary>
    /// <remarks>
    /// The contract refuses an unmapped request property on purpose, so sending the field to a build that
    /// predates it would be refused as a malformed request — correct, but naming neither the key nor what to
    /// do about it. Asked in advance so the worker can refuse in those terms instead. Never used to fall
    /// back: a host that selects an engine and a build that cannot compare it is a run that must not
    /// produce an artifact.
    /// </remarks>
    public bool SupportsCapabilitySelection { get; }

    /// <summary>
    /// Whether this host's tooling contract carries the <c>skewAllowance</c> request field, which <c>status</c> judges the
    /// cluster members' liveness with. A build that predates it lists no members and judges nothing, so the worker leaves the
    /// field out rather than send what its closed contract would refuse.
    /// </summary>
    public bool SupportsSkewAllowance { get; }

    /// <summary>
    /// Whether this host's tooling contract carries the <c>sqliteMigrationLockStaleAfter</c> request field, which <c>apply</c> waits
    /// for a SQLite migration lock with. A build that predates it waits the default, so the worker leaves the field out rather than
    /// send what its closed contract would refuse.
    /// </summary>
    public bool SupportsSqliteMigrationLockStaleAfter { get; }

    /// <summary>True only when the exact context factory, operation, disposable type, and versions agree.</summary>
    public bool SupportsConfigurationContext => contextApi is not null;

    /// <summary>
    /// Binds the entry point in <paramref name="persistence"/>, refusing when that build predates it
    /// (FR-010).
    /// </summary>
    /// <remarks>
    /// The decision is made on the entry point's presence rather than on a version comparison, because a
    /// version string cannot say whether a build carries a type. The message still names both versions
    /// FR-010 asks for: the one this host pins, read from its deps file, and a version known to carry the
    /// entry point — this tool's own, since the front end, the worker and the persistence assembly are
    /// built and released from one repository together.
    /// </remarks>
    public static ToolingEntryPoint Resolve(Assembly persistence, string? pinnedVersion, string toolVersion)
    {
        var hostType = persistence.GetType(ToolingHostTypeName, throwOnError: false);
        var run = hostType?.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static, [typeof(Stream), typeof(Stream)]);
        var binding = persistence.GetType(ProviderBindingTypeName, throwOnError: false);
        var packageId = binding?.GetMethod("ProviderPackageId", BindingFlags.Public | BindingFlags.Static, [typeof(string)]);
        var describe = binding?.GetMethod("DescribeBindingFailure", BindingFlags.Public | BindingFlags.Static, [typeof(string)]);
        var canonical = binding?.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(method => method.Name == "Select" && method.IsGenericMethodDefinition && method.GetParameters().Length == 6)
            ?.MakeGenericMethod(typeof(string));

        if (run is null || packageId is null || describe is null || canonical is null)
        {
            throw WorkerRefusal.Resolution(
                "host-tooling-entry-point-missing",
                $"This host pins {HostClosure.PersistenceAssemblyName} {pinnedVersion ?? "(version unknown)"}, which carries no " +
                $"{ToolingHostTypeName} entry point for dotnet-elsa to call. The minimum version that carries it is the one " +
                $"released beside this tool, {toolVersion}; upgrade the host's {HostClosure.PersistenceAssemblyName} to that version or newer.");
        }

        var requestType = persistence.GetType(RequestTypeName, throwOnError: false);
        var capabilitySelection = Declares(requestType, CapabilitySelectionField);
        var skewAllowance = Declares(requestType, SkewAllowanceField);
        var sqliteLockStaleAfter = Declares(requestType, SqliteMigrationLockStaleAfterField);
        var contextApi = BindContextApi(
            hostType!,
            persistence.GetType(ContextTypeName, throwOnError: false),
            persistence.GetType(ContextContractTypeName, throwOnError: false));

        return new(run, packageId, describe, canonical, capabilitySelection, contextApi, skewAllowance, sqliteLockStaleAfter);
    }

    /// <summary>Binds only the independently versioned candidate API, with no legacy tooling fallback.</summary>
    public static MethodInfo BindCandidateInspection(Type? hostType, Type? operationContract)
    {
        return BindCandidateOperation(hostType, operationContract, "RunCandidateInspectionAsync",
            requireVersionDeclaredByContract: false);
    }

    /// <summary>Binds only the additive explicit-environment candidate API, with no legacy tooling fallback.</summary>
    public static MethodInfo BindCandidateEnvironmentInspection(Type? hostType, Type? operationContract)
    {
        return BindCandidateOperation(hostType, operationContract, CandidateEnvironmentInspectionMethodName,
            requireVersionDeclaredByContract: true);
    }

    /// <summary>Resolves both additive capability types from one selected persistence assembly.</summary>
    internal static MethodInfo ResolveCandidateEnvironmentInspection(Assembly persistence)
    {
        ArgumentNullException.ThrowIfNull(persistence);
        return BindCandidateEnvironmentInspection(
            persistence.GetType(ToolingHostTypeName, throwOnError: false),
            persistence.GetType(CandidateEnvironmentInspectionContractTypeName, throwOnError: false));
    }

    private static MethodInfo BindCandidateOperation(
        Type? hostType,
        Type? operationContract,
        string methodName,
        bool requireVersionDeclaredByContract)
    {
        try
        {
            var version = operationContract?.GetField("Version", BindingFlags.Public | BindingFlags.Static);
            var run = hostType?.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static,
                [typeof(Stream), typeof(Stream), typeof(CancellationToken)]);
            if ((requireVersionDeclaredByContract && version?.DeclaringType != operationContract) ||
                version?.IsLiteral != true || version.FieldType != typeof(int) ||
                version.GetRawConstantValue() is not 1 || !IsCandidateOperation(run))
                throw CandidateCapabilityUnavailable();
            return run!;
        }
        catch (WorkerRefusal)
        {
            throw;
        }
        catch (Exception failure) when (WorkerRunner.IsNonFatal(failure))
        {
            throw WorkerRefusal.Resolution("candidate-capability-unavailable",
                "The selected host has no complete candidate inspection capability.");
        }
    }

    /// <summary>
    /// Verifies the selected host's explicit-environment enrollment using metadata only. Neither the host declaration
    /// nor the selected persistence attribute constructor is invoked.
    /// </summary>
    public static void ValidateCandidateEnvironmentEnrollment(Assembly hostAssembly, Assembly persistenceAssembly)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(hostAssembly);
            ArgumentNullException.ThrowIfNull(persistenceAssembly);

            var attributeType = persistenceAssembly.GetType(CandidateEnvironmentInputsAttributeTypeName, throwOnError: false);
            if (attributeType is null || attributeType.Assembly != persistenceAssembly ||
                !typeof(Attribute).IsAssignableFrom(attributeType) || !HasCandidateAttributeShape(attributeType))
                throw CandidateEnvironmentHostUnenrolled();

            var declarations = hostAssembly.GetCustomAttributesData()
                .Where(attribute => attribute.AttributeType == attributeType)
                .ToArray();
            if (declarations.Length != 1 || !IsCandidateEnrollmentDeclaration(declarations[0], attributeType))
                throw CandidateEnvironmentHostUnenrolled();
        }
        catch (WorkerRefusal)
        {
            throw;
        }
        catch (Exception failure) when (WorkerRunner.IsNonFatal(failure))
        {
            throw CandidateEnvironmentHostUnenrolled();
        }
    }

    private static bool IsCandidateOperation(MethodInfo? method) =>
        method is { IsStatic: true, ContainsGenericParameters: false, ReturnType: not null } &&
        method.ReturnType == typeof(Task<int>) &&
        method.GetParameters() is { Length: 3 } parameters &&
        parameters[0].ParameterType == typeof(Stream) &&
        parameters[1].ParameterType == typeof(Stream) &&
        parameters[2].ParameterType == typeof(CancellationToken);

    private static bool HasCandidateAttributeShape(Type attributeType)
    {
        if (!attributeType.IsPublic || !attributeType.IsClass || !attributeType.IsSealed)
            return false;

        var constructor = attributeType.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null,
            [typeof(int), typeof(string)], null);
        var version = attributeType.GetProperty("Version", BindingFlags.Public | BindingFlags.Instance);
        var policy = attributeType.GetProperty("Policy", BindingFlags.Public | BindingFlags.Instance);
        if (constructor is null || version is null || version.PropertyType != typeof(int) ||
            version.GetMethod is not { IsPublic: true, IsStatic: false } || version.SetMethod is not null ||
            policy is null || policy.PropertyType != typeof(string) ||
            policy.GetMethod is not { IsPublic: true, IsStatic: false } || policy.SetMethod is not null)
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

    private static bool IsCandidateEnrollmentDeclaration(CustomAttributeData declaration, Type attributeType)
    {
        var constructor = declaration.Constructor;
        if (constructor.DeclaringType != attributeType || constructor.IsPublic is false ||
            constructor.GetParameters() is not { Length: 2 } parameters ||
            parameters[0].ParameterType != typeof(int) || parameters[1].ParameterType != typeof(string) ||
            declaration.ConstructorArguments is not { Count: 2 } arguments || declaration.NamedArguments.Count != 0)
            return false;

        return arguments[0].ArgumentType == typeof(int) && arguments[0].Value is 1 &&
            arguments[1].ArgumentType == typeof(string) &&
            arguments[1].Value is string policy && policy == CandidateEnvironmentInputsPolicy;
    }

    private static bool EnumValueIs(CustomAttributeTypedArgument argument, AttributeTargets expected) =>
        argument.Value is AttributeTargets value && value == expected ||
        argument.Value is int numeric && numeric == (int)expected;

    private static WorkerRefusal CandidateCapabilityUnavailable() =>
        WorkerRefusal.Resolution("candidate-capability-unavailable",
            "The selected host has no complete candidate inspection capability.");

    private static WorkerRefusal CandidateEnvironmentHostUnenrolled() =>
        WorkerRefusal.Resolution("candidate-environment-host-unenrolled",
            "The selected host is not enrolled for explicit environment inspection.");

    /// <summary>Invokes only the separately-versioned, file-only candidate host operation.</summary>
    /// <exception cref="WorkerRefusal">The bounded candidate request, capability or host response is unavailable or invalid.</exception>
    public static Task<WorkerResponse> InvokeCandidateInspectionAsync(
        MethodInfo method,
        string hostName,
        string hostDirectory,
        WorkerCandidatePayload candidate,
        CancellationToken cancellationToken) =>
        InvokeCandidateCoreAsync(method, hostName, hostDirectory, candidate, environmentInput: null, cancellationToken);

    /// <summary>
    /// Invokes the additive explicit-environment candidate operation. Its inner envelope is deliberately distinct
    /// from the legacy request: the host receives the original captured document and correlation token, never a
    /// normalized map or the private source path.
    /// </summary>
    public static Task<WorkerResponse> InvokeCandidateEnvironmentInspectionAsync(
        MethodInfo method,
        string hostName,
        string hostDirectory,
        WorkerCandidatePayload candidate,
        WorkerEnvironmentInput environmentInput,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(environmentInput);
        WorkerContract.ValidateCandidateEnvironmentInput(candidate, environmentInput);
        return InvokeCandidateCoreAsync(method, hostName, hostDirectory, candidate, environmentInput, cancellationToken);
    }

    private static async Task<WorkerResponse> InvokeCandidateCoreAsync(
        MethodInfo method,
        string hostName,
        string hostDirectory,
        WorkerCandidatePayload candidate,
        WorkerEnvironmentInput? environmentInput,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(hostName);
        ArgumentNullException.ThrowIfNull(hostDirectory);
        ArgumentNullException.ThrowIfNull(candidate);

        if (!method.IsStatic || method.ContainsGenericParameters || method.ReturnType != typeof(Task<int>) ||
            method.GetParameters() is not { Length: 3 } parameters ||
            parameters[0].ParameterType != typeof(Stream) || parameters[1].ParameterType != typeof(Stream) ||
            parameters[2].ParameterType != typeof(CancellationToken))
            throw CandidateCapabilityUnavailable();

        cancellationToken.ThrowIfCancellationRequested();
        using var request = new CandidateBoundedMemoryStream(CandidateHostRequestMaximumBytes);
        try
        {
            object envelope;
            if (environmentInput is null)
                envelope = new { version = 1, host = new { name = hostName, directory = hostDirectory }, candidate };
            else
                envelope = new { version = 1, host = new { name = hostName, directory = hostDirectory }, candidate, environmentInput };
            await JsonSerializer.SerializeAsync(request, envelope, RequestJson, cancellationToken).ConfigureAwait(false);
            request.Position = 0;
            request.CompleteWrites();
        }
        catch (CandidateStreamLimitException)
        {
            throw WorkerRefusal.Usage("candidate-request-too-large", CandidateRequestTooLargeMessage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure) when (WorkerRunner.IsNonFatal(failure))
        {
            if (request.LimitExceeded)
                throw WorkerRefusal.Usage("candidate-request-too-large", CandidateRequestTooLargeMessage);
            throw WorkerRefusal.Usage("candidate-request-invalid", "The candidate request could not be prepared.");
        }

        using var response = new CandidateBoundedMemoryStream(CandidateHostResponseMaximumBytes);
        int processExitCode;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = method.Invoke(null, [request, response, cancellationToken]);
            if (result is not Task<int> operation)
                throw new InvalidOperationException();
            processExitCode = await operation.ConfigureAwait(false);
        }
        catch (TargetInvocationException failure) when
            (failure.InnerException is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            throw (OperationCanceledException)failure.InnerException!;
        }
        catch (TargetInvocationException failure) when
            (failure.InnerException is { } inner && inner is not BadImageFormatException && !WorkerRunner.IsNonFatal(inner))
        {
            // Reflection wraps synchronous fatal host failures; preserve their original type and stack.
            ExceptionDispatchInfo.Capture(failure.InnerException!).Throw();
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure) when (failure is BadImageFormatException || WorkerRunner.IsNonFatal(failure))
        {
            // A malformed selected host image is a closure-resolution refusal in candidate mode.
            if (response.LimitExceeded)
                throw WorkerRefusal.Resolution("candidate-response-too-large", CandidateResponseTooLargeMessage);
            throw WorkerRefusal.Resolution("candidate-host-unavailable", CandidateHostUnavailableMessage);
        }

        cancellationToken.ThrowIfCancellationRequested();
        response.CompleteWrites();
        if (response.LimitExceeded)
            throw WorkerRefusal.Resolution("candidate-response-too-large", CandidateResponseTooLargeMessage);

        try
        {
            response.Position = 0;
            var tooling = environmentInput is null
                ? await WorkerContract.ReadCandidateHostResponseAsync(response,
                    candidate.InvocationId ?? string.Empty, candidate.CaptureId ?? string.Empty, processExitCode,
                    cancellationToken)
                : await WorkerContract.ReadCandidateEnvironmentHostResponseAsync(response,
                    candidate.InvocationId ?? string.Empty, candidate.CaptureId ?? string.Empty, processExitCode,
                    cancellationToken);
            if (environmentInput is null)
                WorkerContract.ValidateCandidateHostResponse(tooling, candidate, processExitCode);
            else
                WorkerContract.ValidateCandidateEnvironmentHostResponse(tooling, candidate, processExitCode);
            return new WorkerResponse { ExitCode = processExitCode, Tooling = tooling };
        }
        catch (WorkerRefusal)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure) when (WorkerRunner.IsNonFatal(failure))
        {
            throw WorkerRefusal.Resolution("candidate-host-unavailable", CandidateHostUnavailableMessage);
        }
    }

    private sealed class CandidateBoundedMemoryStream : Stream
    {
        private readonly int maximumBytes;
        private readonly byte[] storage;
        private readonly MemoryStream buffer;
        private bool writesCompleted;

        public CandidateBoundedMemoryStream(int maximumBytes)
        {
            if (maximumBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            this.maximumBytes = maximumBytes;
            storage = new byte[maximumBytes];
            buffer = new MemoryStream(storage, writable: true);
            buffer.SetLength(0);
        }

        public bool LimitExceeded { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => !writesCompleted;
        public override long Length => buffer.Length;

        public override long Position
        {
            get => buffer.Position;
            set
            {
                if (value > maximumBytes)
                    ThrowLimitExceeded();
                buffer.Position = value;
            }
        }

        public override void Flush() => buffer.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => buffer.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => this.buffer.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => this.buffer.Read(buffer);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            this.buffer.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            this.buffer.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin)
        {
            var position = buffer.Seek(offset, origin);
            if (position > maximumBytes)
                ThrowLimitExceeded();
            return position;
        }

        public override void SetLength(long value)
        {
            EnsureWritesOpen();
            if (value > maximumBytes)
                ThrowLimitExceeded();
            buffer.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureWriteFits(count);
            this.buffer.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureWriteFits(buffer.Length);
            this.buffer.Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            EnsureWriteFits(1);
            buffer.WriteByte(value);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Write(buffer, offset, count);
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public void CompleteWrites() => writesCompleted = true;

        private void EnsureWriteFits(int count)
        {
            EnsureWritesOpen();
            if (count < 0 || buffer.Position > maximumBytes - count)
                ThrowLimitExceeded();
        }

        private void EnsureWritesOpen()
        {
            if (writesCompleted)
                throw new NotSupportedException("The bounded stream is read-only.");
        }

        private void ThrowLimitExceeded()
        {
            LimitExceeded = true;
            throw new CandidateStreamLimitException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Array.Clear(storage);
                buffer.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private sealed class CandidateStreamLimitException : Exception { }

    /// <summary>Whether a host's request type declares <paramref name="field"/>: the probe each optional request field is gated on.</summary>
    internal static bool Declares(Type? requestType, string field) =>
        requestType?.GetProperty(field, BindingFlags.Public | BindingFlags.Instance) is not null;

    /// <summary>Refuses partial or version-skewed host context APIs instead of silently choosing v1.</summary>
    internal static ToolingContextApi? BindContextApi(Type hostType, Type? contextType, Type? operationContract)
    {
        try
        {
            return BindContextApiCore(hostType, contextType, operationContract);
        }
        catch (WorkerRefusal)
        {
            throw;
        }
        catch (Exception failure) when (WorkerRunner.IsNonFatal(failure))
        {
            throw WorkerRefusal.Resolution("context-capability-unavailable",
                "The selected host's persistence context API could not be inspected.");
        }
    }

    private static ToolingContextApi? BindContextApiCore(Type hostType, Type? contextType, Type? operationContract)
    {
        ArgumentNullException.ThrowIfNull(hostType);
        var factoryCandidates = hostType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name == "CreateConfigurationContext").ToArray();
        var contextRuns = hostType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name == "RunAsync" && method.GetParameters() is { Length: 4 } parameters &&
                             parameters[2].ParameterType.FullName == ContextTypeName)
            .ToArray();
        if (contextType is null && operationContract is null && factoryCandidates.Length == 0 && contextRuns.Length == 0)
            return null;

        var factory = hostType.GetMethod("CreateConfigurationContext", BindingFlags.Public | BindingFlags.Static,
            [typeof(Stream), typeof(CancellationToken)]);
        var run = contextType is null ? null : hostType.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static,
            [typeof(Stream), typeof(Stream), contextType, typeof(CancellationToken)]);
        var contextVersion = contextType?.GetField("Version", BindingFlags.Public | BindingFlags.Static);
        var operationVersion = operationContract?.GetField("Version", BindingFlags.Public | BindingFlags.Static);
        if (contextType is null || !contextType.IsSealed || !typeof(IDisposable).IsAssignableFrom(contextType) ||
            factory is null || factory.ReturnType != contextType ||
            run is null || run.ReturnType != typeof(Task<int>) ||
            contextVersion?.IsLiteral != true || contextVersion.GetRawConstantValue() is not 1 ||
            operationVersion?.IsLiteral != true || operationVersion.GetRawConstantValue() is not 2)
            throw WorkerRefusal.Resolution("context-capability-unavailable",
                "The selected host exposes an incomplete or unsupported persistence configuration-context API.");

        return new ToolingContextApi(factory, run, contextType);
    }

    /// <summary>Creates one opaque host snapshot; no configuration value is projected into this worker.</summary>
    internal IDisposable CreateConfigurationContext(object descriptor, CancellationToken cancellationToken)
    {
        var api = contextApi ?? throw WorkerRefusal.Resolution("context-capability-unavailable",
            "The selected host has no persistence configuration-context API.");
        using var input = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(descriptor, RequestJson));
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var created = api.Factory.Invoke(null, [input, cancellationToken]);
            return created is IDisposable disposable && api.ContextType.IsInstanceOfType(created)
                ? disposable
                : throw WorkerRefusal.Resolution("configuration-context-invalid",
                    "The selected host did not return a usable configuration context.");
        }
        catch (TargetInvocationException failure) when (failure.InnerException is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            throw (OperationCanceledException)failure.InnerException!;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (WorkerRefusal)
        {
            throw;
        }
        catch (Exception failure) when (WorkerRunner.IsNonFatal(failure))
        {
            throw WorkerRefusal.Resolution("configuration-context-invalid",
                "The selected host configuration context could not be created.");
        }
    }

    /// <summary>Inspects resource applicability and validates the closed v2 response before any legacy fallback.</summary>
    internal async Task<(int ExitCode, JsonElement Response, string? Outcome)> InspectConfigurationContextAsync(
        IDisposable context,
        object? selection,
        CancellationToken cancellationToken) =>
        await InvokeConfigurationContextAsync(context,
            new { version = 2, command = "inspect-context", selection }, "inspect-context", cancellationToken);

    /// <summary>Invokes the context operation and validates the closed host response before forwarding it.</summary>
    internal async Task<(int ExitCode, JsonElement Response, string? Outcome)> InvokeConfigurationContextAsync(
        IDisposable context,
        object operation,
        string command,
        CancellationToken cancellationToken)
    {
        var api = contextApi ?? throw WorkerRefusal.Resolution("context-capability-unavailable",
            "The selected host has no persistence configuration-context API.");
        if (!api.ContextType.IsInstanceOfType(context))
            throw WorkerRefusal.Resolution("configuration-context-invalid", "The selected host context has the wrong type.");
        using var input = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(operation, RequestJson));
        using var output = new MemoryStream();
        int exitCode;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            exitCode = await (Task<int>)api.RunAsync.Invoke(null, [input, output, context, cancellationToken])!;
        }
        catch (TargetInvocationException failure) when (failure.InnerException is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            throw (OperationCanceledException)failure.InnerException!;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure) when (WorkerRunner.IsNonFatal(failure))
        {
            throw WorkerRefusal.Resolution("configuration-context-invalid",
                "The selected host configuration context operation could not be completed.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var document = JsonDocument.Parse(output.ToArray());
            if (HasDuplicateFields(document.RootElement))
                throw WorkerRefusal.Resolution("context-capability-unavailable",
                    "The selected host returned a repeated context response field.");
            var parsed = document.RootElement.Deserialize<HostContextInspectionResponse>(WorkerContract.Json)
                         ?? throw WorkerRefusal.Resolution("context-capability-unavailable",
                             "The selected host returned an empty context inspection response.");
            var outcome = parsed.Validate(command, exitCode);
            return (exitCode, document.RootElement.Clone(), outcome);
        }
        catch (JsonException)
        {
            throw WorkerRefusal.Resolution("context-capability-unavailable",
                "The selected host returned an invalid context inspection response.");
        }
    }

    internal static void DisposeConfigurationContext(IDisposable context)
    {
        try
        {
            context.Dispose();
        }
        catch (Exception failure) when (WorkerRunner.IsNonFatal(failure))
        {
            throw WorkerRefusal.Resolution("configuration-context-invalid",
                "The selected host configuration context could not be released.");
        }
    }

    private static bool HasDuplicateFields(JsonElement value)
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

    /// <summary>
    /// The refusal for a host that selects its engine through the capability key while pinning a persistence
    /// build that predates the check. Loud on purpose: the quiet alternative is an artifact generated for a
    /// provider the host's closure never agreed to.
    /// </summary>
    public static WorkerRefusal CapabilitySelectionUnsupported(IReadOnlyList<string> options, string? pinnedVersion, string toolVersion) =>
        WorkerRefusal.Resolution(
            "host-tooling-capability-unaware",
            $"This host selects its provider engine with '{HostCapabilitySelection.Key}' " +
            $"({string.Join(", ", options.Select(option => $"'{option}'"))}), and the " +
            $"{HostClosure.PersistenceAssemblyName} {pinnedVersion ?? "(version unknown)"} it pins predates the check " +
            "that keeps --provider authoritative against that selection. Nothing is scripted from a selection this " +
            $"host's own build cannot compare: upgrade its {HostClosure.PersistenceAssemblyName} to {toolVersion} or " +
            "newer, or remove the key and name the engine as an explicit root in the host's package closure.");

    /// <summary>The canonical spelling of a provider name the operator may have aliased, decided by the host's own binding table.</summary>
    public string CanonicalProvider(string provider)
    {
        try
        {
            return (string)select.Invoke(null, [provider, "relational", "Sqlite", "SqlServer", "PostgreSql", "MySql"])!;
        }
        catch (TargetInvocationException failure) when (failure.InnerException is ArgumentException inner)
        {
            throw WorkerRefusal.Usage("unknown-provider", inner.Message);
        }
    }

    /// <summary>The package the host's binding table expects this provider's engine to come from.</summary>
    public string ProviderPackageId(string provider) => (string)providerPackageId.Invoke(null, [provider])!;

    /// <summary>Why this provider's engine would not bind in this process, or <c>null</c> when it binds.</summary>
    public string? DescribeBindingFailure(string provider) => (string?)describeBindingFailure.Invoke(null, [provider]);

    /// <summary>
    /// Runs one command and returns both the exit code and the response verbatim. The code is the one
    /// <c>RunAsync</c> itself returned, not one this worker derived: a refusal classified twice is a refusal
    /// that can acquire two meanings.
    /// </summary>
    public async Task<(int ExitCode, JsonElement Response)> InvokeAsync(object request, CancellationToken cancellationToken)
    {
        using var input = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(request, RequestJson));
        using var output = new MemoryStream();
        int exitCode;
        try
        {
            exitCode = await (Task<int>)runAsync.Invoke(null, [input, output])!;
        }
        catch (TargetInvocationException failure) when (failure.InnerException is not null)
        {
            throw WorkerRefusal.Resolution(
                "host-tooling-failed",
                $"The host's migration tooling entry point failed: {failure.InnerException.GetType().Name}: {failure.InnerException.Message}");
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var document = JsonDocument.Parse(output.ToArray());
        return (exitCode, document.RootElement.Clone());
    }
}

internal sealed record ToolingContextApi(MethodInfo Factory, MethodInfo RunAsync, Type ContextType);
