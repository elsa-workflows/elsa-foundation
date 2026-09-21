using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Events;

namespace Elsa.Secrets.Services;

public sealed class NullSecretAuditSink : ISecretAuditSink
{
    public ValueTask RecordAsync(SecretOperationAuditRecord record, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}
