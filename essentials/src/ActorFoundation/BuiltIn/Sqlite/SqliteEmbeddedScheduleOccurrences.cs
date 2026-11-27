using Kernel.Durable;
using Kernel.EntityFramework;
using Microsoft.Data.Sqlite;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Sqlite;

public sealed partial class SqliteEmbeddedFoundation
{
    /// <summary>Binds the trusted occurrence relay to this installed file and tenant.</summary>
    public Contract.IScheduleOccurrenceAdmissions BindScheduleOccurrences(Contract.BoundScope tenant, string scheduleActorType)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        RootScheduleInventory.RequireActorType(scheduleActorType);
        if (scheduleActorType == actorType)
            throw new ArgumentException("The workflow type must differ from the schedule actor type.", nameof(scheduleActorType));
        return new ScheduleOccurrenceAdmissions(InstalledPath, tenant, actorType, scheduleActorType, requests.Value);
    }

    private sealed class ScheduleOccurrenceAdmissions(string databasePath, Contract.BoundScope scope, string workflowType,
        string scheduleActorType, SqliteEmbeddedHosting.SqliteDirectRequests requests) : Contract.IScheduleOccurrenceAdmissions
    {
        public async ValueTask<Contract.Admission> AdmitAsync(Contract.AcceptedScheduleOccurrence occurrence, Contract.StartSpec workflow,
            CancellationToken cancellationToken = default)
        {
            var selected = RootScheduleOccurrence.FromContract(occurrence);
            var start = RootScheduleOccurrence.WorkflowStart(workflow, workflowType);
            try
            {
                return await WithStoreAsync(admission => admission.AdmitScheduledOccurrenceAsync(scope.Tenant, selected, start, cancellationToken),
                    cancellationToken);
            }
            catch (RootAdmissionRefused refusal) { return refusal.Admission; }
        }

        public ValueTask<bool> ConfirmRetiredAsync(Contract.AcceptedScheduleOccurrence occurrence, Contract.StartSpec workflow,
            Contract.ActorRef actor, CancellationToken cancellationToken = default)
        {
            var selected = RootScheduleOccurrence.FromContract(occurrence);
            var start = RootScheduleOccurrence.WorkflowStart(workflow, workflowType);
            return WithStoreAsync(admission => admission.IsScheduledOccurrenceRetiredAsync(scope.Tenant, selected, start, actor.Id,
                cancellationToken, requireLineage: true), cancellationToken);
        }

        private ValueTask<T> WithStoreAsync<T>(Func<SqliteRootAdmission, Task<T>> operation, CancellationToken token) =>
            SqliteEmbeddedHosting.WithScheduleOccurrenceStoreAsync(databasePath, workflowType, scheduleActorType, requests, operation, token);
    }
}

public static partial class SqliteEmbeddedHosting
{
    internal static async ValueTask<T> WithScheduleOccurrenceStoreAsync<T>(string databasePath, string workflowType,
        string scheduleActorType, SqliteDirectRequests requests, Func<SqliteRootAdmission, Task<T>> operation, CancellationToken cancellationToken)
    {
        try
        {
            using var file = AcquireFile(databasePath, requests: requests);
            requests.RequireBinding(file.Path, workflowType);
            var admission = new SqliteRootAdmission(() => KernelDb.ForSqliteRootShipped(file.Options, requests.QualificationFormat),
                requests.DeduplicationWindow, file.AdmissionDefinitions, requests.Pressure, scheduleActorType);
            return await operation(admission);
        }
        catch (SqliteException error) when (error.SqliteErrorCode is 5 or 6)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("SQLite occurrence access could not acquire its store lock.", error);
        }
    }
}
