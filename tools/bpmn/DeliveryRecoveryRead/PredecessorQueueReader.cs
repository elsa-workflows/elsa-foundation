using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Bounded compatibility witness: write the real current queue, then invoke a predecessor's checked reader in an
/// isolated load context. This proves reader refusal only; it does not prove host admission, placement or recovery.
/// </summary>
internal static class PredecessorQueueReader
{
    public static async Task<object> RunAsync(string predecessorHost, string fixturePath)
    {
        predecessorHost = Path.GetFullPath(predecessorHost);
        fixturePath = Path.GetFullPath(fixturePath);
        if (!File.Exists(predecessorHost)) throw new FileNotFoundException("A built predecessor host is required.", predecessorHost);
        if (File.Exists(fixturePath)) throw new IOException("The queue fixture path must be new.");
        var scope = new PersistenceScope("queue-compatibility");
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = fixturePath }.ToString()))
        {
            await connection.OpenAsync();
            await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            var store = new EfSchedulerWorkQueueStore(context, new Access(scope), new NoTokens());
            var now = DateTimeOffset.UtcNow;
            await store.EnqueueAsync(new("native", "native-execution", "native-command", WorkflowExecutionCommandKind.RunSchedulerWork,
                "native-envelope", "native-key", now, now));
            await store.EnqueueAsync(new("model", "model-execution", "model-command", WorkflowExecutionCommandKind.ExecuteModel,
                "model-envelope", "model-key", now, now));
            // Native control uses the unchanged predecessor format. This deliberately constructed stamp is not an
            // upgraded-host witness. The model row is the actual unmodified output of the current queue writer.
            await context.SchedulerWorkItems.Where(row => row.WorkItemId == EfRelationalIdentity.Encode("native"))
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.SchemaVersion, "1.0.0"));
            var rows = await context.SchedulerWorkItems.AsNoTracking().ToArrayAsync();
            var predecessor = new PredecessorContext(predecessorHost);
            try
            {
                var assemblyPath = Path.Combine(Path.GetDirectoryName(predecessorHost)!, "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.dll");
                var assembly = predecessor.LoadFromAssemblyPath(assemblyPath);
                var core = predecessor.LoadFromAssemblyName(new("Elsa.Workflows.Runtime.Core"));
                var kind = core.GetType("Elsa.Workflows.Runtime.Core.Models.WorkflowExecutionCommandKind", throwOnError: true)!;
                if (Enum.GetNames(kind).Contains("ExecuteModel")) throw new InvalidOperationException("The supplied host already knows model commands.");
                var rowType = assembly.GetType("Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities.SchedulerWorkItemEntity", throwOnError: true)!;
                var storeType = assembly.GetType(typeof(EfSchedulerWorkQueueStore).FullName!, throwOnError: true)!;
                var read = storeType.GetMethod("ReadChecked", BindingFlags.NonPublic | BindingFlags.Static)
                    ?? throw new MissingMethodException("The predecessor's checked queue reader is missing.");
                var native = rows.Single(row => row.SchemaVersion == "1.0.0");
                var model = rows.Single(row => row.SchemaVersion == "2.0.0");
                object OldRow(object row) => JsonSerializer.Deserialize(JsonSerializer.Serialize(row), rowType)!;
                var oldNative = read.Invoke(null, [OldRow(native), scope.Value, "native-execution", "native"]);
                if (oldNative is null) throw new InvalidOperationException("The predecessor native control did not read.");
                string refusal;
                try
                {
                    read.Invoke(null, [OldRow(model), scope.Value, "model-execution", "model"]);
                    throw new InvalidOperationException("The predecessor accepted the model queue row.");
                }
                catch (TargetInvocationException exception) when (exception.InnerException?.GetType().FullName == typeof(EfSchemaVersionSkewException).FullName)
                {
                    refusal = exception.InnerException!.GetType().FullName!;
                }
                return new
                {
                    Kind = "actual-predecessor-queue-reader-refusal",
                    CurrentWriterProductVersion = FileVersionInfo.GetVersionInfo(typeof(EfSchedulerWorkQueueStore).Assembly.Location).ProductVersion,
                    CurrentWriterSha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(typeof(EfSchedulerWorkQueueStore).Assembly.Location))),
                    CurrentCoreProductVersion = FileVersionInfo.GetVersionInfo(typeof(RuntimeSchedulerWorkItem).Assembly.Location).ProductVersion,
                    ToolProductVersion = FileVersionInfo.GetVersionInfo(typeof(PredecessorQueueReader).Assembly.Location).ProductVersion,
                    PredecessorProductVersion = FileVersionInfo.GetVersionInfo(assemblyPath).ProductVersion,
                    PredecessorProviderSha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(assemblyPath))),
                    PredecessorCoreProductVersion = FileVersionInfo.GetVersionInfo(core.Location).ProductVersion,
                    PredecessorCoreSha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(core.Location))),
                    NativeV1ControlRead = true,
                    ActualModelWriterStamp = model.SchemaVersion,
                    ModelReaderRefusal = refusal,
                    ModelClaimToken = model.ClaimToken,
                    ModelRevision = model.Revision,
                    ActualHostRecoveryProved = false,
                    WorkerPlacementProved = false
                };
            }
            finally { predecessor.Unload(); }
        }
    }

    private sealed class PredecessorContext(string host) : AssemblyLoadContext(isCollectible: true)
    {
        private readonly AssemblyDependencyResolver resolver = new(host);
        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name is null || name.Name.StartsWith("System.", StringComparison.Ordinal) || name.Name is "System" or "netstandard") return null;
            return resolver.ResolveAssemblyToPath(name) is { } path ? LoadFromAssemblyPath(path) : null;
        }
    }
    private sealed class Access(PersistenceScope scope) : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(scope); }
    private sealed class NoTokens : IRuntimeRecoveryContinuationCodec
    {
        public string Encode(string purpose, ReadOnlySpan<byte> payload) => throw new NotSupportedException();
        public byte[] Decode(string purpose, string token) => throw new NotSupportedException();
    }
}
