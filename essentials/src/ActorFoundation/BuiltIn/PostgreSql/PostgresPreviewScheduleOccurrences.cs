using Kernel.Durable;
using Kernel.EntityFramework;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Postgres;

public static partial class PostgresPreviewHosting
{
    /// <summary>Binds the trusted occurrence relay to an installed tenant, workflow type and schedule actor type.</summary>
    public static Contract.IScheduleOccurrenceAdmissions BindScheduleOccurrences(Func<KernelDb> create, Contract.BoundScope tenant,
        string workflowType, string scheduleActorType, PostgresDirectRequests? requests = null)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(tenant);
        PostgresRootAdmission.RequireActorType(workflowType);
        RootScheduleInventory.RequireActorType(scheduleActorType);
        if (workflowType == scheduleActorType)
            throw new ArgumentException("The workflow type must differ from the schedule actor type.", nameof(scheduleActorType));
        return new ScheduleOccurrenceAdmissions(new PostgresRootAdmission(create, TimeSpan.Zero,
            pressure: requests?.Pressure, scheduleActorType: scheduleActorType), tenant, workflowType);
    }

    private sealed class ScheduleOccurrenceAdmissions(PostgresRootAdmission admission, Contract.BoundScope scope, string workflowType)
        : Contract.IScheduleOccurrenceAdmissions
    {
        public async ValueTask<Contract.Admission> AdmitAsync(Contract.AcceptedScheduleOccurrence occurrence, Contract.StartSpec workflow,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await admission.AdmitScheduledOccurrenceAsync(scope.Tenant, RootScheduleOccurrence.FromContract(occurrence),
                    RootScheduleOccurrence.WorkflowStart(workflow, workflowType), cancellationToken);
            }
            catch (RootAdmissionRefused refusal) { return refusal.Admission; }
        }
    }
}
