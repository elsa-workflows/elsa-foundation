using Elsa.Secrets.Core.Events;

namespace Elsa.Secrets.Core.Contracts;

public interface ISecretAuditSink
{
    ValueTask RecordAsync(SecretOperationAuditRecord record, CancellationToken cancellationToken = default);
}
