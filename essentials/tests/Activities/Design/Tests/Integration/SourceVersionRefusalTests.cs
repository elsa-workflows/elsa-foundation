using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Core.Reconciliation.Models;
using Elsa.Activities.Design.Reconciliation;
using Elsa.Primitives.Enums;
using Elsa.Activities.Design.Reconciliation.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Activities.Design.Tests.Integration;

/// <summary>
/// A publishing bridge can refuse one source-owned version. The reconciler then records and logs that version's
/// diagnostics, leaves it out of the catalog it has seen this pass, and goes on with the remaining candidates.
/// </summary>
public sealed class SourceVersionRefusalTests
{
    private const string TypeKey = "Acme.Activities.Summarize";

    [Fact]
    public async Task A_refused_version_is_recorded_logged_and_skipped_and_the_pass_goes_on()
    {
        var publisher = new RefusingPublisher("1.0.0");
        var diagnostics = new InMemoryActivityReconciliationDiagnosticStore();
        var logger = new RecordingLogger();
        var source = new InMemoryReconcilerHarness.InMemorySource("acme", "Configuration", Model("1.0.0"), Model("2.0.0"));

        await InMemoryReconcilerHarness.BuildReconciler(new(), source, sourceVersionPublisher: publisher, diagnosticStore: diagnostics, logger: logger)
            .Reconcile(CancellationToken.None);

        var refusedId = ActivityCatalogStableIds.VersionId(TypeKey, "1.0.0")!;
        // One retry round, which publishes nothing, ends the pass.
        Assert.Equal(["1.0.0", "2.0.0", "1.0.0"], publisher.Calls.Select(x => x.Version));
        Assert.Equal("acme.refused", Assert.Single(diagnostics.Find(refusedId)).Code);
        Assert.Empty(diagnostics.Find(ActivityCatalogStableIds.VersionId(TypeKey, "2.0.0")!));
        var warning = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains(refusedId, warning.Message, StringComparison.Ordinal);
        Assert.Contains("acme.refused", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refused_version_is_not_indexed_as_seen_so_a_repeated_candidate_is_published_again()
    {
        var publisher = new RefusingPublisher("1.0.0");
        var source = new InMemoryReconcilerHarness.InMemorySource("acme", "Configuration", Model("1.0.0"), Model("1.0.0"));

        // Throw on duplicates: had the refusal been indexed, the second candidate would be taken for a persisted duplicate.
        await InMemoryReconcilerHarness.BuildReconciler(new(), source, DuplicateHandling.Throw, sourceVersionPublisher: publisher)
            .Reconcile(CancellationToken.None);

        // Both candidates were tried; the version was retried once, not once per candidate.
        Assert.Equal(3, publisher.Calls.Count);
    }

    [Fact]
    public async Task A_version_refused_until_a_later_candidate_publishes_is_published_in_the_same_pass_without_a_recorded_refusal()
    {
        // 1.0.0 uses 2.0.0, which comes later; 2.0.0 uses 3.0.0, which comes last. Two retry rounds publish both.
        var publisher = new DependentPublisher(("1.0.0", "2.0.0"), ("2.0.0", "3.0.0"));
        var diagnostics = new InMemoryActivityReconciliationDiagnosticStore();
        var logger = new RecordingLogger();
        var source = new InMemoryReconcilerHarness.InMemorySource("acme", "Configuration", Model("1.0.0"), Model("2.0.0"), Model("3.0.0"));

        await InMemoryReconcilerHarness.BuildReconciler(new(), source, sourceVersionPublisher: publisher, diagnosticStore: diagnostics, logger: logger)
            .Reconcile(CancellationToken.None);

        Assert.Equal(["3.0.0", "2.0.0", "1.0.0"], publisher.Published);
        Assert.Equal(["1.0.0", "2.0.0", "3.0.0", "1.0.0", "2.0.0", "1.0.0"], publisher.Calls);
        Assert.Empty(diagnostics.List());
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task A_pass_that_ends_on_an_exception_still_records_and_logs_what_it_refused_before()
    {
        var publisher = new RefusingPublisher("1.0.0");
        var diagnostics = new InMemoryActivityReconciliationDiagnosticStore();
        var logger = new RecordingLogger();
        // 2.0.0 is contributed twice with the same content; duplicate handling Throw ends the pass at the second one.
        var source = new InMemoryReconcilerHarness.InMemorySource("acme", "Configuration", Model("1.0.0"), Model("2.0.0"), Model("2.0.0"));

        await Assert.ThrowsAnyAsync<Exception>(() => InMemoryReconcilerHarness
            .BuildReconciler(new(), source, DuplicateHandling.Throw, sourceVersionPublisher: publisher, diagnosticStore: diagnostics, logger: logger)
            .Reconcile(CancellationToken.None));

        Assert.Equal("acme.refused", Assert.Single(diagnostics.Find(ActivityCatalogStableIds.VersionId(TypeKey, "1.0.0")!)).Code);
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("acme.refused", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Versions_that_use_each_other_are_refused_and_the_pass_ends()
    {
        var publisher = new DependentPublisher(("1.0.0", "2.0.0"), ("2.0.0", "1.0.0"));
        var diagnostics = new InMemoryActivityReconciliationDiagnosticStore();
        var source = new InMemoryReconcilerHarness.InMemorySource("acme", "Configuration", Model("1.0.0"), Model("2.0.0"));

        await InMemoryReconcilerHarness.BuildReconciler(new(), source, sourceVersionPublisher: publisher, diagnosticStore: diagnostics)
            .Reconcile(CancellationToken.None);

        Assert.Empty(publisher.Published);
        Assert.Equal(4, publisher.Calls.Count);
        Assert.Equal(2, diagnostics.List().Count);
    }

    [Fact]
    public async Task A_version_that_publishes_clears_what_an_earlier_pass_recorded_for_it()
    {
        var versionId = ActivityCatalogStableIds.VersionId(TypeKey, "1.0.0")!;
        var diagnostics = new InMemoryActivityReconciliationDiagnosticStore();
        diagnostics.Record(versionId, [Diagnostic(versionId)]);
        var source = new InMemoryReconcilerHarness.InMemorySource("acme", "Configuration", Model("1.0.0"));

        await InMemoryReconcilerHarness.BuildReconciler(new(), source, sourceVersionPublisher: new RefusingPublisher(), diagnosticStore: diagnostics)
            .Reconcile(CancellationToken.None);

        Assert.Empty(diagnostics.List());
    }

    [Fact]
    public async Task A_complete_pass_drops_entries_for_versions_it_did_not_refuse()
    {
        // 1.0.0 was refused earlier and the source now contributes a fixed 1.0.1 instead; 2.0.0 is still refused.
        var stale = ActivityCatalogStableIds.VersionId(TypeKey, "1.0.0")!;
        var stillRefused = ActivityCatalogStableIds.VersionId(TypeKey, "2.0.0")!;
        var diagnostics = new InMemoryActivityReconciliationDiagnosticStore();
        diagnostics.Record(stale, [Diagnostic(stale)]);
        var source = new InMemoryReconcilerHarness.InMemorySource("acme", "Configuration", Model("1.0.1"), Model("2.0.0"));

        await InMemoryReconcilerHarness.BuildReconciler(new(), source, sourceVersionPublisher: new RefusingPublisher("2.0.0"), diagnosticStore: diagnostics)
            .Reconcile(CancellationToken.None);

        Assert.Equal([stillRefused], diagnostics.List().Keys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)] // Only a warning: still no error saying why.
    public async Task A_refusal_without_an_error_is_still_recorded_and_logged_as_one(bool withWarning)
    {
        var versionId = ActivityCatalogStableIds.VersionId(TypeKey, "1.0.0")!;
        var diagnostics = new InMemoryActivityReconciliationDiagnosticStore();
        var logger = new RecordingLogger();
        var source = new InMemoryReconcilerHarness.InMemorySource("acme", "Configuration", Model("1.0.0"));

        await InMemoryReconcilerHarness.BuildReconciler(new(), source, sourceVersionPublisher: new SilentRefusingPublisher(withWarning), diagnosticStore: diagnostics, logger: logger)
            .Reconcile(CancellationToken.None);

        var recorded = diagnostics.Find(versionId);
        Assert.Equal(withWarning ? 2 : 1, recorded.Count);
        var refusal = Assert.Single(recorded, x => x.Code == "activity.source.publication-refused");
        Assert.Equal(ActivityDiagnosticSeverity.Error, refusal.Severity);
        Assert.Equal(new ActivityDiagnosticSubject("ActivitySourceVersion", versionId, ActivityCatalogStableIds.DefinitionId(TypeKey), Revision: 0), refusal.Subject);
        Assert.Equal(recorded.Count, logger.Entries.Count);
        Assert.All(logger.Entries, entry => Assert.Equal(LogLevel.Warning, entry.Level));
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("activity.source.publication-refused", StringComparison.Ordinal));
    }

    [Fact]
    public void The_feature_registers_a_replaceable_in_memory_diagnostic_store()
    {
        var defaults = new ServiceCollection();
        new ActivitiesDesignReconciliationFeature().ConfigureServices(defaults);
        var registration = Assert.Single(defaults, x => x.ServiceType == typeof(IActivityReconciliationDiagnosticStore));
        Assert.Equal(ServiceLifetime.Singleton, registration.Lifetime);
        Assert.Equal(typeof(InMemoryActivityReconciliationDiagnosticStore), registration.ImplementationType);

        var replaced = new ServiceCollection();
        replaced.AddSingleton<IActivityReconciliationDiagnosticStore, ReplacementStore>();
        new ActivitiesDesignReconciliationFeature().ConfigureServices(replaced);
        Assert.Equal(typeof(ReplacementStore), Assert.Single(replaced, x => x.ServiceType == typeof(IActivityReconciliationDiagnosticStore)).ImplementationType);
    }

    private static ActivityVersionReconciliationModel Model(string version) => new(
        Id: null,
        Version: version,
        ActivityTypeKey: TypeKey,
        DisplayName: "Summarize",
        Category: null,
        Description: null,
        ProviderKey: "acme.provider",
        ProviderSchemaVersion: "1",
        ConsumerKey: "acme.consumer",
        ConsumerSchemaVersion: "1",
        Descriptor: new { instructions = "Summarize." },
        Inputs: [],
        Outputs: [],
        DesignFacets: []);

    private static ActivityDiagnostic Diagnostic(string versionId) =>
        new("acme.refused", ActivityDiagnosticSeverity.Error, "Refused.", new("ActivitySourceVersion", versionId));

    /// <summary>Refuses the listed versions and publishes the rest, recording every call.</summary>
    private sealed class RefusingPublisher(params string[] refused) : IActivitySourceVersionPublisher
    {
        public List<(string DefinitionId, string Version)> Calls { get; } = [];

        public Task<ActivitySourceVersionPublicationResult> PublishAsync(IActivityDefinition definition, IActivityDefinitionVersion version, CancellationToken cancellationToken = default)
        {
            Calls.Add((definition.Id, version.Version));
            return Task.FromResult(refused.Contains(version.Version)
                ? ActivitySourceVersionPublicationResult.Refused([Diagnostic(version.Id)])
                : ActivitySourceVersionPublicationResult.Published);
        }
    }

    /// <summary>Refuses a version until the version it uses is published, as the template compiler refuses an unpublished dependency.</summary>
    private sealed class DependentPublisher(params (string Version, string Uses)[] uses) : IActivitySourceVersionPublisher
    {
        public List<string> Calls { get; } = [];
        public List<string> Published { get; } = [];

        public Task<ActivitySourceVersionPublicationResult> PublishAsync(IActivityDefinition definition, IActivityDefinitionVersion version, CancellationToken cancellationToken = default)
        {
            Calls.Add(version.Version);
            if (uses.Any(x => x.Version == version.Version && !Published.Contains(x.Uses)))
                return Task.FromResult(ActivitySourceVersionPublicationResult.Refused([Diagnostic(version.Id)]));
            Published.Add(version.Version);
            return Task.FromResult(ActivitySourceVersionPublicationResult.Published);
        }
    }

    private sealed class SilentRefusingPublisher(bool withWarning) : IActivitySourceVersionPublisher
    {
        public Task<ActivitySourceVersionPublicationResult> PublishAsync(IActivityDefinition definition, IActivityDefinitionVersion version, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActivitySourceVersionPublicationResult.Refused(withWarning
                ? [new("acme.note", ActivityDiagnosticSeverity.Warning, "Note.", new("ActivitySourceVersion", version.Id))]
                : []));
    }

    private sealed class ReplacementStore : IActivityReconciliationDiagnosticStore
    {
        public void Record(string definitionVersionId, IReadOnlyList<ActivityDiagnostic> diagnostics) { }
        public void Clear(string definitionVersionId) { }
        public IReadOnlyList<ActivityDiagnostic> Find(string definitionVersionId) => [];
        public IReadOnlyDictionary<string, IReadOnlyList<ActivityDiagnostic>> List() => new Dictionary<string, IReadOnlyList<ActivityDiagnostic>>();
    }

    private sealed class RecordingLogger : ILogger<ActivityVersionReconciler>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
                Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
