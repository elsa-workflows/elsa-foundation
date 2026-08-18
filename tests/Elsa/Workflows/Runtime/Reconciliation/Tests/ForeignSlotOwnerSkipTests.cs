using Elsa.Activities.Testing;
using Elsa.Locking.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Options;
using Elsa.Workflows.Runtime.Reconciliation.Services;
using Elsa.Workflows.Runtime.Reconciliation.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Reconciliation.Tests;

/// <summary>
/// T118 (Joey, 2026-08-17, amending FR-B-006): reconciliation <b>never</b> reclaims a definition's default slot
/// from another activation source, and a skip for that reason reaches the boot log by name.
/// </summary>
/// <remarks>
/// <para>
/// The rule is <b>generic</b>, which is why the incumbent here is a second <em>reconciliation</em> source rather
/// than the publish pipeline. Reconciliation yields to whoever owns the slot; it has no way to ask who that is,
/// and a test that only held for one particular owner would be asserting the coupling this design forbids. The
/// production asymmetry — publishing takes over, reconciliation does not — lives entirely in who passes
/// <see cref="WorkflowActivationOwnershipIntent.TakeOver"/>, and is covered where that choice is made.
/// </para>
/// <para>
/// <b>The candidate deliberately sorts above what is serving.</b> Latest-wins would activate it, so the reason it
/// does not is ownership rather than an earlier gate quietly refusing it first.
/// </para>
/// </remarks>
public sealed class ForeignSlotOwnerSkipTests : IDisposable
{
    private const string DefinitionId = "definition-invoice";
    private const string IncumbentSourceId = "another-mount";
    private const string IncumbentActivationId = "import:another-mount:definition-invoice:incumbent";

    private static readonly WorkflowActivationSource Incumbent =
        WorkflowActivationSource.ArtifactReconciliation(IncumbentSourceId);

    private readonly string _mount = Path.Combine(
        Path.GetTempPath(),
        "elsa-artifact-foreign-owner",
        Guid.NewGuid().ToString("N"));

    public ForeignSlotOwnerSkipTests() => Directory.CreateDirectory(_mount);

    public void Dispose()
    {
        if (Directory.Exists(_mount))
            Directory.Delete(_mount, true);
    }

    [Fact]
    public async Task A_foreign_owned_definition_is_skipped_by_name_and_is_never_reclaimed_on_a_later_pass()
    {
        await using var harness = ArtifactImportHarness.Build(_mount);
        var incumbent = ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-incumbent"), DefinitionId, "1.0.0");
        await ArtifactImportHarness.GiveTheSlotToAsync(harness, Incumbent, IncumbentActivationId, incumbent);

        // The mounted candidate is NEWER than what is serving, so latest-wins is not what stops it.
        var candidate = ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-candidate"), DefinitionId, "2.0.0");
        ArtifactClosureFixture.Mount(harness.Services, _mount, "invoice.json", ArtifactClosureFixture.Closure(candidate));

        var firstPass = await ArtifactImportHarness.ReconcileAsync(harness);

        var skip = Assert.Single(firstPass.Entries);
        Assert.Equal(WorkflowArtifactImportOutcome.Skipped, skip.Outcome);
        Assert.Equal(WorkflowArtifactSkipReason.ForeignSlotOwner, skip.SkipReason);
        Assert.Equal(candidate.Identity.ArtifactId, skip.ArtifactId);
        // Not a rejection: the artifact is sound, its unit imported, and it IS in the content-addressed store.
        Assert.Equal(0, firstPass.RejectedCount);
        Assert.True(await ArtifactImportHarness.IsInStoreAsync(harness, candidate.Identity.ArtifactId));

        // The OWNER is named — an operator must be told who holds the definition, not merely that somebody does.
        Assert.NotNull(skip.Diagnostic);
        Assert.Contains(Incumbent.Describe(), skip.Diagnostic!, StringComparison.Ordinal);
        Assert.Contains(DefinitionId, skip.Diagnostic!, StringComparison.Ordinal);
        Assert.Same(skip, Assert.Single(firstPass.OwnershipSkips));

        // Nothing moved, and a later pass does not change its mind: "never takes it back" is a property of every
        // pass, not just the first one to notice.
        var afterFirstPass = await ArtifactImportHarness.FindSlotAsync(harness, DefinitionId);
        Assert.Equal(IncumbentActivationId, afterFirstPass!.ActiveActivationId);
        Assert.Equal(Incumbent.SourceId, afterFirstPass.Source!.SourceId);

        var secondPass = await ArtifactImportHarness.ReconcileAsync(harness);

        Assert.Equal(WorkflowArtifactSkipReason.ForeignSlotOwner, Assert.Single(secondPass.Entries).SkipReason);
        var afterSecondPass = await ArtifactImportHarness.FindSlotAsync(harness, DefinitionId);
        Assert.Equal(IncumbentActivationId, afterSecondPass!.ActiveActivationId);
        Assert.Equal(afterFirstPass.Revision, afterSecondPass.Revision);
    }

    /// <summary>
    /// The skip has to reach the boot log, or the mount is silently ignored for that definition.
    /// </summary>
    /// <remarks>
    /// Asserted at <see cref="LogLevel.Warning"/> and against the entry's own diagnostic, because the failure this
    /// guards against is precisely a skip that is recorded somewhere nobody reads. The reconciler is a stub here on
    /// purpose — the obligation under test belongs to the startup task, and the reconciler's own half is covered by
    /// the real composition above.
    /// </remarks>
    [Fact]
    public async Task The_startup_task_surfaces_an_ownership_skip_at_warning_level()
    {
        const string diagnostic = "definition 'definition-invoice' slot 'default' is owned by activation source 'publishing'.";
        var logger = new RecordingLogger();
        var task = new WorkflowArtifactReconcilerStartupTask(
            new StubReconciler(new WorkflowArtifactReconciliationResult(
            [
                WorkflowArtifactImportEntry.Skipped(
                    "invoice.json",
                    ArtifactImportHarness.SourceId,
                    "artifact-1",
                    DefinitionId,
                    "2.0.0",
                    WorkflowArtifactSkipReason.ForeignSlotOwner,
                    diagnostic),
            ])),
            new GrantingLockProvider(),
            MicrosoftOptions.Create(new WorkflowArtifactReconcilerStartupTaskOptions()),
            logger);

        await task.ExecuteAsync(CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains(diagnostic, entry.Message, StringComparison.Ordinal);
        Assert.Contains("artifact-1", entry.Message, StringComparison.Ordinal);
    }

    private sealed class StubReconciler(WorkflowArtifactReconciliationResult result) : IWorkflowArtifactReconciler
    {
        public ValueTask<WorkflowArtifactReconciliationResult> ReconcileAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(result);
    }

    private sealed class RecordingLogger : ILogger<WorkflowArtifactReconcilerStartupTask>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        // Information is filtered out so the assertion is about the ONE entry the skip produces; the task's
        // lock-acquired chatter is not the subject.
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                Entries.Add((logLevel, formatter(state, exception)));
        }
    }

    private sealed class GrantingLockProvider : IDistributedLockProvider
    {
        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            new Handle();

        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDistributedSynchronizationHandle?>(new Handle());

        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle());

        private sealed class Handle : IDistributedSynchronizationHandle
        {
            public CancellationToken HandleLostToken => CancellationToken.None;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
