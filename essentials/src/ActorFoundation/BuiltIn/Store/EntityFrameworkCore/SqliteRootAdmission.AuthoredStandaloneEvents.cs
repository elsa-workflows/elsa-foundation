using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

internal sealed partial class SqliteRootAdmission
{
    internal Task<IReadOnlyList<Contract.AuthoredStandaloneEvent>> ReadAuthoredStandaloneEventsAsync(Contract.BoundScope scope,
        Contract.AuthoredStandaloneEventCursor? after, CancellationToken token) =>
        InAdmissionTransactionAsync((db, ct) => RootAuthoredStandaloneEvents.ReadAsync(db, scope, after, bind: null, ct), token,
            readOnly: true);

    internal Task<bool> AcknowledgeAuthoredStandaloneEventAsync(Contract.BoundScope scope, string occurrence,
        string fingerprint, CancellationToken token) =>
        InAdmissionTransactionAsync((db, ct) =>
            RootAuthoredStandaloneEvents.AcknowledgeAsync(db, scope, occurrence, fingerprint, bind: null, ct), token);
}
