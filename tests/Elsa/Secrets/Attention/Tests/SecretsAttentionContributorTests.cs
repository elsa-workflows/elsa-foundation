using System.Security.Claims;
using Elsa.Attention.Core;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Secrets.Attention.Tests;

public sealed class SecretsAttentionContributorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-13T10:00:00Z");

    [Fact]
    public async Task EvaluatesExpiredRevokedAndConfiguredSoonExpiringConditionsOnce()
    {
        var repository = new StubRepository(
        [
            Secret("expired", SecretStatus.Active, Now.AddDays(-1), "expired-value"),
            Secret("revoked", SecretStatus.Revoked, Now.AddDays(90), "revoked-value"),
            Secret("soon", SecretStatus.Active, Now.AddDays(10), "soon-value"),
            Secret("later", SecretStatus.Active, Now.AddDays(11), "later-value")
        ]);
        var contributor = new SecretsAttentionContributor(repository, new FixedTimeProvider(Now));

        var result = await contributor.EvaluateAsync(Context(1, new Dictionary<string, string> { ["soonExpiringDays"] = "10" }));

        Assert.Equal(1, repository.CallCount);
        Assert.Equal("tenant-1", repository.TenantId);
        Assert.Equal(3, result.TotalCount);
        Assert.False(result.IsTruncated);
        Assert.Equal([AttentionSeverity.Critical, AttentionSeverity.Critical, AttentionSeverity.Warning], result.Items.Select(x => x.Severity));
        Assert.Contains(result.Items, x => x.Id == "secret:expired" && x.Title == "A secret has expired");
        Assert.Contains(result.Items, x => x.Id == "secret:revoked" && x.Title == "A secret has been revoked");
        Assert.Contains(result.Items, x => x.Id == "secret:soon" && x.Title == "A secret expires soon");
        Assert.DoesNotContain(result.Items, x => x.Id == "secret:later");
    }

    [Fact]
    public async Task CompletesEvaluationBeyondNormalPageSizeAndReturnsExactBoundedTotal()
    {
        var secrets = Enumerable.Range(0, 125)
            .Select(index => Secret($"healthy-{index:D3}", SecretStatus.Active, Now.AddDays(90)))
            .Concat(Enumerable.Range(0, 7).Select(index => Secret($"expired-{index:D3}", SecretStatus.Active, Now.AddDays(-index - 1))))
            .ToArray();
        var repository = new StubRepository(secrets);
        var contributor = new SecretsAttentionContributor(repository, new FixedTimeProvider(Now));

        var result = await contributor.EvaluateAsync(Context(1));

        Assert.Equal(1, repository.CallCount);
        Assert.Equal(7, result.TotalCount);
        Assert.True(result.IsTruncated);
        Assert.Equal(5, result.Items.Count);
    }

    [Fact]
    public async Task EmitsOnlyRestrictedAllowlistedMetadataWithStableIdentityGenerationAndDestination()
    {
        var secret = Secret("secret-id", SecretStatus.Revoked, Now.AddDays(90), "credential-value");
        secret.Name = "provider.api-key";
        secret.DisplayName = "Provider API key";
        secret.Description = "credential-description";
        secret.Versions[0].Payload.Metadata["token"] = "payload-metadata";
        var contributor = new SecretsAttentionContributor(new StubRepository([secret]), new FixedTimeProvider(Now));

        var first = Assert.Single((await contributor.EvaluateAsync(Context(1))).Items);
        var second = Assert.Single((await contributor.EvaluateAsync(Context(1))).Items);
        var serialized = System.Text.Json.JsonSerializer.Serialize(first);

        Assert.Equal("secret:secret-id", first.Id);
        Assert.Equal(first.Generation, second.Generation);
        Assert.Equal("/security/secrets", first.Destination.Path);
        Assert.Equal(AttentionSensitivity.Restricted, first.Sensitivity);
        Assert.Contains(first.Correlations, correlation => correlation.Kind == "secret" && correlation.Value == "secret-id");
        Assert.DoesNotContain("credential-value", serialized);
        Assert.DoesNotContain("credential-description", serialized);
        Assert.DoesNotContain("payload-metadata", serialized);
        Assert.DoesNotContain("provider.api-key", serialized);
    }

    [Fact]
    public async Task ChangesGenerationWhenAConditionHasANewOccurrence()
    {
        var secret = Secret("secret-id", SecretStatus.Revoked, Now.AddDays(90));
        var repository = new StubRepository([secret]);
        var contributor = new SecretsAttentionContributor(repository, new FixedTimeProvider(Now));
        var first = Assert.Single((await contributor.EvaluateAsync(Context(1))).Items);

        secret.UpdatedAt = Now.AddMinutes(1);
        var second = Assert.Single((await contributor.EvaluateAsync(Context(1))).Items);

        Assert.NotEqual(first.Generation, second.Generation);
    }

    [Fact]
    public async Task ReportsUnavailableWhenTenantContextOrThresholdIsInvalid()
    {
        var repository = new StubRepository([]);
        var contributor = new SecretsAttentionContributor(repository, new FixedTimeProvider(Now));

        var noTenant = await contributor.EvaluateAsync(new(
            new(new ClaimsPrincipal(), null), new(1), new Dictionary<string, string>()));
        var invalidThreshold = await contributor.EvaluateAsync(Context(1, new Dictionary<string, string> { ["soonExpiringDays"] = "invalid" }));

        Assert.Equal(AttentionContributionStatus.Unavailable, noTenant.Status);
        Assert.Equal("SECRETS_ATTENTION_TENANT_UNAVAILABLE", noTenant.ErrorCode);
        Assert.Equal(AttentionContributionStatus.Unavailable, invalidThreshold.Status);
        Assert.Equal("SECRETS_ATTENTION_CONFIGURATION_INVALID", invalidThreshold.ErrorCode);
        Assert.Equal(0, repository.CallCount);
    }

    [Fact]
    public async Task FailsClosedWhenRepositoryViolatesTenantIsolation()
    {
        var contributor = new SecretsAttentionContributor(
            new StubRepository([Secret("wrong-tenant", SecretStatus.Revoked, Now, tenantId: "tenant-2")]),
            new FixedTimeProvider(Now));

        var result = await contributor.EvaluateAsync(Context(1));

        Assert.Equal(AttentionContributionStatus.Unavailable, result.Status);
        Assert.Equal("SECRETS_ATTENTION_TENANT_SCOPE_INVALID", result.ErrorCode);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task ConsumesOneDownstreamCallAndRequiresSecretsReadPermission()
    {
        var contributor = new SecretsAttentionContributor(new StubRepository([]), new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<AttentionBudgetExceededException>(async () => await contributor.EvaluateAsync(Context(0)));

        Assert.Equal("secrets", contributor.Descriptor.Id);
        Assert.Equal("secrets:read", contributor.Descriptor.RequiredPermission);
        var threshold = Assert.Single(contributor.Descriptor.Thresholds!);
        Assert.Equal("soonExpiringDays", threshold.Key);
        Assert.Equal("30", threshold.DefaultValue);
    }

    [Fact]
    public void FeatureRegistersContributor()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISecretRepository>(new StubRepository([]));
        new SecretsAttentionFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        Assert.Contains(provider.GetServices<IAttentionContributor>(), contributor => contributor is SecretsAttentionContributor);
    }

    private static AttentionContributorContext Context(int calls, IReadOnlyDictionary<string, string>? thresholds = null) => new(
        new(new ClaimsPrincipal(), "tenant-1"),
        new(calls),
        thresholds ?? new Dictionary<string, string> { ["soonExpiringDays"] = "30" });

    private static Secret Secret(
        string id,
        SecretStatus status,
        DateTimeOffset expiresAt,
        string value = "not-exposed",
        string tenantId = "tenant-1") => new()
    {
        Id = id,
        TenantId = tenantId,
        Name = id,
        DisplayName = $"Display {id}",
        Description = $"Description {id}",
        Status = status,
        CreatedAt = Now.AddDays(-60),
        UpdatedAt = status == SecretStatus.Revoked ? Now.AddHours(-1) : null,
        Versions =
        [
            new()
            {
                Version = 1,
                Status = status == SecretStatus.Revoked ? SecretStatus.Revoked : SecretStatus.Active,
                CreatedAt = Now.AddDays(-60),
                ExpiresAt = expiresAt,
                Payload = new() { Value = value }
            }
        ]
    };

    private sealed class StubRepository(IReadOnlyCollection<Secret> secrets) : ISecretRepository
    {
        public int CallCount { get; private set; }
        public string? TenantId { get; private set; }

        public ValueTask<IReadOnlyCollection<Secret>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            CallCount++;
            TenantId = tenantId;
            return ValueTask.FromResult(secrets);
        }

        public ValueTask<Secret?> FindAsync(string tenantId, string normalizedName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> TryAddAsync(Secret secret, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask SaveAsync(Secret secret, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
