using System.Reflection;
using System.Text.Json;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Documents.Store;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

public class ProviderDriverContractTests
{
    private const string ProbeSchemaVersion = "1.0.0";

    public static TheoryData<string, string, string> Providers => new()
    {
        { "sqlite", "groundwork-sqlite", "SqliteGroundworkProviderDriver" },
        { "sqlserver", "groundwork-sqlserver", "SqlServerGroundworkProviderDriver" },
        { "postgresql", "groundwork-postgresql", "PostgreSqlGroundworkProviderDriver" },
        { "mongodb", "groundwork-mongodb", "MongoDbGroundworkProviderDriver" }
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Provider_driver_satisfies_reset_client_reopen_restart_topology_and_evidence_contracts(
        string providerIdentity,
        string expectedProviderIdentity,
        string driverTypeName)
    {
        await using var driver = CreateDriver(providerIdentity, driverTypeName);
        await driver.InitializeAsync(CancellationToken.None);

        Assert.Equal(providerIdentity, driver.Descriptor.ProviderKey);
        Assert.Equal(expectedProviderIdentity, driver.Descriptor.ProviderIdentity);
        Assert.False(string.IsNullOrWhiteSpace(driver.Descriptor.ProviderVersion));
        Assert.False(string.IsNullOrWhiteSpace(driver.Descriptor.Topology.Description));
        driver.Descriptor.Topology.EnsureSupports(driver.RequiredTopology);
        Assert.True(driver.RequiredTopology.HasFlag(GroundworkTopologyCapabilities.PersistentStorage));
        Assert.True(driver.RequiredTopology.HasFlag(GroundworkTopologyCapabilities.IndependentClients));
        Assert.True(driver.RequiredTopology.HasFlag(GroundworkTopologyCapabilities.ExternalProcessRestart));
        if (providerIdentity == "mongodb")
        {
            Assert.Equal("transaction-capable-replica-set", driver.Descriptor.Topology.Description);
            Assert.True(driver.RequiredTopology.HasFlag(GroundworkTopologyCapabilities.MultiDocumentTransactions));
            Assert.True(driver.RequiredTopology.HasFlag(GroundworkTopologyCapabilities.TransactionCapableMongoTopology));
            var rejectionProbe = Assert.IsAssignableFrom<IGroundworkTopologyRejectionProbe>(driver);
            var rejection = await rejectionProbe.CaptureTopologyRejectionAsync(CancellationToken.None);
            Assert.Equal("topology-rejection", rejection.Kind);
            Assert.Contains("observed-topology=standalone", rejection.Content, StringComparison.Ordinal);
            Assert.Contains("outcome=rejected", rejection.Content, StringComparison.Ordinal);
        }

        await AssertResetRejectsActiveClientsAsync(driver);
        await AssertDeterministicResetAsync(driver);
        await AssertIndependentClientsAsync(driver);
        await AssertDisposeAndReopenAsync(driver);
        await AssertProcessRestartAsync(driver);
        await AssertSanitizedEvidenceAsync(driver);
    }

    [Fact]
    public void Unsupported_topology_is_rejected_with_provider_and_capability_context()
    {
        var topology = new GroundworkProviderTopology(
            "mongodb",
            "transaction-capable-replica-set",
            GroundworkTopologyCapabilities.PersistentStorage | GroundworkTopologyCapabilities.IndependentClients);

        var exception = Assert.Throws<GroundworkProviderTopologyException>(() =>
            topology.EnsureSupports(
                GroundworkTopologyCapabilities.MultiDocumentTransactions |
                GroundworkTopologyCapabilities.TransactionCapableMongoTopology));

        Assert.Contains("mongodb", exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(GroundworkTopologyCapabilities.MultiDocumentTransactions), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(GroundworkTopologyCapabilities.TransactionCapableMongoTopology), exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("connection", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Scenario_result_digest_is_provider_independent_and_evidence_rejects_secrets()
    {
        var observations = new[]
        {
            new GroundworkScenarioObservation("winner-count", "1"),
            new GroundworkScenarioObservation("final-state", "committed")
        };
        var composition = GroundworkCompositionFingerprint.Create("fixture-composition:v1");

        var sqlite = GroundworkScenarioResult.Create(
            "checkpoint-race",
            "runtime-checkpoint-commit",
            new GroundworkProviderDescriptor(
                "sqlite",
                "groundwork-sqlite",
                "1.0.0",
                new GroundworkProviderTopology(
                    "sqlite",
                    "file-backed-distinct-connections",
                    GroundworkTopologyCapabilities.PersistentStorage | GroundworkTopologyCapabilities.IndependentClients)),
            composition,
            GroundworkExecutionPath.Create("sqlite", "checkpoint-race", composition).Value,
            2,
            observations,
            GroundworkScenarioOutcome.Pass);
        var mongodb = GroundworkScenarioResult.Create(
            "checkpoint-race",
            "runtime-checkpoint-commit",
            new GroundworkProviderDescriptor(
                "mongodb",
                "groundwork-mongodb",
                "1.0.0",
                new GroundworkProviderTopology(
                    "mongodb",
                    "transaction-capable-replica-set",
                    GroundworkTopologyCapabilities.PersistentStorage | GroundworkTopologyCapabilities.IndependentClients)),
            composition,
            GroundworkExecutionPath.Create("mongodb", "checkpoint-race", composition).Value,
            2,
            observations.Reverse().ToArray(),
            GroundworkScenarioOutcome.Pass);

        Assert.Equal(sqlite.ResultDigest, mongodb.ResultDigest);
        Assert.Throws<ArgumentException>(() =>
            GroundworkSanitizedEvidence.Create("diagnostics", FixtureConnectionString()));
        Assert.Throws<ArgumentException>(() =>
            GroundworkSanitizedEvidence.Create("diagnostics", "Server=db.internal;Database=elsa"));
        Assert.Throws<ArgumentException>(() =>
            GroundworkSanitizedEvidence.Create("metrics", "operation.count{tenant.id=tenant-a} 1"));
    }

    [Fact]
    public void Process_launch_fingerprint_is_machine_independent_and_rejects_connection_arguments()
    {
        var first = new GroundworkProcessLaunchDescriptor(
            "/tmp/first/dotnet",
            ["/tmp/first/Elsa.Persistence.Groundwork.ProcessProbe.dll"],
            "1");
        var second = new GroundworkProcessLaunchDescriptor(
            "/opt/second/dotnet",
            ["/opt/second/Elsa.Persistence.Groundwork.ProcessProbe.dll"],
            "1");

        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal("redirected-standard-input", first.StateLocatorTransport);
        Assert.Throws<ArgumentException>(() =>
            new GroundworkProcessLaunchDescriptor("dotnet", ["Server=db.internal"], "1"));
    }

    [Fact]
    public void Process_probe_diagnostic_strings_redact_state_document_and_payload_values()
    {
        var connectionString = FixtureConnectionString();
        const string documentId = "tenant-a-sensitive-document";
        const string payload = "sensitive-payload";
        var state = new GroundworkProcessProbeState(connectionString);
        var request = new GroundworkProcessProbeRequest("redaction-probe", GroundworkProcessProbeOperation.Save, documentId, payload);
        var command = new GroundworkProcessProbeCommand(
            GroundworkProcessProbeProtocol.CurrentVersion,
            new string('a', 64),
            "sqlite",
            "groundwork-sqlite",
            "1.0.0",
            "probe-document",
            request,
            state);

        foreach (var diagnostic in new[] { state.ToString(), request.ToString(), command.ToString() })
        {
            Assert.Contains("[REDACTED]", diagnostic, StringComparison.Ordinal);
            Assert.DoesNotContain(connectionString, diagnostic, StringComparison.Ordinal);
            Assert.DoesNotContain(documentId, diagnostic, StringComparison.Ordinal);
            Assert.DoesNotContain(payload, diagnostic, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Scenario_and_native_evidence_require_the_exact_execution_path()
    {
        var composition = GroundworkCompositionFingerprint.Create("fixture-composition:v1");
        var otherComposition = GroundworkCompositionFingerprint.Create("fixture-composition:v2");
        var descriptor = new GroundworkProviderDescriptor(
            "sqlite",
            "groundwork-sqlite",
            "1.0.0",
            new GroundworkProviderTopology(
                "sqlite",
                "file-backed-distinct-connections",
                GroundworkTopologyCapabilities.PersistentStorage));
        var expectedPath = GroundworkExecutionPath.Create("sqlite", "checkpoint-race", composition);
        var wrongPath = GroundworkExecutionPath.Create("sqlite", "checkpoint-race", otherComposition);
        var evidence = GroundworkSanitizedEvidence.Create("native-plan", "indexed bounded route");

        Assert.Throws<ArgumentException>(() => GroundworkNativePlanEvidence.Create(expectedPath, "other-scenario", evidence));
        Assert.Throws<ArgumentException>(() => GroundworkScenarioResult.Create(
            "checkpoint-race",
            "runtime-checkpoint-commit",
            descriptor,
            composition,
            wrongPath.Value,
            2,
            [new GroundworkScenarioObservation("final-state", "committed")],
            GroundworkScenarioOutcome.Pass));
    }

    [Fact]
    public async Task Failure_controller_triggers_each_armed_window_once_and_cancellation_is_deterministic()
    {
        var controller = new GroundworkFailureController();
        var failureWindow = new GroundworkFailureWindow("after-durable-decision");
        var cancellationWindow = new GroundworkFailureWindow("during-recovery");
        controller.FailAt(failureWindow, () => new InjectedGroundworkFailureException(failureWindow));
        controller.CancelAt(cancellationWindow);

        await Assert.ThrowsAsync<InjectedGroundworkFailureException>(() =>
            controller.ReachAsync(failureWindow, CancellationToken.None).AsTask());
        await controller.ReachAsync(failureWindow, CancellationToken.None);
        var cancellation = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            controller.ReachAsync(cancellationWindow, CancellationToken.None).AsTask());
        Assert.True(cancellation.CancellationToken.IsCancellationRequested);
        await controller.ReachAsync(cancellationWindow, CancellationToken.None);

        Assert.Equal([failureWindow, failureWindow, cancellationWindow, cancellationWindow], controller.ReachedWindows);
    }

    private static GroundworkProviderDriver CreateDriver(string providerIdentity, string driverTypeName)
    {
        var testingAssembly = typeof(GroundworkProviderDriver).Assembly;
        var qualifiedName = $"Elsa.Persistence.Groundwork.Testing.{driverTypeName}";
        var driverType = testingAssembly.GetType(qualifiedName);

        Assert.True(
            driverType is not null,
            $"The required {providerIdentity} provider driver '{qualifiedName}' has not been implemented.");
        Assert.True(
            typeof(GroundworkProviderDriver).IsAssignableFrom(driverType),
            $"The {providerIdentity} provider driver must inherit {nameof(GroundworkProviderDriver)}.");

        try
        {
            return Assert.IsAssignableFrom<GroundworkProviderDriver>(Activator.CreateInstance(driverType));
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw exception.InnerException;
        }
    }

    private static async Task AssertDeterministicResetAsync(GroundworkProviderDriver driver)
    {
        await driver.ResetAsync(CancellationToken.None);
        await using (var client = await driver.OpenClientAsync(CancellationToken.None))
            await SaveProbeAsync(driver, client, "reset-probe", "first");

        await driver.ResetAsync(CancellationToken.None);
        await using (var client = await driver.OpenClientAsync(CancellationToken.None))
            Assert.Null(await client.DocumentStore.LoadAsync(driver.ProbeDocumentKind, "reset-probe", CancellationToken.None));

        await driver.ResetAsync(CancellationToken.None);
        await using var secondPassClient = await driver.OpenClientAsync(CancellationToken.None);
        Assert.Null(await secondPassClient.DocumentStore.LoadAsync(driver.ProbeDocumentKind, "reset-probe", CancellationToken.None));
    }

    private static async Task AssertResetRejectsActiveClientsAsync(GroundworkProviderDriver driver)
    {
        await using (var activeClient = await driver.OpenClientAsync(CancellationToken.None))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                driver.ResetAsync(CancellationToken.None).AsTask());
        }

        await driver.ResetAsync(CancellationToken.None);
    }

    private static async Task AssertIndependentClientsAsync(GroundworkProviderDriver driver)
    {
        await driver.ResetAsync(CancellationToken.None);
        await using var first = await driver.OpenClientAsync(CancellationToken.None);
        await using var second = await driver.OpenClientAsync(CancellationToken.None);

        Assert.NotEqual(first.ClientId, second.ClientId);
        Assert.NotSame(first.Services, second.Services);
        Assert.NotSame(first.DocumentStore, second.DocumentStore);
        await SaveProbeAsync(driver, first, "independent-client-probe", "visible");
        var observed = await second.DocumentStore.LoadAsync(
            driver.ProbeDocumentKind,
            "independent-client-probe",
            CancellationToken.None);
        Assert.Equal("visible", ReadValue(observed?.ContentJson));
    }

    private static async Task AssertDisposeAndReopenAsync(GroundworkProviderDriver driver)
    {
        await driver.ResetAsync(CancellationToken.None);
        await using (var first = await driver.OpenClientAsync(CancellationToken.None))
        {
            await SaveProbeAsync(driver, first, "reopen-probe", "durable");
        }

        await using var reopened = await driver.OpenClientAsync(CancellationToken.None);
        var observed = await reopened.DocumentStore.LoadAsync(
            driver.ProbeDocumentKind,
            "reopen-probe",
            CancellationToken.None);
        Assert.Equal("durable", ReadValue(observed?.ContentJson));
    }

    private static async Task AssertProcessRestartAsync(GroundworkProviderDriver driver)
    {
        await driver.ResetAsync(CancellationToken.None);
        var saveRequest = new GroundworkProcessProbeRequest(
            "restart-probe-save",
            GroundworkProcessProbeOperation.Save,
            "restart-probe",
            "survived");
        var saved = await driver.RunInNewProcessAsync(
            saveRequest,
            CancellationToken.None);
        var loaded = await driver.RunInNewProcessAsync(
            new GroundworkProcessProbeRequest(
                "restart-probe-load",
                GroundworkProcessProbeOperation.Load,
                "restart-probe"),
            CancellationToken.None);

        Assert.Equal(GroundworkProcessProbeOperation.Save, saved.Operation);
        Assert.Equal(GroundworkProcessProbeOperation.Load, loaded.Operation);
        Assert.NotEqual(Environment.ProcessId, saved.ProcessId);
        Assert.NotEqual(Environment.ProcessId, loaded.ProcessId);
        Assert.NotEqual(saved.ProcessId, loaded.ProcessId);
        Assert.Equal(driver.ProcessLaunchDescriptor.Fingerprint, saved.LaunchDescriptorFingerprint);
        Assert.Equal(driver.ProcessLaunchDescriptor.Fingerprint, loaded.LaunchDescriptorFingerprint);
        Assert.Equal(saveRequest.PayloadSha256, saved.PayloadSha256);
        Assert.Equal(saveRequest.PayloadSha256, loaded.PayloadSha256);
        Assert.Equal(saved.SchemaVersion, loaded.SchemaVersion);
        Assert.Equal(saved.DocumentVersion, loaded.DocumentVersion);
    }

    private static async Task AssertSanitizedEvidenceAsync(GroundworkProviderDriver driver)
    {
        var diagnostics = await driver.CaptureDiagnosticsAsync(CancellationToken.None);
        var executionPath = GroundworkExecutionPath.Create(
            driver.Descriptor.ProviderKey,
            "provider-contract-probe",
            driver.CompositionFingerprint);
        var nativePlan = await driver.CaptureNativePlanAsync(
            executionPath,
            "provider-contract-probe",
            CancellationToken.None);

        Assert.Equal("diagnostics", diagnostics.Kind);
        Assert.False(string.IsNullOrWhiteSpace(diagnostics.Content));
        Assert.Equal(executionPath, nativePlan.ExecutionPath);
        Assert.False(string.IsNullOrWhiteSpace(nativePlan.Evidence.Content));
        Assert.Equal(64, nativePlan.EvidenceSha256.Length);
    }

    private static async Task SaveProbeAsync(
        GroundworkProviderDriver driver,
        GroundworkProviderClient client,
        string id,
        string value)
    {
        var content = JsonSerializer.Serialize(new { value });
        var result = await client.DocumentStore.SaveAsync(
            new SaveDocumentRequest(driver.ProbeDocumentKind, id, ProbeSchemaVersion, content, ExpectedVersion: 0),
            CancellationToken.None);
        Assert.Equal(DocumentStoreWriteStatus.Saved, result.Status);
    }

    private static string? ReadValue(string? contentJson)
    {
        if (contentJson is null)
            return null;

        using var document = JsonDocument.Parse(contentJson);
        return document.RootElement.GetProperty("value").GetString();
    }

    private static string FixtureConnectionString() => new SqlConnectionStringBuilder
    {
        DataSource = "db.invalid",
        UserID = "fixture-user",
        Password = "x"
    }.ConnectionString;
}
