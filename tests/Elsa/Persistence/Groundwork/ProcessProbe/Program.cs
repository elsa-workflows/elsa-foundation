using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Core.Capabilities;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.MongoDb.Documents;
using Groundwork.PostgreSql.Documents;
using Groundwork.Sqlite.Documents;
using Groundwork.SqlServer.Documents;

namespace Elsa.Persistence.Groundwork.ProcessProbe;

internal static class Program
{
    private const string ProbeCollection = "groundwork-process-probe";

    public static async Task<int> Main()
    {
        var stdout = Console.Out;
        var stderr = Console.Error;
        Console.SetOut(TextWriter.Null);
        Console.SetError(TextWriter.Null);
        GroundworkProcessProbeCommand? command = null;

        try
        {
            var input = await Console.In.ReadToEndAsync();
            command = GroundworkProcessProbeProtocol.DeserializeCommand(input);
            var result = await ExecuteAsync(command, CancellationToken.None);
            Console.SetOut(stdout);
            await stdout.WriteLineAsync(GroundworkProcessProbeProtocol.SerializeResult(result));
            return 0;
        }
        catch
        {
            Console.SetError(stderr);
            var error = new GroundworkProcessProbeError(
                command?.ProtocolVersion ?? GroundworkProcessProbeProtocol.CurrentVersion,
                command?.Request.ProbeId ?? "unknown-probe",
                command?.ProviderIdentity ?? "unknown",
                command is null ? "invalid-command" : "provider-operation-failed");
            await stderr.WriteLineAsync(GroundworkProcessProbeProtocol.SerializeError(error));
            return command is null ? 2 : 3;
        }
        finally
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
        }
    }

    private static async Task<GroundworkProcessProbeResult> ExecuteAsync(
        GroundworkProcessProbeCommand command,
        CancellationToken cancellationToken)
    {
        await using var lease = await OpenStoreAsync(command, cancellationToken);
        var (payloadSha256, schemaVersion, documentVersion) = command.Request.Operation switch
        {
            GroundworkProcessProbeOperation.Save => await SaveAsync(lease.Store, command, cancellationToken),
            GroundworkProcessProbeOperation.Load => await LoadAsync(lease.Store, command, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command.Request.Operation, null)
        };

        return new GroundworkProcessProbeResult(
            command.Request.ProbeId,
            command.ProviderIdentity,
            command.Request.Operation,
            Environment.ProcessId,
            0,
            command.LaunchDescriptorFingerprint,
            payloadSha256,
            schemaVersion,
            documentVersion);
    }

    private static async Task<(string PayloadSha256, string SchemaVersion, long DocumentVersion)> SaveAsync(
        IDocumentStore store,
        GroundworkProcessProbeCommand command,
        CancellationToken cancellationToken)
    {
        var content = JsonSerializer.Serialize(new ProbeDocument(command.Request.Value!, ProbeCollection));
        var write = await store.SaveAsync(
            new SaveDocumentRequest(
                command.DocumentKind,
                command.Request.DocumentId,
                command.ProtocolVersion,
                content,
                ExpectedVersion: 0),
            cancellationToken);
        if (write.Status != DocumentStoreWriteStatus.Saved || write.Document is null)
            throw new InvalidOperationException("The process-probe document was not created.");
        return (command.Request.PayloadSha256!, write.Document.SchemaVersion, write.Document.Version);
    }

    private static async Task<(string PayloadSha256, string SchemaVersion, long DocumentVersion)> LoadAsync(
        IDocumentStore store,
        GroundworkProcessProbeCommand command,
        CancellationToken cancellationToken)
    {
        var document = await store.LoadAsync(command.DocumentKind, command.Request.DocumentId, cancellationToken)
                       ?? throw new InvalidOperationException("The process-probe document was not found.");
        var payload = JsonSerializer.Deserialize<ProbeDocument>(document.ContentJson)
                      ?? throw new InvalidOperationException("The process-probe payload was invalid.");
        if (!string.Equals(payload.Collection, ProbeCollection, StringComparison.Ordinal))
            throw new InvalidOperationException("The process-probe payload did not belong to the probe collection.");
        return (GroundworkProcessProbeProtocol.ComputeSha256(payload.Value), document.SchemaVersion, document.Version);
    }

    private static async ValueTask<StoreLease> OpenStoreAsync(
        GroundworkProcessProbeCommand command,
        CancellationToken cancellationToken)
    {
        var manifest = ElsaRuntimeStorageManifest.Create();
        var provider = new ProviderIdentity(command.ProviderIdentity, command.ProviderVersion);
        var access = GroundworkTestAccess.DefaultScoped;

        return command.ProviderKey switch
        {
            "sqlite" => new StoreLease(
                await SqliteDocumentStoreFactory.CreateAsync(
                    command.State.ConnectionString,
                    manifest,
                    provider,
                    access,
                    cancellationToken: cancellationToken)),
            "sqlserver" => new StoreLease(
                await SqlServerDocumentStoreFactory.CreateAsync(
                    command.State.ConnectionString,
                    manifest,
                    provider,
                    access,
                    cancellationToken: cancellationToken)),
            "postgresql" => new StoreLease(
                await PostgreSqlDocumentStoreFactory.CreateAsync(
                    command.State.ConnectionString,
                    manifest,
                    provider,
                    access,
                    cancellationToken: cancellationToken)),
            "mongodb" => await OpenMongoDbAsync(command, manifest, provider, access, cancellationToken),
            _ => throw new ArgumentException("The process-probe provider is unsupported.", nameof(command))
        };
    }

    private static async ValueTask<StoreLease> OpenMongoDbAsync(
        GroundworkProcessProbeCommand command,
        global::Groundwork.Core.Manifests.StorageManifest manifest,
        ProviderIdentity provider,
        DocumentStoreAccess access,
        CancellationToken cancellationToken)
    {
        var handle = await MongoDbDocumentStoreFactory.CreateAsync(
            command.State.ConnectionString,
            command.State.MongoDatabaseName!,
            manifest,
            provider,
            access,
            cancellationToken: cancellationToken);
        return new StoreLease(handle.Store, handle);
    }

    private sealed record ProbeDocument(
        [property: JsonPropertyName("value")] string Value,
        [property: JsonPropertyName("collection")] string Collection);

    private sealed class StoreLease(IDocumentStore store, IAsyncDisposable? owner = null) : IAsyncDisposable
    {
        public IDocumentStore Store { get; } = store;
        public ValueTask DisposeAsync() => owner?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
