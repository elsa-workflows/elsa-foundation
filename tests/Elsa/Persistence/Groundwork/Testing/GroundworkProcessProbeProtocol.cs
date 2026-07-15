using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>
/// Sensitive provider state sent only to the child process over redirected standard input.
/// It must never be placed in process arguments, environment variables, diagnostics, or results.
/// </summary>
public sealed record GroundworkProcessProbeState
{
    public GroundworkProcessProbeState(string connectionString, string? mongoDatabaseName = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A provider state locator is required.", nameof(connectionString));
        if (mongoDatabaseName is not null && string.IsNullOrWhiteSpace(mongoDatabaseName))
            throw new ArgumentException("A MongoDB database name cannot be blank.", nameof(mongoDatabaseName));
        ConnectionString = connectionString;
        MongoDatabaseName = mongoDatabaseName;
    }

    public string ConnectionString { get; }
    public string? MongoDatabaseName { get; }

    public override string ToString() => $"{nameof(GroundworkProcessProbeState)} {{ StateLocator = [REDACTED] }}";
}

/// <summary>A closed child-process command envelope. Its serialized form is written only to redirected stdin.</summary>
public sealed record GroundworkProcessProbeCommand
{
    [JsonConstructor]
    public GroundworkProcessProbeCommand(
        string protocolVersion,
        string launchDescriptorFingerprint,
        string providerKey,
        string providerIdentity,
        string providerVersion,
        string documentKind,
        GroundworkProcessProbeRequest request,
        GroundworkProcessProbeState state)
    {
        if (string.IsNullOrWhiteSpace(protocolVersion))
            throw new ArgumentException("A protocol version is required.", nameof(protocolVersion));
        if (!GroundworkProcessProbeProtocol.IsSha256(launchDescriptorFingerprint))
            throw new ArgumentException("A launch-descriptor SHA-256 fingerprint is required.", nameof(launchDescriptorFingerprint));
        var descriptor = new GroundworkProviderDescriptor(
            providerKey,
            providerIdentity,
            providerVersion,
            GroundworkProcessProbeProtocol.CreateProtocolTopology(providerKey));
        if (string.IsNullOrWhiteSpace(documentKind))
            throw new ArgumentException("A document kind is required.", nameof(documentKind));
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(state);
        if (providerKey == "mongodb" && string.IsNullOrWhiteSpace(state.MongoDatabaseName))
            throw new ArgumentException("MongoDB process probes require a database name inside stdin state.", nameof(state));
        if (providerKey != "mongodb" && state.MongoDatabaseName is not null)
            throw new ArgumentException("Only MongoDB process probes accept a database name.", nameof(state));

        ProtocolVersion = protocolVersion;
        LaunchDescriptorFingerprint = launchDescriptorFingerprint;
        ProviderKey = descriptor.ProviderKey;
        ProviderIdentity = descriptor.ProviderIdentity;
        ProviderVersion = descriptor.ProviderVersion;
        DocumentKind = documentKind;
        Request = request;
        State = state;
    }

    public string ProtocolVersion { get; }
    public string LaunchDescriptorFingerprint { get; }
    public string ProviderKey { get; }
    public string ProviderIdentity { get; }
    public string ProviderVersion { get; }
    public string DocumentKind { get; }
    public GroundworkProcessProbeRequest Request { get; }
    public GroundworkProcessProbeState State { get; }

    public override string ToString() =>
        $"{nameof(GroundworkProcessProbeCommand)} {{ ProtocolVersion = {ProtocolVersion}, ProviderKey = {ProviderKey}, " +
        $"ProviderIdentity = {ProviderIdentity}, ProbeId = {Request.ProbeId}, Payload = [REDACTED], State = [REDACTED] }}";
}

/// <summary>Sanitized closed error envelope written to stderr. It intentionally carries no exception message.</summary>
public sealed record GroundworkProcessProbeError
{
    [JsonConstructor]
    public GroundworkProcessProbeError(string protocolVersion, string probeId, string providerIdentity, string code)
    {
        if (string.IsNullOrWhiteSpace(protocolVersion))
            throw new ArgumentException("A protocol version is required.", nameof(protocolVersion));
        EvidenceCatalog.EnsureSlug(probeId, nameof(probeId));
        if (providerIdentity != "unknown")
            EvidenceCatalog.EnsureProviderIdentity(providerIdentity);
        EvidenceCatalog.EnsureSlug(code, nameof(code));
        ProtocolVersion = protocolVersion;
        ProbeId = probeId;
        ProviderIdentity = providerIdentity;
        Code = code;
    }

    public string ProtocolVersion { get; }
    public string ProbeId { get; }
    public string ProviderIdentity { get; }
    public string Code { get; }
}

public static class GroundworkProcessProbeProtocol
{
    public const string CurrentVersion = "1.0.0";
    public const int MaximumEnvelopeLength = 1024 * 1024;

    private static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    public static string SerializeCommand(GroundworkProcessProbeCommand command) => Serialize(command);
    public static string SerializeResult(GroundworkProcessProbeResult result) => Serialize(result);
    public static string SerializeError(GroundworkProcessProbeError error) => Serialize(error);
    public static GroundworkProcessProbeCommand DeserializeCommand(string json) => Deserialize<GroundworkProcessProbeCommand>(json);
    public static GroundworkProcessProbeResult DeserializeResult(string json) => Deserialize<GroundworkProcessProbeResult>(json);
    public static GroundworkProcessProbeError DeserializeError(string json) => Deserialize<GroundworkProcessProbeError>(json);
    public static string ComputeSha256(string value) => StableDigest.FromText(value);

    internal static bool IsSha256(string value) => EvidenceCatalog.IsSha256(value);

    internal static GroundworkProviderTopology CreateProtocolTopology(string providerKey) => providerKey switch
    {
        "sqlite" => new GroundworkProviderTopology(
            providerKey,
            "file-backed-distinct-connections",
            GroundworkTopologyCapabilities.PersistentStorage | GroundworkTopologyCapabilities.IndependentClients),
        "sqlserver" => new GroundworkProviderTopology(
            providerKey,
            "real-sqlserver-container",
            GroundworkTopologyCapabilities.PersistentStorage | GroundworkTopologyCapabilities.IndependentClients),
        "postgresql" => new GroundworkProviderTopology(
            providerKey,
            "real-postgresql-container",
            GroundworkTopologyCapabilities.PersistentStorage | GroundworkTopologyCapabilities.IndependentClients),
        "mongodb" => new GroundworkProviderTopology(
            providerKey,
            "transaction-capable-replica-set",
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients |
            GroundworkTopologyCapabilities.MultiDocumentTransactions |
            GroundworkTopologyCapabilities.TransactionCapableMongoTopology),
        _ => throw new ArgumentException($"Unknown provider catalog key '{providerKey}'.", nameof(providerKey))
    };

    private static string Serialize<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var json = JsonSerializer.Serialize(value, JsonOptions);
        if (json.Length > MaximumEnvelopeLength)
            throw new InvalidOperationException("The process-probe envelope exceeds the protocol limit.");
        return json;
    }

    private static T Deserialize<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumEnvelopeLength)
            throw new InvalidOperationException("The process-probe envelope is blank or exceeds the protocol limit.");
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
               ?? throw new InvalidOperationException("The process-probe envelope contained JSON null.");
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = false
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}

/// <summary>Launches the copied helper executable without ever placing provider state in its launch surface.</summary>
public sealed class GroundworkProcessProbeRunner
{
    public const string HelperDirectoryName = "GroundworkProcessProbe";
    public const string HelperAssemblyName = "Elsa.Persistence.Groundwork.ProcessProbe.dll";
    public static TimeSpan DefaultTimeout { get; } = TimeSpan.FromSeconds(60);

    public GroundworkProcessLaunchDescriptor CreateLaunchDescriptor(string protocolVersion = GroundworkProcessProbeProtocol.CurrentVersion)
    {
        var helperPath = ResolveHelperAssemblyPath();
        var dotnetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (string.IsNullOrWhiteSpace(dotnetHost))
            dotnetHost = "dotnet";
        return new GroundworkProcessLaunchDescriptor(dotnetHost, [helperPath], protocolVersion);
    }

    public async ValueTask<GroundworkProcessProbeResult> RunAsync(
        GroundworkProcessLaunchDescriptor launchDescriptor,
        GroundworkProviderDescriptor provider,
        string documentKind,
        GroundworkProcessProbeState state,
        GroundworkProcessProbeRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(launchDescriptor);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(request);
        var effectiveTimeout = timeout ?? DefaultTimeout;
        if (effectiveTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        var command = new GroundworkProcessProbeCommand(
            launchDescriptor.ProtocolVersion,
            launchDescriptor.Fingerprint,
            provider.ProviderKey,
            provider.ProviderIdentity,
            provider.ProviderVersion,
            documentKind,
            request,
            state);
        var serializedCommand = GroundworkProcessProbeProtocol.SerializeCommand(command);
        var startInfo = new ProcessStartInfo
        {
            FileName = launchDescriptor.ExecutablePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in launchDescriptor.Arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("The Groundwork process-probe helper could not be started.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var timeoutSource = new CancellationTokenSource(effectiveTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        try
        {
            await process.StandardInput.WriteAsync(serializedCommand.AsMemory(), linkedSource.Token);
            await process.StandardInput.FlushAsync(linkedSource.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(linkedSource.Token);
        }
        catch (OperationCanceledException) when (linkedSource.IsCancellationRequested)
        {
            await KillAndReapAsync(process, stdoutTask, stderrTask);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"The Groundwork process-probe helper exceeded its {effectiveTimeout.TotalSeconds:F0}-second timeout.");
        }
        catch
        {
            await KillAndReapAsync(process, stdoutTask, stderrTask);
            throw new InvalidOperationException("The Groundwork process-probe helper transport failed.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw CreateSanitizedFailure(process.ExitCode, stderr);
        if (!string.IsNullOrWhiteSpace(stderr))
            throw new InvalidOperationException("The successful Groundwork process-probe helper wrote unexpected stderr output.");

        var result = GroundworkProcessProbeProtocol.DeserializeResult(stdout.Trim());
        if (result.ProcessId != process.Id)
            throw new InvalidOperationException("The process-probe result process ID does not match the launched helper.");
        if (result.ExitCode != process.ExitCode || result.ExitCode != 0)
            throw new InvalidOperationException("The process-probe result exit code does not match the launched helper.");
        if (!string.Equals(result.ProbeId, request.ProbeId, StringComparison.Ordinal) || result.Operation != request.Operation)
            throw new InvalidOperationException("The process-probe result does not match the requested probe.");
        if (!string.Equals(result.ProviderIdentity, provider.ProviderIdentity, StringComparison.Ordinal))
            throw new InvalidOperationException("The process-probe result does not match the requested provider.");
        if (!string.Equals(result.LaunchDescriptorFingerprint, launchDescriptor.Fingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("The process-probe result does not match the launch descriptor.");
        if (!string.Equals(result.SchemaVersion, launchDescriptor.ProtocolVersion, StringComparison.Ordinal))
            throw new InvalidOperationException("The process-probe result does not match the protocol version.");
        if (request.PayloadSha256 is not null &&
            !string.Equals(result.PayloadSha256, request.PayloadSha256, StringComparison.Ordinal))
            throw new InvalidOperationException("The process-probe result does not match the requested payload digest.");
        return result;
    }

    public static string ResolveHelperAssemblyPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, HelperDirectoryName, HelperAssemblyName);
        if (!File.Exists(path))
            throw new FileNotFoundException("The copied Groundwork process-probe helper artifact was not found.", path);
        return path;
    }

    private static InvalidOperationException CreateSanitizedFailure(int exitCode, string stderr)
    {
        try
        {
            var error = GroundworkProcessProbeProtocol.DeserializeError(stderr.Trim());
            return new InvalidOperationException(
                $"The Groundwork process-probe helper exited with code {exitCode} and sanitized error '{error.Code}'.");
        }
        catch
        {
            return new InvalidOperationException(
                $"The Groundwork process-probe helper exited with code {exitCode} and invalid sanitized error output.");
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task KillAndReapAsync(Process process, Task<string> stdoutTask, Task<string> stderrTask)
    {
        KillProcessTree(process);
        try
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
        }
        await Task.WhenAll(stdoutTask, stderrTask);
    }
}
