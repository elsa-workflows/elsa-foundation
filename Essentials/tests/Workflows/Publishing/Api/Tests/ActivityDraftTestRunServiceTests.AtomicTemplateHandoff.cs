using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed partial class ActivityDraftTestRunServiceTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Atomic_template_handoff_refuses_real_mixed_DI_before_any_hidden_material_or_reference_write(bool replaceReferences)
    {
        var services = new ServiceCollection(); services.AddSingleton<TimeProvider>(new FakeTimeProvider(new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero))); services.AddWorkflowRuntime();
        if (replaceReferences) services.AddScoped<IWorkflowExecutableSourceReferenceStore, InMemoryWorkflowExecutableSourceReferenceStore>();
        else services.AddScoped<IWorkflowExecutableStore, InMemoryWorkflowExecutableStore>();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }); using var scope = provider.CreateScope();
        AtomicTemplateProbe probe = null!;
        var fixture = await CreateFixture(inner => probe = new(inner), childTemplate: ValidChildTemplate(), runtimeServices: scope.ServiceProvider);
        Assert.False(fixture.Templates.CanMaintainReferences(fixture.Artifacts, fixture.References));
        var result = await fixture.Service.StartAsync(fixture.Request);
        Assert.Equal("ValidationRejected", result.Status); Assert.Equal("activity.test-run.preparation-unavailable", result.Failure?.Code);
        Assert.Equal(0, probe.AtomicCalls); Assert.Equal(0, probe.RawSaves); Assert.False(fixture.Dispatcher.WasCalled);
        Assert.Null(await fixture.Templates.FindAsync(fixture.RootTemplate.TemplateId));
        Assert.Single(await fixture.References.ListAllAsync()); Assert.Empty(await fixture.Artifacts.ListAllAsync());
    }

    [Fact]
    public async Task Atomic_template_handoff_uses_real_default_DI_and_the_winning_root_for_wrapper_placement()
    {
        var services = new ServiceCollection(); services.AddSingleton<TimeProvider>(new FakeTimeProvider(new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero))); services.AddWorkflowRuntime();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }); using var scope = provider.CreateScope();
        AtomicTemplateProbe probe = null!;
        var fixture = await CreateFixture(inner => probe = new(inner), childTemplate: ValidChildTemplate(), runtimeServices: scope.ServiceProvider);
        var result = await fixture.Service.StartAsync(fixture.Request);
        Assert.Equal("DispatchAmbiguous", result.Status); // Recording dispatcher deliberately loses acknowledgement; no host execution is claimed.
        Assert.True(fixture.Dispatcher.WasCalled); Assert.Equal(1, probe.AtomicCalls); Assert.Equal(0, probe.RawSaves);
        var winner = Assert.Single(probe.Winners); Assert.Same(winner, await fixture.References.FindAsync(winner.SourceReferenceId));
        Assert.Equal(WorkflowExecutableReferenceScope.TestRun, winner.Scope);
        var receipt = (await fixture.Receipts.FindAsync(result.TestRunId))!; Assert.Equal(receipt.SourceReferenceExpiresAt, winner.ExpiresAt);
        var wrapper = (await fixture.Artifacts.FindAsync(result.ArtifactId!))!;
        Assert.Equal(winner.SourceReferenceId, wrapper.RootActivity.Metadata["activity.sourceReferenceId"]);
        Assert.Equal(fixture.RootTemplate.TemplateHash, wrapper.CompatibilityMetadata["activity.templateHash"]);
    }

    [Theory]
    [InlineData("live")] [InlineData("expired")] [InlineData("retired")]
    public async Task Atomic_template_handoff_gives_a_new_run_its_own_root_without_borrowing_or_renewing_an_older_runs_root(string older)
    {
        AtomicTemplateProbe probe = null!; var fixture = await CreateFixture(inner => probe = new(inner), childTemplate: ValidChildTemplate());
        var first = await fixture.Service.StartAsync(fixture.Request); var original = Assert.Single(probe.Winners);
        if (older == "retired")
        {
            var retired = original.Retire(fixture.Clock.GetUtcNow(), "retired"); Assert.True(await fixture.References.TryRetireAsync(original, retired)); original = retired;
        }
        fixture.Clock.Advance(TimeSpan.FromMinutes(older == "expired" ? 31 : 1));
        var second = await fixture.Service.StartAsync(fixture.Request with { IdempotencyKey = "different-run" });
        Assert.NotEqual(first.TestRunId, second.TestRunId); Assert.Equal("DispatchAmbiguous", second.Status);
        var next = probe.Winners.Last(); Assert.NotEqual(original.SourceReferenceId, next.SourceReferenceId);
        var storedSecondWrapper = (await fixture.Artifacts.FindAsync(second.ArtifactId!))!;
        Assert.Equal(next.SourceReferenceId, storedSecondWrapper.RootActivity.Metadata["activity.sourceReferenceId"]);
        Assert.Equal(next.SourceReferenceId, storedSecondWrapper.RootActivity.Descriptor.Payload.GetProperty("sourceReferenceId").GetString());
        Assert.NotEqual(first.ArtifactId, second.ArtifactId); // Source binding participates in the existing full-descriptor hash.
        Assert.Equal(original.ArtifactId, next.ArtifactId); Assert.Equal(fixture.Clock.GetUtcNow().AddMinutes(30), next.ExpiresAt);
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(original, (await fixture.References.FindAsync(original.SourceReferenceId))!));
        Assert.True(next.IsLive(fixture.Clock.GetUtcNow())); Assert.Equal(2, (await fixture.References.ListAllAsync()).Count(reference => reference.ArtifactId == fixture.RootTemplate.TemplateId));
    }

    [Fact]
    public async Task Atomic_template_handoff_leaves_a_legacy_hash_only_root_untouched()
    {
        AtomicTemplateProbe probe = null!; var fixture = await CreateFixture(inner => probe = new(inner), childTemplate: ValidChildTemplate());
        var legacyId = "activity-test-template-ref-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fixture.RootTemplate.TemplateHash)));
        var legacy = new WorkflowExecutableSourceReference(legacyId, fixture.RootTemplate.TemplateId, "ActivityDraft", fixture.Draft.Id, "4",
            fixture.Draft.DefinitionId, "legacy-test-version", "draft-4", fixture.Clock.GetUtcNow(), null, WorkflowExecutableReferenceScope.TestRun,
            ExpiresAt: fixture.Clock.GetUtcNow().AddMinutes(-1), DeletedAt: fixture.Clock.GetUtcNow(), DeletedReason: "retired");
        await fixture.References.SaveAsync(legacy);
        var result = await fixture.Service.StartAsync(fixture.Request);
        Assert.Equal("DispatchAmbiguous", result.Status); Assert.NotEqual(legacyId, Assert.Single(probe.Winners).SourceReferenceId);
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(legacy, (await fixture.References.FindAsync(legacyId))!));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Atomic_template_handoff_retry_after_provider_failure_uses_one_stable_receipt_root(bool afterCommit)
    {
        AtomicTemplateProbe probe = null!;
        var fixture = await CreateFixture(inner => probe = new(inner) { FailBeforeOnce = !afterCommit, FailAfterOnce = afterCommit }, childTemplate: ValidChildTemplate());
        var failure = await Assert.ThrowsAsync<IOException>(() => fixture.Service.StartAsync(fixture.Request)); Assert.Same(probe.Cause, failure);
        var id = Elsa.Workflows.Publishing.Core.Models.ActivityDraftTestRunIdentity.CreateTestRunId(
            Elsa.Workflows.Publishing.Core.Models.ActivityDraftTestRunIdentity.CreateOperationScope(null), fixture.Draft.Id, fixture.Request.IdempotencyKey);
        var pending = (await fixture.Receipts.FindAsync(id))!; Assert.Equal("Preparing", pending.Status.ToString());
        Assert.False(fixture.Dispatcher.WasCalled); Assert.Empty(await fixture.Artifacts.ListAllAsync());
        Assert.Equal(afterCommit, await fixture.Templates.FindAsync(fixture.RootTemplate.TemplateId) is not null);
        var firstWinner = probe.Winners.FirstOrDefault(); fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var result = await fixture.Service.StartAsync(fixture.Request); Assert.Equal("DispatchAmbiguous", result.Status); Assert.Equal(id, result.TestRunId);
        Assert.Equal(2, probe.AtomicCalls); Assert.Equal(0, probe.RawSaves);
        var winner = probe.Winners.Last(); Assert.Equal(pending.SourceReferenceExpiresAt, winner.ExpiresAt);
        if (afterCommit) Assert.Same(firstWinner, winner);
        Assert.Single(await fixture.References.ListAllAsync(), reference => reference.ArtifactId == fixture.RootTemplate.TemplateId);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Atomic_template_handoff_cannot_restore_or_extend_a_root_changed_after_an_uncertain_commit(bool retired)
    {
        AtomicTemplateProbe probe = null!;
        var fixture = await CreateFixture(inner => probe = new(inner) { FailAfterOnce = true }, childTemplate: ValidChildTemplate());
        await Assert.ThrowsAsync<IOException>(() => fixture.Service.StartAsync(fixture.Request)); var original = Assert.Single(probe.Winners);
        var current = retired ? original.Retire(fixture.Clock.GetUtcNow(), "retired") : original with { ExpiresAt = fixture.Clock.GetUtcNow().AddMinutes(-1) };
        if (retired) Assert.True(await fixture.References.TryRetireAsync(original, current));
        else await fixture.References.SaveAsync(current); // Native admin mutation is a negative control, not a production expiry-renewal path.
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.StartAsync(fixture.Request));
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(current, (await fixture.References.FindAsync(original.SourceReferenceId))!));
        Assert.Single(await fixture.References.ListAllAsync(), reference => reference.ArtifactId == fixture.RootTemplate.TemplateId);
        Assert.False(fixture.Dispatcher.WasCalled); Assert.Empty(await fixture.Artifacts.ListAllAsync());
    }

    [Fact]
    public async Task Atomic_template_handoff_concurrent_preparation_adopts_one_receipt_root()
    {
        AtomicTemplateProbe probe = null!;
        var fixture = await CreateFixture(inner => probe = new(inner, waitForTwo: true), childTemplate: ValidChildTemplate());
        var results = await Task.WhenAll(fixture.Service.StartAsync(fixture.Request), fixture.Service.StartAsync(fixture.Request));
        Assert.Equal(results[0].TestRunId, results[1].TestRunId); Assert.Equal(2, probe.AtomicCalls);
        var winners = probe.Winners.ToArray(); Assert.Equal(2, winners.Length); Assert.Same(winners[0], winners[1]);
        Assert.Single(await fixture.References.ListAllAsync(), reference => reference.ArtifactId == fixture.RootTemplate.TemplateId);
        // This is preparation convergence; start effects and fresh-host recovery require their own evidence.
    }

    private sealed class AtomicTemplateProbe(IExecutableActivityTemplateStore inner, bool waitForTwo = false) : IExecutableActivityTemplateStore
    {
        private int calls, rawSaves, beforeFailure, afterFailure;
        private readonly TaskCompletionSource barrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int AtomicCalls => Volatile.Read(ref calls);
        public int RawSaves => Volatile.Read(ref rawSaves);
        public bool FailBeforeOnce { get; init; }
        public bool FailAfterOnce { get; init; }
        public IOException Cause { get; } = new("Template provider acknowledgement was unavailable.");
        public ConcurrentQueue<WorkflowExecutableSourceReference> Winners { get; } = new();
        public bool CanMaintainReferences(IWorkflowExecutableStore artifacts, IWorkflowExecutableSourceReferenceStore references) => inner.CanMaintainReferences(artifacts, references);
        public async ValueTask<WorkflowExecutableSourceReference> SaveWithReferenceAsync(ExecutableActivityTemplate template, WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref calls) == 2) barrier.TrySetResult();
            if (waitForTwo) await barrier.Task.WaitAsync(cancellationToken);
            if (FailBeforeOnce && Interlocked.Exchange(ref beforeFailure, 1) == 0) throw Cause;
            var winner = await inner.SaveWithReferenceAsync(template, reference, cancellationToken); Winners.Enqueue(winner);
            if (FailAfterOnce && Interlocked.Exchange(ref afterFailure, 1) == 0) throw Cause;
            return winner;
        }
        public ValueTask SaveAsync(ExecutableActivityTemplate template, CancellationToken cancellationToken = default)
        { Interlocked.Increment(ref rawSaves); return inner.SaveAsync(template, cancellationToken); }
        public ValueTask<ExecutableActivityTemplate?> FindAsync(string id, CancellationToken cancellationToken = default) => inner.FindAsync(id, cancellationToken);
        public ValueTask<ExecutableActivityTemplate?> FindByHashAsync(string hash, CancellationToken cancellationToken = default) => inner.FindByHashAsync(hash, cancellationToken);
        public ValueTask<RuntimeStorePage<ExecutableActivityTemplate>> ListPageAsync(RuntimeStorePageRequest request, CancellationToken cancellationToken = default) => inner.ListPageAsync(request, cancellationToken);
        public ValueTask<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => inner.DeleteAsync(id, cancellationToken);
        public ValueTask<bool> TryDeleteDoomedReferenceAsync(WorkflowExecutableSourceReference reference, DateTimeOffset now, CancellationToken cancellationToken = default) => inner.TryDeleteDoomedReferenceAsync(reference, now, cancellationToken);
        public ValueTask<bool> TryDeleteUnreferencedAsync(ExecutableActivityTemplate template, CancellationToken cancellationToken = default) => inner.TryDeleteUnreferencedAsync(template, cancellationToken);
    }
}
