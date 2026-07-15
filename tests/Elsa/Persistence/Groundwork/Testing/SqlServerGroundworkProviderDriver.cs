using System.Data.SqlTypes;
using System.Xml.Linq;
using Elsa.Persistence.Groundwork;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.SqlServer;
using Groundwork.SqlServer.Documents;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>Real SQL Server provider mechanics for the shared Groundwork conformance fixture.</summary>
public sealed class SqlServerGroundworkProviderDriver : GroundworkProviderDriver
{
    private const string Image = "mcr.microsoft.com/mssql/server:2022-CU21-ubuntu-22.04";
    private const string ProtocolVersion = "1.0.0";
    private readonly MsSqlContainer _container = new MsSqlBuilder(Image).Build();
    private readonly string _databaseName = $"elsa_groundwork_driver_{Guid.NewGuid():N}";
    private readonly GroundworkProcessProbeRunner _processProbeRunner = new();
    private readonly GroundworkProcessLaunchDescriptor _processLaunchDescriptor;
    private string? _connectionString;

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
            GroundworkTopologyCapabilities.ExternalProcessRestart));

    public override GroundworkTopologyCapabilities RequiredTopology =>
        GroundworkTopologyCapabilities.PersistentStorage |
        GroundworkTopologyCapabilities.IndependentClients |
        GroundworkTopologyCapabilities.ExternalProcessRestart;

    public override GroundworkCompositionFingerprint CompositionFingerprint { get; } =
        GroundworkCompositionFingerprint.Create("elsa-runtime-provider-fixture:v1");

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
        await using var connection = new SqlConnection(AdminConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var database = Quote(_databaseName);
        command.CommandText = $"""
            IF DB_ID(N'{_databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE {database} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE {database};
            END;
            CREATE DATABASE {database};
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
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
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE {Quote(_databaseName)};";
        await command.ExecuteNonQueryAsync(cancellationToken);
        _connectionString = TargetConnectionString();
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

}
