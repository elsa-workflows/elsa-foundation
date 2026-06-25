using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Events;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class SecretAuditTests : IDisposable
{
    private readonly RecordingSecretAuditSink _auditSink = new();
    private readonly ServiceProvider _provider;
    private readonly ISecretManager _manager;
    private readonly ISecretResolver _resolver;

    public SecretAuditTests()
    {
        var services = new ServiceCollection().AddSecrets();
        services.RemoveAll<ISecretAuditSink>();
        services.AddSingleton<ISecretAuditSink>(_auditSink);
        _provider = services.BuildServiceProvider();
        _manager = _provider.GetRequiredService<ISecretManager>();
        _resolver = _provider.GetRequiredService<ISecretResolver>();
    }

    [Fact]
    public async Task Lifecycle_And_Resolve_Emit_Audit_Records_Without_Values()
    {
        await _manager.CreateAsync(new CreateSecretRequest { Name = "payments.api", Value = "audit-secret-value" });
        await _manager.TestAsync("payments.api");
        await _resolver.ResolveAsync(new SecretReference("payments.api"));

        Assert.Contains(_auditSink.Records, x => x.Operation == "create" && x.Outcome == "succeeded");
        Assert.Contains(_auditSink.Records, x => x.Operation == "test" && x.Outcome == "succeeded");
        Assert.Contains(_auditSink.Records, x => x.Operation == "resolve" && x.Outcome == "succeeded");
        Assert.DoesNotContain(_auditSink.Records, x => (x.Reason ?? "").Contains("audit-secret-value", StringComparison.Ordinal));
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
