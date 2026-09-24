using System.Reflection;
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
    private const string ToolingHostTypeName = "Elsa.Persistence.EntityFramework.Tooling.EfToolingHost";
    private const string ProviderBindingTypeName = "Elsa.Persistence.EntityFramework.EfRelationalProviderBinding";
    private const string RequestTypeName = "Elsa.Persistence.EntityFramework.Tooling.EfToolingRequest";
    private const string ContextTypeName = "Elsa.Persistence.EntityFramework.Tooling.EfToolingConfigurationContext";
    private const string ContextContractTypeName = "Elsa.Persistence.EntityFramework.Tooling.EfToolingContextContract";
    private const string CapabilitySelectionField = "CapabilitySelection";

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

    private ToolingEntryPoint(
        MethodInfo runAsync,
        MethodInfo providerPackageId,
        MethodInfo describeBindingFailure,
        MethodInfo select,
        bool supportsCapabilitySelection,
        ToolingContextApi? contextApi)
    {
        this.runAsync = runAsync;
        this.providerPackageId = providerPackageId;
        this.describeBindingFailure = describeBindingFailure;
        this.select = select;
        this.contextApi = contextApi;
        SupportsCapabilitySelection = supportsCapabilitySelection;
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

        var capabilitySelection = persistence.GetType(RequestTypeName, throwOnError: false)
            ?.GetProperty(CapabilitySelectionField, BindingFlags.Public | BindingFlags.Instance) is not null;
        var contextApi = BindContextApi(
            hostType!,
            persistence.GetType(ContextTypeName, throwOnError: false),
            persistence.GetType(ContextContractTypeName, throwOnError: false));

        return new(run, packageId, describe, canonical, capabilitySelection, contextApi);
    }

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
                "The selected host configuration context could not be inspected.");
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
