using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed partial class ActivityDraftTestRunServiceTests
{
    [Fact]
    public void Wrapper_handoff_preserves_the_original_twenty_argument_CLR_constructor() =>
        Assert.Single(typeof(ActivityDraftTestRunService).GetConstructors(), constructor => constructor.GetParameters().Length == 20);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Wrapper_handoff_retries_one_native_winning_source_after_failure_before_or_after_atomic_write(bool afterWrite)
    {
        WrapperLeaseProbe probe = null!;
        var fixture = await CreateFixture(childTemplate: ValidChildTemplate(),
            wrapRootWrites: manager => probe = new(manager) { FailBeforeOnce = !afterWrite, FailAfterOnce = afterWrite });
        var failure = await Assert.ThrowsAsync<ActivityDraftTestRunStorageException>(() => fixture.Service.StartAsync(fixture.Request));
        Assert.Same(probe.Cause, failure.InnerException);
        var id = ActivityDraftTestRunIdentity.CreateTestRunId(ActivityDraftTestRunIdentity.CreateOperationScope(null), fixture.Draft.Id, fixture.Request.IdempotencyKey);
        Assert.Equal(id, failure.TestRunId);
        var pending = (await fixture.Receipts.FindAsync(id))!; Assert.Equal(ActivityDraftTestRunReceiptStatus.Preparing, pending.Status);
        var before = (await fixture.References.ListAllAsync()).SingleOrDefault(reference => reference.SourceKind == "ActivityDraftTestRun");
        Assert.Equal(afterWrite, before is not null); Assert.False(fixture.Dispatcher.WasCalled);
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var result = await fixture.Service.StartAsync(fixture.Request); Assert.Equal("DispatchAmbiguous", result.Status);
        var winner = Assert.Single((await fixture.References.ListAllAsync()).Where(reference => reference.SourceKind == "ActivityDraftTestRun"));
        if (before is not null) Assert.Same(before, winner);
        Assert.Equal(pending.SourceReferenceExpiresAt, winner.ExpiresAt); Assert.Equal(winner.SourceReferenceId, result.SourceReferenceId);
        Assert.Equal(2, probe.Invocations.Count); Assert.Equal(2, probe.Invocations.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, probe.Pins.Count); Assert.All(probe.Pins, pin => Assert.Equal(winner.ArtifactId, pin.ArtifactId));
        // The real coordinator has released its invocation grants after each outcome.
        var guard = await fixture.Artifacts.TryBeginDeletionAsync(winner.ArtifactId, "post-write-proof", fixture.Clock.GetUtcNow().AddMinutes(1), fixture.Clock.GetUtcNow());
        Assert.NotNull(guard); Assert.True(await fixture.Artifacts.CancelDeletionAsync(guard!));
    }

    [Fact]
    public async Task Wrapper_handoff_uses_the_actual_first_winning_producer_pin_without_rewriting_material()
    {
        WrapperLeaseProbe probe = null!;
        var fixture = await CreateFixture(childTemplate: ValidChildTemplate(), wrapRootWrites: manager => probe = new(manager) { FailBeforeOnce = true });
        Assert.Same(probe.Cause, (await Assert.ThrowsAsync<ActivityDraftTestRunStorageException>(() => fixture.Service.StartAsync(fixture.Request))).InnerException);
        var original = Assert.Single(await fixture.Artifacts.ListAllAsync());
        var winner = CopyWrapper(original, original.Identity with { DefinitionId = "first-producer", DefinitionVersionId = "first-version", ArtifactVersion = "first-label" });
        Assert.True(await fixture.Artifacts.DeleteAsync(original.Identity.ArtifactId)); await fixture.Artifacts.SaveAsync(winner);
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var result = await fixture.Service.StartAsync(fixture.Request); Assert.Equal("DispatchAmbiguous", result.Status);
        Assert.Equal(winner.Identity, probe.Pins.Last()); Assert.Same(winner, await fixture.Artifacts.FindAsync(result.ArtifactId!));
        var source = (await fixture.References.FindAsync(result.SourceReferenceId!))!;
        Assert.Equal("activity-test-wrapper:definition-1", source.DefinitionId); Assert.NotEqual(winner.Identity.DefinitionId, source.DefinitionId);
    }

    [Theory]
    [InlineData("metadata")] [InlineData("hash")] [InlineData("incident")]
    public async Task Wrapper_handoff_refuses_conflicting_stored_material_before_any_source_or_dispatch(string defect)
    {
        WrapperLeaseProbe probe = null!;
        var fixture = await CreateFixture(childTemplate: ValidChildTemplate(), wrapRootWrites: manager => probe = new(manager) { FailBeforeOnce = true });
        Assert.Same(probe.Cause, (await Assert.ThrowsAsync<ActivityDraftTestRunStorageException>(() => fixture.Service.StartAsync(fixture.Request))).InnerException);
        var original = Assert.Single(await fixture.Artifacts.ListAllAsync());
        var json = JsonSerializer.SerializeToNode(original)!.AsObject();
        if (defect == "metadata")
        {
            // Nodes are constructor-derived from RootActivity; change the actual persisted root provenance.
            json["RootActivity"]!["Metadata"]!["activity.sourceReferenceId"] = "another-runs-template-reference";
        }
        else if (defect == "hash") json["Identity"]!["ArtifactHash"] = "sha256:conflicting";
        var changed = JsonSerializer.Deserialize<WorkflowExecutable>(json)!;
        if (defect == "incident")
        {
            // The normal fixture has no resume targets. A different incident policy is another unhashed field.
            changed = CopyWrapper(original, original.Identity, new("different-policy", "1"));
        }
        Assert.True(await fixture.Artifacts.DeleteAsync(original.Identity.ArtifactId)); await fixture.Artifacts.SaveAsync(changed);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.StartAsync(fixture.Request));
        Assert.Single(probe.Invocations); Assert.False(fixture.Dispatcher.WasCalled);
        Assert.DoesNotContain(await fixture.References.ListAllAsync(), reference => reference.SourceKind == "ActivityDraftTestRun");
    }

    [Fact]
    public async Task Wrapper_handoff_refuses_a_legacy_coordinator_without_using_its_unfenced_callback()
    {
        var legacy = new LegacyWrapperLeaseManager();
        var fixture = await CreateFixture(childTemplate: ValidChildTemplate(), wrapRootWrites: _ => legacy);
        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Service.StartAsync(fixture.Request));
        Assert.False(legacy.OldCalled); Assert.False(fixture.Dispatcher.WasCalled);
        Assert.DoesNotContain(await fixture.References.ListAllAsync(), reference => reference.SourceKind == "ActivityDraftTestRun");
    }

    [Fact]
    public async Task Wrapper_handoff_preflights_the_selected_legacy_source_port_before_atomic_template_or_wrapper_writes()
    {
        var fixture = await CreateFixture(childTemplate: ValidChildTemplate(), selectRootPorts: (templates, references) =>
        {
            var legacy = new LegacyWrapperSources(references);
            return (new LegacyCompatibleTemplates(templates, references, legacy), legacy);
        });
        Assert.True(fixture.Templates.CanMaintainReferences(fixture.Artifacts, fixture.References));
        Assert.False(fixture.References.CanWriteRootReferences(fixture.Artifacts));
        var result = await fixture.Service.StartAsync(fixture.Request);
        Assert.Equal("ValidationRejected", result.Status); Assert.Equal("activity.test-run.preparation-unavailable", result.Failure?.Code);
        Assert.Null(await fixture.Templates.FindAsync(fixture.RootTemplate.TemplateId)); Assert.Empty(await fixture.Artifacts.ListAllAsync());
        Assert.Single(await fixture.References.ListAllAsync()); Assert.False(fixture.Dispatcher.WasCalled);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Wrapper_handoff_preserves_requested_cancellation_and_contextualizes_unrequested_cancellation(bool requested)
    {
        using var cancellation = new CancellationTokenSource(); var cause = new OperationCanceledException(cancellation.Token);
        var fixture = await CreateFixture(childTemplate: ValidChildTemplate(), wrapRootWrites: manager =>
            new WrapperLeaseProbe(manager) { FailBeforeOnce = true, Cause = cause, BeforeWrite = () => { if (requested) cancellation.Cancel(); } });
        if (requested) Assert.Same(cause, await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.StartAsync(fixture.Request, cancellation.Token)));
        else Assert.Same(cause, (await Assert.ThrowsAsync<ActivityDraftTestRunStorageException>(() => fixture.Service.StartAsync(fixture.Request, cancellation.Token))).InnerException);
        Assert.False(fixture.Dispatcher.WasCalled);
        Assert.DoesNotContain(await fixture.References.ListAllAsync(), reference => reference.SourceKind == "ActivityDraftTestRun");
    }

    [Theory]
    [InlineData("json")] [InlineData("database")] [InlineData("provider-domain")] [InlineData("aggregate")]
    public async Task Wrapper_handoff_exposes_a_catchable_domain_error_for_infrastructure_and_existing_provider_wrappers(string kind)
    {
        Exception cause = kind switch
        {
            "database" => new WrapperDatabaseFailure(),
            "provider-domain" => new InvalidOperationException("provider publication failed", new WrapperDatabaseFailure()),
            "aggregate" => new AggregateException(new IOException("write failed"), new InvalidOperationException("renewal failed")),
            _ => new JsonException("wrapper serialization failed")
        };
        var fixture = await CreateFixture(childTemplate: ValidChildTemplate(), wrapRootWrites: manager => new WrapperLeaseProbe(manager) { FailBeforeOnce = true, Cause = cause });
        var failure = await Assert.ThrowsAsync<ActivityDraftTestRunStorageException>(() => fixture.Service.StartAsync(fixture.Request));
        Assert.Same(cause, failure.InnerException);
        Assert.Equal(ActivityDraftTestRunIdentity.CreateTestRunId(ActivityDraftTestRunIdentity.CreateOperationScope(null), fixture.Draft.Id, fixture.Request.IdempotencyKey), failure.TestRunId);
        Assert.False(fixture.Dispatcher.WasCalled);
        Assert.DoesNotContain(await fixture.References.ListAllAsync(), reference => reference.SourceKind == "ActivityDraftTestRun");
    }

    private sealed class WrapperDatabaseFailure() : System.Data.Common.DbException("wrapper database failed");

    [Fact]
    public async Task Wrapper_handoff_one_concurrent_preparation_cannot_release_the_other_preparations_actual_native_grants()
    {
        ConcurrentWrapperLeases leases = null!;
        var fixture = await CreateFixture(childTemplate: ValidChildTemplate(), wrapRootWrites: manager => leases = new(manager));
        var first = fixture.Service.StartAsync(fixture.Request);
        await leases.FirstReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = fixture.Service.StartAsync(fixture.Request);
        try
        {
            await leases.SecondReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
            leases.ReleaseFirst.TrySetResult(); var result = await first;
            Assert.Equal("DispatchAmbiguous", result.Status);
            var guard = await fixture.Artifacts.TryBeginDeletionAsync(result.ArtifactId!, "between-preparations",
                fixture.Clock.GetUtcNow().AddMinutes(1), fixture.Clock.GetUtcNow());
            if (guard is not null) await fixture.Artifacts.CancelDeletionAsync(guard);
            Assert.Null(guard); // The second invocation still owns a real stored grant after the first releases.
            leases.ReleaseSecond.TrySetResult(); Assert.Equal(result.TestRunId, (await second).TestRunId);
            Assert.Single((await fixture.References.ListAllAsync()).Where(reference => reference.SourceKind == "ActivityDraftTestRun"));
        }
        finally { leases.ReleaseFirst.TrySetResult(); leases.ReleaseSecond.TrySetResult(); await Task.WhenAll(first, second); }
    }

    private static WorkflowExecutable CopyWrapper(WorkflowExecutable original, WorkflowExecutableIdentity pin, IncidentStrategyReference? incident = null) =>
        new(pin, original.RootActivity, original.ResumeTargets, original.CreatedAt, original.CompatibilityMetadata,
            original.InputContract, original.Dependencies, original.RuntimeRequirements, original.StorageDriverRequirements,
            incident ?? original.IncidentStrategy, original.PersistenceMode, original.WorkflowVariables);

    private sealed class WrapperLeaseProbe(IWorkflowExecutableRootWriteLeaseManager inner) : IWorkflowExecutableRootWriteLeaseManager
    {
        public Exception Cause { get; init; } = new IOException("wrapper source acknowledgement lost");
        public Action? BeforeWrite { get; init; }
        public bool FailBeforeOnce { get; set; }
        public bool FailAfterOnce { get; set; }
        public List<string> Invocations { get; } = [];
        public List<WorkflowExecutableIdentity> Pins { get; } = [];
        public ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Unfenced handoff is forbidden.");
        public async ValueTask ExecuteFencedAsync(IReadOnlyCollection<WorkflowExecutableIdentity> roots, string leaseId,
            Func<IReadOnlyList<WorkflowExecutableRootWriteLease>, CancellationToken, ValueTask> write, CancellationToken cancellationToken = default)
        {
            Invocations.Add(leaseId); Pins.Add(Assert.Single(roots));
            BeforeWrite?.Invoke();
            if (FailBeforeOnce) { FailBeforeOnce = false; throw Cause; }
            await inner.ExecuteFencedAsync(roots, leaseId, write, cancellationToken);
            if (FailAfterOnce) { FailAfterOnce = false; throw Cause; }
        }
    }

    private sealed class LegacyWrapperLeaseManager : IWorkflowExecutableRootWriteLeaseManager
    {
        public bool OldCalled { get; private set; }
        public ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write, CancellationToken cancellationToken = default)
        { OldCalled = true; return write(cancellationToken); }
    }

    private sealed class ConcurrentWrapperLeases(IWorkflowExecutableRootWriteLeaseManager inner) : IWorkflowExecutableRootWriteLeaseManager
    {
        private int entered;
        public TaskCompletionSource FirstReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSecond { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask ExecuteAsync(string id, string leaseId, Func<CancellationToken, ValueTask> write, CancellationToken ct = default) => throw new InvalidOperationException("Unfenced handoff is forbidden.");
        public ValueTask ExecuteFencedAsync(IReadOnlyCollection<WorkflowExecutableIdentity> roots, string leaseId,
            Func<IReadOnlyList<WorkflowExecutableRootWriteLease>, CancellationToken, ValueTask> write, CancellationToken ct = default) =>
            inner.ExecuteFencedAsync(roots, leaseId, async (grants, token) =>
            {
                var first = Interlocked.Increment(ref entered) == 1;
                (first ? FirstReady : SecondReady).TrySetResult();
                await (first ? ReleaseFirst : ReleaseSecond).Task.WaitAsync(token);
                await write(grants, token);
            }, ct);
    }

    // A coherent adapter built against the earlier source contract: its template path is supported,
    // but the additive executable-root capability retains the genuine default-refusal behavior.
    private sealed class LegacyWrapperSources(IWorkflowExecutableSourceReferenceStore inner) : IWorkflowExecutableSourceReferenceStore
    {
        public ValueTask SaveAsync(WorkflowExecutableSourceReference reference, CancellationToken ct = default) => inner.SaveAsync(reference, ct);
        public ValueTask<WorkflowExecutableSourceReference?> FindAsync(string id, CancellationToken ct = default) => inner.FindAsync(id, ct);
        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListPageAsync(WorkflowExecutableSourceReferencePageQuery query, CancellationToken ct = default) => inner.ListPageAsync(query, ct);
        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListByArtifactPageAsync(WorkflowExecutableSourceReferenceArtifactPageQuery query, CancellationToken ct = default) => inner.ListByArtifactPageAsync(query, ct);
        public ValueTask<IReadOnlyCollection<string>> ListUnreferencedArtifactIdsAsync(WorkflowExecutableArtifactCandidateBatch batch, DateTimeOffset now, CancellationToken ct = default) => inner.ListUnreferencedArtifactIdsAsync(batch, now, ct);
        public ValueTask<bool> RetireAsync(string id, DateTimeOffset now, string? reason = null, CancellationToken ct = default) => inner.RetireAsync(id, now, reason, ct);
        public ValueTask<bool> TryDeleteDoomedAsync(WorkflowExecutableSourceReference reference, DateTimeOffset now, CancellationToken ct = default) => inner.TryDeleteDoomedAsync(reference, now, ct);
        public ValueTask<IReadOnlyCollection<string>> DeleteExpiredOrRetiredAsync(WorkflowExecutableSourceReferenceCleanupBatch batch, DateTimeOffset now, CancellationToken ct = default) => inner.DeleteExpiredOrRetiredAsync(batch, now, ct);
    }

    private sealed class LegacyCompatibleTemplates(IExecutableActivityTemplateStore inner,
        IWorkflowExecutableSourceReferenceStore actualSources, LegacyWrapperSources selectedSources) : IExecutableActivityTemplateStore
    {
        public bool CanMaintainReferences(IWorkflowExecutableStore artifacts, IWorkflowExecutableSourceReferenceStore references) =>
            ReferenceEquals(references, selectedSources) && inner.CanMaintainReferences(artifacts, actualSources);
        public ValueTask<WorkflowExecutableSourceReference> SaveWithReferenceAsync(ExecutableActivityTemplate template, WorkflowExecutableSourceReference reference, CancellationToken ct = default) => inner.SaveWithReferenceAsync(template, reference, ct);
        public ValueTask SaveAsync(ExecutableActivityTemplate template, CancellationToken ct = default) => inner.SaveAsync(template, ct);
        public ValueTask<ExecutableActivityTemplate?> FindAsync(string id, CancellationToken ct = default) => inner.FindAsync(id, ct);
        public ValueTask<ExecutableActivityTemplate?> FindByHashAsync(string hash, CancellationToken ct = default) => inner.FindByHashAsync(hash, ct);
        public ValueTask<RuntimeStorePage<ExecutableActivityTemplate>> ListPageAsync(RuntimeStorePageRequest query, CancellationToken ct = default) => inner.ListPageAsync(query, ct);
    }
}
