using System.Text.Json;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

public sealed class AspNetCoreIdentitySchemaCliTests
{
    private const int Success = 0;
    private const int PendingChanges = 2;
    private const string ExternalProviderOptIn = "ELSA_RUN_IDENTITY_SCHEMA_PARITY_PROVIDERS";
    private static readonly Type SelectedSchemaType = typeof(GroundworkAllFeaturesWithIdentityDeploymentSchema);

    [Fact]
    [Trait("Category", "Sqlite")]
    public async Task Sqlite_shell_schema_has_runtime_cli_fingerprint_parity_and_read_only_inspection() =>
        _ = await CaptureSchemaParityAsync(new SqliteGroundworkProviderDriver());

    [SkippableFact]
    [Trait("Category", "PostgreSql")]
    public async Task PostgreSql_shell_schema_has_runtime_cli_fingerprint_parity_and_read_only_inspection()
    {
        RequireExternalProviderOptIn();
        _ = await CaptureSchemaParityAsync(new PostgreSqlGroundworkProviderDriver());
    }

    [SkippableFact]
    [Trait("Category", "SqlServer")]
    public async Task SqlServer_shell_schema_has_runtime_cli_fingerprint_parity_and_read_only_inspection()
    {
        RequireExternalProviderOptIn();
        _ = await CaptureSchemaParityAsync(new SqlServerGroundworkProviderDriver());
    }

    [SkippableFact]
    [Trait("Category", "MongoDb")]
    public async Task MongoDb_shell_schema_has_runtime_cli_fingerprint_parity_and_read_only_inspection()
    {
        RequireExternalProviderOptIn();
        _ = await CaptureSchemaParityAsync(new MongoDbGroundworkProviderDriver());
    }

    internal static async Task<AspNetCoreIdentitySchemaEvidence> CaptureSchemaParityAsync(
        GroundworkProviderDriver driver)
    {
        await using (driver)
        {
            await driver.InitializeAsync(CancellationToken.None);
            var (deploymentManifest, manifestSources) = await ShellSelectedManifestSourcesAsync();
            var source = await driver.PrepareSchemaParityAsync(manifestSources, CancellationToken.None);
            Assert.Equal(
                deploymentManifest.StorageUnits.Select(unit => unit.Identity.Value).Order(StringComparer.Ordinal),
                source.CreateManifest().StorageUnits.Select(unit => unit.Identity.Value).Order(StringComparer.Ordinal));
            Assert.Contains(source.CreateManifest().StorageUnits, unit =>
                unit.Identity.Value == IdentityStorageManifest.IdentityUserDocumentKind);
            Assert.Contains(source.CreateManifest().StorageUnits, unit =>
                unit.Identity.Value == ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind);

            var pendingState = await driver.CaptureSchemaStateAsync(CancellationToken.None);

            var offline = await driver.RunSchemaToolAsync(
                "validate",
                SelectedSchemaType,
                CancellationToken.None,
                "--offline");
            Assert.Equal(Success, offline.ExitCode);
            Assert.Equal(string.Empty, offline.StandardError);
            Assert.Equal("offline", offline.Report.GetProperty("inspectionMode").GetString());
            AssertTargetFingerprint(source.TargetFingerprint, offline.Report);
            Assert.Equal(EffectiveNames(source.ResolvedNames), EffectiveNames(offline.Report));
            await AssertStateAsync(driver, pendingState);

            var validatePending = await driver.RunSchemaToolAsync(
                "validate",
                SelectedSchemaType,
                CancellationToken.None);
            Assert.Equal(Success, validatePending.ExitCode);
            Assert.Equal("ready", validatePending.Report.GetProperty("outcome").GetString());
            Assert.True(validatePending.Report.GetProperty("pendingOperations").GetArrayLength() > 0);
            Assert.False(validatePending.Report.GetProperty("targetMutated").GetBoolean());
            AssertTargetFingerprint(source.TargetFingerprint, validatePending.Report);
            await AssertStateAsync(driver, pendingState);

            var planPending = await driver.RunSchemaToolAsync(
                "plan",
                SelectedSchemaType,
                CancellationToken.None);
            Assert.Equal(PendingChanges, planPending.ExitCode);
            Assert.Equal("pending", planPending.Report.GetProperty("outcome").GetString());
            Assert.False(planPending.Report.GetProperty("targetMutated").GetBoolean());
            AssertTargetFingerprint(source.TargetFingerprint, planPending.Report);
            await AssertStateAsync(driver, pendingState);

            var statusPending = await driver.RunSchemaToolAsync(
                "status",
                SelectedSchemaType,
                CancellationToken.None);
            Assert.Equal(PendingChanges, statusPending.ExitCode);
            Assert.Equal("pending", statusPending.Report.GetProperty("outcome").GetString());
            Assert.False(statusPending.Report.GetProperty("targetMutated").GetBoolean());
            Assert.Equal(
                planPending.Report.GetProperty("planFingerprint").GetString(),
                statusPending.Report.GetProperty("planFingerprint").GetString());
            AssertTargetFingerprint(source.TargetFingerprint, statusPending.Report);
            await AssertStateAsync(driver, pendingState);

            var pendingAdmission = await driver.InspectSchemaParityAdmissionAsync(source, CancellationToken.None);
            Assert.False(pendingAdmission.IsReady);
            Assert.Equal(source.TargetFingerprint, pendingAdmission.TargetFingerprint);
            Assert.NotEmpty(pendingAdmission.PendingOperations);
            await AssertStateAsync(driver, pendingState);

            var apply = await driver.RunSchemaToolAsync(
                "apply",
                SelectedSchemaType,
                CancellationToken.None,
                "--safe");
            Assert.Equal(Success, apply.ExitCode);
            Assert.Equal("applied", apply.Report.GetProperty("outcome").GetString());
            Assert.True(apply.Report.GetProperty("targetMutated").GetBoolean());
            AssertTargetFingerprint(source.TargetFingerprint, apply.Report);
            var appliedState = await driver.CaptureSchemaStateAsync(CancellationToken.None);
            Assert.NotEqual(pendingState.Fingerprint, appliedState.Fingerprint);
            Assert.True(appliedState.CanonicalItemCount > pendingState.CanonicalItemCount);

            var validateReady = await driver.RunSchemaToolAsync(
                "validate",
                SelectedSchemaType,
                CancellationToken.None);
            Assert.Equal(Success, validateReady.ExitCode);
            Assert.Equal(0, validateReady.Report.GetProperty("pendingOperations").GetArrayLength());
            Assert.False(validateReady.Report.GetProperty("targetMutated").GetBoolean());
            AssertTargetFingerprint(source.TargetFingerprint, validateReady.Report);
            await AssertStateAsync(driver, appliedState);

            var statusReady = await driver.RunSchemaToolAsync(
                "status",
                SelectedSchemaType,
                CancellationToken.None);
            Assert.Equal(Success, statusReady.ExitCode);
            Assert.Equal("ready", statusReady.Report.GetProperty("outcome").GetString());
            Assert.False(statusReady.Report.GetProperty("targetMutated").GetBoolean());
            AssertTargetFingerprint(source.TargetFingerprint, statusReady.Report);
            await AssertStateAsync(driver, appliedState);

            var readyAdmission = await driver.InspectSchemaParityAdmissionAsync(source, CancellationToken.None);
            Assert.True(readyAdmission.IsReady);
            Assert.Equal(source.TargetFingerprint, readyAdmission.TargetFingerprint);
            Assert.Equal(source.TargetFingerprint, readyAdmission.AppliedTargetFingerprint);
            Assert.Empty(readyAdmission.PendingOperations);
            await AssertStateAsync(driver, appliedState);

            Assert.All(
                new[] { offline, validatePending, planPending, statusPending, apply, validateReady, statusReady },
                result => Assert.Equal(string.Empty, result.StandardError));
            return new AspNetCoreIdentitySchemaEvidence(
                source.CompositionFingerprint,
                source.TargetFingerprint,
                pendingState,
                appliedState,
                [
                    Receipt("validate-offline", offline, pendingState, pendingState),
                    Receipt("validate-pending", validatePending, pendingState, pendingState),
                    Receipt("plan-pending", planPending, pendingState, pendingState),
                    Receipt("status-pending", statusPending, pendingState, pendingState),
                    Receipt("apply-safe", apply, pendingState, appliedState),
                    Receipt("validate-ready", validateReady, appliedState, appliedState),
                    Receipt("status-ready", statusReady, appliedState, appliedState)
                ],
                new AspNetCoreIdentityRuntimeAdmissionEvidence(
                    pendingAdmission.IsReady,
                    pendingAdmission.PendingOperations.Count,
                    pendingAdmission.TargetFingerprint,
                    pendingAdmission.AppliedTargetFingerprint),
                new AspNetCoreIdentityRuntimeAdmissionEvidence(
                    readyAdmission.IsReady,
                    readyAdmission.PendingOperations.Count,
                    readyAdmission.TargetFingerprint,
                    readyAdmission.AppliedTargetFingerprint));
        }
    }

    private static AspNetCoreIdentitySchemaToolReceipt Receipt(
        string operation,
        GroundworkSchemaToolResult result,
        GroundworkProviderSchemaStateSnapshot before,
        GroundworkProviderSchemaStateSnapshot after) =>
        new(
            operation,
            result.ExitCode,
            OptionalString(result.Report, "inspectionMode"),
            OptionalString(result.Report, "outcome"),
            result.Report.TryGetProperty("targetMutated", out var mutated) && mutated.GetBoolean(),
            before.Fingerprint,
            after.Fingerprint,
            string.Equals(before.Fingerprint, after.Fingerprint, StringComparison.Ordinal));

    private static string? OptionalString(JsonElement report, string propertyName) =>
        report.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static async Task<(global::Groundwork.Core.Manifests.StorageManifest Manifest, IReadOnlyCollection<IGroundworkStorageManifestSource> Sources)>
        ShellSelectedManifestSourcesAsync()
    {
        var services = new ServiceCollection();
        services.AddGroundworkStorageComposition<GroundworkAllFeaturesWithIdentityDeploymentSchema>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var selected = scope.ServiceProvider
            .GetRequiredService<global::Groundwork.Core.SchemaEvolution.IPhysicalSchemaManifestSource>();
        Assert.IsType<GroundworkAllFeaturesWithIdentityDeploymentSchema>(selected);
        var sources = scope.ServiceProvider.GetServices<IGroundworkStorageManifestSource>().ToArray();
        Assert.NotEmpty(sources);
        return (selected.CreateManifest(), sources);
    }

    private static async Task AssertStateAsync(
        GroundworkProviderDriver driver,
        GroundworkProviderSchemaStateSnapshot expected)
    {
        var actual = await driver.CaptureSchemaStateAsync(CancellationToken.None);
        Assert.Equal(expected.ProviderKey, actual.ProviderKey);
        Assert.Equal(expected.Fingerprint, actual.Fingerprint);
        Assert.Equal(expected.CanonicalItemCount, actual.CanonicalItemCount);
        Assert.Equal("provider-schema-state", actual.Evidence.Kind);
    }

    private static void AssertTargetFingerprint(string expected, JsonElement report) =>
        Assert.Equal(expected, report.GetProperty("target").GetProperty("fingerprint").GetString());

    private static IReadOnlyList<string> EffectiveNames(
        IReadOnlyCollection<GroundworkResolvedPhysicalNameSnapshot> names) => names
        .Select(name => $"{name.LogicalName}\u001f{name.Identifier}")
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static IReadOnlyList<string> EffectiveNames(JsonElement report) => report
        .GetProperty("resolvedNames")
        .EnumerateArray()
        .Select(name => $"{name.GetProperty("logicalName").GetString()}\u001f{name.GetProperty("identifier").GetString()}")
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static void RequireExternalProviderOptIn() =>
        Skip.IfNot(
            string.Equals(Environment.GetEnvironmentVariable(ExternalProviderOptIn), "1", StringComparison.Ordinal),
            $"Set {ExternalProviderOptIn}=1 to run the external-provider schema-parity matrix.");
}

internal sealed record AspNetCoreIdentitySchemaEvidence(
    string CompositionFingerprint,
    string TargetFingerprint,
    GroundworkProviderSchemaStateSnapshot PendingState,
    GroundworkProviderSchemaStateSnapshot AppliedState,
    IReadOnlyList<AspNetCoreIdentitySchemaToolReceipt> ToolReceipts,
    AspNetCoreIdentityRuntimeAdmissionEvidence PendingAdmission,
    AspNetCoreIdentityRuntimeAdmissionEvidence ReadyAdmission);

internal sealed record AspNetCoreIdentitySchemaToolReceipt(
    string Operation,
    int ExitCode,
    string? InspectionMode,
    string? Outcome,
    bool TargetMutated,
    string BeforeFingerprint,
    string AfterFingerprint,
    bool StateUnchanged);

internal sealed record AspNetCoreIdentityRuntimeAdmissionEvidence(
    bool IsReady,
    int PendingOperationCount,
    string TargetFingerprint,
    string? AppliedTargetFingerprint);
