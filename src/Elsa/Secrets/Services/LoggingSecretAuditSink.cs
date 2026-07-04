using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Events;
using Microsoft.Extensions.Logging;

namespace Elsa.Secrets.Services;

/// <summary>
/// Default <see cref="ISecretAuditSink"/> that writes audit records to the application log so that
/// secret lifecycle activity is visible out of the box instead of being silently discarded.
/// <para>
/// Only non-sensitive audit metadata is logged (operation, secret name, outcome, actor and reason);
/// <see cref="SecretOperationAuditRecord"/> never carries secret values.
/// </para>
/// <para>
/// Because this sink only writes to the log, the first record it handles also emits a one-time
/// warning so operators are told that no dedicated audit sink has been configured. Registering a
/// custom <see cref="ISecretAuditSink"/> replaces this sink and suppresses the warning.
/// </para>
/// </summary>
public sealed class LoggingSecretAuditSink(ILogger<LoggingSecretAuditSink> logger) : ISecretAuditSink
{
    private int _warned;

    public ValueTask RecordAsync(SecretOperationAuditRecord record, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _warned, 1) == 0)
        {
            logger.LogWarning(
                "Secret auditing is using the default logging sink. Audit events are only written to the application log; register a dedicated ISecretAuditSink to persist the secrets audit trail.");
        }

        logger.LogInformation(
            "Secret audit: operation={Operation} secret={SecretName} outcome={Outcome} actor={ActorId} reason={Reason} at {Timestamp:o}",
            record.Operation,
            record.SecretName,
            record.Outcome,
            record.ActorId,
            record.Reason,
            record.Timestamp);

        return ValueTask.CompletedTask;
    }
}
