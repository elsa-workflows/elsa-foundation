using System.Text.Json;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.RegularExpressions;
using CShells.Features;
using Elsa.Activities.Primitives.Activities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workbench.RecoveryProbe;

// Included solely by -p:ElsaBpmnRecoveryProbe=true. Recovery runs the standard host without this feature.
[ShellFeature(name: "BpmnRecoveryProbe", DependsOn = new object[]
    { "WorkflowsRuntimeEntityFrameworkCore", "WorkflowsRuntimeTriggers", "ActivitiesPrimitives" })]
public class BpmnRecoveryProbeFeature : IShellFeature
{
    public string ControlDirectory { get; set; } = "";
    public int? AdmissionStaticLimit { get; set; }

    public virtual void ConfigureServices(IServiceCollection services)
    {
        if (!Path.IsPathFullyQualified(ControlDirectory) || !Directory.Exists(ControlDirectory))
            throw new InvalidOperationException("The recovery probe requires an existing owned absolute control directory.");
        var control = new RecoveryProbeControl(ControlDirectory);
        services.AddSingleton(control);
        if (AdmissionStaticLimit is { } limit)
        {
            if (limit <= 0) throw new InvalidOperationException("The owned admission limit must be positive.");
            services.Replace(ServiceDescriptor.Singleton(new RuntimeAdmissionOptions { StaticLimit = limit }));
        }
        services.AddScoped<IWorkflowSchedulerDrainObserver>(provider => new ProbeLoadDrain(control,
            provider.GetRequiredService<IRuntimeAdmissionController>(), provider.GetRequiredService<IRuntimeAdmissionLoadSignal>()));
        Decorate<IStimulusDeliveryStore>(services, (inner, provider) => new ProbeSources(inner, provider.GetRequiredService<RecoveryProbeControl>()));
        Decorate<IBookmarkResumeDispatcher>(services, (inner, provider) => new ProbeResumes(inner, provider.GetRequiredService<RecoveryProbeControl>()));
        Decorate<IWorkflowStartDispatcher>(services, (inner, provider) => new ProbeStarts(inner,
            provider.GetRequiredService<IStimulusDeliveryStore>(), provider.GetRequiredService<RecoveryProbeControl>(),
            provider.GetRequiredService<IRuntimeAdmissionController>(), provider.GetRequiredService<IRuntimeAdmissionLoadSignal>()));
        Decorate<IWorkflowExecutableReferenceGarbageCollector>(services, (inner, provider) => new ProbeCollection(inner,
            provider.GetRequiredService<RecoveryProbeControl>(), provider.GetRequiredService<IOptions<WorkflowExecutableGarbageCollectionOptions>>(),
            provider.GetRequiredService<IPersistenceAccessContextAccessor>()));
        Decorate<IRuntimePostCommitIntentDispatcher>(services, (inner, provider) => new ProbeIntents(inner,
            provider.GetRequiredService<IStimulusDeliveryStore>(), provider.GetRequiredService<RecoveryProbeControl>(),
            provider.GetRequiredService<RuntimeDbContext>(), provider.GetRequiredService<IPostCommitOutboxLookupStore>(),
            provider.GetRequiredService<IPersistenceAccessContextAccessor>()));
    }

    private static void Decorate<T>(IServiceCollection services, Func<T, IServiceProvider, T> decorate) where T : class
    {
        var selected = services.Where(item => item.ServiceType == typeof(T) && !item.IsKeyedService).ToArray();
        if (selected.Length != 1) throw new InvalidOperationException("A recovery probe must wrap one actual selected provider port.");
        var descriptor = selected[0];
        services.Remove(descriptor);
        services.Add(new ServiceDescriptor(typeof(T), provider => decorate((T)(descriptor.ImplementationInstance ??
            descriptor.ImplementationFactory?.Invoke(provider) ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!)), provider), descriptor.Lifetime));
    }

    private sealed class ProbeSources(IStimulusDeliveryStore inner, RecoveryProbeControl control) : IStimulusDeliveryStore
    {
        public async ValueTask<PostCommitDeliveryContext> ReadSourceAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default)
        {
            var current = await inner.ReadSourceAsync(source, cancellationToken);
            await control.WaitForActualLoadAsync(current, cancellationToken);
            if (current.Claim.Item.RecipientSnapshot is null)
                await control.CutAsync("before-selection", current, cancellationToken);
            return current;
        }
        public async ValueTask<StimulusDeliverySnapshot> InstallAsync(PostCommitDeliveryContext source, StimulusDeliverySnapshot candidate, CancellationToken cancellationToken = default)
        {
            var attemptedProviderInstall = false;
            StimulusDeliverySnapshot winner;
            try
            {
                await control.CutAsync("before-install", source, cancellationToken, candidate);
                attemptedProviderInstall = true;
                winner = await inner.InstallAsync(source, candidate, cancellationToken);
            }
            catch (Exception exception)
            {
                try { await control.RecordRejectedProposalAsync(source, attemptedProviderInstall, exception, cancellationToken.IsCancellationRequested, cancellationToken); }
                catch { /* Missing evidence fails the runner; observation must never replace the actual refusal. */ }
                throw;
            }
            var current = await inner.ReadSourceAsync(source, cancellationToken);
            await control.CutAsync("after-cohort", current, cancellationToken);
            return winner;
        }
        public bool CanRetainExecutables(IRuntimePostCommitOutboxStore outbox, IWorkflowExecutableStore material,
            IWorkflowExecutableSourceReferenceStore references, IWorkflowExecutionStateStore executions) => inner.CanRetainExecutables(outbox, material, references, executions);
        public ValueTask EnsureOrdinaryCollectionAllowedAsync(IRuntimePostCommitOutboxStore outbox, IWorkflowExecutableStore material,
            IWorkflowExecutableSourceReferenceStore references, IWorkflowExecutionStateStore executions, CancellationToken cancellationToken = default) =>
            inner.EnsureOrdinaryCollectionAllowedAsync(outbox, material, references, executions, cancellationToken);
        public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(IWorkflowExecutableStore material,
            IWorkflowExecutableSourceReferenceStore references, IWorkflowExecutionStateStore executions, CancellationToken cancellationToken = default) =>
            inner.ListPinnedExecutableArtifactIdsAsync(material, references, executions, cancellationToken);
        public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default) => inner.ListPinnedExecutableArtifactIdsAsync(cancellationToken);
        public ValueTask<PostCommitDeliveryContext> ReadSavedStartSourceAsync(WorkflowExecutableStimulusDeliveryAuthority authority, CancellationToken cancellationToken = default) =>
            inner.ReadSavedStartSourceAsync(authority, cancellationToken);
    }

    private sealed class ProbeCollection(IWorkflowExecutableReferenceGarbageCollector inner, RecoveryProbeControl control,
        IOptions<WorkflowExecutableGarbageCollectionOptions> options, IPersistenceAccessContextAccessor access) : IWorkflowExecutableReferenceGarbageCollector
    {
        public bool SupportsStimulusDeliveryRetention => inner.SupportsStimulusDeliveryRetention;
        public async ValueTask<WorkflowExecutableReferenceSweepResult> SweepAsync(CancellationToken cancellationToken = default)
        {
            await control.WaitForCollectionAsync(cancellationToken);
            var startedAt = DateTimeOffset.UtcNow;
            var result = await inner.SweepAsync(cancellationToken);
            await control.RecordCollectionAsync(startedAt, result, options.Value.ArtifactCreationGracePeriod,
                inner.GetType().FullName!, SupportsStimulusDeliveryRetention, access.Current.RequireScope().Value, cancellationToken);
            return result;
        }
        public async ValueTask<WorkflowExecutableReferenceSweepResult> SweepAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            await control.WaitForCollectionAsync(cancellationToken);
            var startedAt = DateTimeOffset.UtcNow;
            var result = await inner.SweepAsync(now, cancellationToken);
            await control.RecordCollectionAsync(startedAt, result, options.Value.ArtifactCreationGracePeriod,
                inner.GetType().FullName!, SupportsStimulusDeliveryRetention, access.Current.RequireScope().Value, cancellationToken);
            return result;
        }
    }

    private sealed class ProbeIntents(IRuntimePostCommitIntentDispatcher inner, IStimulusDeliveryStore sources,
        RecoveryProbeControl control, RuntimeDbContext context, IPostCommitOutboxLookupStore outbox,
        IPersistenceAccessContextAccessor access) : IRuntimePostCommitIntentDispatcher, IDisposable
    {
        private readonly IDisposable? initialClaimObserver = control.ArmInitialClaimCut(context);
        private readonly IDisposable parentCompletionObserver = control.ArmNativeParentCompletionCut(context);
        private IDisposable? completionObserver;
        public void Dispose() { initialClaimObserver?.Dispose(); parentCompletionObserver.Dispose(); completionObserver?.Dispose(); }
        public bool RequiresDurableSource(string intentKind) => inner.RequiresDurableSource(intentKind);
        public async ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
        {
            var cut = await control.CaptureCalledControlClaimAsync(intent, outbox, access, cancellationToken);
            await inner.DispatchAsync(intent, cancellationToken);
            await control.CutCalledControlBeforeAcknowledgmentAsync(cut, outbox, cancellationToken);
        }
        public async ValueTask DispatchAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default)
        {
            var cut = await control.CaptureCalledControlClaimAsync(source.Intent, outbox, access, cancellationToken);
            await inner.DispatchAsync(source, cancellationToken);
            await control.CutCalledControlBeforeAcknowledgmentAsync(cut, outbox, cancellationToken);
            if (source.Intent.Kind == PublishStimulusConstants.PublishStimulusIntentKind)
            {
                var current = await sources.ReadSourceAsync(source, cancellationToken);
                if (await control.ArmCommittedSourceCutAsync(context, current, cancellationToken) is { } observer)
                { completionObserver?.Dispose(); completionObserver = observer; }
                await control.CutAsync("after-fanout", current, cancellationToken);
            }
        }
    }

    private sealed class ProbeStarts(IWorkflowStartDispatcher inner, IStimulusDeliveryStore sources, RecoveryProbeControl control,
        IRuntimeAdmissionController admission, IRuntimeAdmissionLoadSignal load) : IWorkflowStartDispatcher
    {
        public bool CanDispatchSavedStimulus(IStimulusDeliveryStore selectedSources, IWorkflowExecutableStore material,
            IWorkflowExecutionStateStore executions) => inner.CanDispatchSavedStimulus(selectedSources, material, executions);
        public async ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(WorkflowExecutionStartDispatchRequest request,
            WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.Published,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null, CancellationToken cancellationToken = default)
        {
            var inFlightBefore = load.InFlightDispatches; var ambientBefore = load.HasAmbientCharge;
            var result = await inner.DispatchAsync(request, requiredScope, dispatchOptions, cancellationToken);
            var observedStart = new { request.IdempotencyKey, request.WorkflowExecutionId, request.ArtifactId,
                TriggerBindingId = request.StartAuthority?.StimulusDelivery?.TriggerBindingId,
                Status = result.CommandDispatch.Status.ToString(), result.CommandDispatch.IsShed, InFlightBefore = inFlightBefore,
                HadAmbientChargeBefore = ambientBefore, Limit = admission.Limit, Controller = admission.GetType().FullName };
            if (request.StartAuthority?.StimulusDelivery is { } shedAuthority && result.CommandDispatch.IsShed)
            {
                var source = await sources.ReadSavedStartSourceAsync(shedAuthority, cancellationToken);
                await control.CutAsync("after-shed", source, cancellationToken, observedStart: observedStart);
            }
            if (request.StartAuthority?.StimulusDelivery is { } authority && !result.CommandDispatch.IsShed &&
                result.CommandDispatch.Status == WorkflowExecutionCommandDispatchStatus.Accepted)
            {
                var source = await sources.ReadSavedStartSourceAsync(authority, cancellationToken);
                await control.CutAsync("after-start", source, cancellationToken, acceptedExecution: result.WorkflowExecutionId, observedStart: observedStart);
            }
            return result;
        }
    }

    private sealed class ProbeLoadDrain(RecoveryProbeControl control, IRuntimeAdmissionController admission,
        IRuntimeAdmissionLoadSignal load) : IWorkflowSchedulerDrainObserver
    {
        public async ValueTask OnDrainedAsync(WorkflowExecutionCommandEnvelope envelope, RuntimeSchedulerDrainResult result,
            CancellationToken cancellationToken = default)
        {
            if (envelope.Command.Kind != WorkflowExecutionCommandKind.Start ||
                !envelope.Metadata.TryGetValue("runtime.artifactId", out var artifactId)) return;
            await control.HoldActualLoadAsync(envelope.WorkflowExecutionId,
                artifactId, result.StopReason.ToString(), admission, load, cancellationToken);
        }
    }

    private sealed class ProbeResumes(IBookmarkResumeDispatcher inner, RecoveryProbeControl control) : IBookmarkResumeDispatcher
    {
        public bool CanDispatchSavedStimulus(IStimulusDeliveryStore sources, IWorkflowExecutableStore material, IWorkflowExecutionStateStore executions) =>
            inner.CanDispatchSavedStimulus(sources, material, executions);
        public ValueTask<BookmarkResumeDispatchResult> DispatchAsync(BookmarkResumeDispatchRequest request,
            WorkflowExecutionCommandDispatchOptions? options = null, CancellationToken cancellationToken = default) => inner.DispatchAsync(request, options, cancellationToken);
        public async ValueTask<BookmarkResumeDispatchResult> DispatchAsync(PostCommitDeliveryContext source, string workflowExecutionId,
            string bookmarkId, CancellationToken cancellationToken = default)
        {
            var result = await inner.DispatchAsync(source, workflowExecutionId, bookmarkId, cancellationToken);
            if (result.Status == BookmarkResumeDispatchStatus.Dispatched)
                await control.CutAsync("after-resume", source, cancellationToken, acceptedExecution: workflowExecutionId, acceptedBookmark: bookmarkId);
            return result;
        }
    }
}

internal sealed class RecoveryProbeControl(string directory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public IDisposable ArmNativeParentCompletionCut(RuntimeDbContext context) => new NativeParentCompletionCut(context, directory);

    // Observe only the selected context's real physical transaction. Buffered/coalesced logical
    // acknowledgments never reach this observer. No checkpoint row or queue item is manufactured.
    private sealed class NativeParentCompletionCut : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly RuntimeDbContext context;
        private readonly string directory;
        private readonly List<IDisposable> subscriptions = new();
        private readonly object gate = new();
        private IDisposable? allListeners;
        private bool disposed;

        public NativeParentCompletionCut(RuntimeDbContext context, string directory)
        {
            this.context = context; this.directory = directory;
            allListeners = DiagnosticListener.AllListeners.Subscribe(this);
        }
        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name != DbLoggerCategory.Name) return;
            lock (gate)
            {
                if (!disposed) subscriptions.Add(listener.Subscribe((IObserver<KeyValuePair<string, object?>>)this,
                    name => name == RelationalEventId.TransactionCommitted.Name));
            }
        }
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Key != RelationalEventId.TransactionCommitted.Name || value.Value is not TransactionEndEventData data ||
                !ReferenceEquals(data.Context, context)) return;
            lock (gate) { if (disposed) return; }
            var path = Path.Combine(directory, "native-parent-plan.json");
            if (!File.Exists(path)) return;
            using var planDocument = JsonDocument.Parse(File.ReadAllText(path));
            var plan = planDocument.RootElement;
            var caseId = plan.GetProperty("caseId").GetString()!;
            if (!Regex.IsMatch(caseId, "^[a-z0-9-]{1,64}$", RegexOptions.CultureInvariant)) throw new InvalidDataException("Invalid native-parent probe case.");
            var workflowId = plan.GetProperty("workflowExecutionId").GetString();
            var nodeId = plan.GetProperty("childNodeId").GetString();
            foreach (var entry in context.ChangeTracker.Entries<RuntimePostCommitOutboxEntity>())
            {
                var row = entry.Entity;
                if (row.WorkflowExecutionId != EfRelationalIdentity.Encode(workflowId!) ||
                    row.IntentKind != EfRelationalIdentity.Encode(RuntimePostCommitIntentKinds.EnqueueSchedulerWork) ||
                    row.Status != (int)RuntimePostCommitOutboxStatus.Pending) continue;
                using var document = JsonDocument.Parse(row.ContentJson);
                var work = document.RootElement.GetProperty("intent").GetProperty("payload");
                if (work.GetProperty("CommandKind").GetInt32() != (int)WorkflowExecutionCommandKind.CompleteActivity ||
                    !work.TryGetProperty("Payload", out var payload) ||
                    !payload.TryGetProperty("ExecutableNodeId", out var node) || node.GetString() != nodeId) continue;
                var caseDirectory = Path.Combine(directory, caseId); Directory.CreateDirectory(caseDirectory);
                var claimed = Path.Combine(caseDirectory, "claimed");
                try { using var token = new FileStream(claimed, FileMode.CreateNew, FileAccess.Write, FileShare.Read); token.Flush(true); }
                catch (IOException) when (File.Exists(claimed)) { return; }
                var marker = new { CaseId = caseId, Phase = "native-parent-completion-before-consumption", ProviderEvent = "EF.TransactionCommitted",
                    WorkflowExecutionId = workflowId, ChildNodeId = nodeId, SourceOutboxItemId = EfRelationalIdentity.Decode(row.OutboxItemId),
                    Scope = EfRelationalIdentity.Decode(row.ScopeKey), Work = work.Clone(), CapturedAtUtc = DateTimeOffset.UtcNow };
                var temporary = Path.Combine(caseDirectory, "reached.tmp");
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { JsonSerializer.Serialize(stream, marker, Json); stream.Flush(true); }
                File.Move(temporary, Path.Combine(caseDirectory, "reached.json"));
                Dispose();
                while (!File.Exists(Path.Combine(caseDirectory, "release"))) Thread.Sleep(100);
                return;
            }
        }
        public void OnError(Exception error) { }
        public void OnCompleted() { }
        public void Dispose()
        {
            List<IDisposable> owned;
            lock (gate)
            {
                if (disposed) return;
                disposed = true; owned = new(subscriptions); subscriptions.Clear();
                if (allListeners is { } all) owned.Add(all);
                allListeners = null;
            }
            foreach (var subscription in owned) subscription.Dispose();
        }
    }

    public async ValueTask<CalledControlCapture?> CaptureCalledControlClaimAsync(RuntimePostCommitIntent intent,
        IPostCommitOutboxLookupStore outbox, IPersistenceAccessContextAccessor access, CancellationToken cancellationToken)
    {
        const string notify = "Elsa.Activities.DispatchWorkflow.NotifyControl";
        if (intent.Kind is not (notify or WorkflowDispatchLifecycle.ResumeParentIntentKind)) return null;
        var path = Path.Combine(directory, "called-control-plan.json");
        if (!File.Exists(path)) return null;
        var plan = JsonSerializer.Deserialize<CalledControlPlan>(await File.ReadAllTextAsync(path, cancellationToken), Json)
            ?? throw new InvalidDataException("The owned called-control cut plan is empty.");
        if (!Regex.IsMatch(plan.CaseId, "^[a-z0-9-]{1,64}$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("The owned called-control cut case is invalid.");
        if (intent.Kind != plan.IntentKind || intent.Payload is not { } payload) return null;
        JsonElement wire;
        if (intent.Kind == notify) wire = payload;
        else if (!payload.TryGetProperty("result", out var result) || !result.TryGetProperty("control", out wire)) return null;
        if (wire.ValueKind is not JsonValueKind.Object || !wire.TryGetProperty("batch", out var batch) ||
            !batch.TryGetProperty("childWorkflowExecutionId", out var child) || child.GetString() != plan.ChildWorkflowExecutionId) return null;
        var transport = wire.Deserialize<RuntimeChildControlTransport>(Json)
            ?? throw new InvalidDataException("Actual called-control transport is missing.");
        if (transport.Batch.Ordinal != plan.Ordinal) return null;
        // The normal handler uses the intent overload. Read the actual selected provider's existing
        // claim before forwarding it; never force a context overload or construct a claim/context.
        var source = await outbox.FindAsync(transport.SourceOutboxItemId, cancellationToken);
        if (source is not { Status: RuntimePostCommitOutboxStatus.Delivering, DeliveryFencingToken: > 0 } ||
            string.IsNullOrEmpty(source.DeliveringOwnerId) || source.DeliveryVisibleAfter is null || !source.Intent.IsEquivalentTo(intent)) return null;
        return new(plan, transport, source, access.Current.RequireScope());
    }

    public async ValueTask CutCalledControlBeforeAcknowledgmentAsync(CalledControlCapture? captured,
        IPostCommitOutboxLookupStore outbox, CancellationToken cancellationToken)
    {
        if (captured is null) return;
        var (plan, transport, before, scope) = captured;
        var source = await outbox.FindAsync(before.OutboxItemId, cancellationToken)
            ?? throw new InvalidDataException("Actual called-control source disappeared after its handler returned.");
        if (source.Status != RuntimePostCommitOutboxStatus.Delivering || source.DeliveringOwnerId != before.DeliveringOwnerId ||
            source.DeliveryFencingToken != before.DeliveryFencingToken || source.DeliveryVisibleAfter != before.DeliveryVisibleAfter ||
            !source.Intent.IsEquivalentTo(before.Intent))
            throw new InvalidDataException("The actual called-control claim changed before the observed pre-acknowledgment cut.");
        var caseDirectory = Path.Combine(directory, plan.CaseId); Directory.CreateDirectory(caseDirectory);
        var token = Path.Combine(caseDirectory, "claimed");
        try { using var claimed = new FileStream(token, FileMode.CreateNew, FileAccess.Write, FileShare.Read); claimed.Flush(true); }
        catch (IOException) when (File.Exists(token)) { return; }
        var marker = new { plan.CaseId, Phase = "called-control-before-acknowledgment", ProviderEvent = "ActualIntentDispatcherReturnedBeforeDeliveryResult",
            SourceOutboxItemId = source.OutboxItemId, transport.SourceCommitId, IntentFingerprint = RuntimeCheckpointCommitFingerprint.ComputeIntent(source.Intent), Scope = scope.Value,
            OwnerId = source.DeliveringOwnerId, FencingToken = source.DeliveryFencingToken, VisibleAfter = source.DeliveryVisibleAfter, source.Intent.WorkflowExecutionId,
            IntentKind = source.Intent.Kind, transport, CapturedAtUtc = DateTimeOffset.UtcNow };
        var temporary = Path.Combine(caseDirectory, "reached.tmp");
        await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { await JsonSerializer.SerializeAsync(stream, marker, Json, cancellationToken); stream.Flush(true); }
        File.Move(temporary, Path.Combine(caseDirectory, "reached.json"));
        while (!File.Exists(Path.Combine(caseDirectory, "release")))
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
    }

    internal sealed record CalledControlPlan(string CaseId, string IntentKind, string ChildWorkflowExecutionId, long Ordinal);
    internal sealed record CalledControlCapture(CalledControlPlan Plan, RuntimeChildControlTransport Transport, RuntimePostCommitOutboxItem Source, PersistenceScope Scope);

    public async ValueTask WaitForActualLoadAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, "plan.json");
        if (!File.Exists(path)) return;
        var plan = JsonSerializer.Deserialize<Plan>(await File.ReadAllTextAsync(path, cancellationToken), Json);
        if (plan?.Phase != "after-shed" || plan.LoadArtifactId is null || source.Intent.Kind != PublishStimulusConstants.PublishStimulusIntentKind ||
            source.Intent.Payload?.GetProperty("eventName").GetString() != plan.EventName) return;
        if (!Regex.IsMatch(plan.CaseId, "^[a-z0-9-]{1,64}$", RegexOptions.CultureInvariant)) throw new InvalidDataException("Invalid owned load case.");
        while (!File.Exists(Path.Combine(directory, plan.CaseId, "load-held.json")))
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
    }
    public async ValueTask HoldActualLoadAsync(string executionId, string artifactId, string stopReason,
        IRuntimeAdmissionController admission, IRuntimeAdmissionLoadSignal load, CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, "plan.json");
        if (!File.Exists(path)) return;
        var plan = JsonSerializer.Deserialize<Plan>(await File.ReadAllTextAsync(path, cancellationToken), Json);
        if (plan?.Phase != "after-shed" || plan.LoadArtifactId != artifactId) return;
        if (!Regex.IsMatch(plan.CaseId, "^[a-z0-9-]{1,64}$", RegexOptions.CultureInvariant)) throw new InvalidDataException("Invalid owned load case.");
        var caseDirectory = Path.Combine(directory, plan.CaseId); Directory.CreateDirectory(caseDirectory);
        var token = Path.Combine(caseDirectory, "load-claimed");
        try { using var claimed = new FileStream(token, FileMode.CreateNew, FileAccess.Write, FileShare.Read); claimed.Flush(true); }
        catch (IOException) when (File.Exists(token)) { return; }
        var temporary = Path.Combine(caseDirectory, "load-held.tmp");
        await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { await JsonSerializer.SerializeAsync(stream, new { WorkflowExecutionId = executionId, ArtifactId = artifactId,
            StopReason = stopReason, InFlightDispatches = load.InFlightDispatches, HasAmbientCharge = load.HasAmbientCharge,
            Limit = admission.Limit, Controller = admission.GetType().FullName, CapturedAtUtc = DateTimeOffset.UtcNow }, Json, cancellationToken); stream.Flush(true); }
        File.Move(temporary, Path.Combine(caseDirectory, "load-held.json"));
        while (!File.Exists(Path.Combine(caseDirectory, "release-load")))
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
    }

    private async ValueTask<Plan?> CollectionPlanAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, "plan.json");
        if (!File.Exists(path)) return null;
        var plan = JsonSerializer.Deserialize<Plan>(await File.ReadAllTextAsync(path, cancellationToken), Json);
        if (plan is null || plan.Phase != "republished-cohort" && !plan.ObserveCollection) return null;
        if (!Regex.IsMatch(plan.CaseId, "^[a-z0-9-]{1,64}$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("The owned collection case name is invalid.");
        return plan;
    }
    public async ValueTask WaitForCollectionAsync(CancellationToken cancellationToken)
    {
        if (await CollectionPlanAsync(cancellationToken) is not { } plan) return;
        // Do not erase the staging grace while normal publication is still creating its first durable root.
        // The runner releases this scheduling gate only after all publications and retirements finish.
        while (!File.Exists(Path.Combine(directory, plan.CaseId, "run-collection")))
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
    }
    public async ValueTask RecordCollectionAsync(DateTimeOffset startedAt, WorkflowExecutableReferenceSweepResult result,
        TimeSpan grace, string collector, bool supportsRetention, string scope, CancellationToken cancellationToken)
    {
        if (await CollectionPlanAsync(cancellationToken) is not { } plan) return;
        var caseDirectory = Path.Combine(directory, plan.CaseId);
        var name = "collection-" + Guid.NewGuid().ToString("N");
        var temporary = Path.Combine(caseDirectory, name + ".tmp");
        await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, new { StartedAtUtc = startedAt, CompletedAtUtc = DateTimeOffset.UtcNow,
                Result = result, ArtifactCreationGracePeriodSeconds = grace.TotalSeconds, Collector = collector,
                SupportsStimulusDeliveryRetention = supportsRetention, Scope = scope }, Json, cancellationToken);
            stream.Flush(true);
        }
        File.Move(temporary, Path.Combine(caseDirectory, name + ".json"));
    }

    // This diagnostic holds the first real claim save before the processor can renew it. Raw marker
    // fields are observations only; the independent checked reader must prove the committed source.
    public IDisposable? ArmInitialClaimCut(RuntimeDbContext context)
    {
        var path = Path.Combine(directory, "plan.json");
        if (!File.Exists(path)) return null;
        var plan = JsonSerializer.Deserialize<Plan>(File.ReadAllText(path), Json);
        return plan?.Phase == "renewed-claim" ? new InitialClaimCut(context, this, plan) : null;
    }

    private sealed class InitialClaimCut
        : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly object gate = new();
        private readonly List<IDisposable> subscriptions = new();
        private IDisposable? allListeners;
        private bool disposed;
        private readonly RuntimeDbContext context;
        private readonly RecoveryProbeControl control;
        private readonly Plan plan;
        public InitialClaimCut(RuntimeDbContext context, RecoveryProbeControl control, Plan plan)
        {
            this.context = context; this.control = control; this.plan = plan;
            allListeners = DiagnosticListener.AllListeners.Subscribe(this);
        }
        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name != DbLoggerCategory.Name) return;
            lock (gate)
            {
                if (disposed) return;
                subscriptions.Add(listener.Subscribe((IObserver<KeyValuePair<string, object?>>)this,
                    name => name == CoreEventId.SaveChangesCompleted.Name));
            }
        }
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Key != CoreEventId.SaveChangesCompleted.Name || value.Value is not SaveChangesCompletedEventData data ||
                !ReferenceEquals(data.Context, context)) return;
            lock (gate) { if (disposed) return; }
            // ClaimAsync saves without an enclosing transaction. A transactional save is not commit evidence.
            if (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null) return;
            foreach (var entry in context.ChangeTracker.Entries<RuntimePostCommitOutboxEntity>())
            {
                var row = entry.Entity;
                if (row.Status != (int)RuntimePostCommitOutboxStatus.Delivering) continue;
                using var document = JsonDocument.Parse(row.ContentJson);
                var item = document.RootElement;
                if (item.GetProperty("deliveryFencingToken").GetInt64() != 1 || !item.TryGetProperty("stimulusDeliveryProtocolVersion", out var protocol) || protocol.ValueKind != JsonValueKind.Number || protocol.GetInt32() != 1 ||
                    item.TryGetProperty("recipientSnapshot", out _)) continue;
                var intent = item.GetProperty("intent");
                if (EfRelationalIdentity.Decode(intent.GetProperty("kind").GetString()!) != PublishStimulusConstants.PublishStimulusIntentKind ||
                    intent.GetProperty("payload").GetProperty("eventName").GetString() != plan.EventName) continue;
                Dispose();
                var marker = new { plan.CaseId, Phase = "after-claim", ProviderEvent = "EF.SaveChangesCompleted",
                    SourceOutboxItemId = EfRelationalIdentity.Decode(item.GetProperty("outboxItemId").GetString()!),
                    Scope = EfRelationalIdentity.Decode(row.ScopeKey), OwnerId = EfRelationalIdentity.Decode(item.GetProperty("deliveringOwnerId").GetString()!),
                    FencingToken = item.GetProperty("deliveryFencingToken").GetInt64(), VisibleAfter = item.GetProperty("deliveryVisibleAfter").GetDateTimeOffset(),
                    WorkflowExecutionId = EfRelationalIdentity.Decode(intent.GetProperty("workflowExecutionId").GetString()!),
                    Snapshot = (object?)null, CapturedAtUtc = DateTimeOffset.UtcNow };
                control.HoldInitialClaimAsync(plan.CaseId, marker).GetAwaiter().GetResult();
                return;
            }
        }
        public void OnError(Exception error) { }
        public void OnCompleted() { }
        public void Dispose()
        {
            List<IDisposable> owned;
            lock (gate)
            {
                if (disposed) return;
                disposed = true; owned = new(subscriptions); subscriptions.Clear();
                if (allListeners is { } all) owned.Add(all);
                allListeners = null;
            }
            foreach (var subscription in owned) subscription.Dispose();
        }
    }

    private async Task HoldInitialClaimAsync(string caseId, object marker)
    {
        if (!Regex.IsMatch(caseId, "^[a-z0-9-]{1,64}$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("The owned recovery case name is invalid.");
        var caseDirectory = Path.Combine(directory, caseId); Directory.CreateDirectory(caseDirectory);
        var temporary = Path.Combine(caseDirectory, "initial-claim.tmp");
        await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { await JsonSerializer.SerializeAsync(stream, marker, Json); stream.Flush(true); }
        File.Move(temporary, Path.Combine(caseDirectory, "initial-claim.json"));
        while (!File.Exists(Path.Combine(caseDirectory, "release-initial-claim")))
            await Task.Delay(TimeSpan.FromMilliseconds(100));
    }

    // TransactionCommitted is diagnostic only; a separate checked reader proves the physical commit.
    public async ValueTask<IDisposable?> ArmCommittedSourceCutAsync(RuntimeDbContext context, PostCommitDeliveryContext source,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, "plan.json");
        if (!File.Exists(path)) return null;
        var plan = JsonSerializer.Deserialize<Plan>(await File.ReadAllTextAsync(path, cancellationToken), Json);
        if (plan is null || plan.Phase != "after-source-commit" ||
            source.Intent.Payload?.GetProperty("eventName").GetString() != plan.EventName) return null;
        return new CommittedSourceCut(context, source, this, cancellationToken);
    }

    private sealed class CommittedSourceCut : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly RuntimeDbContext context;
        private readonly PostCommitDeliveryContext source;
        private readonly RecoveryProbeControl control;
        private readonly CancellationToken cancellationToken;
        private readonly string scopeKey;
        private readonly string sourceKey;
        private readonly object gate = new();
        private readonly List<IDisposable> subscriptions = new();
        private IDisposable? allListeners;
        private bool disposed;

        public CommittedSourceCut(RuntimeDbContext context, PostCommitDeliveryContext source,
            RecoveryProbeControl control, CancellationToken cancellationToken)
        {
            this.context = context; this.source = source; this.control = control; this.cancellationToken = cancellationToken;
            scopeKey = EfRelationalIdentity.Encode(source.Scope.Value); sourceKey = EfRelationalIdentity.Encode(source.SourceOutboxItemId);
            allListeners = DiagnosticListener.AllListeners.Subscribe(this);
        }
        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name != DbLoggerCategory.Name) return;
            lock (gate)
            {
                if (disposed) return;
                subscriptions.Add(listener.Subscribe((IObserver<KeyValuePair<string, object?>>)this,
                    name => name == RelationalEventId.TransactionCommitted.Name));
            }
        }
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Key != RelationalEventId.TransactionCommitted.Name || value.Value is not TransactionEndEventData data ||
                !ReferenceEquals(data.Context, context)) return;
            lock (gate) { if (disposed) return; }
            var row = context.ChangeTracker.Entries<RuntimePostCommitOutboxEntity>().SingleOrDefault(entry =>
                entry.Entity.ScopeKey == scopeKey && entry.Entity.OutboxItemId == sourceKey)?.Entity;
            if (row?.Status != (int)RuntimePostCommitOutboxStatus.Delivered) return;
            Dispose();
            control.CutAsync("after-source-commit", source, cancellationToken,
                providerEvent: "EF.TransactionCommitted").AsTask().GetAwaiter().GetResult();
        }
        public void OnError(Exception error) { }
        public void OnCompleted() { }
        public void Dispose()
        {
            List<IDisposable> owned;
            lock (gate)
            {
                if (disposed) return;
                disposed = true; owned = new(subscriptions); subscriptions.Clear();
                if (allListeners is { } all) owned.Add(all);
                allListeners = null;
            }
            foreach (var subscription in owned) subscription.Dispose();
        }
    }

    public async ValueTask CutAsync(string phase, PostCommitDeliveryContext source, CancellationToken cancellationToken,
        StimulusDeliverySnapshot? proposal = null, string? acceptedExecution = null, string? acceptedBookmark = null,
        string? providerEvent = null, object? observedStart = null)
    {
        var path = Path.Combine(directory, "plan.json");
        if (!File.Exists(path) || source.Intent.Kind != PublishStimulusConstants.PublishStimulusIntentKind) return;
        var plan = JsonSerializer.Deserialize<Plan>(await File.ReadAllTextAsync(path, cancellationToken), Json)
            ?? throw new InvalidDataException("The owned recovery plan is empty.");
        if ((plan.Phase != phase && !(plan.Phase is "renewed-claim" or "republished-cohort" && phase == "after-cohort")) || source.Intent.Payload?.GetProperty("eventName").GetString() != plan.EventName ||
            plan.TargetExecutionId is { } target && target != acceptedExecution) return;
        if (!Regex.IsMatch(plan.CaseId, "^[a-z0-9-]{1,64}$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("The owned recovery case name is invalid.");
        var caseDirectory = Path.Combine(directory, plan.CaseId); Directory.CreateDirectory(caseDirectory);
        var token = Path.Combine(caseDirectory, "claimed");
        try { using var claimed = new FileStream(token, FileMode.CreateNew, FileAccess.Write, FileShare.Read); claimed.Flush(true); }
        catch (IOException) when (File.Exists(token)) { return; }
        var marker = new { plan.CaseId, Phase = phase, ProviderEvent = providerEvent, SourceOutboxItemId = source.SourceOutboxItemId,
            source.SourceCommitId, source.IntentFingerprint, Scope = source.Scope.Value, source.Claim.OwnerId,
            source.Claim.FencingToken, source.Claim.VisibleAfter, source.Intent.WorkflowExecutionId,
            Snapshot = source.Claim.Item.RecipientSnapshot, Proposal = proposal, AcceptedExecution = acceptedExecution,
            AcceptedBookmark = acceptedBookmark, ObservedStart = observedStart, CapturedAtUtc = DateTimeOffset.UtcNow };
        var temporary = Path.Combine(caseDirectory, "reached.tmp");
        await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { await JsonSerializer.SerializeAsync(stream, marker, Json, cancellationToken); stream.Flush(true); }
        File.Move(temporary, Path.Combine(caseDirectory, "reached.json"));
        while (!File.Exists(Path.Combine(caseDirectory, "release")))
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
    }

    // An observation of the real refusal, never a provider/claim result used by application code.
    public async ValueTask RecordRejectedProposalAsync(PostCommitDeliveryContext source, bool attemptedProviderInstall,
        Exception exception, bool cancellationRequested, CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, "plan.json");
        if (!File.Exists(path)) return;
        var plan = JsonSerializer.Deserialize<Plan>(await File.ReadAllTextAsync(path), Json);
        if (plan is null || plan.Phase != "before-install" ||
            !Regex.IsMatch(plan.CaseId, "^[a-z0-9-]{1,64}$", RegexOptions.CultureInvariant) ||
            source.Intent.Payload?.GetProperty("eventName").GetString() != plan.EventName) return;
        var caseDirectory = Path.Combine(directory, plan.CaseId);
        if (!File.Exists(Path.Combine(caseDirectory, "reached.json"))) return;
        var marker = new { SourceOutboxItemId = source.SourceOutboxItemId, source.Claim.OwnerId,
            source.Claim.FencingToken, AttemptedProviderInstall = attemptedProviderInstall,
            FailureType = exception.GetType().Name, InnerFailureType = exception.InnerException?.GetType().Name,
            CancellationRequested = cancellationRequested, IsUnavailable = (exception as StimulusDeliveryBoundaryException)?.IsUnavailable,
            DependencyGraphFailure = (exception.InnerException as WorkflowExecutableDependencyGraphException)?.Kind.ToString(),
            DependencyGraphDiagnostic = exception.InnerException is WorkflowExecutableDependencyGraphException { Kind: WorkflowExecutableDependencyGraphFailureKind.MissingArtifact } missing
                ? missing.Message : null, ObservedAtUtc = DateTimeOffset.UtcNow };
        var temporary = Path.Combine(caseDirectory, "rejected.tmp");
        if (File.Exists(temporary) || File.Exists(Path.Combine(caseDirectory, "rejected.json"))) return;
        await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { await JsonSerializer.SerializeAsync(stream, marker, Json); stream.Flush(true); }
        File.Move(temporary, Path.Combine(caseDirectory, "rejected.json"));
        // Preserve the actual failed provider attempt before the processor records a result or retries.
        // The observer never changes that exception or supplies an application verdict.
        while (plan.HoldRejectedProposal && !File.Exists(Path.Combine(caseDirectory, "release-rejection")))
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
    }

    private sealed record Plan(string CaseId, string Phase, string EventName, string? TargetExecutionId = null, string? LoadArtifactId = null, bool ObserveCollection = false, bool HoldRejectedProposal = false);
}
