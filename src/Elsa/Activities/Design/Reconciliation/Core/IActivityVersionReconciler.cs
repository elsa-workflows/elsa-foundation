namespace Elsa.Activities.Design.Reconciliation.Core;

/// <summary>
/// Idempotent reconciliation lifecycle for the activity catalog. Each pass publishes
/// <see cref="OnActivityVersionsReconciling"/> to gather candidate versions from source
/// modules, then upserts the catalog. Provisioning is one trigger of this lifecycle —
/// reconciliation is the broader concept (Sipke item 6).
/// </summary>
public interface IActivityVersionReconciler
{
    Task Reconcile(CancellationToken cancellationToken);
}
