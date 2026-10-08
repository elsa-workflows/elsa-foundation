using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CShells.Features;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Http.IntegrationTests;

internal static class ResponseReplayFileExternalPayloadStoreProfile
{
    public const string Name = "response-replay-file-v1";
}

[ShellFeature(
    name: "ResponseReplayFileExternalPayloadStore",
    DisplayName = "Response Replay File External Payload Store",
    Description = "Test-only stable file-backed IExternalPayloadStore for process-restart proofs.",
    DependsOn = new object[] { "ActivitiesRuntime" })]
public sealed class ResponseReplayFileExternalPayloadStoreFeature : IShellFeature
{
    public string RootPath { get; set; } = string.Empty;

    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RootPath);
        services.AddSingleton<IExternalPayloadStore>(_ => new ResponseReplayFileExternalPayloadStore(RootPath));
    }
}

internal sealed class ResponseReplayFileExternalPayloadStore : IExternalPayloadStore
{
    private const string PayloadHashMetadataKey = "payloadSha256";
    private const string OwnerHashMetadataKey = "ownerKeySha256";
    private readonly string _rootPath;

    public ResponseReplayFileExternalPayloadStore(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _rootPath = Path.GetFullPath(rootPath);
        InstanceId = Guid.NewGuid().ToString("N");
    }

    public string InstanceId { get; }

    public async ValueTask<DurableValueExternalReference> WriteAsync(
        ExternalPayloadWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!StringComparer.Ordinal.Equals(request.StorageProfile, ResponseReplayFileExternalPayloadStoreProfile.Name))
            throw new InvalidOperationException($"Unsupported response-replay test storage profile '{request.StorageProfile}'.");

        var payload = JsonSerializer.SerializeToUtf8Bytes(request.Payload);
        var payloadHash = Hash(payload);
        var ownerHash = Hash(Encoding.UTF8.GetBytes($"{request.WorkflowExecutionId}\n{request.OwnerKey}"));
        var locator = $"{ownerHash}.json";
        var path = ResolvePayloadPath(locator);
        Directory.CreateDirectory(_rootPath);

        if (File.Exists(path))
        {
            await EnsureSamePayloadAsync(path, payload, cancellationToken);
        }
        else
        {
            var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var stream = new FileStream(
                                 temporaryPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 bufferSize: 4096,
                                 FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await stream.WriteAsync(payload, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                    stream.Flush(flushToDisk: true);
                }

                try
                {
                    File.Move(temporaryPath, path);
                }
                catch (IOException) when (File.Exists(path))
                {
                    await EnsureSamePayloadAsync(path, payload, cancellationToken);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        await RecordOperationAsync("write", locator, payloadHash, cancellationToken);

        return new DurableValueExternalReference(
            ResponseReplayFileExternalPayloadStoreProfile.Name,
            locator,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [PayloadHashMetadataKey] = payloadHash,
                [OwnerHashMetadataKey] = ownerHash,
                ["payloadLength"] = payload.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["typeAlias"] = request.Type.Alias,
                ["collectionKind"] = request.Type.CollectionKind.ToString()
            });
    }

    public async ValueTask<JsonElement> ReadAsync(
        DurableValueExternalReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!StringComparer.Ordinal.Equals(reference.StorageProfile, ResponseReplayFileExternalPayloadStoreProfile.Name))
            throw new InvalidOperationException($"Unsupported response-replay test storage profile '{reference.StorageProfile}'.");

        var path = ResolvePayloadPath(reference.Locator);
        if (!reference.Metadata.TryGetValue(OwnerHashMetadataKey, out var ownerHash) ||
            !StringComparer.Ordinal.Equals(ownerHash, reference.Locator[..64]))
            throw new InvalidDataException($"External payload '{reference.Locator}' does not match its recorded owner identity.");

        var payload = await File.ReadAllBytesAsync(path, cancellationToken);
        var actualHash = Hash(payload);
        if (!reference.Metadata.TryGetValue(PayloadHashMetadataKey, out var expectedHash) ||
            !StringComparer.Ordinal.Equals(actualHash, expectedHash))
            throw new InvalidDataException($"External payload '{reference.Locator}' does not match its recorded SHA-256.");

        await RecordOperationAsync("read", reference.Locator, actualHash, cancellationToken);
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.Clone();
    }

    private string ResolvePayloadPath(string locator)
    {
        if (locator.Length != 69 || !locator.EndsWith(".json", StringComparison.Ordinal) ||
            !locator.AsSpan(0, 64).ToArray().All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new InvalidDataException("The external payload locator is not a canonical owner-hash file name.");

        var path = Path.GetFullPath(Path.Combine(_rootPath, locator));
        if (!path.StartsWith(_rootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("The external payload locator escaped its owned file store.");
        return path;
    }

    private static async Task EnsureSamePayloadAsync(string path, byte[] expected, CancellationToken cancellationToken)
    {
        var existing = await File.ReadAllBytesAsync(path, cancellationToken);
        if (!existing.AsSpan().SequenceEqual(expected))
            throw new InvalidOperationException("An external payload owner key was reused with different payload bytes.");
    }

    private async Task RecordOperationAsync(
        string operation,
        string locator,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        var operationsPath = Path.Combine(_rootPath, "operations");
        Directory.CreateDirectory(operationsPath);
        var record = JsonSerializer.SerializeToUtf8Bytes(new
        {
            operation,
            processId = Environment.ProcessId,
            providerInstanceId = InstanceId,
            locator,
            payloadSha256 = payloadHash
        });
        var destination = Path.Combine(operationsPath, $"{Guid.NewGuid():N}.json");
        var temporary = $"{destination}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, record, cancellationToken);
            File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static string Hash(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}

[ShellFeature(
    name: "ResponseReplayDiscreteSchedulerHops",
    DisplayName = "Response Replay Discrete Scheduler Hops",
    Description = "Test-only switch to exercise normal discrete ReplaySafe scheduler continuations.",
    DependsOn = new object[] { "ActivitiesRuntime" })]
public sealed class ResponseReplayDiscreteSchedulerHopsFeature : IShellFeature, IPostConfigureShellServices
{
    public void ConfigureServices(IServiceCollection services)
    {
    }

    public void PostConfigureServices(IServiceCollection services) =>
        services.Replace(ServiceDescriptor.Singleton(new RuntimeReplaySafeFusionOptions { Enabled = false }));
}
