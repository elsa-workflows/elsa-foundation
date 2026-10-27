using System.Security.Cryptography;
using System.Reflection.PortableExecutable;
using System.Reflection.Metadata;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

// Closed, coherent fixture snapshots only. Every runtime row is read through the actual checked provider.
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
try
{
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
    if (args.Length < 4) throw new ArgumentException("Expected snapshot, hash, scope, source ID and optional workflow IDs.");
    var path = Path.GetFullPath(args[0]);
    await VerifyAsync();
    var cs = new SqliteConnectionStringBuilder { DataSource = new Uri(path).AbsoluteUri + "?immutable=1", Mode = SqliteOpenMode.ReadOnly }.ToString();
    await using var connection = new SqliteConnection(cs); await connection.OpenAsync();
    await using (var command = connection.CreateCommand()) { command.CommandText = "PRAGMA query_only=ON"; await command.ExecuteNonQueryAsync(); }
    await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
    var scope = new PersistenceScope(args[2]); var access = new ScopedAccess(scope);
    var source = await new EfRuntimePostCommitOutboxStore(context, access).FindAsync(args[3])
        ?? throw new InvalidDataException("The exact committed source is missing.");
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
                bookmark.StimulusHash, observed.Generation, bookmark.CreatedAt, bookmark.ExpiresAt,
                ActivityStatus = activity?.Status.ToString(), AttemptCount = activity?.Attempts?.Count });
        }
        var selectedActivities = new List<object>();
        foreach (var selected in cohort?.Resumes.Where(entry => entry.WorkflowExecutionId == id) ?? [])
        {
            var activity = await activities.FindAsync(id, selected.ActivityExecutionId);
            selectedActivities.Add(new { selected.ActivityExecutionId, Status = activity?.Status.ToString(),
                BookmarkIds = activity?.BookmarkIds, AttemptCount = activity?.Attempts?.Count });
        }
        observations.Add(new { WorkflowExecutionId = id, Exists = state is not null, Status = state?.Status.ToString(),
            PinnedExecutable = state?.PinnedExecutable, PinnedSource = state?.PinnedSource, RunKind = state?.RunKind, Bookmarks = waits, SelectedActivities = selectedActivities });
    }
    var all = await executions.ListAsync();
    await VerifyAsync();
    Console.WriteLine(JsonSerializer.Serialize(new { ProviderChecked = true, ReadOnly = true, SnapshotSha256 = args[1],
        Source = new { source.OutboxItemId, source.Intent.WorkflowExecutionId, source.RecordedAt, Status = source.Status.ToString(), source.DeliveryAttemptCount,
            source.DeliveringOwnerId, source.DeliveryFencingToken, source.DeliveryVisibleAfter, source.DeliveredAt, source.LastFailureMessage,
            IntentFingerprint = RuntimeCheckpointCommitFingerprint.ComputeIntent(source.Intent), Snapshot = cohort,
            SelectedResumes = cohort?.Resumes.Select(entry => new { entry.WorkflowExecutionId, entry.BookmarkId, entry.ActivityExecutionId, entry.ExecutableNodeId, entry.CreatedAt, Generation = entry.RegistrationCapture?.Generation }),
            SelectedStarts = cohort?.Starts.Select(entry => new { entry.Executable.ArtifactId, BindingCreatedAt = entry.Binding.CreatedAt,
                WorkflowExecutionId = KeyedWorkflowStartIdentity.For(source.Intent.IdempotencyKey!, entry.Executable.ArtifactId).WorkflowExecutionId }) }, Workflows = observations,
        AllWorkflows = all.Select(state => new { state.WorkflowExecutionId, state.PinnedExecutable.ArtifactId, Status = state.Status.ToString() }) }, json));
    return 0;

    async Task VerifyAsync()
    {
        if (File.Exists(path + "-wal") || File.Exists(path + "-shm") || File.Exists(path + "-journal"))
            throw new InvalidDataException("A private closed snapshot without live sidecars is required.");
        await using var stream = File.OpenRead(path);
        if (Convert.ToHexStringLower(await SHA256.HashDataAsync(stream)) != args[1])
            throw new InvalidDataException("The private snapshot differs from its expected hash.");
    }
}
catch (Exception exception)
{
    Console.WriteLine(JsonSerializer.Serialize(new { ProviderChecked = false, Status = "ObservationUnavailable", FailureType = exception.GetType().Name }, json));
    return 2;
}

internal sealed class ScopedAccess(PersistenceScope scope) : IPersistenceAccessContextAccessor
{ public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(scope); }
// Exact Find calls do not use continuation codecs. This inspector refuses any accidental paging-token use.
internal sealed class NoPaging : IRuntimeRecoveryContinuationCodec
{
    public string Encode(string purpose, ReadOnlySpan<byte> payload) => throw new NotSupportedException("The closed snapshot inspector does not expose paging tokens.");
    public byte[] Decode(string purpose, string token) => throw new NotSupportedException("The closed snapshot inspector does not accept paging tokens.");
}
