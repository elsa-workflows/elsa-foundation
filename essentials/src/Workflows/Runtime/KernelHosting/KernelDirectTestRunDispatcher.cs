using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.KernelApi;
using Elsa.Workflows.Runtime.KernelHost;
using Elsa.Workflows.Runtime.Services.Executions;
using Kernel.Foundation.Contracts;
using Kernel.Foundation.Hosting;
using Kernel.Native;
using Kernel.WorkflowActor;
using Microsoft.Extensions.DependencyInjection;
using Admission = Kernel.Foundation.Contracts.Admission;

namespace Elsa.Workflows.Runtime.KernelHosting;

/// <summary>Admits the exact material and Runtime scope of an ordinary direct authoring Test Run.</summary>
internal sealed class KernelDirectTestRunDispatcher(IServiceScopeFactory scopes, KernelServing serving, PersistenceScope persistence,
    KernelShellRuntime runtime, KernelTestRuns testRuns, KernelOperatorIngress operators, TimeProvider time) : IWorkflowDirectTestRunDispatcher
{
    private static readonly TimeSpan StopBudget = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan OutcomeBudget = TimeSpan.FromSeconds(5);

    public bool SupportsTrigger(string driverId) => driverId is "Elsa.Event" or "Elsa.HttpEndpoint" or "Elsa.Timer" or "Elsa.Cron";

    public async ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(WorkflowExecutionStartDispatchRequest request,
        WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.TestRun,
        WorkflowExecutionCommandDispatchOptions? dispatchOptions = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var triggered = request.TriggerNodeId is not null;
        var ordinary = request is { TriggerNodeId: null, StimulusInput: null, IdempotencyKey: null, WorkflowExecutionId: null };
        var prepared = request is { TriggerNodeId: not null, IdempotencyKey: not null, WorkflowExecutionId: not null };
        if (requiredScope != WorkflowExecutableReferenceScope.TestRun || (!ordinary && !prepared) ||
            request is not { RunKind: WorkflowRunKind.TestRun, TestScope: { } test, SourceSelection.SourceReferenceId: { },
                DispatchNestingDepth: 0, ParentWorkflowExecutionId: null, StartAuthority: null } ||
            (test.TenantId ?? PersistenceScope.DefaultValue) != persistence.Value)
            throw new InvalidOperationException("The direct Test Run must select an exact source and the owning Runtime scope.");

        await using var scope = await scopes.CreateAsyncScopeAsync(persistence);
        var services = scope.ServiceProvider;
        var clock = services.GetRequiredService<TimeProvider>();
        var executable = await services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(request.ArtifactId, cancellationToken)
            ?? throw new WorkflowExecutableNotFoundException(request.ArtifactId);
        var sources = new WorkflowExecutableStartSourceResolver(services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>(), clock);
        var source = await sources.ResolveDirectTestAsync(request.ArtifactId, request.SourceSelection, persistence, cancellationToken);
        var stored = await services.GetRequiredService<IWorkflowTestScopeStore>().FindAsync(test.ScopeId, cancellationToken);
        if (stored is not { State: WorkflowTestScopeState.Open } || !WorkflowTestScope.ContextEquals(stored.Scope, test) ||
            source.SourceReferenceId != test.ScopeId || source.TenantId != test.TenantId ||
            source.ExpiresAt != test.ExpiresAt || test.IsExpired(clock.GetUtcNow()))
            throw new WorkflowExecutableReferenceRejectedException(request.ArtifactId, requiredScope,
                WorkflowExecutableReferenceRejectionReason.NoLiveReference);

        var inputs = WorkflowStartDispatcher.ApplyInputContract(executable, request.Inputs,
            services.GetRequiredService<IWorkflowExecutableInputValidator>());
        var policies = services.GetServices<IWorkflowExecutableStartPolicy>().ToArray();
        if (policies is not [var policy])
            throw new InvalidOperationException($"Runtime composition requires exactly one {nameof(IWorkflowExecutableStartPolicy)} registration, but found {policies.Length}.");
        await WorkflowStartDispatcher.EnforceStartPolicyAsync(request, executable.Identity, test.Partition, policy, cancellationToken);

        var provenance = WorkflowExecutableSourceProvenance.From(source);
        var mode = services.GetRequiredService<IRuntimePersistenceModeResolver>().Resolve(executable);
        if (mode == PersistenceMode.Ephemeral && (!runtime.TakesEphemeralTestRuns || triggered))
            return Refused("EphemeralTestRunUnavailable");

        Workflow workflow;
        try
        {
            // Fire once runs on the selected test root and creates no recurring schedule.
            workflow = NativeExecutableMapper.Map(executable, serving.Capabilities with { Children = false, Schedules = true },
                source.DefinitionId, scopedHttpTransport: true);
        }
        catch (NativeMappingRefusedException error)
        {
            return Refused(error.Message);
        }

        var input = new JsonObject();
        foreach (var (name, value) in inputs) input.Add(name, JsonNode.Parse(value.GetRawText()));
        JsonNode? stimulus = request.StimulusInput is { } payload ? JsonNode.Parse(payload.GetRawText()) : null;
        var http = triggered && NativeStartTriggers.StartEndpoints(workflow).Any(node => node.Id == request.TriggerNodeId);
        if (http)
        {
            if (request.StimulusInput?.Deserialize<HttpTestRequest>() is not { } wire)
                return Refused("The selected HTTP Test Run is missing its request.");
            try { stimulus = Values.From(wire.ToNative()); }
            catch (HttpFieldsException error) { return Refused(error.Message); }
        }
        else if (triggered && !NativeStartTriggers.StartEvents(workflow).Any(node => node.Id == request.TriggerNodeId))
        {
            try
            {
                var schedule = NativeSchedules.ForPublications([workflow], CronosCalendar.Error).All
                    .SingleOrDefault(item => item.Node == request.TriggerNodeId);
                if (schedule is null) return Refused("The selected node is not a start trigger in this executable.");
                stimulus = JsonValue.Create(schedule.Key);
            }
            catch (NativeScheduleException error) { return Refused(error.Message); }
        }
        var subject = request.Authority.Metadata.GetValueOrDefault("elsa.identity.subject-kind") == "persisted-user"
            ? request.Authority.Metadata.GetValueOrDefault("elsa.identity.subject-id") : null;
        var selection = new WorkflowTestStart(test.ScopeId, test.ExpiresAt, test.TenantId, test.Partition.Value,
            source.SourceReferenceId, source.SourceKind, source.SourceId, source.SourceVersion, source.DefinitionVersionId,
            source.ArtifactVersion, KernelPublishedRunDispatcher.SourceDigest(source), subject);
        var native = NativeExecutionModel.Start(workflow.Id, mode.ToString(), input, request.TriggerNodeId, stimulus, workflow.Definition)
            with { ExecutableSelection = workflow.ExecutableSelection };
        var spec = WorkflowActor.Start(native, null, selection);
        if (mode == PersistenceMode.Ephemeral) return await StartInlineAsync();
        if (http && request.TriggerMetadata.GetValueOrDefault("http:responseMode") == "Sync")
        {
            if (dispatchOptions?.TestRunHttpBudget is not { } budget)
                return Refused("The selected HTTP Test Run is missing its request timeout.");
            if (await CommittedDuplicateAsync(stored) is { } previous) return previous;
            var reply = await runtime.StartHeldTestAsync(test.ScopeId, spec, budget, cancellationToken);
            if (reply.Error == "duplicate" || reply.RefusalReason == RefusalReason.TargetMismatch)
            {
                var existing = await services.GetRequiredService<IWorkflowTestScopeStore>().FindAsync(test.ScopeId, cancellationToken);
                if (await CommittedDuplicateAsync(existing) is { } duplicate) return duplicate;
            }
            var response = KernelHttpEdge.Answer(reply);
            if (reply.Error == "timeout") response = response with { StatusCode = 408 };
            var heldAdmission = reply.Actor is { } actor
                ? new Admission(AdmissionOutcome.Accepted, actor)
                : new Admission(AdmissionOutcome.Refused, Reason: reply.RefusalReason, RetryAfter: reply.RetryAfter);
            return KernelPublishedRunDispatcher.Result(test.ScopeId, executable.Identity, provenance, heldAdmission, clock.GetUtcNow(),
                reply.Error, new(response.StatusCode, response.ContentType, response.Body, response.Headers));

            async ValueTask<WorkflowExecutionStartDispatchResult?> CommittedDuplicateAsync(WorkflowTestScopeRecord? current)
            {
                if (current is not { State: WorkflowTestScopeState.Open, IsolatedActor: { } binding } ||
                    !WorkflowTestScope.ContextEquals(current.Scope, test) || test.IsExpired(clock.GetUtcNow()) ||
                    await ObserveAsync(current, source, cancellationToken) is not
                        (WorkflowDirectTestRunObservation.Started or WorkflowDirectTestRunObservation.Ended))
                    return null;
                return KernelPublishedRunDispatcher.Result(test.ScopeId, executable.Identity, provenance,
                    new(AdmissionOutcome.Duplicate, new(binding.ActorReference)), clock.GetUtcNow());
            }
        }
        var admission = await runtime.StartDirectTestAsync(test.ScopeId, spec, cancellationToken);
        return KernelPublishedRunDispatcher.Result(test.ScopeId, executable.Identity, provenance, admission, clock.GetUtcNow());

        WorkflowExecutionStartDispatchResult Refused(string reason) => KernelPublishedRunDispatcher.Result(
            test.ScopeId, executable.Identity, provenance, new(AdmissionOutcome.Refused), clock.GetUtcNow(), reason);

        async ValueTask<WorkflowExecutionStartDispatchResult> StartInlineAsync()
        {
            var admissions = services.GetRequiredService<IWorkflowTestScopeAdmissionStore>();
            WorkflowExecutionStartDispatchResult Result(InlineStartOutcome outcome) => KernelPublishedRunDispatcher.InlineResult(
                test.ScopeId, executable.Identity, provenance, outcome, clock.GetUtcNow(), testRun: true);
            var unknown = new InlineStartOutcome(InlineStartDisposition.Unknown);
            if (!await admissions.BeginInlineStartAsync(test, clock.GetUtcNow, cancellationToken)) return Result(unknown);

            var outcome = await runtime.StartInlineTestAsync(test.ScopeId, spec, workflow, cancellationToken);
            var settlement = outcome switch
            {
                { Disposition: InlineStartDisposition.Volatile, Status: ActorStatus.Finished } => WorkflowTestRunInlineState.CompletedWithoutRecord,
                { Disposition: InlineStartDisposition.Volatile } => throw new InvalidOperationException("An inline Test Run without a record must have completed."),
                { Disposition: InlineStartDisposition.Refused } => WorkflowTestRunInlineState.Refused,
                { Disposition: InlineStartDisposition.Unavailable } => WorkflowTestRunInlineState.Unavailable,
                _ => (WorkflowTestRunInlineState?)null
            };
            if (settlement is not { } definite) return Result(outcome);

            // A disconnected HTTP caller does not undo an outcome already returned by the owner.
            using var record = new CancellationTokenSource(OutcomeBudget, clock);
            try
            {
                return await admissions.SettleInlineStartAsync(test, definite, record.Token) ? Result(outcome) : Result(unknown);
            }
            catch (OperationCanceledException) when (record.IsCancellationRequested)
            {
                return Result(unknown);
            }
        }
    }

    public async ValueTask<WorkflowDirectTestRunObservation?> ObserveAsync(WorkflowTestScopeRecord scope,
        WorkflowExecutableSourceReference source, CancellationToken cancellationToken = default)
    {
        var exact = scope.Scope;
        if ((exact.TenantId ?? PersistenceScope.DefaultValue) != persistence.Value ||
            source.Scope != WorkflowExecutableReferenceScope.TestRun || source.SourceReferenceId != exact.ScopeId ||
            source.TenantId != exact.TenantId || source.CreatedAt != scope.CreatedAt || source.ExpiresAt != exact.ExpiresAt)
            return null;
        if (scope.IsolatedActor is not { } binding)
            return scope.InlineStart switch
            {
                WorkflowTestRunInlineState.Pending => WorkflowDirectTestRunObservation.Pending,
                WorkflowTestRunInlineState.CompletedWithoutRecord => WorkflowDirectTestRunObservation.CompletedWithoutRecord,
                WorkflowTestRunInlineState.Refused => WorkflowDirectTestRunObservation.Disarmed,
                WorkflowTestRunInlineState.Unavailable => WorkflowDirectTestRunObservation.Unavailable,
                _ => WorkflowDirectTestRunObservation.Prepared
            };

        var actor = new ActorRef(binding.ActorReference);
        var result = await testRuns.Reads.GetAsync(actor, cancellationToken);
        if (result.Missing)
            return (await runtime.ConfirmPreparedTestAsync(actor, binding.Issuance, cancellationToken)) switch
            {
                RootStartProof.PreparationDisarmed => WorkflowDirectTestRunObservation.Disarmed,
                RootStartProof.BirthRetired => WorkflowDirectTestRunObservation.Retired,
                _ => WorkflowDirectTestRunObservation.Pending
            };
        if (result.Run is not { Run: { TestStart: { } selected } run } found || !MatchesScope(selected, exact) ||
            run.Model != NativeExecutionModel.ModelKey || run.Workflow != source.ArtifactId || run.Definition != source.DefinitionId ||
            selected.SourceDigest != KernelPublishedRunDispatcher.SourceDigest(source))
            return null;

        return IsStopped(found.View) ? WorkflowDirectTestRunObservation.Ended : WorkflowDirectTestRunObservation.Started;
    }

    public async ValueTask RequestStopAsync(WorkflowTestScope scope, string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        await using var runtime = await scopes.CreateAsyncScopeAsync(persistence);
        var current = await runtime.ServiceProvider.GetRequiredService<IWorkflowTestScopeStore>()
            .FindAsync(scope.ScopeId, cancellationToken);
        if (current is not { State: WorkflowTestScopeState.Closing or WorkflowTestScopeState.Closed } ||
            !WorkflowTestScope.ContextEquals(current.Scope, scope) ||
            current.IsolatedActor is { } different && different.ActorReference != workflowExecutionId)
            throw new TestScopeAdmissionException("Only the exact closed Test Run scope can stop its test actor.");

        var actor = new ActorRef(workflowExecutionId);
        var tenant = testRuns.Reads.Tenant;
        using var budget = new CancellationTokenSource(StopBudget, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);
        var token = linked.Token;
        try
        {
            var ingress = await operators.Open((binding, target, kind) =>
                binding.Tenant == tenant.Tenant && target == actor && kind == Kinds.Terminate, token);
            while (true)
            {
                var view = await FindActorAsync(scope, actor, token);
                if (IsStopped(view)) return;
                if (view is null && await IsRetiredAsync(actor, current.IsolatedActor, token)) return;
                if (view?.Selection is { } selection)
                {
                    var deadline = checked(await operators.ReadClock(token) + (long)StopBudget.TotalMilliseconds);
                    var command = new OperatorCommand(selection, ExpectedTarget: actor);
                    var reply = await ingress.RequestAsync(tenant, new Target.Ref(actor), Guid.NewGuid().ToString("N"),
                        Kinds.Terminate, JsonSerializer.Serialize(command), null, new(null, deadline), token);
                    if (reply is { Error: null, Item: "terminated" or "terminated and kept" }) return;
                }
                await Task.Delay(RetryDelay, time, token);
            }
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The Test Run stop did not settle. Retry Stop for the selected test run.");
        }
    }

    public async ValueTask<bool> IsStoppedAsync(WorkflowTestScope scope, string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        var actor = new ActorRef(workflowExecutionId);
        var view = await FindActorAsync(scope, actor, cancellationToken);
        if (view is not null) return IsStopped(view);
        await using var bound = await scopes.CreateAsyncScopeAsync(persistence);
        var current = await bound.ServiceProvider.GetRequiredService<IWorkflowTestScopeStore>().FindAsync(scope.ScopeId, cancellationToken);
        return current is not null && WorkflowTestScope.ContextEquals(current.Scope, scope) &&
            await IsRetiredAsync(actor, current.IsolatedActor, cancellationToken);
    }

    private async ValueTask<ActorView?> FindActorAsync(WorkflowTestScope scope, ActorRef actor, CancellationToken token)
    {
        var run = await testRuns.Reads.GetAsync(actor, token);
        return run.Run is { Run.TestStart: { } selection } test && MatchesScope(selection, scope) ? test.View : null;
    }

    private static bool MatchesScope(WorkflowTestStart selection, WorkflowTestScope scope) =>
        selection.ScopeId == scope.ScopeId && selection.SourceReferenceId == scope.ScopeId && selection.Tenant == scope.TenantId &&
        selection.Partition == scope.Partition.Value && selection.ExpiresAt == scope.ExpiresAt;

    private static bool IsStopped(ActorView? view) =>
        view is { Header.Status: not ActorStatus.Running, Park: null };

    private async ValueTask<bool> IsRetiredAsync(ActorRef actor, WorkflowTestRunActorBinding? binding, CancellationToken token) =>
        binding is { } bound && bound.ActorReference == actor.Id &&
        (await runtime.ConfirmPreparedTestAsync(actor, bound.Issuance, token)) is RootStartProof.BirthRetired or RootStartProof.PreparationDisarmed;

    private sealed record HttpTestRequest(string Path, string Method, Dictionary<string, string[]> Headers,
        Dictionary<string, string[]> Query, string? Body, Dictionary<string, string> RouteData)
    {
        internal HttpRequest ToNative() => new(Method, Path, ParsedBody())
        {
            Headers = HttpFields.RequestHeaders(Pairs(Headers)),
            Query = HttpFields.Query(Pairs(Query)),
            RouteValues = RouteData
        };

        private JsonNode? ParsedBody()
        {
            if (string.IsNullOrEmpty(Body)) return null;
            var contentType = Headers.FirstOrDefault(field => field.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                .Value?.FirstOrDefault();
            var mediaType = contentType?.Split(';', 2)[0].Trim();
            if (mediaType is null) return null;
            if (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
                mediaType.Equals("text/json", StringComparison.OrdinalIgnoreCase) ||
                mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase))
            {
                try { return JsonNode.Parse(Body); }
                catch (JsonException) { return null; }
            }
            return mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ? JsonValue.Create(Body) : null;
        }

        private static IEnumerable<KeyValuePair<string, string>> Pairs(Dictionary<string, string[]> fields) =>
            fields.SelectMany(field => field.Value.Select(value => KeyValuePair.Create(field.Key, value)));
    }
}
