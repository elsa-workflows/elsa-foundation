using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Testing;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.Fixtures;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.V2.ProviderMatrix;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Xunit.Sdk;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.V2.ProviderMatrix.Tests;

public sealed class AspNetCoreIdentityV2ProviderMatrixTests
{
    public static TheoryData<string> Providers => new() { "sqlite", "postgresql", "sqlserver", "mongodb" };

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Public_v2_identity_survives_process_restart_and_rejects_duplicate_names(string provider)
    {
        var connectionEnvironmentVariable = GroundworkV2ProviderRuntime.ConnectionEnvironmentVariable(provider);
        var configured = connectionEnvironmentVariable is null
            ? null
            : Environment.GetEnvironmentVariable(connectionEnvironmentVariable);
        Skip.If(
            provider != "sqlite" && string.IsNullOrWhiteSpace(configured) && !GroundworkV2ProviderRuntime.IsCi,
            $"Set {connectionEnvironmentVariable} locally, or run the matrix in CI.");
        await using var runtime = await GroundworkV2ProviderRuntime.CreateAsync(provider, configured);
        var suffix = $"identity_{Guid.NewGuid():N}"[..17];
        var original = new IdentityProcessProbeUser(
            "tenant-process-restart",
            "user-original",
            "ada",
            "ADA",
            "ada@example.test",
            "ADA@EXAMPLE.TEST");
        var duplicate = new IdentityProcessProbeUser(
            original.TenantId,
            "user-duplicate",
            "ada-duplicate",
            original.NormalizedUserName,
            "ada-duplicate@example.test",
            "ADA-DUPLICATE@EXAMPLE.TEST");
        var state = new IdentityProcessProbeState(runtime.ConnectionString);
        var runner = new IdentityProcessProbeRunner();

        var created = await runner.RunAsync(
            provider,
            suffix,
            IdentityProcessProbeOperation.CreateUser,
            original,
            state);
        var found = await runner.RunAsync(
            provider,
            suffix,
            IdentityProcessProbeOperation.FindByNormalizedUserName,
            original,
            state);
        var rejected = await runner.RunAsync(
            provider,
            suffix,
            IdentityProcessProbeOperation.DuplicateCreate,
            duplicate,
            state);

        var originalIdDigest = IdentityProcessProbeProtocol.ComputeSha256(original.UserId);
        Assert.Equal("created", created.Outcome);
        Assert.Equal("found", found.Outcome);
        Assert.Equal("duplicate-rejected", rejected.Outcome);
        Assert.Equal("DuplicateUserName", rejected.ErrorCode);
        Assert.Equal(originalIdDigest, created.FoundUserIdSha256);
        Assert.Equal(originalIdDigest, found.FoundUserIdSha256);
        Assert.Equal(originalIdDigest, rejected.FoundUserIdSha256);
        Assert.Equal(1, created.DocumentVersion);
        Assert.Equal(1, found.DocumentVersion);
        Assert.Equal(1, rejected.DocumentVersion);
        Assert.Equal(3, new[] { created.ProcessId, found.ProcessId, rejected.ProcessId }.Distinct().Count());
    }

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Public_v2_identity_exercises_framework_relationship_and_tenant_contracts(string provider)
    {
        var connectionEnvironmentVariable = GroundworkV2ProviderRuntime.ConnectionEnvironmentVariable(provider);
        var configured = connectionEnvironmentVariable is null
            ? null
            : Environment.GetEnvironmentVariable(connectionEnvironmentVariable);
        Skip.If(
            provider != "sqlite" && string.IsNullOrWhiteSpace(configured) && !GroundworkV2ProviderRuntime.IsCi,
            $"Set {connectionEnvironmentVariable} locally, or run the matrix in CI.");
        await using var runtime = await GroundworkV2ProviderRuntime.CreateAsync(provider, configured);

        await AspNetCoreIdentityNativeProviderScenario.RunAsync(provider, runtime.CreateConnection);
    }

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Public_v2_identity_schema_matches_all_declared_units_and_indexes_after_reopen(string provider)
    {
        var connectionEnvironmentVariable = GroundworkV2ProviderRuntime.ConnectionEnvironmentVariable(provider);
        var configured = connectionEnvironmentVariable is null
            ? null
            : Environment.GetEnvironmentVariable(connectionEnvironmentVariable);
        Skip.If(
            provider != "sqlite" && string.IsNullOrWhiteSpace(configured) && !GroundworkV2ProviderRuntime.IsCi,
            $"Set {connectionEnvironmentVariable} locally, or run the matrix in CI.");
        await using var runtime = await GroundworkV2ProviderRuntime.CreateAsync(provider, configured);
        var units = IdentityV2StorageManifest.CreateUnits();

        using (var connection = runtime.CreateConnection())
        {
            foreach (var unit in units)
            {
                connection.Schema.Apply(unit);
                Assert.True(connection.Schema.Diff(unit).IsEmpty, $"{provider}:{unit.Id.Value} did not reach its declaration.");
                Assert.Equal(
                    unit.Indexes.Select(index => index.Name).Order(StringComparer.Ordinal),
                    connection.Catalog.ReadIndexes(unit.Id).Select(index => index.Name).Order(StringComparer.Ordinal));
            }
        }

        using var reopened = runtime.CreateConnection();
        foreach (var unit in units)
        {
            Assert.True(reopened.Schema.Diff(unit).IsEmpty, $"{provider}:{unit.Id.Value} drifted after reopen.");
            Assert.Equal(
                unit.Indexes.Select(index => index.Name).Order(StringComparer.Ordinal),
                reopened.Catalog.ReadIndexes(unit.Id).Select(index => index.Name).Order(StringComparer.Ordinal));
        }
    }
}
