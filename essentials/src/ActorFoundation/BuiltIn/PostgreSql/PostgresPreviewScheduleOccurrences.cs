using Kernel.Durable;
using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Postgres;

public static partial class PostgresPreviewHosting
{
    /// <summary>Binds the trusted occurrence relay to an installed tenant, workflow type and schedule actor type.</summary>
    /// <remarks>Shared requests must come from the same database factory and primary actor type.</remarks>
    public static async Task<Contract.IScheduleOccurrenceAdmissions> OpenScheduleOccurrencesAsync(Func<KernelDb> create, Contract.BoundScope tenant,
        string workflowType, string scheduleActorType, PostgresDirectRequests? requests = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(tenant);
        PostgresRootAdmission.RequireActorType(workflowType);
        RootScheduleInventory.RequireActorType(scheduleActorType);
        if (workflowType == scheduleActorType)
            throw new ArgumentException("The workflow type must differ from the schedule actor type.", nameof(scheduleActorType));
        requests?.RequireScheduleBinding(create, workflowType);
        var pressure = requests?.Pressure;
        bool usesPressure;
        await using (var db = create())
        {
            if (!db.Database.IsNpgsql() || !db.UsesRootScheduleSettlement)
                throw new NotSupportedException("The PostgreSQL store profile does not admit scheduled workflow occurrences.");
            await PostgresRootAdmission.VerifyScheduleOccurrenceStoreAsync(db, cancellationToken);
            usesPressure = db.UsesRootDueBacklog;
        }
        if (usesPressure)
        {
            if (pressure is null)
                pressure = await EfStore.OpenStandalonePostgresPressureAsync(create, cancellationToken);
            else
                await new EfStore(create, postgres: true) { AdmissionPressure = pressure }.PrimeAdmissionPressureAsync(cancellationToken);
        }
        return new ScheduleOccurrenceAdmissions(new PostgresRootAdmission(create, TimeSpan.Zero,
            pressure: pressure, scheduleActorType: scheduleActorType), tenant, workflowType);
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

        public ValueTask<bool> ConfirmRetiredAsync(Contract.AcceptedScheduleOccurrence occurrence, Contract.StartSpec workflow,
            Contract.ActorRef actor, CancellationToken cancellationToken = default) =>
            new(admission.IsScheduledOccurrenceRetiredAsync(scope.Tenant, RootScheduleOccurrence.FromContract(occurrence),
                RootScheduleOccurrence.WorkflowStart(workflow, workflowType), actor.Id, cancellationToken, requireLineage: true));
    }
}
