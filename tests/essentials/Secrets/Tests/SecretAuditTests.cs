using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Events;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Extensions;
using Elsa.Secrets.Options;
using Elsa.Secrets.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class SecretAuditTests : IDisposable
{
    private const string TenantId = "tenant-1";
    private readonly RecordingSecretAuditSink _auditSink = new();
    private readonly ServiceProvider _provider;
    private readonly ISecretManager _manager;
    private readonly ISecretValueResolver _resolver;

    public SecretAuditTests()
    {
        var services = new ServiceCollection().AddSecrets();
        services.Configure<SecretsOptions>(options => options.EncryptionKey = "audit-test-encryption-key");
        services.RemoveAll<ISecretAuditSink>();
        services.AddSingleton<ISecretAuditSink>(_auditSink);
        _provider = services.BuildServiceProvider();
        _manager = _provider.GetRequiredService<ISecretManager>();
        _resolver = _provider.GetRequiredService<ISecretValueResolver>();
    }

    [Fact]
    public async Task Lifecycle_And_Resolve_Emit_Audit_Records_Without_Values()
    {
        await _manager.CreateAsync(TenantId, new CreateSecretRequest { Name = "payments.api", Value = "audit-secret-value" });
        await _manager.TestAsync(TenantId, "payments.api");
        await _resolver.ResolveAsync(TenantId, new SecretReference("payments.api"));

        Assert.Contains(_auditSink.Records, x => x.Operation == "create" && x.Outcome == "succeeded");
        Assert.Contains(_auditSink.Records, x => x.Operation == "test" && x.Outcome == "succeeded");
        Assert.Contains(_auditSink.Records, x => x.Operation == "resolve" && x.Outcome == "succeeded");
        Assert.All(_auditSink.Records, x => Assert.Equal(TenantId, x.TenantId));
        Assert.DoesNotContain(_auditSink.Records, x => (x.Reason ?? "").Contains("audit-secret-value", StringComparison.Ordinal));
    }

    [Fact]
    public void Default_Audit_Sink_Is_The_Visible_Logging_Sink()
    {
        using var provider = new ServiceCollection().AddSecrets().BuildServiceProvider();

        var sink = provider.GetRequiredService<ISecretAuditSink>();

        Assert.IsType<LoggingSecretAuditSink>(sink);
    }

    [Fact]
    public async Task Failed_Operation_Emits_Failed_Audit_Record_Without_Secret_Material()
    {
        await _manager.CreateAsync(TenantId, new CreateSecretRequest { Name = "dup.secret", Value = "audit-secret-value" });
        _auditSink.Records.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _manager.CreateAsync(TenantId, new CreateSecretRequest { Name = "dup.secret", Value = "audit-secret-value" }));

        var failure = Assert.Single(_auditSink.Records.Where(x => x.Operation == "create" && x.Outcome == "failed"));
        Assert.Equal("dup.secret", failure.SecretName);
        Assert.DoesNotContain("audit-secret-value", failure.Reason ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain("audit-secret-value", failure.SecretName, StringComparison.Ordinal);
    }

    public void Dispose() => _provider.Dispose();

    private sealed class RecordingSecretAuditSink : ISecretAuditSink
    {
        public List<SecretOperationAuditRecord> Records { get; } = [];

        public ValueTask RecordAsync(SecretOperationAuditRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return ValueTask.CompletedTask;
        }
    }
}
