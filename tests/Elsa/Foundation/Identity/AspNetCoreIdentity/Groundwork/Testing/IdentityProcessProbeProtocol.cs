using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Testing;

public enum IdentityProcessProbeOperation
{
    CreateUser,
    FindByNormalizedUserName,
    DuplicateCreate
}

public sealed record IdentityProcessProbeUser
{
    [JsonConstructor]
    public IdentityProcessProbeUser(
        string tenantId,
        string userId,
        string userName,
        string normalizedUserName,
        string email,
        string normalizedEmail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedUserName);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedEmail);
        TenantId = tenantId;
        UserId = userId;
        UserName = userName;
        NormalizedUserName = normalizedUserName;
        Email = email;
        NormalizedEmail = normalizedEmail;
    }

    public string TenantId { get; }
    public string UserId { get; }
    public string UserName { get; }
    public string NormalizedUserName { get; }
    public string Email { get; }
    public string NormalizedEmail { get; }

    public override string ToString() =>
        $"{nameof(IdentityProcessProbeUser)} {{ User = [REDACTED], NormalizedUserNameSha256 = {IdentityProcessProbeProtocol.ComputeSha256(NormalizedUserName)} }}";
}

public sealed record IdentityProcessProbeState
{
    [JsonConstructor]
    public IdentityProcessProbeState(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public override string ToString() => $"{nameof(IdentityProcessProbeState)} {{ ConnectionString = [REDACTED] }}";
}

public sealed record IdentityProcessProbeCommand
{
    [JsonConstructor]
    public IdentityProcessProbeCommand(
        string protocolVersion,
        string launchFingerprint,
        string providerKey,
        string physicalSuffix,
        IdentityProcessProbeOperation operation,
        IdentityProcessProbeUser user,
        IdentityProcessProbeState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolVersion);
        IdentityProcessProbeValidation.EnsureSha256(launchFingerprint, nameof(launchFingerprint));
        if (providerKey is not ("sqlite" or "postgresql" or "sqlserver" or "mongodb"))
            throw new ArgumentException("The Identity process probe provider is unsupported.", nameof(providerKey));
        IdentityProcessProbeValidation.EnsureSlug(physicalSuffix, nameof(physicalSuffix));
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(state);
        ProtocolVersion = protocolVersion;
        LaunchFingerprint = launchFingerprint;
        ProviderKey = providerKey;
        PhysicalSuffix = physicalSuffix;
        Operation = operation;
        User = user;
        State = state;
    }

    public string ProtocolVersion { get; }
    public string LaunchFingerprint { get; }
    public string ProviderKey { get; }
    public string PhysicalSuffix { get; }
    public IdentityProcessProbeOperation Operation { get; }
    public IdentityProcessProbeUser User { get; }
    public IdentityProcessProbeState State { get; }

    public override string ToString() =>
        $"{nameof(IdentityProcessProbeCommand)} {{ ProviderKey = {ProviderKey}, Operation = {Operation}, User = [REDACTED], State = [REDACTED] }}";
}

public sealed record IdentityProcessProbeResult
{
    [JsonConstructor]
    public IdentityProcessProbeResult(
        string protocolVersion,
        string launchFingerprint,
        string providerKey,
        IdentityProcessProbeOperation operation,
        int processId,
        string outcome,
        string foundUserIdSha256,
        string? errorCode,
        long documentVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolVersion);
        IdentityProcessProbeValidation.EnsureSha256(launchFingerprint, nameof(launchFingerprint));
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        if (processId <= 0)
            throw new ArgumentOutOfRangeException(nameof(processId));
        IdentityProcessProbeValidation.EnsureSlug(outcome, nameof(outcome));
        IdentityProcessProbeValidation.EnsureSha256(foundUserIdSha256, nameof(foundUserIdSha256));
        if (errorCode is not null)
            IdentityProcessProbeValidation.EnsureSlug(errorCode, nameof(errorCode));
        if (documentVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(documentVersion));
        ProtocolVersion = protocolVersion;
        LaunchFingerprint = launchFingerprint;
        ProviderKey = providerKey;
        Operation = operation;
        ProcessId = processId;
        Outcome = outcome;
        FoundUserIdSha256 = foundUserIdSha256;
        ErrorCode = errorCode;
        DocumentVersion = documentVersion;
    }

    public string ProtocolVersion { get; }
    public string LaunchFingerprint { get; }
    public string ProviderKey { get; }
    public IdentityProcessProbeOperation Operation { get; }
    public int ProcessId { get; }
    public string Outcome { get; }
    public string FoundUserIdSha256 { get; }
    public string? ErrorCode { get; }
    public long DocumentVersion { get; }
}

public sealed record IdentityProcessProbeError
{
    [JsonConstructor]
    public IdentityProcessProbeError(string protocolVersion, string providerKey, string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        IdentityProcessProbeValidation.EnsureSlug(code, nameof(code));
        ProtocolVersion = protocolVersion;
        ProviderKey = providerKey;
        Code = code;
    }

    public string ProtocolVersion { get; }
    public string ProviderKey { get; }
    public string Code { get; }
}

public static class IdentityProcessProbeProtocol
{
    public const string CurrentVersion = "2.0.0";
    public const int MaximumEnvelopeLength = 1024 * 1024;

    private static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    public static string SerializeCommand(IdentityProcessProbeCommand command) => Serialize(command);
    public static string SerializeResult(IdentityProcessProbeResult result) => Serialize(result);
    public static string SerializeError(IdentityProcessProbeError error) => Serialize(error);
    public static IdentityProcessProbeCommand DeserializeCommand(string json) => Deserialize<IdentityProcessProbeCommand>(json);
    public static IdentityProcessProbeResult DeserializeResult(string json) => Deserialize<IdentityProcessProbeResult>(json);
    public static IdentityProcessProbeError DeserializeError(string json) => Deserialize<IdentityProcessProbeError>(json);
    public static string ComputeSha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string Serialize<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var json = JsonSerializer.Serialize(value, JsonOptions);
        if (json.Length > MaximumEnvelopeLength)
            throw new InvalidOperationException("The Identity process-probe envelope exceeds the protocol limit.");
        return json;
    }

    private static T Deserialize<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumEnvelopeLength)
            throw new InvalidOperationException("The Identity process-probe envelope is blank or exceeds the protocol limit.");
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
               ?? throw new InvalidOperationException("The Identity process-probe envelope contained JSON null.");
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

public sealed class IdentityProcessProbeRunner
{
    public const string HelperDirectoryName = "IdentityProcessProbe";
    public const string HelperAssemblyName = "Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.ProcessProbe.dll";
    public static TimeSpan DefaultTimeout { get; } = TimeSpan.FromSeconds(60);
    private static TimeSpan CleanupTimeout { get; } = TimeSpan.FromSeconds(10);

    public async Task<IdentityProcessProbeResult> RunAsync(
        string providerKey,
        string physicalSuffix,
        IdentityProcessProbeOperation operation,
        IdentityProcessProbeUser user,
        IdentityProcessProbeState state,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveTimeout = timeout ?? DefaultTimeout;
        if (effectiveTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        cancellationToken.ThrowIfCancellationRequested();
        var helperPath = Path.Combine(AppContext.BaseDirectory, HelperDirectoryName, HelperAssemblyName);
        if (!File.Exists(helperPath))
            throw new FileNotFoundException("The copied Identity process-probe helper artifact was not found.", helperPath);
        var dotnetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (string.IsNullOrWhiteSpace(dotnetHost))
            dotnetHost = "dotnet";
        var launchFingerprint = IdentityProcessProbeProtocol.ComputeSha256($"{Path.GetFileName(dotnetHost)}\n{Path.GetFileName(helperPath)}\n{IdentityProcessProbeProtocol.CurrentVersion}");
        var command = new IdentityProcessProbeCommand(
            IdentityProcessProbeProtocol.CurrentVersion,
            launchFingerprint,
            providerKey,
            physicalSuffix,
            operation,
            user,
            state);
        var startInfo = new ProcessStartInfo
        {
            FileName = dotnetHost,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(helperPath);
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("The Identity process-probe helper could not be started.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var timeoutSource = new CancellationTokenSource(effectiveTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        try
        {
            await process.StandardInput.WriteAsync(IdentityProcessProbeProtocol.SerializeCommand(command).AsMemory(), linkedSource.Token);
            await process.StandardInput.FlushAsync(linkedSource.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(linkedSource.Token);
        }
        catch (OperationCanceledException) when (linkedSource.IsCancellationRequested)
        {
            await KillAndReapAsync(process, stdoutTask, stderrTask);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"The Identity process-probe helper exceeded its {effectiveTimeout.TotalSeconds:F0}-second timeout.");
        }
        catch
        {
            await KillAndReapAsync(process, stdoutTask, stderrTask);
            throw;
        }
        var standardOutput = await stdoutTask;
        var standardError = await stderrTask;
        if (process.ExitCode != 0)
        {
            var error = IdentityProcessProbeProtocol.DeserializeError(standardError.Trim());
            throw new InvalidOperationException($"The Identity process-probe helper failed with sanitized code '{error.Code}'.");
        }
        if (!string.IsNullOrWhiteSpace(standardError))
            throw new InvalidOperationException("The successful Identity process-probe helper wrote unexpected stderr output.");
        var result = IdentityProcessProbeProtocol.DeserializeResult(standardOutput.Trim());
        if (result.ProcessId != process.Id ||
            result.ProtocolVersion != command.ProtocolVersion ||
            result.LaunchFingerprint != command.LaunchFingerprint ||
            result.ProviderKey != command.ProviderKey ||
            result.Operation != command.Operation)
        {
            throw new InvalidOperationException("The Identity process-probe result did not match the launched request.");
        }
        return result;
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private static async Task KillAndReapAsync(
        Process process,
        Task<string> stdoutTask,
        Task<string> stderrTask)
    {
        KillProcessTree(process);
        try
        {
            await process.WaitForExitAsync(CancellationToken.None).WaitAsync(CleanupTimeout);
        }
        catch (Exception exception) when (exception is InvalidOperationException or TimeoutException)
        {
        }
        try
        {
            await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(CleanupTimeout);
        }
        catch (TimeoutException)
        {
        }
    }
}

internal static class IdentityProcessProbeValidation
{
    public static void EnsureSlug(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
            throw new ArgumentException("A non-blank ASCII slug is required.", parameterName);
    }

    public static void EnsureSha256(string value, string parameterName)
    {
        if (value.Length != 64 || value.Any(character => !char.IsAsciiHexDigit(character)))
            throw new ArgumentException("A SHA-256 digest is required.", parameterName);
    }
}
