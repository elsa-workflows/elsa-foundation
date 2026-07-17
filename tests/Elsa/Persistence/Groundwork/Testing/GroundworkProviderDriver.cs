using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Groundwork.Core.Manifests;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using ElsaRuntimeSchemaAdmissionResult = Elsa.Persistence.Groundwork.Unified.Composition.GroundworkRuntimeSchemaAdmissionResult;

namespace Elsa.Persistence.Groundwork.Testing;

[Flags]
public enum GroundworkTopologyCapabilities
{
    None = 0,
    PersistentStorage = 1 << 0,
    IndependentClients = 1 << 1,
    MultiDocumentTransactions = 1 << 2,
    TransactionCapableMongoTopology = 1 << 3,
    ExternalProcessRestart = 1 << 4
}

public sealed record GroundworkProviderTopology
{
    private static readonly HashSet<string> TopologyCatalog =
    [
        "file-backed-distinct-connections",
        "real-sqlserver-container",
        "real-postgresql-container",
        "transaction-capable-replica-set",
        "transaction-capable-sharded-cluster"
    ];
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> ProviderTopologies =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["sqlite"] = new HashSet<string>(["file-backed-distinct-connections"], StringComparer.Ordinal),
            ["sqlserver"] = new HashSet<string>(["real-sqlserver-container"], StringComparer.Ordinal),
            ["postgresql"] = new HashSet<string>(["real-postgresql-container"], StringComparer.Ordinal),
            ["mongodb"] = new HashSet<string>(
                ["transaction-capable-replica-set", "transaction-capable-sharded-cluster"],
                StringComparer.Ordinal)
        };

    public GroundworkProviderTopology(string providerKey, string description, GroundworkTopologyCapabilities capabilities)
    {
        EvidenceCatalog.EnsureProviderKey(providerKey);
        if (!TopologyCatalog.Contains(description))
            throw new ArgumentException($"Unknown topology catalog value '{description}'.", nameof(description));
        if (!ProviderTopologies[providerKey].Contains(description))
            throw new ArgumentException($"Topology '{description}' is not valid for provider '{providerKey}'.", nameof(description));
        ProviderKey = providerKey;
        Description = description;
        Capabilities = capabilities;
    }

    public string ProviderKey { get; }
    public string Description { get; }
    public GroundworkTopologyCapabilities Capabilities { get; }

    public void EnsureSupports(GroundworkTopologyCapabilities required)
    {
        var missing = required & ~Capabilities;
        if (missing != GroundworkTopologyCapabilities.None)
            throw new GroundworkProviderTopologyException(ProviderKey, Description, missing);
    }
}

public sealed class GroundworkProviderTopologyException(
    string providerKey,
    string topology,
    GroundworkTopologyCapabilities missingCapabilities)
    : InvalidOperationException(
        $"Provider '{providerKey}' topology '{topology}' does not support required capabilities: {missingCapabilities}.")
{
    public string ProviderKey { get; } = providerKey;
    public string Topology { get; } = topology;
    public GroundworkTopologyCapabilities MissingCapabilities { get; } = missingCapabilities;
}

public interface IGroundworkTopologyRejectionProbe
{
    ValueTask<GroundworkSanitizedEvidence> CaptureTopologyRejectionAsync(
        CancellationToken cancellationToken = default);
}

public sealed record GroundworkProviderDescriptor
{
    private static readonly IReadOnlyDictionary<string, string> ProviderIdentities =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sqlite"] = "groundwork-sqlite",
            ["sqlserver"] = "groundwork-sqlserver",
            ["postgresql"] = "groundwork-postgresql",
            ["mongodb"] = "groundwork-mongodb"
        };

    public GroundworkProviderDescriptor(
        string providerKey,
        string providerIdentity,
        string providerVersion,
        GroundworkProviderTopology topology)
    {
        EvidenceCatalog.EnsureProviderKey(providerKey);
        if (!ProviderIdentities.TryGetValue(providerKey, out var expectedIdentity) ||
            !string.Equals(providerIdentity, expectedIdentity, StringComparison.Ordinal))
            throw new ArgumentException(
                $"Provider key '{providerKey}' requires identity '{expectedIdentity}'.",
                nameof(providerIdentity));
        if (string.IsNullOrWhiteSpace(providerVersion))
            throw new ArgumentException("A provider version is required.", nameof(providerVersion));
        ArgumentNullException.ThrowIfNull(topology);
        if (!string.Equals(providerKey, topology.ProviderKey, StringComparison.Ordinal))
            throw new ArgumentException("The topology provider key must match the descriptor provider key.", nameof(topology));

        ProviderKey = providerKey;
        ProviderIdentity = providerIdentity;
        ProviderVersion = providerVersion;
        Topology = topology;
    }

    public string ProviderKey { get; }
    public string ProviderIdentity { get; }
    public string ProviderVersion { get; }
    public GroundworkProviderTopology Topology { get; }
}

public sealed record GroundworkProcessLaunchDescriptor
{
    public GroundworkProcessLaunchDescriptor(
        string executablePath,
        IEnumerable<string> arguments,
        string protocolVersion)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new ArgumentException("An executable path is required.", nameof(executablePath));
        if (string.IsNullOrWhiteSpace(protocolVersion))
            throw new ArgumentException("A process-probe protocol version is required.", nameof(protocolVersion));

        ExecutablePath = executablePath;
        Arguments = arguments.ToArray();
        if (Arguments.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Process-launch arguments cannot be blank.", nameof(arguments));
        ProtocolVersion = protocolVersion;
        var launchCommand = string.Join('\n', new[] { ExecutablePath }.Concat(Arguments));
        GroundworkSanitizedEvidence.Create("process-launch", launchCommand);
        var canonicalDescriptor = string.Join(
            '\n',
            new[] { Path.GetFileName(ExecutablePath), StateLocatorTransport, ProtocolVersion }
                .Concat(Arguments.Select(NormalizeArgument)));
        Fingerprint = StableDigest.FromText(canonicalDescriptor);
    }

    public string ExecutablePath { get; }
    public IReadOnlyList<string> Arguments { get; }
    public string StateLocatorTransport => "redirected-standard-input";
    public string ProtocolVersion { get; }
    public string Fingerprint { get; }

    private static string NormalizeArgument(string argument)
    {
        var normalized = Path.IsPathFullyQualified(argument) ? Path.GetFileName(argument) : argument;
        return $"{normalized.Length}:{normalized}";
    }
}

public enum GroundworkProcessProbeOperation
{
    Save,
    Load,
    IdentityCreateUser,
    IdentityFindByNormalizedUserName,
    IdentityDuplicateCreate
}

public sealed record GroundworkProcessProbeRequest
{
    public GroundworkProcessProbeRequest(
        string probeId,
        GroundworkProcessProbeOperation operation,
        string documentId,
        string? value = null)
    {
        EvidenceCatalog.EnsureSlug(probeId, nameof(probeId));
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("A probe document ID is required.", nameof(documentId));
        var requiresValue = operation is GroundworkProcessProbeOperation.Save or
            GroundworkProcessProbeOperation.IdentityCreateUser or
            GroundworkProcessProbeOperation.IdentityFindByNormalizedUserName or
            GroundworkProcessProbeOperation.IdentityDuplicateCreate;
        if (requiresValue && value is null)
            throw new ArgumentException("The selected process-probe operation requires a value.", nameof(value));
        ProbeId = probeId;
        Operation = operation;
        DocumentId = documentId;
        Value = value;
        PayloadSha256 = value is null ? null : StableDigest.FromText(value);
    }

    public string ProbeId { get; }
    public GroundworkProcessProbeOperation Operation { get; }
    public string DocumentId { get; }
    public string? Value { get; }
    public string? PayloadSha256 { get; }

    public override string ToString() =>
        $"{nameof(GroundworkProcessProbeRequest)} {{ ProbeId = {ProbeId}, Operation = {Operation}, " +
        $"DocumentId = [REDACTED], PayloadSha256 = {PayloadSha256 ?? "-"} }}";
}

public sealed record GroundworkProcessProbeResult
{
    public GroundworkProcessProbeResult(
        string probeId,
        string providerIdentity,
        GroundworkProcessProbeOperation operation,
        int processId,
        int exitCode,
        string launchDescriptorFingerprint,
        string payloadSha256,
        string schemaVersion,
        long documentVersion)
    {
        EvidenceCatalog.EnsureSlug(probeId, nameof(probeId));
        EvidenceCatalog.EnsureProviderIdentity(providerIdentity);
        if (processId <= 0)
            throw new ArgumentOutOfRangeException(nameof(processId));
        if (!EvidenceCatalog.IsSha256(launchDescriptorFingerprint))
            throw new ArgumentException("A launch-descriptor SHA-256 fingerprint is required.", nameof(launchDescriptorFingerprint));
        if (!EvidenceCatalog.IsSha256(payloadSha256))
            throw new ArgumentException("A payload SHA-256 digest is required.", nameof(payloadSha256));
        if (string.IsNullOrWhiteSpace(schemaVersion))
            throw new ArgumentException("A probe schema version is required.", nameof(schemaVersion));
        if (documentVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(documentVersion), "A persisted document version is required.");
        ProbeId = probeId;
        ProviderIdentity = providerIdentity;
        Operation = operation;
        ProcessId = processId;
        ExitCode = exitCode;
        LaunchDescriptorFingerprint = launchDescriptorFingerprint;
        PayloadSha256 = payloadSha256;
        SchemaVersion = schemaVersion;
        DocumentVersion = documentVersion;
    }

    public string ProbeId { get; }
    public string ProviderIdentity { get; }
    public GroundworkProcessProbeOperation Operation { get; }
    public int ProcessId { get; }
    public int ExitCode { get; }
    public string LaunchDescriptorFingerprint { get; }
    public string PayloadSha256 { get; }
    public string SchemaVersion { get; }
    public long DocumentVersion { get; }
}

public sealed class GroundworkProviderClient : IAsyncDisposable
{
    private Func<ValueTask>? _disposeAsync;
    private Action? _onDisposed;
    private GroundworkProviderDescriptor? _ownerDescriptor;

    public GroundworkProviderClient(
        Guid clientId,
        IServiceProvider services,
        IDocumentStore documentStore,
        Func<ValueTask> disposeAsync,
        IBoundedDocumentStore? boundedDocumentStore = null)
    {
        if (clientId == Guid.Empty)
            throw new ArgumentException("A non-empty client ID is required.", nameof(clientId));
        ClientId = clientId;
        Services = services ?? throw new ArgumentNullException(nameof(services));
        DocumentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
        BoundedDocumentStore = boundedDocumentStore ?? documentStore as IBoundedDocumentStore;
        PhysicalDocumentQueryExplainer = BoundedDocumentStore as IPhysicalDocumentQueryExplainer;
        _disposeAsync = disposeAsync ?? throw new ArgumentNullException(nameof(disposeAsync));
    }

    public Guid ClientId { get; }
    public IServiceProvider Services { get; }
    public IDocumentStore DocumentStore { get; }
    public IBoundedDocumentStore? BoundedDocumentStore { get; }
    public IPhysicalDocumentQueryExplainer? PhysicalDocumentQueryExplainer { get; }
    public GroundworkProviderDescriptor OwnerDescriptor =>
        Volatile.Read(ref _ownerDescriptor)
        ?? throw new InvalidOperationException("The provider client was not opened and owned by a Groundwork provider driver.");

    public async ValueTask DisposeAsync()
    {
        var disposeAsync = Interlocked.Exchange(ref _disposeAsync, null);
        if (disposeAsync is null)
            return;
        try
        {
            await disposeAsync();
        }
        finally
        {
            Interlocked.Exchange(ref _onDisposed, null)?.Invoke();
        }
    }

    internal void RegisterDisposalCallback(Action onDisposed)
    {
        ArgumentNullException.ThrowIfNull(onDisposed);
        if (Interlocked.CompareExchange(ref _onDisposed, onDisposed, null) is not null)
            throw new InvalidOperationException("A provider-client disposal callback is already registered.");
    }

    internal void StampOwnerDescriptor(GroundworkProviderDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (Interlocked.CompareExchange(ref _ownerDescriptor, descriptor, null) is not null)
            throw new InvalidOperationException("A provider client owner descriptor can only be stamped once.");
    }
}

public abstract class GroundworkProviderDriver : IAsyncDisposable
{
    private enum StoreMode
    {
        Logical,
        Physical
    }

    private readonly ConcurrentDictionary<Guid, GroundworkProviderClient> _clients = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private bool _initialized;
    private bool _disposed;
    private StoreMode _storeMode;

    public abstract GroundworkProviderDescriptor Descriptor { get; }
    public abstract GroundworkTopologyCapabilities RequiredTopology { get; }
    public abstract GroundworkCompositionFingerprint CompositionFingerprint { get; }
    public abstract GroundworkProcessLaunchDescriptor ProcessLaunchDescriptor { get; }
    public abstract string ProbeDocumentKind { get; }
    public GroundworkFailureController Failures { get; } = new();

    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (_initialized)
                return;
            Descriptor.Topology.EnsureSupports(RequiredTopology | GroundworkTopologyCapabilities.ExternalProcessRestart);
            await InitializeCoreAsync(cancellationToken);
            _storeMode = StoreMode.Logical;
            _initialized = true;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            EnsureInitialized();
            if (!_clients.IsEmpty)
                throw new InvalidOperationException("A provider driver cannot reset while client leases are active.");
            await ResetCoreAsync(cancellationToken);
            _storeMode = StoreMode.Logical;
            Failures.Reset();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public ValueTask ResetPhysicalAsync(CancellationToken cancellationToken = default) =>
        ResetPhysicalAsync(manifestSources: null, cancellationToken);

    public async ValueTask ResetPhysicalAsync(
        IReadOnlyCollection<IGroundworkStorageManifestSource>? manifestSources,
        CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            EnsureInitialized();
            if (!_clients.IsEmpty)
                throw new InvalidOperationException("A provider driver cannot reset while client leases are active.");
            await ResetPhysicalCoreAsync(manifestSources, cancellationToken);
            _storeMode = StoreMode.Physical;
            Failures.Reset();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public ValueTask<GroundworkProviderClient> OpenClientAsync(CancellationToken cancellationToken = default) =>
        OpenTrackedClientAsync(StoreMode.Logical, OpenClientCoreAsync, cancellationToken);

    public ValueTask<GroundworkProviderClient> OpenClientAsync(
        StorageManifest manifest,
        DocumentStoreAccess access,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(access);
        return OpenTrackedClientAsync(
            StoreMode.Logical,
            (clientId, token) => OpenClientCoreAsync(clientId, manifest, access, token),
            cancellationToken);
    }

    public ValueTask<GroundworkProviderClient> OpenPhysicalClientAsync(CancellationToken cancellationToken = default) =>
        OpenPhysicalClientAsync(GroundworkTestAccess.DefaultScoped, cancellationToken);

    public ValueTask<GroundworkProviderClient> OpenPhysicalClientAsync(
        DocumentStoreAccess access,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        return OpenTrackedClientAsync(
            StoreMode.Physical,
            (clientId, token) => OpenPhysicalClientCoreAsync(clientId, access, token),
            cancellationToken);
    }

    private async ValueTask<GroundworkProviderClient> OpenTrackedClientAsync(
        StoreMode requiredMode,
        Func<Guid, CancellationToken, ValueTask<GroundworkProviderClient>> openClientAsync,
        CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            EnsureInitialized();
            if (_storeMode != requiredMode)
                throw new InvalidOperationException($"The provider driver is prepared for {_storeMode.ToString().ToLowerInvariant()} stores, not {requiredMode.ToString().ToLowerInvariant()} stores.");
            var clientId = Guid.NewGuid();
            GroundworkProviderClient? client = null;
            try
            {
                client = await openClientAsync(clientId, cancellationToken);
                if (client.ClientId != clientId)
                    throw new InvalidOperationException("The provider returned a client with a different identity.");
                var ownerDescriptor = Descriptor;
                client.StampOwnerDescriptor(ownerDescriptor);
                if (!ReferenceEquals(client.OwnerDescriptor, ownerDescriptor))
                    throw new InvalidOperationException("The provider client owner descriptor does not match its opening driver.");
                if (_clients.Values.Any(existing =>
                        ReferenceEquals(existing.Services, client.Services) ||
                        ReferenceEquals(existing.DocumentStore, client.DocumentStore)))
                    throw new InvalidOperationException("Provider clients must not share service providers or document-store adapters.");
                if (!_clients.TryAdd(clientId, client))
                    throw new InvalidOperationException("The provider returned a duplicate client identity.");
                client.RegisterDisposalCallback(() => _clients.TryRemove(clientId, out _));
                return client;
            }
            catch
            {
                _clients.TryRemove(clientId, out _);
                if (client is not null)
                    await client.DisposeAsync();
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask<GroundworkProcessProbeResult> RunInNewProcessAsync(
        GroundworkProcessProbeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            EnsureInitialized();
            if (!_clients.IsEmpty)
                throw new InvalidOperationException("A process-restart probe requires all in-process client leases to be closed.");
            Descriptor.Topology.EnsureSupports(GroundworkTopologyCapabilities.ExternalProcessRestart);
            var result = await RunInNewProcessCoreAsync(request, cancellationToken);
            if (result.ProcessId == Environment.ProcessId)
                throw new InvalidOperationException("A process-restart probe must execute outside the test process.");
            if (result.ExitCode != 0)
                throw new InvalidOperationException($"The process-restart probe exited with code {result.ExitCode}.");
            if (!string.Equals(result.ProbeId, request.ProbeId, StringComparison.Ordinal) ||
                result.Operation != request.Operation)
                throw new InvalidOperationException("The process-restart result does not match the requested probe.");
            if (!string.Equals(result.ProviderIdentity, Descriptor.ProviderIdentity, StringComparison.Ordinal))
                throw new InvalidOperationException("The process-restart result does not match the declared provider identity.");
            if (!string.Equals(result.LaunchDescriptorFingerprint, ProcessLaunchDescriptor.Fingerprint, StringComparison.Ordinal))
                throw new InvalidOperationException("The process-restart result does not match the declared launch descriptor.");
            if (!string.Equals(result.SchemaVersion, ProcessLaunchDescriptor.ProtocolVersion, StringComparison.Ordinal))
                throw new InvalidOperationException("The process-restart result does not match the declared protocol version.");
            if (request.PayloadSha256 is not null &&
                !string.Equals(result.PayloadSha256, request.PayloadSha256, StringComparison.Ordinal))
                throw new InvalidOperationException("The process-restart result does not match the requested payload digest.");
            return result;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public ValueTask<GroundworkSanitizedEvidence> CaptureDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        return CaptureDiagnosticsCoreAsync(cancellationToken);
    }

    public ValueTask<GroundworkNativePlanEvidence> CaptureNativePlanAsync(
        GroundworkExecutionPath executionPath,
        string scenarioId,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(executionPath);
        return CaptureNativePlanCoreAsync(executionPath, scenarioId, cancellationToken);
    }

    public ValueTask PrepareNativeRoutePlanDatasetAsync(
        IReadOnlyCollection<GroundworkNativeRoutePlanRequest> requests,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(requests);
        if (_storeMode != StoreMode.Physical)
            throw new InvalidOperationException("Native route plans require an applied physical target.");
        if (requests.Count == 0)
            throw new ArgumentException("At least one native route is required to seed a physical evidence dataset.", nameof(requests));
        return PrepareNativeRoutePlanDatasetCoreAsync(requests, cancellationToken);
    }

    public ValueTask<GroundworkNativeRoutePlanResult> CaptureNativeRoutePlanAsync(
        GroundworkNativeRoutePlanRequest request,
        PhysicalDocumentQueryExplanation explanation,
        int materializedCandidateCount,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(explanation);
        if (_storeMode != StoreMode.Physical)
            throw new InvalidOperationException("Native route plans require an applied physical target.");
        if (!string.Equals(explanation.Plan.StorageUnit.Value, request.DocumentKind, StringComparison.Ordinal) ||
            !string.Equals(explanation.Plan.QueryIdentity, request.QueryIdentity, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Native route evidence must describe the exact requested document kind and bounded-query identity.");
        }
        return CaptureNativeRoutePlanCoreAsync(
            request,
            explanation,
            materializedCandidateCount,
            cancellationToken);
    }

    public async ValueTask<GroundworkPhysicalSchemaManifestSource> PrepareSchemaParityAsync(
        IReadOnlyCollection<IGroundworkStorageManifestSource> manifestSources,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifestSources);
        if (manifestSources.Count == 0)
            throw new ArgumentException("At least one shell-selected manifest source is required.", nameof(manifestSources));
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            EnsureInitialized();
            if (!_clients.IsEmpty)
                throw new InvalidOperationException("Schema parity cannot reset a provider while client leases are active.");
            return await PrepareSchemaParityCoreAsync(manifestSources, cancellationToken);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public ValueTask<GroundworkProviderSchemaStateSnapshot> CaptureSchemaStateAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        return CaptureSchemaStateCoreAsync(cancellationToken);
    }

    public ValueTask<ElsaRuntimeSchemaAdmissionResult> InspectSchemaParityAdmissionAsync(
        GroundworkPhysicalSchemaManifestSource source,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(source);
        return InspectSchemaParityAdmissionCoreAsync(source, cancellationToken);
    }

    public async Task<GroundworkSchemaToolResult> RunSchemaToolAsync(
        string command,
        Type manifestSourceType,
        CancellationToken cancellationToken = default,
        params string[] extraArguments)
    {
        EnsureInitialized();
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(manifestSourceType);
        ArgumentNullException.ThrowIfNull(extraArguments);
        var connection = SchemaToolConnectionCore();
        const string connectionEnvironmentVariable = "ELSA_GROUNDWORK_SCHEMA_PARITY_CONNECTION";
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = FindRepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[]
                 {
                     "tool", "run", "groundwork", "--", command,
                     "--manifest-assembly", manifestSourceType.Assembly.Location,
                     "--manifest-type", manifestSourceType.FullName!,
                     "--provider", Descriptor.ProviderKey,
                     "--output", "json"
                 })
            start.ArgumentList.Add(argument);
        if (!extraArguments.Contains("--offline", StringComparer.Ordinal))
        {
            start.Environment[connectionEnvironmentVariable] = connection.ConnectionString;
            start.ArgumentList.Add("--connection-env");
            start.ArgumentList.Add(connectionEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(connection.DatabaseName))
            {
                start.ArgumentList.Add("--database");
                start.ArgumentList.Add(connection.DatabaseName);
            }
        }
        foreach (var argument in extraArguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Groundwork.Tool.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = (await outputTask).Replace(connection.ConnectionString, "<redacted>", StringComparison.Ordinal);
        var error = (await errorTask).Replace(connection.ConnectionString, "<redacted>", StringComparison.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(output);
            return new GroundworkSchemaToolResult(process.ExitCode, document.RootElement.Clone(), error);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Groundwork.Tool returned invalid JSON ({exception.GetType().Name}); process output was suppressed.");
        }
    }

    protected abstract ValueTask InitializeCoreAsync(CancellationToken cancellationToken);
    protected abstract ValueTask ResetCoreAsync(CancellationToken cancellationToken);
    protected abstract ValueTask ResetPhysicalCoreAsync(
        IReadOnlyCollection<IGroundworkStorageManifestSource>? manifestSources,
        CancellationToken cancellationToken);
    protected abstract ValueTask<GroundworkProviderClient> OpenClientCoreAsync(Guid clientId, CancellationToken cancellationToken);
    protected abstract ValueTask<GroundworkProviderClient> OpenPhysicalClientCoreAsync(
        Guid clientId,
        DocumentStoreAccess access,
        CancellationToken cancellationToken);
    protected abstract ValueTask<GroundworkProviderClient> OpenClientCoreAsync(
        Guid clientId,
        StorageManifest manifest,
        DocumentStoreAccess access,
        CancellationToken cancellationToken);
    protected abstract ValueTask<GroundworkProcessProbeResult> RunInNewProcessCoreAsync(
        GroundworkProcessProbeRequest request,
        CancellationToken cancellationToken);
    protected abstract ValueTask<GroundworkSanitizedEvidence> CaptureDiagnosticsCoreAsync(CancellationToken cancellationToken);
    protected abstract ValueTask<GroundworkNativePlanEvidence> CaptureNativePlanCoreAsync(
        GroundworkExecutionPath executionPath,
        string scenarioId,
        CancellationToken cancellationToken);
    protected abstract ValueTask PrepareNativeRoutePlanDatasetCoreAsync(
        IReadOnlyCollection<GroundworkNativeRoutePlanRequest> requests,
        CancellationToken cancellationToken);
    protected abstract ValueTask<GroundworkNativeRoutePlanResult> CaptureNativeRoutePlanCoreAsync(
        GroundworkNativeRoutePlanRequest request,
        PhysicalDocumentQueryExplanation explanation,
        int materializedCandidateCount,
        CancellationToken cancellationToken);
    protected abstract ValueTask<GroundworkPhysicalSchemaManifestSource> PrepareSchemaParityCoreAsync(
        IReadOnlyCollection<IGroundworkStorageManifestSource> manifestSources,
        CancellationToken cancellationToken);
    protected abstract ValueTask<GroundworkProviderSchemaStateSnapshot> CaptureSchemaStateCoreAsync(
        CancellationToken cancellationToken);
    protected abstract ValueTask<ElsaRuntimeSchemaAdmissionResult> InspectSchemaParityAdmissionCoreAsync(
        GroundworkPhysicalSchemaManifestSource source,
        CancellationToken cancellationToken);
    protected abstract SchemaToolConnection SchemaToolConnectionCore();
    protected abstract ValueTask DisposeCoreAsync();

    protected sealed record SchemaToolConnection(string ConnectionString, string? DatabaseName = null);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            if (_disposed)
                return;
            await DisposeClientsAsync();
            await DisposeCoreAsync();
            _disposed = true;
            _initialized = false;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async ValueTask DisposeClientsAsync()
    {
        foreach (var client in _clients.Values)
            await client.DisposeAsync();
        _clients.Clear();
    }

    private void EnsureInitialized()
    {
        ThrowIfDisposed();
        if (!_initialized)
            throw new InvalidOperationException("The provider driver has not been initialized.");
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
