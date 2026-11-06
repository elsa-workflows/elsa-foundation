using Elsa.Activities.Bpmn.Core;
using System.Text.Json;
using Elsa.Jobs.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Extensions.DependencyInjection;

internal sealed record ContinuationCutOptions(string JobId, string MarkerFile);
internal sealed record GrantReadFaultOptions(string JobId, string WorkerExecutionId, string Fault, string MarkerFile);
internal sealed record AdmissionCutOptions(string MarkerFile);

// Qualification-only observers of public contracts. All writes first reach the real selected provider;
// adverse reads change only the response to an actual live BPMN result validator, never stored state.
internal sealed class SupplementalJobsControl(ContinuationCutOptions? cut, GrantReadFaultOptions? fault, AdmissionCutOptions? admissionCut)
{
    private int _cutEntered;
    private int _validatorEntered;
    private int _faultReads;
    private int _admissionEntered;
    internal PostCommitDeliveryContext? CurrentSource { get; set; }
    internal async ValueTask AfterAdmissionAsync(JobAdmission request, JobAdmissionResult result, CancellationToken ct)
    {
        if (admissionCut is null || result.Status != JobCommandStatus.Applied) return;
        var address = request.Request.Association?.Continuation is { } continuation
            ? continuation.Value.Deserialize<Elsa.Jobs.Workflows.Runtime.Models.JobNativeAddress>(ModelProbe.Web) : null;
        if (request.Origin is null || result.Acceptance is null || CurrentSource is not { } source ||
            source.Scope.Value != request.Scope.PersistencePartition || source.Claim.Item.StimulusDeliveryProtocolVersion is not null ||
            address is null || source.Intent.WorkflowExecutionId != address.WorkflowExecutionId ||
            Interlocked.CompareExchange(ref _admissionEntered, 1, 0) != 0)
            throw new InvalidDataException("The admission cut needs one actual durable-origin acceptance from its real source context.");
        try
        {
            await Probe.WriteAsync(admissionCut.MarkerFile, new { state = "AcceptedBeforeSenderAck", occurrence = 1,
                processId = Environment.ProcessId, admission = request, acceptance = result.Acceptance, source });
            await Task.Delay(TimeSpan.FromSeconds(90), ct);
            await Probe.WriteAsync(admissionCut.MarkerFile + ".timed-out.json", new { state = "TimedOut", processId = Environment.ProcessId });
            throw new TimeoutException("The actual accepted sender was not killed before its source acknowledgement.");
        }
        catch (Exception error)
        {
            // Persist independently of the failed delivery token. A later replay cannot erase this cut's loss.
            await Probe.WriteAsync(admissionCut.MarkerFile + ".aborted.json", new { state = "Aborted", processId = Environment.ProcessId,
                occurrence = 1, exceptionType = error.GetType().Name, admission = request, acceptance = result.Acceptance, source });
            throw;
        }
    }
    internal bool AlterGrant(string jobId) => fault is not null && fault.JobId == jobId && Volatile.Read(ref _validatorEntered) == 1;
    internal async ValueTask AfterGrantAsync(JobContinuationRequest request, JobContinuationResult result, CancellationToken ct)
    {
        if (cut is null || request.JobId != cut.JobId || result.Status != JobCommandStatus.Applied) return;
        if (result.Permission is not { } permission || permission.JobId != request.JobId || permission.ResultId != request.ResultId ||
            permission.ContinuationId != request.ContinuationId || permission.ModelInputId != request.ModelInputId ||
            Interlocked.CompareExchange(ref _cutEntered, 1, 0) != 0)
            throw new InvalidDataException("The continuation cut did not observe one exact actual durable grant.");
        try
        {
            await Probe.WriteAsync(cut.MarkerFile, new { state = "DurablyGrantedBeforeDispatch", occurrence = 1,
                processId = Environment.ProcessId, request, permission });
            await Task.Delay(TimeSpan.FromSeconds(90), ct);
            await Probe.WriteAsync(cut.MarkerFile + ".timed-out.json", new { state = "TimedOut", processId = Environment.ProcessId, request });
            throw new TimeoutException("The continuation cut was not terminated at its actual committed grant.");
        }
        catch (Exception error)
        {
            await Probe.WriteAsync(cut.MarkerFile + ".aborted.json", new { state = "Aborted", processId = Environment.ProcessId,
                occurrence = 1, exceptionType = error.GetType().Name, request, permission });
            throw;
        }
    }
    internal ValueTask ObserveValidationAsync(BpmnNativeWorkResultValidation context, CancellationToken ct)
    {
        if (fault is null || context.Worker.WorkflowExecutionId != fault.WorkerExecutionId) return ValueTask.CompletedTask;
        ct.ThrowIfCancellationRequested();
        if (context.InputId != context.Origin.ResultInputId || !context.Worker.Status.IsTerminal())
            throw new InvalidDataException("The adverse read was not reached from an exact live terminal-worker result validation.");
        Interlocked.Exchange(ref _validatorEntered, 1);
        return new(Probe.WriteAsync(fault.MarkerFile + ".validator.json", new { state = "ActualLiveResultValidation", processId = Environment.ProcessId,
            context.InputId, context.ContentFingerprint, context.Origin, workerExecutionId = context.Worker.WorkflowExecutionId, context.Result }));
    }
    internal async ValueTask<JobContinuationPermission?> AlterAsync(JobScope scope, string jobId, string continuationId,
        JobContinuationPermission? actual)
    {
        if (!AlterGrant(jobId)) return actual;
        if (actual is null || actual.JobId != jobId || actual.ContinuationId != continuationId)
            throw new InvalidDataException("The adverse control requires an actual retained provider grant.");
        var returned = fault!.Fault switch
        {
            "missing" => null,
            "wrong-result" => actual with { ResultId = "qualification-forged-result" },
            _ => throw new InvalidDataException("Unknown bounded grant-read fault.")
        };
        await Probe.WriteAsync(fault.MarkerFile, new { state = "AdverseReadReturned", processId = Environment.ProcessId,
            occurrence = Interlocked.Increment(ref _faultReads), scope, jobId, continuationId, actual, returned,
            validationObserved = true, fault = fault.Fault });
        return returned;
    }
    internal static void Register(IServiceCollection services, ContinuationCutOptions? cut, GrantReadFaultOptions? fault, AdmissionCutOptions? admissionCut)
    {
        if (cut is null && fault is null && admissionCut is null) return;
        if (new object?[] { cut, fault, admissionCut }.Count(value => value is not null) != 1) throw new InvalidDataException("Choose a single supplemental intervention.");
        if (cut is not null && (string.IsNullOrWhiteSpace(cut.JobId) || File.Exists(cut.MarkerFile)) ||
            fault is not null && (fault.Fault is not ("missing" or "wrong-result") || string.IsNullOrWhiteSpace(fault.JobId) ||
                string.IsNullOrWhiteSpace(fault.WorkerExecutionId) || File.Exists(fault.MarkerFile)))
            throw new InvalidDataException("The supplemental observer requires exact identities and fresh evidence.");
        var commands = services.Single(item => item.ServiceType == typeof(IJobCommands));
        var queries = services.Single(item => item.ServiceType == typeof(IJobQueries));
        if (commands.ImplementationFactory is not { } factory || commands.Lifetime != ServiceLifetime.Scoped || queries.Lifetime != ServiceLifetime.Scoped)
            throw new InvalidDataException("The supplemental observer requires the selected scoped Jobs provider factory.");
        var control = new SupplementalJobsControl(cut, fault, admissionCut);
        services.AddSingleton(control);
        services.Remove(commands); services.Remove(queries);
        services.AddScoped<SupplementalJobsStore>(provider =>
        {
            var original = (IJobCommands)factory(provider);
            return new(original, original as IJobQueries ?? throw new InvalidDataException("The selected Jobs provider must retain its public reader surfaces."), control);
        });
        services.AddScoped<IJobCommands>(provider => provider.GetRequiredService<SupplementalJobsStore>());
        services.AddScoped<IJobQueries>(provider => provider.GetRequiredService<SupplementalJobsStore>());
        if (admissionCut is not null)
        {
            if (File.Exists(admissionCut.MarkerFile)) throw new InvalidDataException("The sender cut needs fresh evidence.");
            var descriptor = services.Single(item => item.ServiceType == typeof(IRuntimePostCommitIntentDispatcher));
            if (descriptor.ImplementationType != typeof(RuntimePostCommitIntentDispatcher) || descriptor.Lifetime != ServiceLifetime.Scoped)
                throw new InvalidDataException("The sender observer requires the ordinary selected public scoped intent dispatcher.");
            services.Remove(descriptor);
            services.AddScoped<IRuntimePostCommitIntentDispatcher>(provider => new SupplementalSourceObserver(
                new RuntimePostCommitIntentDispatcher(provider.GetServices<RuntimePostCommitIntentHandlerContribution>(), provider), control));
        }
        if (fault is not null)
        {
            var index = services.ToList().FindIndex(item => item.ServiceType == typeof(IBpmnNativeWorkResultValidator));
            if (index < 0) throw new InvalidDataException("The real Jobs BPMN result validator is required before its adverse control.");
            services.Insert(index, ServiceDescriptor.Singleton<IBpmnNativeWorkResultValidator>(new SupplementalValidationObserver(control)));
        }
    }
}

internal sealed class SupplementalValidationObserver(SupplementalJobsControl control) : IBpmnNativeWorkResultValidator
{
    public ValueTask ValidateAsync(BpmnNativeWorkResultValidation context, CancellationToken cancellationToken = default) =>
        control.ObserveValidationAsync(context, cancellationToken);
}

internal sealed class SupplementalJobsStore(IJobCommands commands, IJobQueries queries, SupplementalJobsControl control) : IJobCommands, IJobQueries
{
    public async ValueTask<JobAdmissionResult> AdmitAsync(JobAdmission admission, CancellationToken cancellationToken = default)
    {
        var result = await commands.AdmitAsync(admission, cancellationToken);
        await control.AfterAdmissionAsync(admission, result, cancellationToken);
        return result;
    }
    public async ValueTask<JobContinuationResult> GrantContinuationAsync(JobContinuationRequest request, CancellationToken cancellationToken = default)
    {
        var result = await commands.GrantContinuationAsync(request, cancellationToken);
        await control.AfterGrantAsync(request, result, cancellationToken);
        return result;
    }
    public async ValueTask<JobContinuationPermission?> FindContinuationAsync(JobScope scope, string jobId, string continuationId, CancellationToken cancellationToken = default) =>
        await control.AlterAsync(scope, jobId, continuationId, await queries.FindContinuationAsync(scope, jobId, continuationId, cancellationToken));
    public ValueTask<JobPayloadCleanupResult> ExpirePayloadsAsync(JobPayloadCleanupRequest request, CancellationToken cancellationToken = default) => commands.ExpirePayloadsAsync(request, cancellationToken);
    public ValueTask<JobClaimResult> ClaimAsync(JobClaimRequest request, CancellationToken cancellationToken = default) => commands.ClaimAsync(request, cancellationToken);
    public ValueTask<JobRenewal> RenewAsync(JobAttemptLease lease, TimeSpan leaseDuration, CancellationToken cancellationToken = default) => commands.RenewAsync(lease, leaseDuration, cancellationToken);
    public ValueTask<JobTransition> RecordPhysicalStartAsync(JobAttemptLease lease, CancellationToken cancellationToken = default) => commands.RecordPhysicalStartAsync(lease, cancellationToken);
    public ValueTask<JobTransition> CompleteAsync(JobAttemptLease lease, JobPhysicalReceipt receipt, CancellationToken cancellationToken = default) => commands.CompleteAsync(lease, receipt, cancellationToken);
    public ValueTask<JobTransition> RecordPhysicalReceiptAsync(JobScope scope, string jobId, JobPhysicalReceipt receipt, CancellationToken cancellationToken = default) => commands.RecordPhysicalReceiptAsync(scope, jobId, receipt, cancellationToken);
    public ValueTask<JobTransition> RecordProgressAsync(JobAttemptLease lease, JobProgress progress, CancellationToken cancellationToken = default) => commands.RecordProgressAsync(lease, progress, cancellationToken);
    public ValueTask<JobTransition> CancelAsync(JobCancelRequest request, CancellationToken cancellationToken = default) => commands.CancelAsync(request, cancellationToken);
    public ValueTask<JobTransition> CancelOriginAsync(JobCancelOriginRequest request, CancellationToken cancellationToken = default) => commands.CancelOriginAsync(request, cancellationToken);
    public ValueTask<JobAdmissionResult> RetryAsNewRunAsync(JobRetryAsNewRunRequest request, CancellationToken cancellationToken = default) => commands.RetryAsNewRunAsync(request, cancellationToken);
    public ValueTask<JobTransition> PauseAsync(JobPauseRequest request, CancellationToken cancellationToken = default) => commands.PauseAsync(request, cancellationToken);
    public ValueTask<JobTransition> ReleaseHoldAsync(JobReleaseHoldRequest request, CancellationToken cancellationToken = default) => commands.ReleaseHoldAsync(request, cancellationToken);
    public ValueTask<JobTransition> SetCapacityAsync(JobSetCapacityRequest request, CancellationToken cancellationToken = default) => commands.SetCapacityAsync(request, cancellationToken);
    public ValueTask<JobTransition> SaveScheduleAsync(JobSaveScheduleRequest request, CancellationToken cancellationToken = default) => commands.SaveScheduleAsync(request, cancellationToken);
    public ValueTask<JobTransition> SetScheduleEnabledAsync(JobSetScheduleEnabledRequest request, CancellationToken cancellationToken = default) => commands.SetScheduleEnabledAsync(request, cancellationToken);
    public ValueTask<JobMaterializationResult> MaterializeDueAsync(JobMaterializationRequest request, CancellationToken cancellationToken = default) => commands.MaterializeDueAsync(request, cancellationToken);
    public ValueTask<JobTransition> AcknowledgeDeliveryAsync(JobAcknowledgeDeliveryRequest request, CancellationToken cancellationToken = default) => commands.AcknowledgeDeliveryAsync(request, cancellationToken);
    public ValueTask<JobSnapshot?> FindAsync(JobScope scope, string jobId, CancellationToken cancellationToken = default) => queries.FindAsync(scope, jobId, cancellationToken);
    public ValueTask<JobPage<JobSnapshot>> ListJobsAsync(JobScope scope, JobListQuery query, CancellationToken cancellationToken = default) => queries.ListJobsAsync(scope, query, cancellationToken);
    public ValueTask<JobPage<JobAttemptSnapshot>> ListAttemptsAsync(JobScope scope, string jobId, JobPageRequest page, CancellationToken cancellationToken = default) => queries.ListAttemptsAsync(scope, jobId, page, cancellationToken);
    public ValueTask<JobPage<JobPhysicalReceipt>> ListPhysicalReceiptsAsync(JobScope scope, string jobId, JobPageRequest page, CancellationToken cancellationToken = default) => queries.ListPhysicalReceiptsAsync(scope, jobId, page, cancellationToken);
    public ValueTask<JobPage<JobPhysicalReceiptInspection>> ListPhysicalReceiptInspectionsAsync(JobScope scope, string jobId, JobPageRequest page, CancellationToken cancellationToken = default) => queries.ListPhysicalReceiptInspectionsAsync(scope, jobId, page, cancellationToken);
    public ValueTask<JobPage<JobHold>> ListHoldsAsync(JobScope scope, JobGateKey? gate, JobPageRequest page, CancellationToken cancellationToken = default) => queries.ListHoldsAsync(scope, gate, page, cancellationToken);
    public ValueTask<JobPage<JobCapacity>> ListCapacityAsync(JobScope scope, JobPageRequest page, CancellationToken cancellationToken = default) => queries.ListCapacityAsync(scope, page, cancellationToken);
    public ValueTask<JobScheduleSnapshot?> FindScheduleAsync(JobScope scope, string scheduleId, CancellationToken cancellationToken = default) => queries.FindScheduleAsync(scope, scheduleId, cancellationToken);
    public ValueTask<JobPage<JobScheduleSnapshot>> ListSchedulesAsync(JobScope scope, JobPageRequest page, CancellationToken cancellationToken = default) => queries.ListSchedulesAsync(scope, page, cancellationToken);
    public ValueTask<JobPage<JobDeliverySnapshot>> ListPendingDeliveriesAsync(JobScope scope, JobPageRequest page, CancellationToken cancellationToken = default) => queries.ListPendingDeliveriesAsync(scope, page, cancellationToken);
    public ValueTask<JobDeliveryStatusSnapshot?> FindDeliveryAsync(JobScope scope, string jobId, CancellationToken cancellationToken = default) => queries.FindDeliveryAsync(scope, jobId, cancellationToken);
    public ValueTask<JobOriginSnapshot?> FindOriginAsync(JobScope scope, JobOrigin origin, CancellationToken cancellationToken = default) => queries.FindOriginAsync(scope, origin, cancellationToken);
    public ValueTask<JobOperationAuditSnapshot?> FindOperationAuditAsync(JobScope scope, string operationId, CancellationToken cancellationToken = default) => queries.FindOperationAuditAsync(scope, operationId, cancellationToken);
    public ValueTask<JobPage<JobOperationAuditSnapshot>> ListOperationAuditsAsync(JobScope scope, JobPageRequest page, CancellationToken cancellationToken = default) => queries.ListOperationAuditsAsync(scope, page, cancellationToken);
    public ValueTask<JobPage<JobQueueSnapshot>> ListQueuesAsync(JobScope scope, JobPageRequest page, CancellationToken cancellationToken = default) => queries.ListQueuesAsync(scope, page, cancellationToken);
}

internal sealed class SupplementalSourceObserver(IRuntimePostCommitIntentDispatcher inner, SupplementalJobsControl control) : IRuntimePostCommitIntentDispatcher
{
    public bool RequiresDurableSource(string intentKind) => inner.RequiresDurableSource(intentKind);
    public ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default) => inner.DispatchAsync(intent, cancellationToken);
    public async ValueTask DispatchAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default)
    {
        var prior = control.CurrentSource;
        control.CurrentSource = source;
        try { await inner.DispatchAsync(source, cancellationToken); }
        finally { control.CurrentSource = prior; }
    }
}
