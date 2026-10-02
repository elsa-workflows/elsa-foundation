using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Workflows.Features;
using Elsa.Secrets.Workflows.Tests.Support;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Secrets.Workflows.Tests;

/// <summary>
/// The bridge passes the runtime's tenant and reference to the Secrets module unchanged and maps its answer: the value
/// on success, and on failure a runtime code and classification per Secrets failure code, never the store's text
/// (spec 188, T025 and T036). The runtime copies the classification into the fault unchanged, so FR-003's rule
/// (<c>StoreUnavailable</c> transient, every other code permanent) rests on this mapping. The theory here covers every
/// <see cref="SecretResolutionFailureCode"/> member; <see cref="SecretFailureIntegrationTests"/> covers the states the
/// Secrets module can reach end to end.
/// </summary>
public sealed class SecretValueRuntimeResolverTests
{
    /// <summary>
    /// What each Secrets failure code becomes in the runtime. A member added to the enum later has no row, so the theory
    /// fails until someone classifies it: the mapping's switch has no compiler check for a missing member.
    /// </summary>
    private static readonly IReadOnlyDictionary<SecretResolutionFailureCode, (string Code, bool IsRetryable)> Classification =
        new Dictionary<SecretResolutionFailureCode, (string, bool)>
        {
            // A failed result that names no code breaks the Secrets resolver's own result shape.
            [SecretResolutionFailureCode.None] = ("CorruptState", false),
            [SecretResolutionFailureCode.NotFound] = ("NotFound", false),
            [SecretResolutionFailureCode.Inactive] = ("Inactive", false),
            [SecretResolutionFailureCode.Expired] = ("Expired", false),
            [SecretResolutionFailureCode.Revoked] = ("Revoked", false),
            [SecretResolutionFailureCode.Deleted] = ("Deleted", false),
            [SecretResolutionFailureCode.TypeMismatch] = ("TypeMismatch", false),
            [SecretResolutionFailureCode.ScopeMismatch] = ("ScopeMismatch", false),
            [SecretResolutionFailureCode.StoreUnavailable] = ("StoreUnavailable", true),
            [SecretResolutionFailureCode.Unauthorized] = ("Unauthorized", false),
            [SecretResolutionFailureCode.CorruptState] = ("CorruptState", false)
        };

    /// <summary>
    /// Shaped like a code name, so carrying it into the result would pass <see cref="RuntimeSecretResolution"/>'s own
    /// shape check: only the bridge dropping <see cref="ResolvedSecret.Error"/> keeps it out of a fault message.
    /// </summary>
    private const string StoreDetail = "StoreDetailSentinel";

    private const string TenantId = "tenant-alpha";
    private readonly SecretResolutionRecorder _requests = new();
    private readonly RecordingSecretValueResolver _secrets;
    private readonly SecretValueRuntimeResolver _resolver;

    public SecretValueRuntimeResolverTests()
    {
        _secrets = new(_requests);
        _resolver = new(_secrets);
    }

    public static TheoryData<SecretResolutionFailureCode> FailureCodes => new(Enum.GetValues<SecretResolutionFailureCode>());

    [Theory]
    [InlineData("rsa-key", "billing")]
    [InlineData(null, null)]
    public async Task The_tenant_and_the_reference_reach_the_Secrets_module_unchanged(string? typeName, string? scope)
    {
        var reference = new RuntimeSecretReference("payments.api-key", typeName, scope);
        using var cancellation = new CancellationTokenSource();

        await _resolver.ResolveAsync(new RuntimeSecretResolutionRequest(TenantId, reference), cancellation.Token);

        var request = Assert.Single(_requests.Requests);
        Assert.Equal(TenantId, request.TenantId);
        Assert.Equal(new SecretReference(reference.Name, reference.TypeName, reference.Scope), request.Reference);
        Assert.Equal(cancellation.Token, request.CancellationToken);
    }

    [Fact]
    public async Task A_success_maps_the_value()
    {
        _secrets.Answer = ResolvedSecret.Success("resolved-value", new SecretMetadata());

        var resolution = await ResolveAsync();

        Assert.True(resolution.Succeeded);
        Assert.Equal("resolved-value", resolution.Value);
        Assert.Null(resolution.FailureCode);
    }

    [Theory]
    [MemberData(nameof(FailureCodes))]
    public async Task Every_failure_code_maps_to_its_runtime_code_and_classification_without_the_store_text(SecretResolutionFailureCode code)
    {
        Assert.True(Classification.TryGetValue(code, out var expected), $"Secrets failure code '{code}' has no runtime classification.");
        _secrets.Answer = ResolvedSecret.Failure(code, StoreDetail);

        var resolution = await ResolveAsync();

        // The printed outcome includes the failure code, the only text of a failure that reaches a fault message.
        Assert.DoesNotContain(StoreDetail, resolution.ToString(), StringComparison.Ordinal);
        Assert.False(resolution.Succeeded);
        Assert.Null(resolution.Value);
        Assert.Equal(expected.Code, resolution.FailureCode);
        Assert.Equal(expected.IsRetryable, resolution.IsRetryable);
    }

    [Theory]
    [InlineData("undefined failure code")]
    [InlineData("success without a value")]
    [InlineData("no result")]
    public async Task A_malformed_result_is_permanent_corrupt_state_rather_than_a_throw(string shape)
    {
        _secrets.Answer = shape switch
        {
            "undefined failure code" => ResolvedSecret.Failure((SecretResolutionFailureCode)int.MaxValue, StoreDetail),
            "success without a value" => new ResolvedSecret { Succeeded = true },
            _ => null
        };

        var resolution = await ResolveAsync();

        Assert.True(
            resolution is { Succeeded: false, FailureCode: nameof(SecretResolutionFailureCode.CorruptState), IsRetryable: false },
            $"The malformed result '{shape}' must resolve to permanent {nameof(SecretResolutionFailureCode.CorruptState)}, not {resolution}.");
    }

    [Fact]
    public void The_bridge_registers_no_runtime_contract_but_the_secret_resolver()
    {
        var services = new ServiceCollection();

        new SecretsWorkflowsFeature().ConfigureServices(services);

        // No type-domain contract or any other runtime seam rides along (research R11): the resolver is the bridge's only
        // contribution to the runtime.
        Assert.Equal([typeof(IRuntimeSecretResolver)], services.Select(descriptor => descriptor.ServiceType));
    }

    private Task<RuntimeSecretResolution> ResolveAsync() =>
        _resolver.ResolveAsync(new RuntimeSecretResolutionRequest(TenantId, new RuntimeSecretReference("payments.api-key"))).AsTask();
}
