using System.Text.Json;
using System.Text.RegularExpressions;
using CShells.Features;
using Elsa.Activities.Primitives.Activities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workbench.RecoveryProbe;

// Included solely by -p:ElsaBpmnRecoveryProbe=true. Recovery runs the standard host without this feature.
[ShellFeature(name: "BpmnRecoveryProbe", DependsOn = new object[]
    { "WorkflowsRuntimeEntityFrameworkCore", "WorkflowsRuntimeTriggers", "ActivitiesPrimitives" })]
public class BpmnRecoveryProbeFeature : IShellFeature
{
    public string ControlDirectory { get; set; } = "";

    public virtual void ConfigureServices(IServiceCollection services)
    {
        if (!Path.IsPathFullyQualified(ControlDirectory) || !Directory.Exists(ControlDirectory))
            throw new InvalidOperationException("The recovery probe requires an existing owned absolute control directory.");
        var control = new RecoveryProbeControl(ControlDirectory);
        services.AddSingleton(control);
        Decorate<IStimulusDeliveryStore>(services, (inner, provider) => new ProbeSources(inner, provider.GetRequiredService<RecoveryProbeControl>()));
        Decorate<IBookmarkResumeDispatcher>(services, (inner, provider) => new ProbeResumes(inner, provider.GetRequiredService<RecoveryProbeControl>()));
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
                try { await control.RecordRejectedProposalAsync(source, attemptedProviderInstall, exception, cancellationToken.IsCancellationRequested); }
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
    public async ValueTask CutAsync(string phase, PostCommitDeliveryContext source, CancellationToken cancellationToken,
        StimulusDeliverySnapshot? proposal = null, string? acceptedExecution = null, string? acceptedBookmark = null)
    {
        var path = Path.Combine(directory, "plan.json");
        if (!File.Exists(path) || source.Intent.Kind != PublishStimulusConstants.PublishStimulusIntentKind) return;
        var plan = JsonSerializer.Deserialize<Plan>(await File.ReadAllTextAsync(path, cancellationToken), Json)
            ?? throw new InvalidDataException("The owned recovery plan is empty.");
        if (plan.Phase != phase || source.Intent.Payload?.GetProperty("eventName").GetString() != plan.EventName ||
            plan.TargetExecutionId is { } target && target != acceptedExecution) return;
        if (!Regex.IsMatch(plan.CaseId, "^[a-z0-9-]{1,64}$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("The owned recovery case name is invalid.");
        var caseDirectory = Path.Combine(directory, plan.CaseId); Directory.CreateDirectory(caseDirectory);
        var token = Path.Combine(caseDirectory, "claimed");
        try { using var claimed = new FileStream(token, FileMode.CreateNew, FileAccess.Write, FileShare.Read); claimed.Flush(true); }
        catch (IOException) when (File.Exists(token)) { return; }
        var marker = new { plan.CaseId, Phase = phase, SourceOutboxItemId = source.SourceOutboxItemId,
            source.SourceCommitId, source.IntentFingerprint, Scope = source.Scope.Value, source.Claim.OwnerId,
            source.Claim.FencingToken, source.Claim.VisibleAfter, source.Intent.WorkflowExecutionId,
            Snapshot = source.Claim.Item.RecipientSnapshot, Proposal = proposal, AcceptedExecution = acceptedExecution,
            AcceptedBookmark = acceptedBookmark, CapturedAtUtc = DateTimeOffset.UtcNow };
        var temporary = Path.Combine(caseDirectory, "reached.tmp");
        await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { await JsonSerializer.SerializeAsync(stream, marker, Json, cancellationToken); stream.Flush(true); }
        File.Move(temporary, Path.Combine(caseDirectory, "reached.json"));
        while (!File.Exists(Path.Combine(caseDirectory, "release")))
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
    }

    // An observation of the real refusal, never a provider/claim result used by application code.
    public async ValueTask RecordRejectedProposalAsync(PostCommitDeliveryContext source, bool attemptedProviderInstall,
        Exception exception, bool cancellationRequested)
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
            CancellationRequested = cancellationRequested, ObservedAtUtc = DateTimeOffset.UtcNow };
        var temporary = Path.Combine(caseDirectory, "rejected.tmp");
        if (File.Exists(temporary) || File.Exists(Path.Combine(caseDirectory, "rejected.json"))) return;
        await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { await JsonSerializer.SerializeAsync(stream, marker, Json); stream.Flush(true); }
        File.Move(temporary, Path.Combine(caseDirectory, "rejected.json"));
    }

    private sealed record Plan(string CaseId, string Phase, string EventName, string? TargetExecutionId = null);
}
