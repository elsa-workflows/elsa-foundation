using System.Data;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using Orleans;
using Orleans.Configuration;
using Orleans.Runtime;
using Testcontainers.PostgreSql;

const string clusterId = "membership-probe";
const string mainScriptSha = "623c15320e640cdd3e84d0668dcae481312f2631ac4977bf2eb8cf22e05deb8b";
const string clusteringScriptSha = "39dff7cbb72a390223cd48a9bc7d4a90252092dcba5677318bd510cc9fc1af08";
string? mutation = null;
string? outputPath = null;
for (var index = 0; index < args.Length; index++)
{
    if (args[index] == "--mutation" && index + 1 < args.Length) mutation = args[++index];
    else if (args[index] == "--output" && index + 1 < args.Length) outputPath = args[++index];
    else
    {
        Console.Error.WriteLine("usage: dotnet run -- [--output <json-path>] [--mutation hide-report-read|ignore-incarnation]");
        return 2;
    }
}
if (mutation is not null && mutation is not ("hide-report-read" or "ignore-incarnation"))
{
    Console.Error.WriteLine("mutation must be hide-report-read or ignore-incarnation");
    return 2;
}

var result = new ProbeResult
{
    Candidate = "A: PostgreSQL ADO.NET clustering plus same-database report facet",
    OrleansVersion = "10.4.0",
    MainScriptSha256 = mainScriptSha,
    ClusteringScriptSha256 = clusteringScriptSha,
    Mutation = mutation ?? "none"
};
var postgres = new PostgreSqlBuilder("postgres:16-alpine")
    .WithDatabase("orleans_probe")
    .WithUsername("probe")
    .WithPassword("probe")
    .Build();
var phase = "container-start";
try
{
    await postgres.StartAsync();
    var connectionString = postgres.GetConnectionString();
    result.PostgreSqlVersion = await ReadServerVersion(connectionString);
    phase = "database-initialize";
    await InitializeDatabase(connectionString);
    var ports = AllocatePorts(4);
    var (siloPortA, siloPortB, gatewayPortA, gatewayPortB) = (ports[0], ports[1], ports[2], ports[3]);
    await using var first = new SiloHandle(await StartSilo(connectionString, siloPortA, gatewayPortA, "probe-silo-a"));
    await using var second = new SiloHandle(await StartSilo(connectionString, siloPortB, gatewayPortB, "probe-silo-b"));

    var firstMembership = first.Host.Services.GetRequiredService<IMembershipTable>();
    var secondMembership = second.Host.Services.GetRequiredService<IMembershipTable>();
    phase = "initial-fresh-projections";
    var firstProjection = await ReadFreshProjection(firstMembership, connectionString, mutation);
    var secondProjection = await ReadFreshProjection(secondMembership, connectionString, mutation);
    Require(firstProjection.Members.Count(member => member.Status == nameof(SiloStatus.Active)) == 2,
        "membership-active-count", "first silo's membership table did not return two active entries");
    Require(secondProjection.Members.Count(member => member.Status == nameof(SiloStatus.Active)) == 2,
        "shared-membership-backend", "second silo's membership table did not return the same two active entries");
    Require(firstProjection.Members.Select(member => member.Identity).Order(StringComparer.Ordinal)
            .SequenceEqual(secondProjection.Members.Select(member => member.Identity).Order(StringComparer.Ordinal)),
        "shared-membership-snapshot", "the two configured membership tables returned different membership identities");
    result.Provider = "Orleans.Clustering.AdoNet / Npgsql";
    result.InitialSilos = firstProjection.Members
        .Where(member => member.Status == nameof(SiloStatus.Active))
        .OrderBy(member => member.SiloName, StringComparer.Ordinal)
        .Select(member => new SiloReceipt(member.SiloName, member.Identity, GetGeneration(member.Identity)))
        .ToArray();
    result.ActiveMembershipEntries = firstProjection.Members.Count(member => member.Status == nameof(SiloStatus.Active));
    var oldSilo = firstProjection.Members.Single(member => member.SiloName == "probe-silo-a" && member.Status == nameof(SiloStatus.Active));
    var oldIdentity = oldSilo.Identity;

    phase = "report-revisions-and-fresh-reads";
    Require(oldSilo.ReportState == "unknown" && oldSilo.CapabilityCount == 0,
        "initial-report-unknown", "missing report must have unknown state and no capabilities");
    await PublishReport(connectionString, clusterId, StorageIdentity(oldIdentity, mutation), 0, "{\"capability\":\"probe\"}");
    Report? heldAgain;
    FreshProjection freshAfterUpdateA;
    FreshProjection freshAfterUpdateB;
    await using (var heldRead = await HeldReportRead.BeginAsync(connectionString, clusterId, StorageIdentity(oldIdentity, mutation)))
    {
        Require(heldRead.Report is { Revision: 1 }, "held-read-before-publish", "repeatable-read transaction did not capture revision 1");
        // Removing a capability must become visible together with the new revision, not only its revision number.
        await PublishReport(connectionString, clusterId, StorageIdentity(oldIdentity, mutation), 1, "{}");
        heldAgain = await heldRead.ReadAsync();
        Require(heldAgain is { Revision: 1 }, "held-read-snapshot", "repeatable-read transaction did not retain its original snapshot");
        freshAfterUpdateA = await ReadFreshProjection(firstMembership, connectionString, mutation, mutation == "hide-report-read" ? firstProjection : null);
        freshAfterUpdateB = await ReadFreshProjection(secondMembership, connectionString, mutation, mutation == "hide-report-read" ? firstProjection : null);
    }
    Require(Find(freshAfterUpdateA, oldIdentity) is { Revision: 2, ReportState: "known", CapabilityCount: 0 } &&
            Find(freshAfterUpdateB, oldIdentity) is { Revision: 2, ReportState: "known", CapabilityCount: 0 },
        "fresh-report-visible", "later independent projections did not observe revision 2 and the removed capability");
    result.HeldReadSawRevision = checked((int)(heldAgain?.Revision ?? 0));
    result.FreshReadSawRevision = Find(freshAfterUpdateB, oldIdentity).Revision;
    result.FreshReadCapabilityCount = Find(freshAfterUpdateB, oldIdentity).CapabilityCount;
    result.HeldReadCapabilityCount = CountCapabilities(heldAgain?.Payload ?? "{}");

    var staleWriteRejected = false;
    try { await PublishReport(connectionString, clusterId, StorageIdentity(oldIdentity, mutation), 1, "{\"capability\":\"stale-overwrite\"}"); }
    catch (ProbeFailure failure) when (failure.Code == "report-cas-rejected") { staleWriteRejected = true; }
    Require(staleWriteRejected, "stale-report-write-rejected", "compare-and-swap accepted a stale report revision");

    phase = "same-name-rejoin";
    await first.StopAsync();
    await using var restarted = new SiloHandle(await StartSilo(connectionString, siloPortA, gatewayPortA, "probe-silo-a"));
    var rejoined = await WaitForActiveSilo(secondMembership, "probe-silo-a", GetGeneration(oldIdentity));
    var newIdentity = rejoined.SiloAddress.ToParsableString();
    result.RejoinedSilo = new SiloReceipt(rejoined.SiloName, newIdentity, rejoined.SiloAddress.Generation);
    Require(newIdentity != oldIdentity && rejoined.SiloAddress.Generation != GetGeneration(oldIdentity),
        "rejoin-new-generation", "same-name restarted silo did not receive a new Orleans address generation");
    phase = "rejoined-silo-report-projection";
    var afterRejoin = await ReadFreshProjection(secondMembership, connectionString, mutation);
    Require(Find(afterRejoin, newIdentity) is { ReportState: "unknown", CapabilityCount: 0 }, "new-incarnation-report-unknown",
        "new incarnation inherited the old incarnation report");
    await PublishReport(connectionString, clusterId, StorageIdentity(newIdentity, mutation), 0, "{\"capability\":\"replacement\"}");
    var lateOldRevision = await PublishReport(connectionString, clusterId, StorageIdentity(oldIdentity, mutation), 2, "{\"capability\":\"late-old-report\"}");
    Require(lateOldRevision == 3, "late-old-key-isolated", "late old report did not remain scoped to its old identity");
    var afterNewPublish = await ReadFreshProjection(secondMembership, connectionString, mutation);
    Require(Find(afterNewPublish, newIdentity) is { Revision: 1, CapabilityCount: 1 }, "new-report-visible-from-silo-b",
        "silo B's later all-member projection did not see the new incarnation's published report");
    Require(Find(afterNewPublish, oldIdentity).Revision == 3, "old-report-isolated", "late report did not update the old identity row only");
    result.IncarnationIsolation = true;

    phase = "unsupported-schema-projection";
    await SetReportSchemaVersion(connectionString, StorageIdentity(oldIdentity, mutation), 999);
    var unsupportedSchemaProjection = await ReadFreshProjection(secondMembership, connectionString, mutation);
    var unsupported = Find(unsupportedSchemaProjection, oldIdentity);
    Require(unsupported.ReportState == "unknown" && unsupported.CapabilityCount == 0,
        "unsupported-report-schema-unknown", "unsupported report schema credited capabilities");
    await SetReportSchemaVersion(connectionString, StorageIdentity(oldIdentity, mutation), 1);

    phase = "report-read-failure-control";
    var failureControlSnapshot = (await secondMembership.ReadAllAsync()).Members;
    await RenameReportTable(connectionString, "probe_report", "probe_report_unavailable");
    var reportReadFailedClosed = false;
    try { _ = await ReadFreshProjectionFromSnapshot(failureControlSnapshot, connectionString, mutation); }
    catch (PostgresException) { reportReadFailedClosed = true; }
    finally { await RenameReportTable(connectionString, "probe_report_unavailable", "probe_report"); }
    Require(reportReadFailedClosed, "report-read-failed-closed", "report query failure returned a partial membership projection");

    result.AllMemberProjectionCount = afterNewPublish.Members.Count;
    result.Assertions = ["two-active-silos", "both-hosts-same-membership-table", "missing-report-unknown", "repeatable-read-held-at-revision-one", "fresh-read-revision-two-and-capability-removal", "stale-cas-rejected", "same-name-rejoin-new-generation", "new-incarnation-report-unknown", "new-report-visible-from-silo-b", "late-old-key-isolated", "unsupported-schema-unknown", "report-read-failed-closed"];
    result.Outcome = "passed";
}
catch (Exception exception)
{
    result.Outcome = "failed";
    result.FailureReason = exception is ProbeFailure failure ? failure.Code : $"probe-exception:{phase}";
    result.Detail = $"{exception.GetType().Name}: {exception.Message}";
}
try
{
    await postgres.DisposeAsync();
    result.ContainerCleanupCompleted = true;
}
catch (Exception exception)
{
    result.Outcome = "failed";
    result.FailureReason ??= "container-cleanup-failed";
    result.Detail ??= exception.Message;
}
var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine(json);
if (outputPath is not null) await File.WriteAllTextAsync(outputPath, json);
return result.Outcome == "passed" ? 0 : 1;

static int[] AllocatePorts(int count)
{
    var listeners = new List<TcpListener>();
    try
    {
        for (var index = 0; index < count; index++)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listeners.Add(listener);
            listener.Start();
        }
        return listeners.Select(listener => ((IPEndPoint)listener.LocalEndpoint).Port).ToArray();
    }
    finally
    {
        foreach (var listener in listeners) listener.Stop();
    }
}

static string StorageIdentity(string identity, string? mutation)
{
    var separator = identity.LastIndexOf('@');
    return mutation == "ignore-incarnation" && separator >= 0 ? identity[..separator] : identity;
}

static int GetGeneration(string identity)
{
    var separator = identity.LastIndexOf('@');
    return separator < 0 || !int.TryParse(identity[(separator + 1)..], out var generation) ? -1 : generation;
}

static ProjectedMember Find(FreshProjection projection, string identity) =>
    projection.Members.Single(member => member.Identity == identity);

static async Task<string> ReadServerVersion(string connectionString)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("SHOW server_version", connection);
    return (string)(await command.ExecuteScalarAsync())!;
}

static async Task InitializeDatabase(string connectionString)
{
    var scripts = Path.Combine(AppContext.BaseDirectory, "scripts");
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    foreach (var (file, expectedHash) in new[] { ("PostgreSQL-Main.sql", mainScriptSha), ("PostgreSQL-Clustering.sql", clusteringScriptSha) })
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(scripts, file));
        Require(Convert.ToHexStringLower(SHA256.HashData(bytes)) == expectedHash,
            "schema-script-hash-mismatch", $"Bundled schema script {file} differs from its pinned source.");
        await using var command = new NpgsqlCommand(Encoding.UTF8.GetString(bytes), connection);
        await command.ExecuteNonQueryAsync();
    }
    await using var reports = new NpgsqlCommand("""
        CREATE TABLE probe_report (
            cluster_id text NOT NULL,
            silo_identity text NOT NULL,
            schema_version integer NOT NULL,
            revision bigint NOT NULL,
            payload jsonb NOT NULL,
            PRIMARY KEY (cluster_id, silo_identity)
        );
        """, connection);
    await reports.ExecuteNonQueryAsync();
}

static async Task<IHost> StartSilo(string connectionString, int siloPort, int gatewayPort, string siloName)
{
    var host = Host.CreateDefaultBuilder()
        .ConfigureLogging(logging => logging.ClearProviders())
        .UseOrleans(builder => builder
            .Configure<SiloOptions>(options => options.SiloName = siloName)
            .Configure<ClusterOptions>(options => { options.ClusterId = clusterId; options.ServiceId = clusterId; })
            .Configure<EndpointOptions>(options => { options.AdvertisedIPAddress = IPAddress.Loopback; options.SiloPort = siloPort; options.GatewayPort = gatewayPort; })
            .UseAdoNetClustering(options => { options.Invariant = "Npgsql"; options.ConnectionString = connectionString; }))
        .Build();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    try
    {
        await host.StartAsync(timeout.Token);
        return host;
    }
    catch
    {
        host.Dispose();
        throw;
    }
}

static async Task<MembershipEntry> WaitForActiveSilo(IMembershipTable membership, string name, int oldGeneration)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    while (true)
    {
        var snapshot = await membership.ReadAllAsync(timeout.Token);
        var rejoined = snapshot.Members.Select(row => row.Item1)
            .SingleOrDefault(entry => entry.SiloName == name && entry.Status == SiloStatus.Active);
        if (rejoined is not null && rejoined.SiloAddress.Generation != oldGeneration) return rejoined;
        await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
    }
}

static async Task<FreshProjection> ReadFreshProjection(IMembershipTable membership, string connectionString, string? mutation, FreshProjection? staleSnapshot = null)
{
    var snapshot = await membership.ReadAllAsync();
    return await ReadFreshProjectionFromSnapshot(snapshot.Members, connectionString, mutation, staleSnapshot);
}

static async Task<FreshProjection> ReadFreshProjectionFromSnapshot(IEnumerable<Tuple<MembershipEntry, string>> memberRows, string connectionString, string? mutation, FreshProjection? staleSnapshot = null)
{
    var rows = memberRows.ToArray();
    var identities = rows.Select(row => row.Item1.SiloAddress.ToParsableString()).ToArray();
    var reports = new Dictionary<string, ReportView>(StringComparer.Ordinal);
    if (mutation == "hide-report-read" && staleSnapshot is not null)
    {
        foreach (var member in staleSnapshot.Members)
            reports[StorageIdentity(member.Identity, mutation)] = new ReportView(member.ReportState, member.Revision, member.CapabilityCount);
    }
    else
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT silo_identity,schema_version,revision,payload::text FROM probe_report WHERE cluster_id=@cluster AND silo_identity=ANY(@identities)", connection);
        command.Parameters.AddWithValue("cluster", clusterId);
        command.Parameters.Add("identities", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = identities.Select(identity => StorageIdentity(identity, mutation)).Distinct(StringComparer.Ordinal).ToArray();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var identity = reader.GetString(0);
            var version = reader.GetInt32(1);
            var revision = reader.GetInt64(2);
            var payload = reader.GetString(3);
            var capabilityCount = version == 1 ? CountCapabilities(payload) : 0;
            reports[identity] = new ReportView(version == 1 ? "known" : "unknown", revision, capabilityCount);
        }
    }

    var members = rows.Select(row =>
    {
        var entry = row.Item1;
        var identity = entry.SiloAddress.ToParsableString();
        var key = StorageIdentity(identity, mutation);
        var report = reports.GetValueOrDefault(key, new ReportView("unknown", 0, 0));
        return new ProjectedMember(entry.SiloName, identity, entry.Status.ToString(), report.State, report.Revision, report.CapabilityCount);
    }).ToArray();
    return new FreshProjection(members);
}

static int CountCapabilities(string payload)
{
    using var document = JsonDocument.Parse(payload);
    return document.RootElement.TryGetProperty("capability", out _) ? 1 : 0;
}

static async Task<int> PublishReport(string connectionString, string cluster, string identity, long expectedRevision, string payload)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    if (expectedRevision == 0)
    {
        await using var insert = new NpgsqlCommand("INSERT INTO probe_report(cluster_id,silo_identity,schema_version,revision,payload) VALUES (@cluster,@identity,1,1,@payload::jsonb) ON CONFLICT DO NOTHING", connection, transaction);
        insert.Parameters.AddWithValue("cluster", cluster);
        insert.Parameters.AddWithValue("identity", identity);
        insert.Parameters.AddWithValue("payload", payload);
        if (await insert.ExecuteNonQueryAsync() != 1) throw new ProbeFailure("report-cas-rejected", "report compare-and-swap rejected the write");
        await transaction.CommitAsync();
        return 1;
    }
    await using var update = new NpgsqlCommand("UPDATE probe_report SET revision=revision+1,payload=@payload::jsonb WHERE cluster_id=@cluster AND silo_identity=@identity AND schema_version=1 AND revision=@expected RETURNING revision", connection, transaction);
    update.Parameters.AddWithValue("cluster", cluster);
    update.Parameters.AddWithValue("identity", identity);
    update.Parameters.AddWithValue("payload", payload);
    update.Parameters.AddWithValue("expected", expectedRevision);
    var value = await update.ExecuteScalarAsync();
    if (value is null) throw new ProbeFailure("report-cas-rejected", "report compare-and-swap rejected the write");
    await transaction.CommitAsync();
    return Convert.ToInt32(value);
}

static async Task SetReportSchemaVersion(string connectionString, string identity, int version)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("UPDATE probe_report SET schema_version=@version WHERE cluster_id=@cluster AND silo_identity=@identity", connection);
    command.Parameters.AddWithValue("version", version);
    command.Parameters.AddWithValue("cluster", clusterId);
    command.Parameters.AddWithValue("identity", identity);
    await command.ExecuteNonQueryAsync();
}

static async Task RenameReportTable(string connectionString, string from, string to)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand($"ALTER TABLE {from} RENAME TO {to}", connection);
    await command.ExecuteNonQueryAsync();
}

static void Require(bool condition, string code, string detail)
{
    if (!condition) throw new ProbeFailure(code, detail);
}

internal sealed record ReportView(string State, long Revision, int CapabilityCount);
internal sealed record ProjectedMember(string SiloName, string Identity, string Status, string ReportState, long Revision, int CapabilityCount);
internal sealed record FreshProjection(IReadOnlyList<ProjectedMember> Members);
internal sealed record SiloReceipt(string Name, string Address, int Generation);
internal sealed class ProbeFailure(string code, string message) : Exception(message) { public string Code { get; } = code; }
internal sealed class HeldReportRead : IAsyncDisposable
{
    private readonly NpgsqlConnection connection;
    private readonly NpgsqlTransaction transaction;
    private readonly string cluster;
    private readonly string identity;
    public Report? Report { get; private set; }

    private HeldReportRead(NpgsqlConnection connection, NpgsqlTransaction transaction, string cluster, string identity)
    {
        this.connection = connection;
        this.transaction = transaction;
        this.cluster = cluster;
        this.identity = identity;
    }

    public static async Task<HeldReportRead> BeginAsync(string connectionString, string cluster, string identity)
    {
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync();
            var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
            var held = new HeldReportRead(connection, transaction, cluster, identity);
            held.Report = await held.ReadAsync();
            return held;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<Report?> ReadAsync()
    {
        await using var command = new NpgsqlCommand(
            "SELECT schema_version,revision,payload::text FROM probe_report WHERE cluster_id=@cluster AND silo_identity=@identity", connection, transaction);
        command.Parameters.AddWithValue("cluster", cluster);
        command.Parameters.AddWithValue("identity", identity);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync() || reader.GetInt32(0) != 1) return null;
        return new Report(reader.GetInt64(1), reader.GetString(2));
    }

    public async ValueTask DisposeAsync()
    {
        try { await transaction.DisposeAsync(); }
        finally { await connection.DisposeAsync(); }
    }
}
internal sealed record Report(long Revision, string Payload);
internal sealed class SiloHandle(IHost host) : IAsyncDisposable
{
    public IHost Host { get; } = host;
    private bool stopped;

    public async Task StopAsync()
    {
        if (stopped) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Host.StopAsync(timeout.Token);
        stopped = true;
    }

    public async ValueTask DisposeAsync() { try { await StopAsync(); } finally { Host.Dispose(); } }
}
internal sealed class ProbeResult
{
    public string Candidate { get; set; } = "";
    public string? Provider { get; set; }
    public string OrleansVersion { get; set; } = "";
    public string? PostgreSqlVersion { get; set; }
    public string MainScriptSha256 { get; set; } = "";
    public string ClusteringScriptSha256 { get; set; } = "";
    public string Mutation { get; set; } = "";
    public int? ActiveMembershipEntries { get; set; }
    public int? AllMemberProjectionCount { get; set; }
    public SiloReceipt[] InitialSilos { get; set; } = [];
    public SiloReceipt? RejoinedSilo { get; set; }
    public long? FreshReadSawRevision { get; set; }
    public long? HeldReadSawRevision { get; set; }
    public int? FreshReadCapabilityCount { get; set; }
    public int? HeldReadCapabilityCount { get; set; }
    public bool? IncarnationIsolation { get; set; }
    public bool ContainerCleanupCompleted { get; set; }
    public string[] Assertions { get; set; } = [];
    public string Outcome { get; set; } = "failed";
    public string? FailureReason { get; set; }
    public string? Detail { get; set; }
}
