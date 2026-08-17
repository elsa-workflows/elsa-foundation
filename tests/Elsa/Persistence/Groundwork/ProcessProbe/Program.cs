using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.MongoDb;
using Elsa.Persistence.Groundwork.PostgreSql;
using Elsa.Persistence.Groundwork.Conformance.Tests.Probes;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.MongoDb;
using Groundwork.MongoDb.Documents;
using Groundwork.MongoDb.Materialization;
using Groundwork.PostgreSql;
using Groundwork.PostgreSql.Documents;
using Groundwork.PostgreSql.PhysicalStorage;
using Groundwork.Sqlite;
using Groundwork.Sqlite.Documents;
using Groundwork.Sqlite.PhysicalStorage;
using Groundwork.SqlServer;
using Groundwork.SqlServer.Documents;
using Groundwork.SqlServer.PhysicalStorage;

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
        catch (Exception exception)
        {
            Console.SetError(stderr);
            var error = new GroundworkProcessProbeError(
                command?.ProtocolVersion ?? GroundworkProcessProbeProtocol.CurrentVersion,
                command?.Request.ProbeId ?? "unknown-probe",
                command?.ProviderIdentity ?? "unknown",
                command is null ? "invalid-command" : ProviderFailureCode(exception));
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
            GroundworkProcessProbeOperation.RuntimeCheckpointVerifyBundle => await VerifyRuntimeCheckpointBundleAsync(lease.Store, command, cancellationToken),
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

    private static async Task<(string PayloadSha256, string SchemaVersion, long DocumentVersion)> VerifyRuntimeCheckpointBundleAsync(
        IDocumentStore store,
        GroundworkProcessProbeCommand command,
        CancellationToken cancellationToken)
    {
        var payload = RuntimeCheckpointRestartProbe.DecodePayload(command.Request.Value!);
        var observation = await RuntimeCheckpointRestartProbe.VerifyAsync(
            store,
            GroundworkProviderTestSerialization.Serializer,
            GroundworkTestAccess.DefaultAccessContextAccessor,
            payload,
            cancellationToken);
        return (command.Request.PayloadSha256!, command.ProtocolVersion, observation.DocumentVersion);
    }

    private static async ValueTask<StoreLease> OpenStoreAsync(
        GroundworkProcessProbeCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Request.Operation is GroundworkProcessProbeOperation.RuntimeCheckpointVerifyBundle)
            return await OpenRuntimePhysicalStoreAsync(command, cancellationToken);

        // The parent opens its store from the physicalized runtime manifest; the child must compile the very
        // same target or it lands on a different physical layout (and the logical manifest cannot be admitted
        // physically at all — its outbox status projection carries no numeric precision).
        var manifest = ElsaRuntimeStorageManifest.CreatePhysicalized();
        var provider = new ProviderIdentity(command.ProviderIdentity, command.ProviderVersion);
        var access = GroundworkTestAccess.DefaultScoped;

        // Opened on the physical surface through the shared test recipe. The probe only saves and loads by
        // id, so the paired query runtime is discarded; the parent already provisioned the target.
        return command.ProviderKey switch
        {
            "sqlite" => new StoreLease(
                (await GroundworkPhysicalTestStores.OpenSqliteAsync(
                    command.State.ConnectionString,
                    manifest,
                    provider,
                    access,
                    cancellationToken)).Store),
            "sqlserver" => new StoreLease(
                (await GroundworkPhysicalTestStores.OpenSqlServerAsync(
                    command.State.ConnectionString,
                    manifest,
                    provider,
                    access,
                    cancellationToken)).Store),
            "postgresql" => new StoreLease(
                (await GroundworkPhysicalTestStores.OpenPostgreSqlAsync(
                    command.State.ConnectionString,
                    manifest,
                    provider,
                    access,
                    cancellationToken)).Store),
            "mongodb" => await OpenMongoDbAsync(command, manifest, provider, access, cancellationToken),
            _ => throw new ArgumentException("The process-probe provider is unsupported.", nameof(command))
        };
    }

    /// <summary>
    /// The checkpoint contract writes into the admitted physical target, rather than Groundwork's generic
    /// documents table. A restart probe must reconstruct that exact target in its child process before it reads.
    /// </summary>
    private static ValueTask<StoreLease> OpenRuntimePhysicalStoreAsync(
        GroundworkProcessProbeCommand command,
        CancellationToken cancellationToken) =>
        command.ProviderKey switch
        {
            "sqlite" => OpenSqliteRuntimePhysicalStoreAsync(command, cancellationToken),
            "sqlserver" => OpenSqlServerRuntimePhysicalStoreAsync(command, cancellationToken),
            "postgresql" => OpenPostgreSqlRuntimePhysicalStoreAsync(command, cancellationToken),
            "mongodb" => OpenMongoDbRuntimePhysicalStoreAsync(command, cancellationToken),
            _ => throw new ArgumentException("The runtime checkpoint process-probe provider is unsupported.", nameof(command))
        };

    private static async ValueTask<StoreLease> OpenSqliteRuntimePhysicalStoreAsync(
        GroundworkProcessProbeCommand command,
        CancellationToken cancellationToken)
    {
        var source = await GroundworkStoreInitialization.CreateRuntimePhysicalSchemaSourceAsync(
            SqliteGroundworkCapabilities.Runtime(),
            PhysicalTopology(SqliteGroundworkCapabilities.Provider.Name, "sqlite-file"),
            SqliteGroundworkCapabilities.PhysicalNames,
            cancellationToken: cancellationToken);
        return new StoreLease(new SqlitePhysicalDocumentStore(
            command.State.ConnectionString,
            source.CreateManifest(),
            source.PhysicalTarget.Routes,
            GroundworkTestAccess.DefaultScoped));
    }

    private static async ValueTask<StoreLease> OpenSqlServerRuntimePhysicalStoreAsync(
        GroundworkProcessProbeCommand command,
        CancellationToken cancellationToken)
    {
        var source = await GroundworkStoreInitialization.CreateRuntimePhysicalSchemaSourceAsync(
            SqlServerGroundworkCapabilities.Runtime(),
            PhysicalTopology(SqlServerGroundworkCapabilities.Provider.Name, "sqlserver"),
            SqlServerGroundworkCapabilities.PhysicalNames,
            cancellationToken: cancellationToken);
        return new StoreLease(new SqlServerPhysicalDocumentStore(
            command.State.ConnectionString,
            source.CreateManifest(),
            source.PhysicalTarget.Routes,
            GroundworkTestAccess.DefaultScoped));
    }

    private static async ValueTask<StoreLease> OpenPostgreSqlRuntimePhysicalStoreAsync(
        GroundworkProcessProbeCommand command,
        CancellationToken cancellationToken)
    {
        var source = await GroundworkStoreInitialization.CreateRuntimePhysicalSchemaSourceAsync(
            PostgreSqlGroundworkCapabilities.Runtime(),
            PhysicalTopology(PostgreSqlGroundworkCapabilities.Provider.Name, "postgresql"),
            PostgreSqlGroundworkCapabilities.PhysicalNames,
            cancellationToken: cancellationToken);
        return new StoreLease(new PostgreSqlPhysicalDocumentStore(
            command.State.ConnectionString,
            source.CreateManifest(),
            source.PhysicalTarget.Routes,
            GroundworkTestAccess.DefaultScoped));
    }

    private static async ValueTask<StoreLease> OpenMongoDbRuntimePhysicalStoreAsync(
        GroundworkProcessProbeCommand command,
        CancellationToken cancellationToken)
    {
        var topology = await new MongoDbGroundworkRuntimeAdmission().InspectReplicaSetAsync(
            command.State.ConnectionString,
            command.State.MongoDatabaseName!,
            cancellationToken);
        var source = await GroundworkStoreInitialization.CreateRuntimePhysicalSchemaSourceAsync(
            MongoDbGroundworkCapabilities.RuntimeForTransactionCapableDeployment(),
            topology,
            MongoDbPhysicalNameNormalizer.Instance,
            MongoDbGroundworkPhysicalSchemaTargetCompiler.Instance,
            cancellationToken);
        var handle = await MongoDbDocumentStoreFactory.OpenPhysicalAsync(
            command.State.ConnectionString,
            command.State.MongoDatabaseName!,
            source.CreateManifest(),
            source.PhysicalTarget.Provider,
            GroundworkTestAccess.DefaultScoped,
            source.CreateNamePolicy(),
            cancellationToken: cancellationToken);
        return new StoreLease(handle.CreateStore(GroundworkTestAccess.DefaultScoped), handle);
    }

    private static GroundworkProviderTopologySnapshot PhysicalTopology(string providerName, string description) =>
        new(
            providerName,
            description,
            new HashSet<string>(StringComparer.Ordinal)
            {
                RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity
            });

    private static async ValueTask<StoreLease> OpenMongoDbAsync(
        GroundworkProcessProbeCommand command,
        global::Groundwork.Core.Manifests.StorageManifest manifest,
        ProviderIdentity provider,
        DocumentStoreAccess access,
        CancellationToken cancellationToken)
    {
        var handle = await MongoDbDocumentStoreFactory.OpenPhysicalAsync(
            command.State.ConnectionString,
            command.State.MongoDatabaseName!,
            manifest,
            provider,
            access,
            options: new MongoDbPhysicalDocumentStoreOptions { AutoApplyOnStartup = true },
            cancellationToken: cancellationToken);
        return new StoreLease(handle.Store, handle);
    }

    private sealed record ProbeDocument(
        [property: JsonPropertyName("value")] string Value,
        [property: JsonPropertyName("collection")] string Collection);

    private static string ProviderFailureCode(Exception exception)
    {
        if (exception is InvalidOperationException { Message: var message } &&
            message.StartsWith("The child process could not rehydrate the complete runtime checkpoint bundle: ", StringComparison.Ordinal))
        {
            var missing = message["The child process could not rehydrate the complete runtime checkpoint bundle: ".Length..]
                .TrimEnd('.');
            return $"runtime-checkpoint-bundle-incomplete-{ToSlug(missing)}";
        }

        if (exception.GetType().FullName == "Microsoft.Data.Sqlite.SqliteException" &&
            exception.GetType().GetProperty("SqliteErrorCode")?.GetValue(exception) is int sqliteErrorCode)
        {
            return $"provider-operation-failed-sqlite-error-{sqliteErrorCode}";
        }

        if (exception is FileNotFoundException fileNotFound)
        {
            var missing = fileNotFound.FileName;
            if (string.IsNullOrWhiteSpace(missing))
                return "provider-operation-failed-file-not-found";
            var safeName = Path.GetFileName(missing);
            if (string.IsNullOrWhiteSpace(safeName))
                safeName = new string(missing.TakeWhile(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-').ToArray());
            return $"provider-operation-failed-file-not-found-{ToSlug(safeName)}";
        }

        var exceptionType = exception.GetType().Name;
        var suffix = exceptionType.EndsWith("Exception", StringComparison.Ordinal)
            ? exceptionType[..^"Exception".Length]
            : exceptionType;
        return $"provider-operation-failed-{ToSlug(suffix)}";
    }

    private static string ToSlug(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (!char.IsAsciiLetterOrDigit(character))
            {
                if (builder.Length == 0 || builder[^1] == '-')
                    continue;
                builder.Append('-');
                continue;
            }

            if (char.IsAsciiLetterUpper(character) && builder.Length > 0 && builder[^1] != '-')
                builder.Append('-');
            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString().Trim('-');
    }

    private sealed class StoreLease(IDocumentStore store, IAsyncDisposable? owner = null) : IAsyncDisposable
    {
        public IDocumentStore Store { get; } = store;
        public ValueTask DisposeAsync() => owner?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

}
