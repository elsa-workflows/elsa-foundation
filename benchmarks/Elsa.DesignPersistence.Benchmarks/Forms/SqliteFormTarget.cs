using System.Text;
using System.Text.Json;
using Elsa.DesignPersistence.Benchmarks.Harness;
using Elsa.DesignPersistence.Benchmarks.Workload;
using Microsoft.Data.Sqlite;

namespace Elsa.DesignPersistence.Benchmarks.Forms;

/// <summary>
/// A modeled Groundwork physical form over raw SQLite. Canonical JSON is authoritative in all three
/// forms; the physical-entity form additionally materializes query fields as native indexed columns,
/// the dedicated-document form keeps one table per kind with query fields inside JSON, and the
/// shared/linked form keeps every kind in one shared table with query fields inside JSON. The three
/// share identical seeds, payloads, predicates, and canonical result DTOs, so the only measured
/// difference is the physical form. Scale-bearing read routes only (the form-selection gate compares
/// reads and repeats them at 1M).
/// </summary>
public sealed class SqliteFormTarget : IBenchmarkTarget
{
    private readonly PhysicalForm _form;
    private readonly DesignWorkload _workload;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"elsa-form-{Guid.NewGuid():N}");
    private SqliteConnection _connection = null!;

    public SqliteFormTarget(PhysicalForm form, DesignWorkload workload)
    {
        _form = form;
        _workload = workload;
    }

    public string Key => $"groundwork.{_form.Key()}";

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);
        _connection = new SqliteConnection($"Data Source={Path.Combine(_directory, "form.db")}");
        await _connection.OpenAsync(cancellationToken);
        await ExecAsync("PRAGMA journal_mode=WAL;", cancellationToken);

        foreach (var ddl in CreateDdl())
            await ExecAsync(ddl, cancellationToken);

        await SeedRowsAsync(cancellationToken);
        await ExecAsync("ANALYZE;", cancellationToken);
    }

    public IReadOnlyList<BenchmarkOperation> Operations => BuildOperations();

    public async Task<IReadOnlyList<QueryPlanEvidence>> CaptureQueryPlansAsync(CancellationToken cancellationToken = default)
    {
        var evidence = new List<QueryPlanEvidence>();
        foreach (var (route, sql, bind) in PlanRoutes())
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "EXPLAIN QUERY PLAN " + sql;
            bind(command);
            var lines = new List<string>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                lines.Add(reader.GetString(reader.GetOrdinal("detail")));
            var detail = string.Join(" / ", lines);
            var usesIndex = lines.Count > 0 && lines.All(l =>
                !l.StartsWith("SCAN", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("USING", StringComparison.OrdinalIgnoreCase));
            evidence.Add(new QueryPlanEvidence(route, detail, usesIndex));
        }

        return evidence;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A uniquely named temp directory is harmless if SQLite releases a sidecar late.
        }
    }

    // --- Schema -----------------------------------------------------------------------------

    private IReadOnlyList<string> CreateDdl() => _form switch
    {
        PhysicalForm.PhysicalEntity =>
        [
            "CREATE TABLE wf_definition(tenant TEXT NOT NULL, id TEXT NOT NULL, name TEXT, description TEXT, json TEXT NOT NULL, PRIMARY KEY(tenant,id));",
            "CREATE INDEX ix_wf_def_name ON wf_definition(tenant,name);",
            "CREATE TABLE wf_version(tenant TEXT NOT NULL, id TEXT NOT NULL, definition_id TEXT, version TEXT, sort_key TEXT, json TEXT NOT NULL, PRIMARY KEY(tenant,id));",
            "CREATE INDEX ix_wf_ver_def_sort ON wf_version(tenant,definition_id,sort_key);",
            "CREATE TABLE wf_draft(tenant TEXT NOT NULL, id TEXT NOT NULL, definition_id TEXT, json TEXT NOT NULL, PRIMARY KEY(tenant,id));",
            "CREATE INDEX ix_wf_draft_def ON wf_draft(tenant,definition_id);",
            "CREATE TABLE act_definition(tenant TEXT NOT NULL, id TEXT NOT NULL, type_key TEXT, category TEXT, display_name TEXT, description TEXT, json TEXT NOT NULL, PRIMARY KEY(tenant,id));",
            "CREATE INDEX ix_act_def_display ON act_definition(tenant,display_name);",
            "CREATE INDEX ix_act_def_type ON act_definition(tenant,type_key);",
            "CREATE TABLE act_version(tenant TEXT NOT NULL, id TEXT NOT NULL, definition_id TEXT, version TEXT, sort_key TEXT, json TEXT NOT NULL, PRIMARY KEY(tenant,id));",
            "CREATE INDEX ix_act_ver_def_sort ON act_version(tenant,definition_id,sort_key);"
        ],
        PhysicalForm.DedicatedDocument =>
        [
            "CREATE TABLE doc_wf_definition(tenant TEXT NOT NULL, id TEXT NOT NULL, json TEXT NOT NULL, PRIMARY KEY(tenant,id));",
            "CREATE TABLE doc_wf_version(tenant TEXT NOT NULL, id TEXT NOT NULL, json TEXT NOT NULL, PRIMARY KEY(tenant,id));",
            "CREATE TABLE doc_wf_draft(tenant TEXT NOT NULL, id TEXT NOT NULL, json TEXT NOT NULL, PRIMARY KEY(tenant,id));",
            "CREATE TABLE doc_act_definition(tenant TEXT NOT NULL, id TEXT NOT NULL, json TEXT NOT NULL, PRIMARY KEY(tenant,id));",
            "CREATE TABLE doc_act_version(tenant TEXT NOT NULL, id TEXT NOT NULL, json TEXT NOT NULL, PRIMARY KEY(tenant,id));"
        ],
        PhysicalForm.SharedLinked =>
        [
            "CREATE TABLE documents(kind TEXT NOT NULL, tenant TEXT NOT NULL, id TEXT NOT NULL, json TEXT NOT NULL, PRIMARY KEY(kind,tenant,id));"
        ],
        _ => throw new ArgumentOutOfRangeException()
    };

    // --- Seeding ----------------------------------------------------------------------------

    private async Task SeedRowsAsync(CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);

        // Workflow definitions + drafts.
        foreach (var d in _workload.WorkflowDefinitions())
        {
            var defJson = JsonSerializer.Serialize(new { id = d.Id, tenantId = DesignWorkload.Scope, name = d.Name, description = d.Description });
            await InsertAsync("workflowDefinition", "wf_definition", "doc_wf_definition", d.Id, defJson, transaction,
                entity: c => { Set(c, "@name", d.Name); Set(c, "@description", d.Description); },
                entityColumns: "name,description", entityValues: "@name,@description");

            var draftJson = JsonSerializer.Serialize(new { id = d.DraftId, tenantId = DesignWorkload.Scope, workflowDefinitionId = d.Id, payload = d.Payload });
            await InsertAsync("workflowDefinitionDraft", "wf_draft", "doc_wf_draft", d.DraftId, draftJson, transaction,
                entity: c => Set(c, "@definition_id", d.Id),
                entityColumns: "definition_id", entityValues: "@definition_id");
        }

        foreach (var v in _workload.WorkflowVersions())
        {
            var json = JsonSerializer.Serialize(new { id = v.Id, tenantId = DesignWorkload.Scope, definitionId = v.DefinitionId, version = v.Version, semVerSortKey = v.SemVerSortKey });
            await InsertAsync("workflowDefinitionVersion", "wf_version", "doc_wf_version", v.Id, json, transaction,
                entity: c => { Set(c, "@definition_id", v.DefinitionId); Set(c, "@version", v.Version); Set(c, "@sort_key", v.SemVerSortKey); },
                entityColumns: "definition_id,version,sort_key", entityValues: "@definition_id,@version,@sort_key");
        }

        foreach (var a in _workload.ActivityDefinitions())
        {
            var json = JsonSerializer.Serialize(new { id = a.Id, tenantId = DesignWorkload.Scope, activityTypeKey = a.ActivityTypeKey, category = a.Category, displayName = a.DisplayName, description = a.Description });
            await InsertAsync("activityDefinition", "act_definition", "doc_act_definition", a.Id, json, transaction,
                entity: c => { Set(c, "@type_key", a.ActivityTypeKey); Set(c, "@category", a.Category); Set(c, "@display_name", a.DisplayName); Set(c, "@description", a.Description); },
                entityColumns: "type_key,category,display_name,description", entityValues: "@type_key,@category,@display_name,@description");
        }

        foreach (var v in _workload.ActivityVersions())
        {
            var json = JsonSerializer.Serialize(new { id = v.Id, tenantId = DesignWorkload.Scope, definitionId = v.DefinitionId, version = v.Version, semVerSortKey = v.SemVerSortKey });
            await InsertAsync("activityDefinitionVersion", "act_version", "doc_act_version", v.Id, json, transaction,
                entity: c => { Set(c, "@definition_id", v.DefinitionId); Set(c, "@version", v.Version); Set(c, "@sort_key", v.SemVerSortKey); },
                entityColumns: "definition_id,version,sort_key", entityValues: "@definition_id,@version,@sort_key");
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task InsertAsync(
        string documentKind,
        string entityTable,
        string dedicatedTable,
        string id,
        string json,
        SqliteTransaction transaction,
        Action<SqliteCommand> entity,
        string entityColumns,
        string entityValues)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        switch (_form)
        {
            case PhysicalForm.PhysicalEntity:
                command.CommandText = $"INSERT INTO {entityTable}(tenant,id,{entityColumns},json) VALUES(@tenant,@id,{entityValues},@json);";
                Set(command, "@tenant", DesignWorkload.Scope);
                Set(command, "@id", id);
                Set(command, "@json", json);
                entity(command);
                break;
            case PhysicalForm.DedicatedDocument:
                command.CommandText = $"INSERT INTO {dedicatedTable}(tenant,id,json) VALUES(@tenant,@id,@json);";
                Set(command, "@tenant", DesignWorkload.Scope);
                Set(command, "@id", id);
                Set(command, "@json", json);
                break;
            case PhysicalForm.SharedLinked:
                command.CommandText = "INSERT INTO documents(kind,tenant,id,json) VALUES(@kind,@tenant,@id,@json);";
                Set(command, "@kind", documentKind);
                Set(command, "@tenant", DesignWorkload.Scope);
                Set(command, "@id", id);
                Set(command, "@json", json);
                break;
        }

        await command.ExecuteNonQueryAsync();
    }

    // --- Physical mapping -------------------------------------------------------------------

    private (string From, string KindPredicate) Table(string entityTable, string dedicatedTable, string documentKind) => _form switch
    {
        PhysicalForm.PhysicalEntity => ($"{entityTable} x", "1=1"),
        PhysicalForm.DedicatedDocument => ($"{dedicatedTable} x", "1=1"),
        PhysicalForm.SharedLinked => ("documents x", $"x.kind = '{documentKind}'"),
        _ => throw new ArgumentOutOfRangeException()
    };

    private string Col(string entityColumn, string jsonPath) => _form == PhysicalForm.PhysicalEntity
        ? $"x.{entityColumn}"
        : $"json_extract(x.json,'$.{jsonPath}')";

    // id and tenant are real, indexed columns in every form.
    private const string IdCol = "x.id";
    private const string TenantCol = "x.tenant";

    // --- Operations -------------------------------------------------------------------------

    private IReadOnlyList<BenchmarkOperation> BuildOperations()
    {
        var wfIdentity = _workload.WorkflowIdentityRequests();
        var wfVersioned = _workload.VersionedWorkflowDefinitionIds();
        var actIdentity = _workload.ActivityIdentityRequests();
        var actVersioned = _workload.VersionedActivityDefinitionIds();
        var projectionIds = _workload.ProjectionDefinitionIds();

        return
        [
            Read("wf.identity.get", i => GetWorkflowDefinitionAsync(wfIdentity[Idx(i, wfIdentity.Count)].Id)),
            Read("wf.identity.version-exact", i => GetWorkflowVersionExactAsync(wfVersioned[Idx(i, wfVersioned.Count)])),
            Read("wf.identity.version-latest", i => GetWorkflowLatestVersionAsync(wfVersioned[Idx(i, wfVersioned.Count)])),
            Read("wf.catalog.filter-page", _ => WorkflowFilterPageAsync()),
            Read("wf.catalog.count", _ => WorkflowFilterCountAsync()),
            Read("wf.catalog.projection", _ => WorkflowProjectionAsync(projectionIds)),
            Read("wf.version.exists", i => WorkflowVersionExistsAsync(wfVersioned[Idx(i, wfVersioned.Count)])),
            Read("act.identity.get", i => GetActivityDefinitionAsync(actIdentity[Idx(i, actIdentity.Count)].Id)),
            Read("act.identity.version-exact", i => GetActivityVersionExactAsync(actVersioned[Idx(i, actVersioned.Count)])),
            Read("act.catalog.filter-page", _ => ActivityFilterPageAsync()),
            Read("act.catalog.versions-batch", _ => ActivityVersionsBatchAsync(actVersioned))
        ];
    }

    private BenchmarkOperation Read(string name, Func<long, Task<string>> invoke) =>
        new(name, OperationKind.Read, concurrency: 1, scaleBearing: true, (i, _) => invoke(i));

    private static int Idx(long i, int count) => (int)(((i % count) + count) % count);

    private async Task<string> GetWorkflowDefinitionAsync(string id)
    {
        var (from, kind) = Table("wf_definition", "doc_wf_definition", "workflowDefinition");
        var sql = $"SELECT {IdCol} AS id, {Col("name", "name")} AS name, {Col("description", "description")} AS description FROM {from} WHERE {kind} AND {TenantCol}=@t AND {IdCol}=@id;";
        return await QuerySingleAsync(sql, c => { Set(c, "@t", DesignWorkload.Scope); Set(c, "@id", id); },
            r => new { Id = r.GetString(0), Name = r.IsDBNull(1) ? null : r.GetString(1), Description = r.IsDBNull(2) ? null : r.GetString(2) });
    }

    private async Task<string> GetWorkflowVersionExactAsync(string definitionId)
    {
        var (from, kind) = Table("wf_version", "doc_wf_version", "workflowDefinitionVersion");
        var versionId = $"{definitionId}-v1.0.0";
        var sql = $"SELECT {IdCol} AS id, {Col("definition_id", "definitionId")} AS d, {Col("version", "version")} AS v FROM {from} WHERE {kind} AND {TenantCol}=@t AND {IdCol}=@id;";
        return await QuerySingleAsync(sql, c => { Set(c, "@t", DesignWorkload.Scope); Set(c, "@id", versionId); },
            r => new { Id = r.GetString(0), DefinitionId = r.GetString(1), Version = r.GetString(2) });
    }

    private async Task<string> GetWorkflowLatestVersionAsync(string definitionId)
    {
        var (from, kind) = Table("wf_version", "doc_wf_version", "workflowDefinitionVersion");
        var sql = $"SELECT {IdCol} AS id, {Col("definition_id", "definitionId")} AS d, {Col("version", "version")} AS v FROM {from} WHERE {kind} AND {TenantCol}=@t AND {Col("definition_id", "definitionId")}=@d ORDER BY {Col("sort_key", "semVerSortKey")} DESC LIMIT 1;";
        return await QuerySingleAsync(sql, c => { Set(c, "@t", DesignWorkload.Scope); Set(c, "@d", definitionId); },
            r => new { Id = r.GetString(0), DefinitionId = r.GetString(1), Version = r.GetString(2) });
    }

    private async Task<string> WorkflowVersionExistsAsync(string definitionId)
    {
        var (from, kind) = Table("wf_version", "doc_wf_version", "workflowDefinitionVersion");
        var sortKey = Elsa.Primitives.Versioning.SemVer.ToSortKey("1.0.0");
        var sql = $"SELECT 1 FROM {from} WHERE {kind} AND {TenantCol}=@t AND {Col("definition_id", "definitionId")}=@d AND {Col("sort_key", "semVerSortKey")}=@k LIMIT 1;";
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        Set(command, "@t", DesignWorkload.Scope);
        Set(command, "@d", definitionId);
        Set(command, "@k", sortKey);
        var exists = await command.ExecuteScalarAsync() is not null;
        return CanonicalHash.Of(new { Exists = exists });
    }

    private async Task<string> WorkflowFilterPageAsync()
    {
        var (from, kind) = Table("wf_definition", "doc_wf_definition", "workflowDefinition");
        var sql = $"SELECT {IdCol} AS id FROM {from} WHERE {kind} AND {TenantCol}=@t AND {Col("name", "name")} LIKE 'Alpha%' ORDER BY {IdCol} LIMIT 50 OFFSET 450;";
        return await QueryListAsync(sql, c => Set(c, "@t", DesignWorkload.Scope), r => r.GetString(0));
    }

    private async Task<string> WorkflowFilterCountAsync()
    {
        var (from, kind) = Table("wf_definition", "doc_wf_definition", "workflowDefinition");
        var sql = $"SELECT COUNT(*) FROM {from} WHERE {kind} AND {TenantCol}=@t AND {Col("name", "name")} LIKE 'Alpha%';";
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        Set(command, "@t", DesignWorkload.Scope);
        var count = Convert.ToInt64(await command.ExecuteScalarAsync());
        return CanonicalHash.Of(new { Count = count });
    }

    private async Task<string> ActivityFilterPageAsync()
    {
        var (from, kind) = Table("act_definition", "doc_act_definition", "activityDefinition");
        var sql = $"SELECT {IdCol} AS id FROM {from} WHERE {kind} AND {TenantCol}=@t AND {Col("display_name", "displayName")} LIKE 'Alpha%' ORDER BY {IdCol} LIMIT 50 OFFSET 450;";
        return await QueryListAsync(sql, c => Set(c, "@t", DesignWorkload.Scope), r => r.GetString(0));
    }

    private async Task<string> GetActivityDefinitionAsync(string id)
    {
        var (from, kind) = Table("act_definition", "doc_act_definition", "activityDefinition");
        var sql = $"SELECT {IdCol} AS id, {Col("type_key", "activityTypeKey")} AS k, {Col("category", "category")} AS c, {Col("display_name", "displayName")} AS dn FROM {from} WHERE {kind} AND {TenantCol}=@t AND ({IdCol}=@id OR {Col("type_key", "activityTypeKey")}=@id);";
        return await QuerySingleAsync(sql, c => { Set(c, "@t", DesignWorkload.Scope); Set(c, "@id", id); },
            r => new { Id = r.GetString(0), ActivityTypeKey = r.GetString(1), Category = r.GetString(2), DisplayName = r.IsDBNull(3) ? null : r.GetString(3) });
    }

    private async Task<string> GetActivityVersionExactAsync(string definitionId)
    {
        var (from, kind) = Table("act_version", "doc_act_version", "activityDefinitionVersion");
        var sortKey = Elsa.Primitives.Versioning.SemVer.ToSortKey("1.0.0");
        var sql = $"SELECT {IdCol} AS id, {Col("definition_id", "definitionId")} AS d, {Col("version", "version")} AS v FROM {from} WHERE {kind} AND {TenantCol}=@t AND {Col("definition_id", "definitionId")}=@d AND {Col("sort_key", "semVerSortKey")}=@k LIMIT 1;";
        return await QuerySingleAsync(sql, c => { Set(c, "@t", DesignWorkload.Scope); Set(c, "@d", definitionId); Set(c, "@k", sortKey); },
            r => new { Id = r.GetString(0), DefinitionId = r.GetString(1), Version = r.GetString(2) });
    }

    private async Task<string> WorkflowProjectionAsync(IReadOnlyList<string> definitionIds)
    {
        var (verFrom, verKind) = Table("wf_version", "doc_wf_version", "workflowDefinitionVersion");
        var (draftFrom, draftKind) = Table("wf_draft", "doc_wf_draft", "workflowDefinitionDraft");
        var inClause = BuildIn(definitionIds, out var parameters);

        var versions = new Dictionary<string, (string LatestId, string LatestVersion, string LatestSort, int Count)>(StringComparer.Ordinal);
        var versionSql = $"SELECT {Col("definition_id", "definitionId")} AS d, {IdCol} AS id, {Col("version", "version")} AS v, {Col("sort_key", "semVerSortKey")} AS s FROM {verFrom} WHERE {verKind} AND {TenantCol}=@t AND {Col("definition_id", "definitionId")} IN ({inClause});";
        await using (var command = _connection.CreateCommand())
        {
            command.CommandText = versionSql;
            Set(command, "@t", DesignWorkload.Scope);
            BindIn(command, parameters, definitionIds);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var d = reader.GetString(0);
                var id = reader.GetString(1);
                var v = reader.GetString(2);
                var s = reader.GetString(3);
                if (!versions.TryGetValue(d, out var current) || string.CompareOrdinal(s, current.LatestSort) > 0)
                    versions[d] = (id, v, s, (current.Count) + 1);
                else
                    versions[d] = (current.LatestId, current.LatestVersion, current.LatestSort, current.Count + 1);
            }
        }

        var drafts = new Dictionary<string, string>(StringComparer.Ordinal);
        var draftSql = $"SELECT {Col("definition_id", "workflowDefinitionId")} AS d, {IdCol} AS id FROM {draftFrom} WHERE {draftKind} AND {TenantCol}=@t AND {Col("definition_id", "workflowDefinitionId")} IN ({inClause});";
        await using (var command = _connection.CreateCommand())
        {
            command.CommandText = draftSql;
            Set(command, "@t", DesignWorkload.Scope);
            BindIn(command, parameters, definitionIds);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                drafts[reader.GetString(0)] = reader.GetString(1);
        }

        var rows = definitionIds
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .Select(id =>
            {
                versions.TryGetValue(id, out var ver);
                drafts.TryGetValue(id, out var draftId);
                return new
                {
                    WorkflowDefinitionId = id,
                    DraftId = draftId,
                    LatestVersionId = ver.Count > 0 ? ver.LatestId : null,
                    LatestVersion = ver.Count > 0 ? ver.LatestVersion : null,
                    VersionCount = ver.Count
                };
            })
            .ToList();
        return CanonicalHash.Of(rows);
    }

    private async Task<string> ActivityVersionsBatchAsync(IReadOnlyList<string> definitionIds)
    {
        var (from, kind) = Table("act_version", "doc_act_version", "activityDefinitionVersion");
        var inClause = BuildIn(definitionIds, out var parameters);
        var sql = $"SELECT {Col("definition_id", "definitionId")} AS d, {IdCol} AS id, {Col("version", "version")} AS v, {Col("sort_key", "semVerSortKey")} AS s FROM {from} WHERE {kind} AND {TenantCol}=@t AND {Col("definition_id", "definitionId")} IN ({inClause}) ORDER BY d, s;";
        var rows = new List<object>();
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        Set(command, "@t", DesignWorkload.Scope);
        BindIn(command, parameters, definitionIds);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new { DefinitionId = reader.GetString(0), VersionId = reader.GetString(1), Version = reader.GetString(2) });
        return CanonicalHash.Of(rows);
    }

    // --- Plan routes ------------------------------------------------------------------------

    private IEnumerable<(string Route, string Sql, Action<SqliteCommand> Bind)> PlanRoutes()
    {
        var wfDef = Table("wf_definition", "doc_wf_definition", "workflowDefinition");
        var wfVer = Table("wf_version", "doc_wf_version", "workflowDefinitionVersion");
        var actDef = Table("act_definition", "doc_act_definition", "activityDefinition");

        yield return ("wf.identity.get",
            $"SELECT {IdCol} FROM {wfDef.From} WHERE {wfDef.KindPredicate} AND {TenantCol}=@t AND {IdCol}=@id;",
            c => { Set(c, "@t", DesignWorkload.Scope); Set(c, "@id", DesignWorkload.WorkflowDefinitionId(0)); });
        yield return ("wf.catalog.filter-page",
            $"SELECT {IdCol} FROM {wfDef.From} WHERE {wfDef.KindPredicate} AND {TenantCol}=@t AND {Col("name", "name")} LIKE 'Alpha%' ORDER BY {IdCol} LIMIT 50 OFFSET 450;",
            c => Set(c, "@t", DesignWorkload.Scope));
        yield return ("wf.identity.version-latest",
            $"SELECT {IdCol} FROM {wfVer.From} WHERE {wfVer.KindPredicate} AND {TenantCol}=@t AND {Col("definition_id", "definitionId")}=@d ORDER BY {Col("sort_key", "semVerSortKey")} DESC LIMIT 1;",
            c => { Set(c, "@t", DesignWorkload.Scope); Set(c, "@d", DesignWorkload.WorkflowDefinitionId(0)); });
        yield return ("act.catalog.filter-page",
            $"SELECT {IdCol} FROM {actDef.From} WHERE {actDef.KindPredicate} AND {TenantCol}=@t AND {Col("display_name", "displayName")} LIKE 'Alpha%' ORDER BY {IdCol} LIMIT 50 OFFSET 450;",
            c => Set(c, "@t", DesignWorkload.Scope));
    }

    // --- SQLite helpers ---------------------------------------------------------------------

    private async Task<string> QuerySingleAsync<T>(string sql, Action<SqliteCommand> bind, Func<SqliteDataReader, T> project)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        bind(command);
        await using var reader = await command.ExecuteReaderAsync();
        object? row = await reader.ReadAsync() ? project(reader) : null;
        return CanonicalHash.Of(new { Found = row is not null, Row = row });
    }

    private async Task<string> QueryListAsync<T>(string sql, Action<SqliteCommand> bind, Func<SqliteDataReader, T> project)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        bind(command);
        var rows = new List<T>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(project(reader));
        return CanonicalHash.Of(rows);
    }

    private static string BuildIn(IReadOnlyList<string> ids, out string[] parameters)
    {
        var distinct = ids.Distinct(StringComparer.Ordinal).ToArray();
        parameters = distinct.Select((_, i) => $"@i{i}").ToArray();
        return string.Join(",", parameters);
    }

    private static void BindIn(SqliteCommand command, string[] parameters, IReadOnlyList<string> ids)
    {
        var distinct = ids.Distinct(StringComparer.Ordinal).ToArray();
        for (var i = 0; i < parameters.Length; i++)
            Set(command, parameters[i], distinct[i]);
    }

    private static void Set(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private async Task ExecAsync(string sql, CancellationToken cancellationToken)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
