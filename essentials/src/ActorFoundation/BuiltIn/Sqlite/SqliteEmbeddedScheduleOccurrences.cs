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
        public ValueTask<Contract.Admission> AdmitAsync(Contract.AcceptedScheduleOccurrence occurrence, Contract.StartSpec workflow,
            CancellationToken cancellationToken = default) =>
            SqliteEmbeddedHosting.AdmitScheduledOccurrenceAsync(databasePath, scope, workflowType, scheduleActorType, requests,
                RootScheduleOccurrence.FromContract(occurrence), RootScheduleOccurrence.WorkflowStart(workflow, workflowType), cancellationToken);
    }
}

public static partial class SqliteEmbeddedHosting
{
    internal static async ValueTask<Contract.Admission> AdmitScheduledOccurrenceAsync(string databasePath, Contract.BoundScope scope,
        string workflowType, string scheduleActorType, SqliteDirectRequests requests, RootScheduleOccurrence occurrence,
        StartSpec workflow, CancellationToken cancellationToken)
    {
        try
        {
            using var file = AcquireFile(databasePath, requests: requests);
            requests.RequireBinding(file.Path, workflowType);
            var admission = new SqliteRootAdmission(() => KernelDb.ForSqliteRootShipped(file.Options, requests.QualificationFormat),
                requests.DeduplicationWindow, file.AdmissionDefinitions, requests.Pressure, scheduleActorType);
            return await admission.AdmitScheduledOccurrenceAsync(scope.Tenant, occurrence, workflow, cancellationToken);
        }
        catch (RootAdmissionRefused refusal) { return refusal.Admission; }
        catch (SqliteException error) when (error.SqliteErrorCode is 5 or 6)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("SQLite occurrence admission could not acquire its write lock.", error);
        }
    }
}
