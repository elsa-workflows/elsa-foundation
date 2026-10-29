using System.Security.Cryptography;
using System.Reflection.PortableExecutable;
using System.Reflection.Metadata;
using System.Text.Json;
using Elsa.Cluster.EntityFrameworkCore;
using Elsa.Cluster.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

// Closed, coherent fixture snapshots only. Every runtime row is read through the actual checked provider.
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
try
{
    if (args.Length == 4 && args[0] == "--read-bpmn-native" || args.Length is 4 or 5 && args[0] == "--read-bpmn-embedding" ||
        args.Length == 3 && args[0] == "--read-bpmn-retirement")
    {
        var boundDatabase = Path.GetFullPath(args[1]);
        var boundConnectionString = new SqliteConnectionStringBuilder
            { DataSource = boundDatabase, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        var boundAccess = new ScopedAccess(new("default"));
        var boundServices = new ServiceCollection();
        boundServices.AddWorkflowRuntime();
        boundServices.AddSingleton<IPersistenceAccessContextAccessor>(boundAccess);
        boundServices.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
        {
            Provider = "Sqlite", ConnectionString = boundConnectionString, Pooling = false, CacheWorkflowExecutables = false,
            RecoveryContinuationSigningKey = new string('r', 32), HierarchyCursorSigningKey = new string('r', 32)
        });
        await using var boundProvider = boundServices.BuildServiceProvider();
        await using var boundScope = boundProvider.CreateAsyncScope();
        var boundModels = boundScope.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>();
        var boundModel = await boundModels.FindAsync(args[2]);
        if (args[0] == "--read-bpmn-retirement")
        {
            if (boundModel is null) throw new InvalidDataException("The actual cancelling model is absent.");
            var originElement = boundModel.Payload.TryGetProperty("cancellations", out var cancellations) && cancellations.GetArrayLength() != 0
                ? cancellations.EnumerateArray().Single().GetProperty("origin")
                : boundModel.Payload.GetProperty("work").GetArrayLength() != 0
                    ? boundModel.Payload.GetProperty("work").EnumerateArray().Single().GetProperty("nativeWork")
                    : boundModel.Payload.GetProperty("nativeResults").EnumerateArray().Single().GetProperty("origin");
            var origin = originElement.Deserialize<RuntimeModelNativeWorkOrigin>(json)!;
            var sourceIds = new List<string>(); var inputIds = new List<string>();
            await using (var index = new SqliteConnection(boundConnectionString))
            {
                await index.OpenAsync();
                foreach (var selection in new[] { (Table: "elsa_runtime_model_input", Column: "InputId"),
                             (Table: "elsa_runtime_post_commit_outbox", Column: "ContentJson") })
                {
                    await using var command = index.CreateCommand();
                    command.CommandText = $"SELECT {selection.Column} FROM {selection.Table} WHERE WorkflowExecutionId=$worker OR WorkflowExecutionId=$model";
                    if (selection.Column == "InputId") command.CommandText = "SELECT InputId FROM elsa_runtime_model_input WHERE ExecutionId=$model";
                    command.Parameters.AddWithValue("$model", Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(boundModel.ExecutionId)));
                    if (selection.Column != "InputId") command.Parameters.AddWithValue("$worker", Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(origin.WorkerExecutionId)));
                    await using var rows = await command.ExecuteReaderAsync();
                    while (await rows.ReadAsync())
                    {
                        using var document = selection.Column == "InputId" ? null : JsonDocument.Parse(rows.GetString(0));
                        var encoded = document is null ? rows.GetString(0) : document.RootElement.GetProperty("outboxItemId").GetString()!;
                        (selection.Column == "InputId" ? inputIds : sourceIds).Add(System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(encoded)));
                    }
                }
            }
            var inputs = new List<RuntimeModelInputReceipt>(); var sources = new List<RuntimePostCommitOutboxItem>();
            foreach (var id in inputIds.Order(StringComparer.Ordinal)) inputs.Add(await boundModels.FindInputAsync(boundModel.ExecutionId, id) ?? throw new InvalidDataException("An indexed model input is absent."));
            foreach (var id in sourceIds.Order(StringComparer.Ordinal)) sources.Add(await boundScope.ServiceProvider.GetRequiredService<IPostCommitOutboxLookupStore>()
                .FindDurableAsync(id) ?? throw new InvalidDataException("An indexed delivery source is absent."));
            async Task<IReadOnlyList<RuntimeSchedulerWorkItem>> Queue(string execution)
            {
                var items = new List<RuntimeSchedulerWorkItem>(); string? cursor = null;
                do
                {
                    var page = await boundScope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAsync(new(execution, continuationToken: cursor));
                    items.AddRange(page.Items); cursor = page.NextContinuationToken;
                } while (cursor is not null);
                return items.OrderBy(item => item.WorkItemId, StringComparer.Ordinal).ToArray();
            }
            Console.WriteLine(JsonSerializer.Serialize(new { readOnly = true, model = boundModel, origin,
                worker = await boundScope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(origin.WorkerExecutionId),
                inputs, sources, queuedNativeWork = await Queue(origin.WorkerExecutionId), queuedModelInputs = await Queue(boundModel.ExecutionId),
                workerMaterial = await boundScope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().FindMaterialAsync(origin.WorkerExecutable.ArtifactId),
                nativeMarkers = await boundScope.ServiceProvider.GetRequiredService<RuntimeDbContext>().RuntimeCheckpointCommits.AsNoTracking()
                    .Where(row => row.WorkflowExecutionId == Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(origin.WorkerExecutionId)) &&
                        row.ScopeKey == Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(origin.Scope.Value)))
                    .OrderBy(row => row.CommitId).ToArrayAsync() }, json));
            return 0;
        }
        var boundWorker = await boundScope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(args[3])
            ?? throw new InvalidDataException("The actual native worker is absent.");
        if (args[0] == "--read-bpmn-embedding")
        {
            var embeddingActivities = new List<ActivityExecutionState>();
            var embeddingBookmarks = new List<BookmarkState>();
            var queuedModelInputs = new List<RuntimeSchedulerWorkItem>();
            string? cursor = null;
            do
            {
                var page = await boundScope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>()
                    .ListPageAsync(new(boundWorker.WorkflowExecutionId, continuationToken: cursor));
                embeddingActivities.AddRange(page.Items); cursor = page.NextContinuationToken;
            } while (cursor is not null);
            do
            {
                var page = await boundScope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>()
                    .ListAsync(new(args[2], continuationToken: cursor));
                queuedModelInputs.AddRange(page.Items); cursor = page.NextContinuationToken;
            } while (cursor is not null);
            do
            {
                var page = await boundScope.ServiceProvider.GetRequiredService<IBookmarkStateStore>()
                    .ListPageAsync(new(boundWorker.WorkflowExecutionId, continuationToken: cursor));
                embeddingBookmarks.AddRange(page.Items); cursor = page.NextContinuationToken;
            } while (cursor is not null);
            var inputIds = new List<string>(); var sourceIds = new List<string>();
            await using (var indexConnection = new SqliteConnection(boundConnectionString))
            {
                await indexConnection.OpenAsync();
                foreach (var selection in new[] { (Table: "elsa_runtime_model_input", Column: "InputId", Targets: new[] { args[2] }),
                             (Table: "elsa_runtime_post_commit_outbox", Column: "OutboxItemId", Targets: new[] { args[2], boundWorker.WorkflowExecutionId }) })
                {
                    await using var command = indexConnection.CreateCommand();
                    var column = selection.Column == "InputId" ? "ExecutionId" : "WorkflowExecutionId";
                    var projected = selection.Column == "InputId" ? "InputId" : "ContentJson";
                    command.CommandText = $"SELECT {projected} FROM {selection.Table} WHERE {column} IN ({string.Join(',', selection.Targets.Select((_, index) => "$p" + index))})";
                    for (var index = 0; index < selection.Targets.Length; index++)
                        command.Parameters.AddWithValue("$p" + index, Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(selection.Targets[index])));
                    await using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        // Outbox index identities may be bounded projections. Read the retained full logical ID,
                        // then require the normal checked provider to resolve the actual source below.
                        using var sourceDocument = selection.Column == "InputId" ? null : JsonDocument.Parse(reader.GetString(0));
                        var storedIdentity = sourceDocument is null ? reader.GetString(0) : sourceDocument.RootElement.GetProperty("outboxItemId").GetString()!;
                        var identity = System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(storedIdentity));
                        (selection.Column == "InputId" ? inputIds : sourceIds).Add(identity);
                    }
                }
            }
            var embeddingInputs = new List<RuntimeModelInputReceipt>();
            foreach (var identity in inputIds.Order(StringComparer.Ordinal))
                embeddingInputs.Add(await boundModels.FindInputAsync(args[2], identity) ?? throw new InvalidDataException("A physical model receipt disappeared."));
            var embeddingSources = new List<RuntimePostCommitOutboxItem>();
            var lookup = boundScope.ServiceProvider.GetRequiredService<IPostCommitOutboxLookupStore>();
            foreach (var identity in sourceIds.Order(StringComparer.Ordinal))
            {
                var embeddingSource = await lookup.FindDurableAsync(identity) ?? throw new InvalidDataException("A physical delivery source disappeared.");
                if (embeddingSource.Intent.Kind.StartsWith("Elsa.Bpmn.", StringComparison.Ordinal)) embeddingSources.Add(embeddingSource);
            }
            var directCounterpart = args.Length == 5 ? await boundModels.FindAsync(args[4]) ?? throw new InvalidDataException("The actual direct counterpart is absent.") : null;
            Console.WriteLine(JsonSerializer.Serialize(new { ReadOnly = true, Model = boundModel, DirectCounterpart = directCounterpart, NativeParent = boundWorker,
                Activities = embeddingActivities, Bookmarks = embeddingBookmarks, Inputs = embeddingInputs, Sources = embeddingSources,
                QueuedModelInputs = queuedModelInputs }, json));
            return 0;
        }
        if (boundModel is null) throw new InvalidDataException("The actual model execution is absent.");
        var boundOrigin = boundWorker.ModelWorkOrigin ?? throw new InvalidDataException("The native worker has no actual model origin.");
        if (!boundOrigin.MatchesModel(boundModel)) throw new InvalidDataException("The actual model and worker pins differ.");
        var boundOutbox = boundScope.ServiceProvider.GetRequiredService<IPostCommitOutboxLookupStore>();
        var boundStart = await boundOutbox.FindDurableAsync(boundOrigin.SourceOutboxItemId);
        var boundInput = await boundModels.FindInputAsync(boundModel.ExecutionId, boundOrigin.ResultInputId);
        RuntimePostCommitOutboxItem? boundResult = null;
        if (boundModel.Payload.TryGetProperty("nativeResults", out var boundResults) && boundResults.GetArrayLength() != 0)
            boundResult = await boundOutbox.FindDurableAsync(boundResults[0].GetProperty("sourceOutboxItemId").GetString()!);
        Console.WriteLine(JsonSerializer.Serialize(new { ReadOnly = true, Model = boundModel, Worker = boundWorker,
            StartSource = boundStart, ResultSource = boundResult, ResultInput = boundInput }, json));
        return 0;
    }
    if (args.Length == 3 && args[0] == "--backup-sqlite")
    {
        var backupSourceFile = Path.GetFullPath(args[1]);
        var backupTargetFile = Path.GetFullPath(args[2]);
        if (!File.Exists(backupSourceFile) || File.Exists(backupTargetFile))
            throw new InvalidDataException("The snapshot requires an existing source and a fresh target.");
        var backupSourceHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(backupSourceFile)));
        await using (var input = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backupSourceFile, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        await using (var output = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backupTargetFile, Pooling = false }.ToString()))
        {
            await input.OpenAsync(); await output.OpenAsync(); input.BackupDatabase(output);
            await using var check = output.CreateCommand(); check.CommandText = "PRAGMA integrity_check";
            if ((string?)await check.ExecuteScalarAsync() != "ok") throw new InvalidDataException("The actual SQLite backup failed integrity verification.");
        }
        if (backupSourceHash != Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(backupSourceFile))))
            throw new InvalidDataException("The source main file changed during the quiesced snapshot.");
        Console.WriteLine(JsonSerializer.Serialize(new { ReadOnly = true, SourceMainSha256 = backupSourceHash,
            SnapshotSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(backupTargetFile))) }, json));
        return 0;
    }
    if (args.Length == 2 && args[0] is "--inspect-studio-products" or "--inspect-etl-products" or "--inspect-native-bpmn-products")
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(args[1]))!;
        var names = args[0] == "--inspect-studio-products"
            ? new[] { "Elsa.Studio.Web.dll", "Elsa.Studio.Hosting.dll", "Elsa.Studio.Core.dll", "Elsa.Studio.Workflows.dll" }
            : args[0] == "--inspect-native-bpmn-products"
            ? new[] { "Elsa.Workbench.dll", "Elsa.Activities.Bpmn.Core.dll", "Elsa.Activities.Bpmn.dll", "Elsa.Activities.Bpmn.Api.dll",
                "Elsa.Workflows.Runtime.Core.dll", "Elsa.Workflows.Runtime.dll", "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.dll",
                "Elsa.Etl.Core.dll", "Elsa.Etl.Runtime.dll", "Elsa.Etl.Workflows.Runtime.dll", "Elsa.Etl.Persistence.EntityFrameworkCore.dll", "Elsa.Etl.Runtime.Api.dll" }
            : new[] { "Elsa.Workbench.dll", "Elsa.Etl.Core.dll", "Elsa.Etl.Runtime.dll", "Elsa.Etl.Workflows.Runtime.dll", "Elsa.Etl.Persistence.EntityFrameworkCore.dll", "Elsa.Etl.Runtime.Api.dll" };
        var products = names.Select(name =>
        {
            var path = Path.Combine(directory, name);
            return new { Name = name, ProductVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).ProductVersion,
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) };
        }).ToArray();
        var studioSourceCommit = products[0].ProductVersion?.Split('+').Last();
        if (studioSourceCommit?.Length != 40 || studioSourceCommit.Any(character => !char.IsAsciiHexDigitLower(character)) ||
            products.Any(product => product.ProductVersion?.EndsWith("+" + studioSourceCommit, StringComparison.Ordinal) != true))
            throw new InvalidDataException("The actual selected host products do not share one full source commit.");
        Console.WriteLine(JsonSerializer.Serialize(new { SourceCommit = studioSourceCommit, MetadataOnly = true, ActualProductVersions = products }, json));
        return 0;
    }
    if (args.Length == 3 && args[0] == "--check-predecessor-queue-reader")
    {
        Console.WriteLine(JsonSerializer.Serialize(await PredecessorQueueReader.RunAsync(args[1], args[2]), json));
        return 0;
    }
    if (args.Length == 2 && args[0] == "--inspect-host-products")
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(args[1]))!;
        var required = new[] { "Elsa.Workbench.dll", "Elsa.Activities.Bpmn.dll", "Elsa.Activities.Bpmn.Interchange.dll",
            "Elsa.Workflows.Runtime.Core.dll", "Elsa.Workflows.Runtime.dll", "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.dll",
            "Elsa.Activities.Runtime.dll", "Elsa.Activities.DispatchWorkflow.Runtime.dll", "Elsa.Serialization.SystemText.dll" };
        var products = required.Select(name => {
            var path = Path.Combine(directory, name);
            var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).ProductVersion
                ?? throw new InvalidDataException($"Product metadata is missing for '{name}'.");
            using var stream = File.OpenRead(path);
            return new { Name = name, ProductVersion = version, Sha256 = Convert.ToHexStringLower(SHA256.HashData(stream)) };
        }).ToArray();
        var hostProductCommit = products[0].ProductVersion.Split('+').Last();
        if (hostProductCommit.Length != 40 || hostProductCommit.Any(character => !char.IsAsciiHexDigitLower(character)) ||
            products.Any(product => !product.ProductVersion.EndsWith("+" + hostProductCommit, StringComparison.Ordinal)))
            throw new InvalidDataException("The actual host/writer products do not share one full source commit.");
        var libraries = new[] { "Bpmn.Model.dll", "Bpmn.Semantics.dll", "Bpmn.Interchange.dll" }.Select(name => {
            var path = Path.Combine(directory, name);
            using var stream = File.OpenRead(path);
            return new { Name = name, ProductVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).ProductVersion,
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(stream)) };
        }).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new { SourceCommit = hostProductCommit, MetadataOnly = true,
            ActualProductVersions = products, ReleasedLibraryProducts = libraries }, json));
        return 0;
    }
    if (args.Length == 6 && args[0] == "--inspect-native-parent")
    {
        await VerifySnapshotAsync(args[1], args[2]);
        var observed = await OptedCalledSnapshotReader.ReadNativeParentAsync(args[1], args[3], args[4], args[5]);
        await VerifySnapshotAsync(args[1], args[2]);
        Console.WriteLine(JsonSerializer.Serialize(new { ProviderChecked = true, ReadOnly = true,
            SnapshotSha256 = args[2], NativeParent = observed }, json));
        return 0;
    }
    if (args.Length == 6 && args[0] == "--inspect-multi-instance")
    {
        await VerifySnapshotAsync(args[1], args[2]);
        var observed = await OptedCalledSnapshotReader.ReadMultiInstanceAsync(args[1], args[3], args[4], int.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture));
        await VerifySnapshotAsync(args[1], args[2]);
        Console.WriteLine(JsonSerializer.Serialize(new { ProviderChecked = true, ReadOnly = true, SnapshotSha256 = args[2], MultiInstance = observed }, json));
        return 0;
    }
    if (args.Length == 6 && args[0] == "--inspect-compensation")
    {
        await VerifySnapshotAsync(args[1], args[2]);
        var observed = await OptedCalledSnapshotReader.ReadCompensationAsync(args[1], args[3], args[4], args[5]);
        await VerifySnapshotAsync(args[1], args[2]);
        Console.WriteLine(JsonSerializer.Serialize(new { ProviderChecked = true, ReadOnly = true,
            SnapshotSha256 = args[2], Compensation = observed }, json));
        return 0;
    }
    if (args.Length == 7 && args[0] == "--inspect-called-ack")
    {
        await VerifySnapshotAsync(args[1], args[2]);
        var observed = await OptedCalledSnapshotReader.ReadAcknowledgmentAsync(args[1], args[3], args[4], args[5], args[6]);
        await VerifySnapshotAsync(args[1], args[2]);
        Console.WriteLine(JsonSerializer.Serialize(new { ProviderChecked = true, ReadOnly = true,
            SnapshotSha256 = args[2], CalledWorkflow = observed }, json));
        return 0;
    }
    if (args.Length == 6 && args[0] == "--inspect-opted-called")
    {
        await VerifySnapshotAsync(args[1], args[2]);
        var observed = await OptedCalledSnapshotReader.ReadAsync(args[1], args[3], args[4], args[5]);
        await VerifySnapshotAsync(args[1], args[2]);
        Console.WriteLine(JsonSerializer.Serialize(new { ProviderChecked = true, ReadOnly = true,
            SnapshotSha256 = args[2], CalledWorkflows = observed }, json));
        return 0;
    }
    if (args.Length == 6 && args[0] == "--inspect-called")
    {
        await VerifySnapshotAsync(args[1], args[2]);
        var observed = await CalledSnapshotReader.ReadAsync(args[1], args[3], args[4], args[5]);
        await VerifySnapshotAsync(args[1], args[2]);
        Console.WriteLine(JsonSerializer.Serialize(new { ProviderChecked = true, ReadOnly = true,
            SnapshotSha256 = args[2], CalledWorkflows = observed }, json));
        return 0;
    }
    if (args.Length == 2 && args[0] == "--inspect-host")
    {
        var assembly = Path.GetFullPath(args[1]);
        using var stream = File.OpenRead(assembly); using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var containsProbe = metadata.TypeDefinitions.Any(handle => {
            var type = metadata.GetTypeDefinition(handle);
            return metadata.GetString(type.Namespace) == "Elsa.Workbench.RecoveryProbe" &&
                   metadata.GetString(type.Name) == "BpmnRecoveryProbeFeature";
        });
        var manifest = new List<object>();
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(assembly)!).Where(file =>
                     file.EndsWith(".dll", StringComparison.Ordinal) || file.EndsWith(".deps.json", StringComparison.Ordinal) ||
                     file.EndsWith(".runtimeconfig.json", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            await using var dependency = File.OpenRead(file);
            manifest.Add(new { Name = Path.GetFileName(file), Sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(dependency)) });
        }
        Console.WriteLine(JsonSerializer.Serialize(new { ContainsRecoveryProbe = containsProbe, Files = manifest }, json));
        return 0;
    }
    if (args.Length == 3 && args[0] == "--inspect-membership")
    {
        var membershipPath = Path.GetFullPath(args[1]);
        await VerifySnapshotAsync(membershipPath, args[2]);
        var membershipCs = new SqliteConnectionStringBuilder { DataSource = new Uri(membershipPath).AbsoluteUri + "?immutable=1", Mode = SqliteOpenMode.ReadOnly }.ToString();
        await using var membershipConnection = new SqliteConnection(membershipCs); await membershipConnection.OpenAsync();
        await using (var queryOnly = membershipConnection.CreateCommand()) { queryOnly.CommandText = "PRAGMA query_only=ON"; await queryOnly.ExecuteNonQueryAsync(); }
        await using var membership = new ClusterMembershipSqliteDbContext(new DbContextOptionsBuilder<ClusterMembershipSqliteDbContext>().UseSqlite(membershipConnection).Options);
        var members = (await membership.Members.AsNoTracking().ToListAsync()).Select(StoredMember.From).ToArray();
        if (members.Any(member => !member.IsInterpretable)) throw new InvalidDataException("A fixture membership row is not interpretable.");
        var now = DateTimeOffset.UtcNow;
        await VerifySnapshotAsync(membershipPath, args[2]);
        Console.WriteLine(JsonSerializer.Serialize(new { ProviderChecked = true, ReadOnly = true, ProviderKind = "entity-framework-core",
            SnapshotSha256 = args[2], JudgedAtUtc = now, SkewAllowanceSeconds = 0,
            Members = members.Select(member => new { member.Identity.HostId, Incarnation = member.Identity.Incarnation.Value,
                Status = member.Status.ToString(), member.HeartbeatAt, member.ExpiryPeriod, member.ReportRevision,
                member.IsInterpretable, member.IsCurrent, IsLive = member.IsLive(now, TimeSpan.Zero) }) }, json));
        return 0;
    }
    var findPublisher = args.Length > 0 && args[0] == "--find-publisher-source";
    if (findPublisher) args = args.Skip(1).ToArray();
    if (args.Length < 4) throw new ArgumentException("Expected snapshot, hash, scope, source ID and optional workflow IDs.");
    var path = Path.GetFullPath(args[0]);
    await VerifySnapshotAsync(path, args[1]);
    var cs = new SqliteConnectionStringBuilder { DataSource = new Uri(path).AbsoluteUri + "?immutable=1", Mode = SqliteOpenMode.ReadOnly }.ToString();
    await using var connection = new SqliteConnection(cs); await connection.OpenAsync();
    await using (var command = connection.CreateCommand()) { command.CommandText = "PRAGMA query_only=ON"; await command.ExecuteNonQueryAsync(); }
    await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
    var scope = new PersistenceScope(args[2]); var access = new ScopedAccess(scope);
    var scopeKey = EfRelationalIdentity.Encode(scope.Value); var scopeHash = EfRelationalIdentity.Hash(scope.Value);
    var outbox = new EfRuntimePostCommitOutboxStore(context, access);
    var sourceId = args[3];
    if (findPublisher)
    {
        var workflowKey = EfRelationalIdentity.Encode(args[3]); var workflowHash = EfRelationalIdentity.Hash(args[3]);
        var candidateIds = await context.RuntimePostCommitOutbox.AsNoTracking().Where(row => row.ScopeKey == scopeKey && row.ScopeKeyHash == scopeHash &&
            row.WorkflowExecutionId == workflowKey && row.WorkflowExecutionIdHash == workflowHash)
            .Select(row => row.OutboxItemId).ToArrayAsync();
        var candidates = new List<RuntimePostCommitOutboxItem>();
        foreach (var candidateId in candidateIds)
            if (await outbox.FindAsync(EfRelationalIdentity.Decode(candidateId)) is { StimulusDeliveryProtocolVersion: not null } candidate) candidates.Add(candidate);
        if (candidates.Count != 1) throw new InvalidDataException("Exactly one publisher source is required for this owned control fixture.");
        sourceId = candidates[0].OutboxItemId;
    }
    var source = await outbox.FindAsync(sourceId)
        ?? throw new InvalidDataException("The exact committed source is missing.");
    if (findPublisher && source.Intent.WorkflowExecutionId != args[3])
        throw new InvalidDataException("The candidate source differs from the exact control publisher.");
    var sourceKey = EfRelationalIdentity.Encode(source.OutboxItemId); var sourceHash = EfRelationalIdentity.Hash(source.OutboxItemId);
    var sourceRow = await context.RuntimePostCommitOutbox.AsNoTracking().SingleAsync(row =>
        row.ScopeKey == scopeKey && row.ScopeKeyHash == scopeHash && row.OutboxItemId == sourceKey && row.OutboxItemIdHash == sourceHash);
    var sourceContentSha256 = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sourceRow.ContentJson)));
    var executions = new EfWorkflowExecutionStateStore(context, access, new NoPaging());
    var activities = new EfActivityExecutionStateStore(context, access, new NoPaging());
    var bookmarks = new EfBookmarkStateStore(context, access);
    var publisher = await executions.FindAsync(source.Intent.WorkflowExecutionId) ?? throw new InvalidDataException("The source publisher is missing.");
    if (source.StimulusDeliveryProtocolVersion != 1 || source.StimulusDeliverySourceScope != scope)
        throw new InvalidDataException("The expected protocol/source scope is absent.");
    var cohort = source.RecipientSnapshot;
    if (cohort is not null && (cohort.RetentionAcceptanceVersion != 1 || cohort.SourceOutboxItemId != source.OutboxItemId ||
        cohort.IntentFingerprint != RuntimeCheckpointCommitFingerprint.ComputeIntent(source.Intent) || !cohort.Scope.Matches(publisher, scope)))
        throw new InvalidDataException("The saved cohort differs from its canonical source/publisher.");
    var ids = new HashSet<string>(args.Skip(4), StringComparer.Ordinal) { source.Intent.WorkflowExecutionId };
    if (cohort is not null)
    {
        foreach (var resume in cohort.Resumes) ids.Add(resume.WorkflowExecutionId);
        foreach (var start in cohort.Starts) ids.Add(KeyedWorkflowStartIdentity.For(source.Intent.IdempotencyKey!, start.Executable.ArtifactId).WorkflowExecutionId);
    }
    var observations = new List<object>();
    foreach (var id in ids.Order(StringComparer.Ordinal))
    {
        var state = await executions.FindAsync(id);
        var waits = new List<object>();
        foreach (var bookmark in await ((IBookmarkStateStore)bookmarks).ListAllBookmarkStatesAsync(id))
        {
            var observed = await bookmarks.ObserveAsync(id, bookmark.BookmarkId) ?? throw new InvalidDataException("A closed snapshot's wait disappeared.");
            var activity = await activities.FindAsync(id, bookmark.ActivityExecutionId);
            waits.Add(new { bookmark.BookmarkId, bookmark.ActivityExecutionId, bookmark.ExecutableNodeId, bookmark.StimulusType,
                bookmark.StimulusHash, bookmark.ResumeTargetId, observed.Generation, bookmark.CreatedAt, bookmark.ExpiresAt, bookmark.Metadata,
                ActivityStatus = activity?.Status.ToString(), AttemptCount = activity?.Attempts?.Count });
        }
        var selectedActivities = new List<object>();
        foreach (var selected in cohort?.Resumes.Where(entry => entry.WorkflowExecutionId == id) ?? [])
        {
            var activity = await activities.FindAsync(id, selected.ActivityExecutionId);
            selectedActivities.Add(new { selected.ActivityExecutionId, Status = activity?.Status.ToString(),
                BookmarkIds = activity?.BookmarkIds, AttemptCount = activity?.Attempts?.Count });
        }
        var testScope = state?.TestScope is { } isolated
            ? await new EfWorkflowTestScopeStore(context, access, new NoPaging()).FindAsync(isolated.ScopeId) : null;
        if (state?.TestScope is not null && (testScope is null || !WorkflowTestScope.ContextEquals(testScope.Scope, state.TestScope))) throw new InvalidDataException("The observed test execution has no authoritative scope record.");
        var testActivities = state?.TestScope is null ? null : (await activities.ListAllAsync(id)).Select(activity => new {
            activity.Execution.ActivityExecutionId, activity.Execution.ExecutableNodeId, Status = activity.Status.ToString(),
            AttemptCount = activity.Attempts?.Count, activity.BookmarkIds }).ToArray();
        observations.Add(new { WorkflowExecutionId = id, Exists = state is not null, Status = state?.Status.ToString(),
            PinnedExecutable = state?.PinnedExecutable, PinnedSource = state?.PinnedSource, RunKind = state?.RunKind,
            state?.TenantId, state?.Partition, state?.TestScope, TestScopeState = testScope?.State.ToString(),
            TestActivities = testActivities, TestRunDeliveryReceipts = state?.SystemMetadata.Where(item => item.Key.StartsWith(TestRunDeliveryReceipt.ReceiptPrefix, StringComparison.Ordinal)).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
            Bookmarks = waits, SelectedActivities = selectedActivities });
    }
    var all = await executions.ListAsync();
    // Observe declared stimulus fields and the actual checked binding index; these reads grant no dispatch authority.
    var intentPayload = source.Intent.Payload ?? throw new InvalidDataException("The fixture source has no stimulus payload.");
    var stimulusType = intentPayload.GetProperty("stimulusType").GetString() ?? throw new InvalidDataException("Missing declared stimulus type.");
    var stimulusHash = intentPayload.GetProperty("stimulusHash").GetString() ?? throw new InvalidDataException("Missing declared stimulus hash.");
    var bindings = new List<object>();
    var bindingStore = new EfWorkflowTriggerBindingStore(context, access);
    string? continuation = null;
    do
    {
        var page = await bindingStore.ListByStimulusAsync(new(stimulusType, stimulusHash, continuationToken: continuation));
        bindings.AddRange(page.Items.Select(binding => new { binding.TriggerBindingId, binding.ArtifactId,
            binding.ExecutableNodeId, binding.StimulusType, binding.StimulusHash, binding.CorrelationScope, binding.IsActive, binding.CreatedAt }));
        continuation = page.NextContinuationToken;
    } while (continuation is not null);
    var material = new EfWorkflowExecutableStore(context, access);
    var references = new EfWorkflowExecutableSourceReferenceStore(context, access, new NoPaging());
    var observedMaterial = (await material.ListAllAsync()).Select(item => new { item.Identity, item.CreatedAt, item.Dependencies,
        TemplateHashReferences = item.NodesById.Values.Select(node => node.Metadata).Prepend(item.CompatibilityMetadata)
            .Where(metadata => metadata.TryGetValue("activity.templateHash", out var hash) && !string.IsNullOrWhiteSpace(hash))
            .Select(metadata => metadata["activity.templateHash"]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() }).ToArray();
    var observedTemplates = (await new EfExecutableActivityTemplateStore(context, access, new NoPaging()).ListAllAsync())
        .Select(template => new { template.TemplateId, template.TemplateHash, template.DirectDependencies, template.ClosedTemplates,
            TemplateHashReferences = template.NodesById.Values.Select(node => node.Metadata).Prepend(template.CompatibilityMetadata)
                .Where(metadata => metadata.TryGetValue("activity.templateHash", out var hash) && !string.IsNullOrWhiteSpace(hash))
                .Select(metadata => metadata["activity.templateHash"]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() }).ToArray();
    var observedReferences = (await references.ListAllAsync()).Select(reference => new { reference.SourceReferenceId, reference.ArtifactId,
        reference.SourceKind, reference.SourceVersion, reference.DefinitionId, reference.DefinitionVersionId,
        reference.ArtifactVersion, Scope = reference.Scope.ToString(), reference.CreatedAt, reference.PublishedAt,
        reference.ExpiresAt, reference.DeletedAt, reference.DeletedReason, reference.ActivationId, reference.SlotId }).ToArray();
    var executionRootIds = await executions.ListPinnedExecutableArtifactIdsAsync();
    var dispatchRootIds = await new EfWorkflowDispatchStore(context, access).ListPinnedExecutableArtifactIdsAsync();
    await VerifySnapshotAsync(path, args[1]);
    Console.WriteLine(JsonSerializer.Serialize(new { ProviderChecked = true, ReadOnly = true, SnapshotSha256 = args[1],
        ObservedMaterial = observedMaterial, ObservedTemplates = observedTemplates, ObservedSourceReferences = observedReferences, ExecutionRootArtifactIds = executionRootIds, DispatchRootArtifactIds = dispatchRootIds,
        ObservedAtUtc = DateTimeOffset.UtcNow, Source = new { source.OutboxItemId, source.Intent.WorkflowExecutionId, source.RecordedAt, Status = source.Status.ToString(), source.DeliveryAttemptCount,
            ContentSha256 = sourceContentSha256, RowRevision = sourceRow.Revision, StorageSchemaVersion = sourceRow.SchemaVersion,
            source.DeliveringOwnerId, source.DeliveryFencingToken, source.DeliveryVisibleAfter, source.DeliveredAt, source.LastFailureMessage, IntentPayload = intentPayload,
            IntentFingerprint = RuntimeCheckpointCommitFingerprint.ComputeIntent(source.Intent), Snapshot = cohort,
            SelectedResumes = cohort?.Resumes.Select(entry => new { entry.WorkflowExecutionId, entry.BookmarkId, entry.ActivityExecutionId, entry.ExecutableNodeId, entry.CreatedAt, Generation = entry.RegistrationCapture?.Generation }),
            SelectedStarts = cohort?.Starts.Select(entry => new { entry.Executable.ArtifactId, entry.Binding.TriggerBindingId, BindingCreatedAt = entry.Binding.CreatedAt,
                StartKey = KeyedWorkflowStartIdentity.For(source.Intent.IdempotencyKey!, entry.Executable.ArtifactId).StartKey,
                WorkflowExecutionId = KeyedWorkflowStartIdentity.For(source.Intent.IdempotencyKey!, entry.Executable.ArtifactId).WorkflowExecutionId }) }, Workflows = observations, MatchingBindings = bindings,
        AllWorkflows = all.Select(state => new { state.WorkflowExecutionId, state.PinnedExecutable.ArtifactId, Status = state.Status.ToString() }) }, json));
    return 0;

}
catch (Exception exception)
{
    Console.WriteLine(JsonSerializer.Serialize(new { ProviderChecked = false, Status = "ObservationUnavailable", FailureType = exception.GetType().Name }, json));
    return 2;
}

static async Task VerifySnapshotAsync(string path, string expectedHash)
{
    if (File.Exists(path + "-wal") || File.Exists(path + "-shm") || File.Exists(path + "-journal"))
        throw new InvalidDataException("A private closed snapshot without live sidecars is required.");
    await using var stream = File.OpenRead(path);
    if (Convert.ToHexStringLower(await SHA256.HashDataAsync(stream)) != expectedHash)
        throw new InvalidDataException("The private snapshot differs from its expected hash.");
}

internal sealed class ScopedAccess(PersistenceScope scope) : IPersistenceAccessContextAccessor
{ public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(scope); }
// Exact Find calls do not use continuation codecs. This inspector refuses any accidental paging-token use.
internal sealed class NoPaging : IRuntimeRecoveryContinuationCodec
{
    public string Encode(string purpose, ReadOnlySpan<byte> payload) => throw new NotSupportedException("The closed snapshot inspector does not expose paging tokens.");
    public byte[] Decode(string purpose, string token) => throw new NotSupportedException("The closed snapshot inspector does not accept paging tokens.");
}
