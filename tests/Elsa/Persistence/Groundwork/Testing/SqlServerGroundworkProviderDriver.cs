using System.Data.SqlTypes;
using System.Xml.Linq;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.SqlServer;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Core.SchemaEvolution;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.SqlServer;
using Groundwork.SqlServer.Documents;
using Groundwork.SqlServer.PhysicalStorage;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using ElsaRuntimeSchemaAdmissionResult = Elsa.Persistence.Groundwork.Unified.Composition.GroundworkRuntimeSchemaAdmissionResult;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>Real SQL Server provider mechanics for the shared Groundwork conformance fixture.</summary>
public sealed class SqlServerGroundworkProviderDriver : GroundworkProviderDriver
{
    private const string ProviderKey = "sqlserver";
    private const string Image = "mcr.microsoft.com/mssql/server:2022-CU21-ubuntu-22.04";
    private const string ProtocolVersion = "1.0.0";
    private const int AdministrativeCommandTimeoutSeconds = 120;
    private const int NativeRouteDatasetCommandTimeoutSeconds = 300;
    private readonly MsSqlContainer _container = new MsSqlBuilder(Image).Build();
    private readonly string _databaseName = $"elsa_groundwork_driver_{Guid.NewGuid():N}";
    private readonly GroundworkProcessProbeRunner _processProbeRunner = new();
    private readonly GroundworkProcessLaunchDescriptor _processLaunchDescriptor;
    private string? _connectionString;
    private GroundworkPhysicalSchemaManifestSource? _physicalSource;

    public SqlServerGroundworkProviderDriver() =>
        _processLaunchDescriptor = _processProbeRunner.CreateLaunchDescriptor(ProtocolVersion);

    private static readonly string PackageVersion =
        GroundworkProviderDriverSupport.PackageVersion(typeof(SqlServerDocumentStoreFactory).Assembly);

    public override GroundworkProviderDescriptor Descriptor { get; } = new(
        "sqlserver",
        "groundwork-sqlserver",
        PackageVersion,
        new GroundworkProviderTopology(
            "sqlserver",
            "real-sqlserver-container",
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients |
            GroundworkTopologyCapabilities.MultiDocumentTransactions |
            GroundworkTopologyCapabilities.ExternalProcessRestart));

    public override GroundworkTopologyCapabilities RequiredTopology =>
        GroundworkTopologyCapabilities.PersistentStorage |
        GroundworkTopologyCapabilities.IndependentClients |
        GroundworkTopologyCapabilities.MultiDocumentTransactions |
        GroundworkTopologyCapabilities.ExternalProcessRestart;

    public override GroundworkCompositionFingerprint CompositionFingerprint { get; } =
        GroundworkCompositionFingerprint.Create("elsa-runtime-provider-fixture:v1");
    public override string? PhysicalTargetFingerprint => _physicalSource?.PhysicalTarget.Fingerprint;

    public override GroundworkProcessLaunchDescriptor ProcessLaunchDescriptor => _processLaunchDescriptor;

    public override string ProbeDocumentKind => ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind;

    protected override async ValueTask InitializeCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _container.StartAsync(cancellationToken);
            await CreateDatabaseAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"SQL Server provider startup failed ({exception.GetType().Name}); connection details were suppressed.");
        }
    }

    protected override async ValueTask ResetCoreAsync(CancellationToken cancellationToken)
    {
        await ResetDatabaseAsync(cancellationToken);
        _physicalSource = null;
    }

    protected override async ValueTask ResetPhysicalCoreAsync(
        IReadOnlyCollection<IGroundworkStorageManifestSource>? manifestSources,
        CancellationToken cancellationToken)
    {
        await ResetPhysicalStorageCoreAsync(cancellationToken);
        var source = await CreatePhysicalSchemaSourceAsync(manifestSources, cancellationToken);
        await ApplyPhysicalSchemaCoreAsync(source, cancellationToken);
    }

    protected override async ValueTask ResetPhysicalStorageCoreAsync(CancellationToken cancellationToken) =>
        await ResetDatabaseAsync(cancellationToken);

    protected override async ValueTask ApplyPhysicalSchemaCoreAsync(
        GroundworkPhysicalSchemaManifestSource source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var executor = new SqlServerPhysicalSchemaExecutor(RequiredConnectionString());
        var applied = await PhysicalSchemaApplication.ApplyAsync(
            source.PhysicalTarget,
            executor,
            cancellationToken: cancellationToken);
        EnsureSchemaApplied(applied);
        var admission = await source.InspectRuntimeAdmissionAsync(executor, cancellationToken: cancellationToken);
        if (!admission.IsReady)
            throw new InvalidOperationException("SQL Server physical provider driver did not admit its applied runtime target.");
        _physicalSource = source;
    }

    private async Task ResetDatabaseAsync(CancellationToken cancellationToken)
    {
        if (_connectionString is not null)
            ClearPool(_connectionString);
        SqlConnection.ClearAllPools();

        await using var connection = new SqlConnection(AdminConnectionString());
        await connection.OpenAsync(cancellationToken);
        var database = Quote(_databaseName);
        await ExecuteAdminNonQueryAsync(
            connection,
            $"""
             IF DB_ID(@databaseName) IS NOT NULL
             BEGIN
                 DECLARE @killSql nvarchar(max) = N'';
                 SELECT @killSql = @killSql + N'KILL ' + CONVERT(varchar(11), session_id) + N';'
                 FROM sys.dm_exec_sessions
                 WHERE database_id = DB_ID(@databaseName)
                   AND is_user_process = 1
                   AND session_id <> @@SPID;

                 IF LEN(@killSql) > 0
                     EXEC(@killSql);

                 ALTER DATABASE {database} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                 DROP DATABASE {database};
             END;
             """,
            cancellationToken: cancellationToken);
        await ExecuteAdminNonQueryAsync(
            connection,
            $"CREATE DATABASE {database};",
            cancellationToken);
        _connectionString = TargetConnectionString();
    }

    protected override ValueTask<GroundworkProviderClient> OpenClientCoreAsync(
        Guid clientId,
        CancellationToken cancellationToken) =>
        OpenClientCoreAsync(
            clientId,
            ElsaRuntimeStorageManifest.Create(),
            GroundworkTestAccess.DefaultScoped,
            cancellationToken);

    protected override async ValueTask<GroundworkProviderClient> OpenClientCoreAsync(
        Guid clientId,
        StorageManifest manifest,
        DocumentStoreAccess access,
        CancellationToken cancellationToken)
    {
        var connectionString = new SqlConnectionStringBuilder(RequiredConnectionString())
        {
            ApplicationName = $"elsa-groundwork-driver-{clientId:N}"
        }.ConnectionString;
        var store = await SqlServerDocumentStoreFactory.CreateAsync(
            connectionString,
            manifest,
            new ProviderIdentity("groundwork-sqlserver", PackageVersion),
            access,
            cancellationToken: cancellationToken);
        var services = new ServiceCollection()
            .AddSingleton(store)
            .AddSingleton<IDocumentStore>(store)
            .BuildServiceProvider();

        return new GroundworkProviderClient(
            clientId,
            services,
            store,
            async () =>
            {
                await services.DisposeAsync();
                ClearPool(connectionString);
            });
    }

    protected override ValueTask<GroundworkProviderClient> OpenPhysicalClientCoreAsync(
        Guid clientId,
        DocumentStoreAccess access,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = _physicalSource ?? throw new InvalidOperationException("The SQL Server physical target has not been applied.");
        var connectionString = new SqlConnectionStringBuilder(RequiredConnectionString())
        {
            ApplicationName = $"elsa-groundwork-physical-driver-{clientId:N}"
        }.ConnectionString;
        var manifest = source.CreateManifest();
        var store = new SqlServerPhysicalDocumentStore(
            connectionString,
            manifest,
            source.PhysicalTarget.Routes,
            access);
        var boundedStore = new GroundworkBoundedDocumentStoreRouter(
            source.PhysicalTarget.Routes.Select(route =>
                KeyValuePair.Create<string, IBoundedDocumentStore>(
                    route.StorageUnit.Value,
                    SqlServerPhysicalQueryRuntime.Create(
                        store,
                        manifest,
                        route,
                        source.PhysicalTarget.Provider))));
        var services = new ServiceCollection()
            .AddSingleton<IDocumentStore>(store)
            .AddSingleton(boundedStore)
            .AddSingleton<IBoundedDocumentStore>(boundedStore)
            .BuildServiceProvider();
        return ValueTask.FromResult(new GroundworkProviderClient(
            clientId,
            services,
            store,
            async () =>
            {
                await services.DisposeAsync();
                ClearPool(connectionString);
            },
            boundedStore));
    }

    private static ValueTask<GroundworkPhysicalSchemaManifestSource> CreatePhysicalSchemaSourceAsync(
        IReadOnlyCollection<IGroundworkStorageManifestSource>? manifestSources,
        CancellationToken cancellationToken)
    {
        var capabilityReport = SqlServerGroundworkCapabilities.Runtime();
        var topology = new GroundworkProviderTopologySnapshot(
            capabilityReport.Provider.Name,
            "sqlserver",
            new HashSet<string>(StringComparer.Ordinal)
            {
                RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity
            });
        return manifestSources is null
            ? GroundworkStoreInitialization.CreateRuntimePhysicalSchemaSourceAsync(
                capabilityReport,
                topology,
                SqlServerGroundworkCapabilities.PhysicalNames,
                cancellationToken: cancellationToken)
            : GroundworkStoreInitialization.CreatePhysicalSchemaSourceAsync(
                capabilityReport,
                topology,
                SqlServerGroundworkCapabilities.PhysicalNames,
                manifestSources,
                cancellationToken: cancellationToken);
    }

    protected override ValueTask<GroundworkProcessProbeResult> RunInNewProcessCoreAsync(
        GroundworkProcessProbeRequest request,
        CancellationToken cancellationToken) =>
        _processProbeRunner.RunAsync(
            ProcessLaunchDescriptor,
            Descriptor,
            ProbeDocumentKind,
            new GroundworkProcessProbeState(RequiredConnectionString()),
            request,
            cancellationToken: cancellationToken);

    protected override async ValueTask<GroundworkSanitizedEvidence> CaptureDiagnosticsCoreAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(RequiredConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion'));";
        var engineVersion = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken)) ?? "unknown";
        return GroundworkSanitizedEvidence.Create(
            "diagnostics",
            $"provider:sqlserver\ntopology:real-sqlserver-container\nengine-version:{engineVersion}\nstate:ready");
    }

    protected override async ValueTask<GroundworkNativePlanEvidence> CaptureNativePlanCoreAsync(
        GroundworkExecutionPath executionPath,
        string scenarioId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(RequiredConnectionString());
        await connection.OpenAsync(cancellationToken);
        await SetStatisticsXmlAsync(connection, enabled: true, cancellationToken);
        string showPlan;
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT TOP (1) version FROM groundwork_documents WHERE document_kind = @kind;";
            command.Parameters.AddWithValue("@kind", ProbeDocumentKind);
            var plans = new List<string>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            do
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
                    {
                        var value = reader.GetValue(ordinal);
                        if (value is SqlXml sqlXml && !sqlXml.IsNull)
                            plans.Add(sqlXml.Value);
                        else
                        {
                            var text = Convert.ToString(value) ?? string.Empty;
                            if (reader.GetName(ordinal).Contains("XML Showplan", StringComparison.OrdinalIgnoreCase) ||
                                text.Contains("<ShowPlanXML", StringComparison.Ordinal))
                                plans.Add(text);
                        }
                    }
                }
            } while (await reader.NextResultAsync(cancellationToken));
            showPlan = plans.SingleOrDefault(plan => !string.IsNullOrWhiteSpace(plan))
                ?? throw new InvalidOperationException("SQL Server returned no SHOWPLAN XML evidence.");
        }
        finally
        {
            await SetStatisticsXmlAsync(connection, enabled: false, CancellationToken.None);
        }

        var document = XDocument.Parse(showPlan, LoadOptions.None);
        var operators = document.Descendants()
            .Where(element => element.Name.LocalName == "RelOp")
            .Select(element => (string?)element.Attribute("PhysicalOp"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var indexes = document.Descendants()
            .Where(element => element.Name.LocalName == "Object")
            .Select(element => ((string?)element.Attribute("Index"))?.Trim('[', ']'))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (operators.Length == 0)
            throw new InvalidOperationException("SQL Server SHOWPLAN XML contained no physical operators.");

        var evidence = GroundworkSanitizedEvidence.Create(
            "native-plan",
            $"evidence-class:substrate-only-plan-smoke\nadmitted-route-proof:false\nprovider:sqlserver\nformat:showplan-xml\noperators:{string.Join(',', operators)}\nindexes:{string.Join(',', indexes)}\nbound:top-1");
        return GroundworkNativePlanEvidence.Create(executionPath, scenarioId, evidence);
    }

    protected override async ValueTask PrepareNativeRoutePlanDatasetCoreAsync(
        IReadOnlyCollection<GroundworkNativeRoutePlanRequest> requests,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(RequiredConnectionString());
        await connection.OpenAsync(cancellationToken);
        foreach (var group in requests.GroupBy(request => (request.PhysicalName, request.DocumentKind)))
            await EnsureNativeRouteDatasetAsync(connection, group.ToArray(), cancellationToken);
    }

    protected override async ValueTask<GroundworkNativeRoutePlanResult> CaptureNativeRoutePlanCoreAsync(
        GroundworkNativeRoutePlanRequest request,
        PhysicalDocumentQueryExplanation explanation,
        int materializedCandidateCount,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(RequiredConnectionString());
        await connection.OpenAsync(cancellationToken);
        var expectedIndex = await ResolveNativeRouteIndexAsync(connection, request, cancellationToken);
        var lookupIndexes = await ListNativeRouteIndexesAsync(
            connection,
            request.PhysicalName,
            cancellationToken);
        var commands = explanation.Commands.Select((command, ordinal) =>
        {
            if (!string.Equals(command.NativePlanFormat, "sqlserver-statistics-xml", StringComparison.Ordinal))
                throw new InvalidOperationException($"SQL Server route '{request.QueryIdentity}' returned an unexpected native-plan format.");
            var document = XDocument.Parse(command.NativePlan, LoadOptions.None);
            var operators = ExtractShowPlanOperators(document);
            var indexes = ExtractShowPlanIndexes(document);
            if (!indexes.Intersect(lookupIndexes, StringComparer.Ordinal).Any() ||
                !operators.Contains("Index Seek", StringComparer.Ordinal) ||
                operators.Any(value => value.Contains("Scan", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"SQL Server route '{request.QueryIdentity}' command '{command.Identity}' was not a scan-free seek through '{expectedIndex}'.");
            }
            return GroundworkNativeRouteCommandEvidence.Create(
                ordinal,
                command,
                "index-seek",
                indexes);
        }).ToArray();
        var selectedIndex = GroundworkNativeRoutePlanResult.SelectCompiledIndex(
            request,
            lookupIndexes,
            commands);
        var cardinality = await CountPhysicalRowsAsync(connection, request.PhysicalName, cancellationToken);
        return GroundworkNativeRoutePlanResult.Create(
            request,
            ProviderKey,
            cardinality,
            "index-seek",
            selectedIndex,
            request.Limit,
            materializedCandidateCount,
            commands);
    }

    protected override async ValueTask<GroundworkPhysicalSchemaManifestSource> PrepareSchemaParityCoreAsync(
        IReadOnlyCollection<IGroundworkStorageManifestSource> manifestSources,
        CancellationToken cancellationToken)
    {
        await ResetDatabaseAsync(cancellationToken);
        _physicalSource = null;
        return await CreatePhysicalSchemaSourceAsync(manifestSources, cancellationToken);
    }

    protected override async ValueTask<GroundworkProviderSchemaStateSnapshot> CaptureSchemaStateCoreAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(RequiredConnectionString());
        await connection.OpenAsync(cancellationToken);
        var catalog = new List<string>();
        var tables = new List<string>();
        await using (var tableCommand = connection.CreateCommand())
        {
            tableCommand.CommandText = """
                SELECT s.name, t.name
                FROM sys.tables t
                INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
                WHERE t.is_ms_shipped = 0
                ORDER BY s.name, t.name;
                """;
            await using var reader = await tableCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var schema = reader.GetString(0);
                var table = reader.GetString(1);
                catalog.Add($"table\u001f{schema}\u001f{table}");
                if (string.Equals(schema, "dbo", StringComparison.Ordinal))
                    tables.Add(table);
            }
        }
        await using (var catalogCommand = connection.CreateCommand())
        {
            catalogCommand.CommandText = """
                SELECT s.name, t.name, c.column_id, c.name, ty.name, c.max_length,
                       c.precision, c.scale, c.is_nullable, c.is_computed,
                       COALESCE(i.name, ''), COALESCE(i.is_unique, 0),
                       COALESCE(ic.key_ordinal, 0), COALESCE(ic.is_included_column, 0)
                FROM sys.tables t
                INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
                INNER JOIN sys.columns c ON c.object_id = t.object_id
                INNER JOIN sys.types ty ON ty.user_type_id = c.user_type_id
                LEFT JOIN sys.index_columns ic
                  ON ic.object_id = c.object_id AND ic.column_id = c.column_id
                LEFT JOIN sys.indexes i
                  ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                WHERE t.is_ms_shipped = 0
                ORDER BY s.name, t.name, c.column_id, i.name, ic.key_ordinal;
                """;
            await using var reader = await catalogCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var values = Enumerable.Range(0, reader.FieldCount)
                    .Select(ordinal => Convert.ToString(reader.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture));
                catalog.Add($"column-index\u001f{string.Join('\u001f', values)}");
            }
        }
        return await GroundworkRelationalSchemaStateReader.CaptureAsync(
            ProviderKey,
            connection,
            catalog,
            tables,
            Quote,
            cancellationToken);
    }

    protected override async ValueTask<ElsaRuntimeSchemaAdmissionResult> InspectSchemaParityAdmissionCoreAsync(
        GroundworkPhysicalSchemaManifestSource source,
        CancellationToken cancellationToken) =>
        await source.InspectRuntimeAdmissionAsync(
            new SqlServerPhysicalSchemaExecutor(RequiredConnectionString()),
            cancellationToken: cancellationToken);

    protected override SchemaToolConnection SchemaToolConnectionCore() =>
        new(RequiredConnectionString());

    public async ValueTask<GroundworkSanitizedEvidence> CaptureIdentityCompoundKeyBudgetAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(RequiredConnectionString());
        await connection.OpenAsync(cancellationToken);
        var expectedIndex = await ResolveIdentityUserNormalizedNameIndexAsync(connection, cancellationToken);
        await RefreshIdentityUsersStatisticsAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                c.name,
                c.max_length
            FROM sys.indexes i
            INNER JOIN sys.index_columns ic
                ON ic.object_id = i.object_id
               AND ic.index_id = i.index_id
            INNER JOIN sys.columns c
                ON c.object_id = ic.object_id
               AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(N'dbo.identity_users')
              AND i.name = @index
              AND ic.is_included_column = 0
            ORDER BY ic.key_ordinal;
            """;
        command.Parameters.AddWithValue("@index", expectedIndex);
        var columns = new List<string>();
        var totalBytes = 0;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var name = reader.GetString(0);
                var maxLength = reader.GetInt16(1);
                columns.Add($"{name}:{maxLength}");
                totalBytes += maxLength;
            }
        }

        if (columns.Count == 0)
            throw new InvalidOperationException("SQL Server Identity compound-key budget evidence found no key columns.");

        return GroundworkSanitizedEvidence.Create(
            "compound-key-budget",
            "provider=sqlserver\n" +
            "table=identity_users\n" +
            $"index={expectedIndex}\n" +
            $"key-columns={string.Join(',', columns)}\n" +
            $"declared-key-bytes={totalBytes}");
    }

    /// <summary>
    /// Opens a new ADO connection against the driver's current database, for callers that need to read the
    /// physical <c>groundwork_documents</c> table directly rather than through the <see cref="IDocumentStore"/>
    /// abstraction (for example, a read-model data source that runs its own hand-written SQL).
    /// </summary>
    public SqlConnection CreateRawConnection() => new(RequiredConnectionString());

    protected override async ValueTask DisposeCoreAsync()
    {
        if (_connectionString is not null)
            ClearPool(_connectionString);
        _connectionString = null;
        await _container.DisposeAsync();
    }

    private async Task CreateDatabaseAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(AdminConnectionString());
        await connection.OpenAsync(cancellationToken);
        await ExecuteAdminNonQueryAsync(
            connection,
            $"CREATE DATABASE {Quote(_databaseName)};",
            cancellationToken);
        _connectionString = TargetConnectionString();
    }

    private async Task ExecuteAdminNonQueryAsync(
        SqlConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = AdministrativeCommandTimeoutSeconds;
        command.CommandText = commandText;
        command.Parameters.AddWithValue("@databaseName", _databaseName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private string AdminConnectionString() => new SqlConnectionStringBuilder(_container.GetConnectionString())
    {
        InitialCatalog = "master",
        Pooling = false
    }.ConnectionString;

    private string TargetConnectionString() => new SqlConnectionStringBuilder(_container.GetConnectionString())
    {
        InitialCatalog = _databaseName
    }.ConnectionString;

    private string RequiredConnectionString() => _connectionString ??
        throw new InvalidOperationException("The SQL Server provider target has not been initialized.");

    private static Task SetStatisticsXmlAsync(
        SqlConnection connection,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"SET STATISTICS XML {(enabled ? "ON" : "OFF")};";
        return ExecuteAndDisposeAsync(command, cancellationToken);
    }

    private static async Task ExecuteAndDisposeAsync(SqlCommand command, CancellationToken cancellationToken)
    {
        await using (command)
            await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void ClearPool(string connectionString)
    {
        using var connection = new SqlConnection(connectionString);
        SqlConnection.ClearPool(connection);
    }

    private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";

    private static async Task<string> ResolveIdentityUserNormalizedNameIndexAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1) i.name
            FROM sys.indexes i
            WHERE i.object_id = OBJECT_ID(N'dbo.identity_users')
              AND i.name LIKE N'%identity%user%normalized%name%'
            ORDER BY i.name;
            """;
        var value = await command.ExecuteScalarAsync(cancellationToken) as string;
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException("SQL Server Identity normalized-user-name physical index was not found.")
            : value;
    }

    private static async Task EnsureNativeRouteDatasetAsync(
        SqlConnection connection,
        IReadOnlyCollection<GroundworkNativeRoutePlanRequest> requests,
        CancellationToken cancellationToken)
    {
        var dataset = GroundworkNativeRouteDataset.Create(requests);
        var projectedColumns = string.Join(", ", dataset.ProjectedValues.Keys.Select(Quote));
        var projectedValues = string.Join(", ", dataset.ProjectedValues.Values.Select((value, index) =>
        {
            var varies = dataset.VaryingProjectedFields.Contains(value.Field, StringComparer.Ordinal);
            var isPredicate = dataset.PredicateFields.Contains(value.Field);
            return
            $"CASE WHEN value <= @matchingCardinality THEN " +
            $"{NativeMatchingExpression(value, index, varies && dataset.MatchingCardinality > 1)} " +
            $"ELSE {(varies || !isPredicate ? NativeNoiseExpression(value.Kind, index) : $"@noise{index}")} END";
        }));
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandTimeout = NativeRouteDatasetCommandTimeoutSeconds;
            var sequence = """
                WITH sequence(value) AS (
                    SELECT TOP (@cardinality)
                           ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1
                    FROM sys.all_objects first_set
                    CROSS JOIN sys.all_objects second_set
                )
                """;
            const string orderedLookupKey =
                "CONVERT(binary(32), 0xFF + CONVERT(varbinary(31), RIGHT(CONCAT('000000', value), 6)))";
            var primaryName = dataset.PrimaryPhysicalName ?? dataset.PhysicalName;
            var primaryInsert = $"""
                {sequence}
                INSERT INTO {Quote(primaryName)}
                    ([document_kind], [storage_scope], [id], [id_comparison_key], [id_lookup_key],
                     [schema_version], [version], {Quote(dataset.ContentColumn)}, [created_utc], [updated_utc])
                SELECT @kind,
                       CASE WHEN value = 1 THEN @crossScope ELSE @scope END,
                       CASE WHEN value = 0 THEN @candidateId ELSE CONCAT('native-', RIGHT(CONCAT('000000', value), 6)) END,
                       CASE WHEN value = 0 THEN @candidateComparison ELSE CONVERT(varbinary(1350), CONCAT('native-', RIGHT(CONCAT('000000', value), 6))) END,
                       CASE WHEN value = 0 THEN @candidateLookup ELSE {orderedLookupKey} END,
                       @schemaVersion,
                       1,
                       CASE WHEN value = 0 THEN @candidateContent ELSE @emptyContent END,
                       N'2000-01-01T00:00:00.0000000+00:00',
                       N'2000-01-01T00:00:00.0000000+00:00'
                FROM sequence;
                """;
            var lookupInsert = $"""
                {sequence}
                INSERT INTO {Quote(dataset.PhysicalName)}
                    ([document_kind], [storage_scope], [document_id], [document_id_comparison_key],
                     [document_id_lookup_key], {projectedColumns})
                SELECT @kind,
                       CASE WHEN value = 1 THEN @crossScope ELSE @scope END,
                       CASE WHEN value = 0 THEN @candidateId ELSE CONCAT('native-', RIGHT(CONCAT('000000', value), 6)) END,
                       CASE WHEN value = 0 THEN @candidateComparison ELSE CONVERT(varbinary(1350), CONCAT('native-', RIGHT(CONCAT('000000', value), 6))) END,
                       CASE WHEN value = 0 THEN @candidateLookup ELSE {orderedLookupKey} END,
                       {projectedValues}
                FROM sequence;
                """;
            command.CommandText = dataset.PrimaryPhysicalName is null
                ? $"""
                    DELETE FROM {Quote(dataset.PhysicalName)};
                    {sequence}
                    INSERT INTO {Quote(dataset.PhysicalName)}
                        ([document_kind], [storage_scope], [id], [id_comparison_key], [id_lookup_key],
                         [schema_version], [version], {Quote(dataset.ContentColumn)}, [created_utc], [updated_utc], {projectedColumns})
                    SELECT @kind,
                           CASE WHEN value = 1 THEN @crossScope ELSE @scope END,
                           CASE WHEN value = 0 THEN @candidateId ELSE CONCAT('native-', RIGHT(CONCAT('000000', value), 6)) END,
                           CASE WHEN value = 0 THEN @candidateComparison ELSE CONVERT(varbinary(1350), CONCAT('native-', RIGHT(CONCAT('000000', value), 6))) END,
                           CASE WHEN value = 0 THEN @candidateLookup ELSE {orderedLookupKey} END,
                           @schemaVersion,
                           1,
                           CASE WHEN value = 0 THEN @candidateContent ELSE @emptyContent END,
                           N'2000-01-01T00:00:00.0000000+00:00',
                           N'2000-01-01T00:00:00.0000000+00:00',
                           {projectedValues}
                    FROM sequence;
                    """
                : $"""
                    DELETE FROM {Quote(dataset.PhysicalName)};
                    DELETE FROM {Quote(primaryName)} WHERE [document_kind] = @kind;
                    {primaryInsert}
                    {lookupInsert}
                    """;
            command.Parameters.AddWithValue("@kind", dataset.DocumentKind);
            command.Parameters.AddWithValue("@scope", dataset.StorageScope);
            command.Parameters.AddWithValue("@crossScope", dataset.CrossScope);
            command.Parameters.AddWithValue("@candidateId", dataset.CandidateDocumentId);
            command.Parameters.AddWithValue("@candidateComparison", Convert.FromHexString(dataset.CandidateComparisonKey));
            command.Parameters.AddWithValue("@candidateLookup", Convert.FromHexString(dataset.CandidateLookupKey));
            command.Parameters.AddWithValue("@schemaVersion", dataset.CandidateSchemaVersion);
            command.Parameters.AddWithValue("@candidateContent", dataset.CandidateContentJson);
            command.Parameters.AddWithValue("@emptyContent", "{}");
            command.Parameters.AddWithValue("@cardinality", dataset.AcceptanceCardinality);
            command.Parameters.AddWithValue("@matchingCardinality", dataset.MatchingCardinality);
            var index = 0;
            foreach (var value in dataset.ProjectedValues.Values)
            {
                command.Parameters.AddWithValue($"@target{index}", value.ToProviderValue());
                command.Parameters.AddWithValue($"@noise{index}", value.ToProviderNoiseValue());
                index++;
            }
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        await using (var statistics = connection.CreateCommand())
        {
            statistics.CommandTimeout = NativeRouteDatasetCommandTimeoutSeconds;
            statistics.CommandText = dataset.PrimaryPhysicalName is null
                ? $"UPDATE STATISTICS {Quote(dataset.PhysicalName)} WITH FULLSCAN;"
                : $"UPDATE STATISTICS {Quote(dataset.PhysicalName)} WITH FULLSCAN; " +
                  $"UPDATE STATISTICS {Quote(dataset.PrimaryPhysicalName)} WITH FULLSCAN;";
            await statistics.ExecuteNonQueryAsync(cancellationToken);
        }
        var cardinality = await CountPhysicalRowsAsync(connection, dataset.PhysicalName, cancellationToken);
        if (cardinality != dataset.AcceptanceCardinality)
            throw new InvalidOperationException(
                $"SQL Server seeded {cardinality} rows in '{dataset.PhysicalName}', expected {dataset.AcceptanceCardinality}.");
    }

    private static async Task<string> ResolveNativeRouteIndexAsync(
        SqlConnection connection,
        GroundworkNativeRoutePlanRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT i.name, ic.key_ordinal, c.name
            FROM sys.indexes i
            INNER JOIN sys.index_columns ic
                ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            INNER JOIN sys.columns c
                ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(@qualifiedTable)
              AND ic.is_included_column = 0
            ORDER BY i.name, ic.key_ordinal;
            """;
        command.Parameters.AddWithValue("@qualifiedTable", $"dbo.{request.PhysicalName}");
        var indexes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetString(0);
            if (!indexes.TryGetValue(name, out var columns))
                indexes[name] = columns = [];
            columns.Add(reader.GetString(2));
        }
        if (request.ExpectedIndexName is { } expectedIndex && indexes.ContainsKey(expectedIndex))
            return expectedIndex;
        var expected = new[] { "storage_scope" }.Concat(request.IndexFields).ToArray();
        var match = indexes.FirstOrDefault(index =>
            index.Value.Take(expected.Length).SequenceEqual(expected, StringComparer.Ordinal));
        return !string.IsNullOrWhiteSpace(match.Key)
            ? match.Key
            : throw new InvalidOperationException(
                $"SQL Server route '{request.QueryIdentity}' has no physical index prefix ({string.Join(", ", expected)}).");
    }

    private static async Task<IReadOnlyList<string>> ListNativeRouteIndexesAsync(
        SqlConnection connection,
        string physicalName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT i.name
            FROM sys.indexes i
            WHERE i.object_id = OBJECT_ID(@qualifiedTable)
              AND i.name IS NOT NULL
            ORDER BY i.name;
            """;
        command.Parameters.AddWithValue("@qualifiedTable", $"dbo.{physicalName}");
        var indexes = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            indexes.Add(reader.GetString(0));
        return indexes;
    }

    private static string NativeNoiseExpression(
        GroundworkNativeRouteProjectedValueKind kind,
        int targetIndex) =>
        kind switch
        {
            GroundworkNativeRouteProjectedValueKind.String =>
                "CONCAT('noise-', RIGHT(CONCAT('000000', value), 6))",
            GroundworkNativeRouteProjectedValueKind.Boolean =>
                $"CASE WHEN @target{targetIndex} = 1 THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END",
            GroundworkNativeRouteProjectedValueKind.Int64 =>
                $"CONVERT(bigint, @target{targetIndex}) + value + 1",
            GroundworkNativeRouteProjectedValueKind.DateTime =>
                $"DATEADD(second, CONVERT(int, value) + 1, CONVERT(datetimeoffset, @target{targetIndex}))",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown native-route projected value kind.")
        };

    private static string NativeMatchingExpression(
        GroundworkNativeRouteProjectedValue value,
        int targetIndex,
        bool varies) =>
        !varies
            ? $"@target{targetIndex}"
            : value.Kind switch
            {
                GroundworkNativeRouteProjectedValueKind.String =>
                    $"CONCAT(@target{targetIndex}, '-', RIGHT(CONCAT('000000', value), 6))",
                GroundworkNativeRouteProjectedValueKind.Int64 =>
                    $"CONVERT(bigint, @target{targetIndex}) + value",
                GroundworkNativeRouteProjectedValueKind.DateTime =>
                    $"DATEADD(second, CONVERT(int, value), CONVERT(datetimeoffset, @target{targetIndex}))",
                _ => throw new InvalidOperationException(
                    $"Projected field '{value.Field}' cannot vary uniquely at acceptance scale.")
            };

    private static async Task<long> CountPhysicalRowsAsync(
        SqlConnection connection,
        string physicalName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT_BIG(*) FROM {Quote(physicalName)};";
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static Task RefreshIdentityUsersStatisticsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE STATISTICS [identity_users] WITH FULLSCAN;";
        return ExecuteAndDisposeAsync(command, cancellationToken);
    }

    private static string[] ExtractShowPlanOperators(XDocument document) =>
        document.Descendants()
            .Where(element => element.Name.LocalName == "RelOp")
            .Select(element => (string?)element.Attribute("PhysicalOp"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray()!;

    private static string[] ExtractShowPlanIndexes(XDocument document) =>
        document.Descendants()
            .Where(element => element.Name.LocalName == "Object")
            .Select(element => ((string?)element.Attribute("Index"))?.Trim('[', ']'))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray()!;

    private static void EnsureSchemaApplied(PhysicalSchemaApplicationResult result)
    {
        if (result.Outcome is not (PhysicalSchemaApplicationOutcome.Applied or PhysicalSchemaApplicationOutcome.NoChanges))
            throw new InvalidOperationException($"SQL Server physical schema application did not complete: {result.Outcome}.");
    }

}
