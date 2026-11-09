using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Services;
using Xunit;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// The publish phase's states are what a migration engineer acts on, so each one that is not published says why and what
/// to change: a blocked version names what it waits on, an unexpected failure keeps its cause out of the response, and a
/// version below the definition's minimum gets advice that fits an import (spec 246 guidance review).
/// </summary>
public sealed class ReusableActivityImportPublishGuidanceTests
{
    [Fact]
    public async Task A_version_blocked_by_its_dependency_names_it_and_how_to_resume()
    {
        var phase = Phase(new RefusingPublisher("activity.publication.invalid", []));

        var states = await phase.PublishAsync([Activity("inner"), Activity("outer", dependsOn: "inner-version")], "receipt-1");

        var blocked = states.Single(state => state.ActivityDefinitionVersionId == "outer-version");
        Assert.Equal(ReusableActivityPublicationStatus.NotPublishedDependency, blocked.Status);
        Assert.Equal(ReusableActivityImportDiagnosticCodes.DependencyNotPublished, blocked.ErrorCode);
        Assert.Equal("inner-version", blocked.BlockedBy);
        var diagnostic = Assert.Single(blocked.Diagnostics!);
        Assert.Contains("'inner-version'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("new idempotency key", diagnostic.Remediation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unexpected_failure_keeps_its_cause_out_of_the_response()
    {
        var phase = Phase(new ThrowingPublisher("connection string secret=hunter2"));

        var state = Assert.Single(await phase.PublishAsync([Activity("inner")], "receipt-1"));

        Assert.Equal(ReusableActivityImportPublishPhase.PublicationFailed, state.ErrorCode);
        var diagnostic = Assert.Single(state.Diagnostics!);
        Assert.DoesNotContain("hunter2", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("host log", diagnostic.Remediation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_version_below_the_minimum_gets_import_advice_instead_of_choosing_a_version()
    {
        var bump = new ActivityDiagnostic(
            "activity.version.bump-insufficient", ActivityDiagnosticSeverity.Error, "Version 1.0.0 is below the reviewed minimum 3.0.0.",
            new("ActivityDraft", "draft", "definition"), Remediation: "Publish as 3.0.0 or a higher unique semantic version.");
        var phase = Phase(new RefusingPublisher("activity.version.bump-insufficient", [bump]));

        var state = Assert.Single(await phase.PublishAsync([Activity("inner")], "receipt-1"));

        var diagnostic = Assert.Single(state.Diagnostics!);
        Assert.Equal(bump.Message, diagnostic.Message);
        Assert.DoesNotContain("Publish as", diagnostic.Remediation, StringComparison.Ordinal);
        Assert.Contains("oldest first", diagnostic.Remediation, StringComparison.Ordinal);
    }

    private static ReusableActivityImportPublishPhase Phase(IReusableActivityImportPublisher publisher)
    {
        var stores = new EmptyStores();
        return new(stores, stores, stores, stores, publisher);
    }

    private static ImportedReusableActivity Activity(string name, params string[] dependsOn) => new(
        new ActivityDefinition { Id = $"{name}-definition", ActivityTypeKey = $"elsa3.{name}", Category = "Test" },
        new ImportedReusableActivityVersion(
            $"{name}-version", $"{name}-definition", "1.0.0", $"{name}-v1",
            new ActivityContract("1", [], [], []), JsonSerializer.SerializeToElement(new { }), dependsOn, DateTimeOffset.UnixEpoch),
        new ActivityDefinitionAuthoringState { Id = $"{name}-authoring", DefinitionId = $"{name}-definition" },
        new WorkflowDefinitionState([], null, [], [], null));

    private sealed class RefusingPublisher(string code, IReadOnlyList<ActivityDiagnostic> diagnostics) : IReusableActivityImportPublisher
    {
        public ValueTask<ReusableActivityImportPublishOutcome> PublishAsync(ReusableActivityImportPublishRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ReusableActivityImportPublishOutcome.Refused(code, diagnostics));
    }

    private sealed class ThrowingPublisher(string message) : IReusableActivityImportPublisher
    {
        public ValueTask<ReusableActivityImportPublishOutcome> PublishAsync(ReusableActivityImportPublishRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(message);
    }

    private sealed class EmptyStores :
        IActivityDefinitionVersionPublicationStore, IActivityDefinitionAuthoringStore, IActivityDefinitionDraftStore, ICreateActivityDraftCommand
    {
        public Task<ActivityDefinitionVersionPublication?> FindAsync(string definitionVersionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ActivityDefinitionVersionPublication?>(null);

        public Task<IReadOnlyList<ActivityDefinitionVersionPublication>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionVersionPublication>>([]);

        Task<ActivityDefinitionAuthoringState?> IActivityDefinitionAuthoringStore.FindAsync(string definitionId, CancellationToken cancellationToken) =>
            Task.FromResult<ActivityDefinitionAuthoringState?>(null);

        public Task<IReadOnlyList<ActivityDefinitionAuthoringState>> ListAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionAuthoringState>>([]);

        Task<ActivityDefinitionDraft?> IActivityDefinitionDraftStore.FindAsync(string draftId, CancellationToken cancellationToken) =>
            Task.FromResult<ActivityDefinitionDraft?>(null);

        Task<IReadOnlyList<ActivityDefinitionDraft>> IActivityDefinitionDraftStore.ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionDraft>>([]);

        public Task ExecuteAsync(CreateActivityDraftRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
