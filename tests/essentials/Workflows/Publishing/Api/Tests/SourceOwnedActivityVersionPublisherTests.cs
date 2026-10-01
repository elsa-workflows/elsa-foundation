using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Services;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Exceptions;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class SourceOwnedActivityVersionPublisherTests
{
    [Fact]
    public async Task Publishes_provider_source_authority_and_stable_clr_template()
    {
        var command = new RecordingCommand();
        var publisher = CreatePublisher(command);

        await publisher.PublishAsync(Definition(), Version("version-1", "1.0.0"));

        var commit = Assert.Single(command.Commits);
        Assert.Equal(ActivityContentAuthorityKind.ProviderSource, commit.AuthoringState.ContentAuthority.Kind);
        Assert.Equal("elsa.clr-activity", commit.AuthoringState.ContentAuthority.AuthorityKey);
        Assert.Equal("assembly-a", commit.AuthoringState.ContentAuthority.SourceId);
        Assert.Equal("version-1", commit.AuthoringState.RecommendedVersionId);
        Assert.Equal(WellKnownRuntimeActivityConsumers.ClrActivity, commit.CatalogVersion.ConsumerKey);
        Assert.Equal("1", commit.CatalogVersion.ConsumerSchemaVersion);
        Assert.Equal(WellKnownRuntimeActivityConsumers.ClrActivity, commit.ExecutableTemplate.Root.Descriptor.ConsumerKey);
        Assert.Equal(commit.ExecutableTemplate.TemplateId, commit.Publication.TemplateId);
        Assert.Equal(commit.ExecutableTemplate.TemplateHash, commit.Publication.TemplateHash);
        Assert.True(Assert.Single(commit.Publication.Contract.Inputs).IsNullable);
        Assert.False(Assert.Single(commit.Publication.Contract.Outputs).IsNullable);
        Assert.Equal(ActivityDefinitionVersionResolutionKind.AuthorableActivity, commit.Publication.ResolutionKind);
    }

    [Fact]
    public async Task Behaviorally_identical_source_versions_share_template_identity()
    {
        var command = new RecordingCommand();
        var publisher = CreatePublisher(command);

        await publisher.PublishAsync(Definition(), Version("version-1", "1.0.0"));
        await publisher.PublishAsync(Definition(), Version("version-2", "2.0.0"));

        Assert.Equal(2, command.Commits.Count);
        Assert.Equal(command.Commits[0].ExecutableTemplate.TemplateHash, command.Commits[1].ExecutableTemplate.TemplateHash);
        Assert.Equal(command.Commits[0].ExecutableTemplate.TemplateId, command.Commits[1].ExecutableTemplate.TemplateId);
        Assert.NotEqual(command.Commits[0].SourceReference.SourceReferenceId, command.Commits[1].SourceReference.SourceReferenceId);
    }

    [Fact]
    public async Task Publishes_declared_source_outcomes_with_stable_contract_references()
    {
        var command = new RecordingCommand();
        var publisher = CreatePublisher(command);
        var version = Version("version-1", "1.0.0");
        version.DesignFacets =
        [
            new("elsa.outcomes", "1", JsonSerializer.SerializeToElement(new
            {
                ports = new object[]
                {
                    new { referenceKey = "approved", name = "Approved", type = "outcome" },
                    new { name = "Rejected", type = "outcome" }
                }
            }))
        ];

        await publisher.PublishAsync(Definition(), version);

        Assert.Equal(
            [("approved", "Approved"), ("Rejected", "Rejected")],
            Assert.Single(command.Commits).Publication.Contract.Outcomes
                .Select(x => (x.ReferenceKey, x.Name)));
    }

    [Fact]
    public async Task Rejects_design_owned_content()
    {
        var publisher = CreatePublisher(new RecordingCommand());
        var version = Version("version-1", "1.0.0");
        version.ProviderKey = WellKnownActivityContentAuthorities.Design;

        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(Definition(), version));
    }

    [Fact]
    public async Task A_commit_that_loses_to_an_identical_publication_counts_as_published()
    {
        // Another node committed this version between the check and the commit (#2189). Its clock and a lifecycle change
        // since then are the only differences.
        var command = new LosingCommand(AlreadyPublished());
        var store = new PeerPublicationStore(() => Altered(command.Attempted!, publication =>
        {
            publication["PublishedAt"] = DateTimeOffset.UnixEpoch;
            publication["CreatedAt"] = DateTimeOffset.UnixEpoch;
            publication["LastModifiedAt"] = DateTimeOffset.UnixEpoch;
            publication["Lifecycle"] = (int)ActivityDefinitionVersionLifecycle.Retired;
        }));

        await CreatePublisher(command, store).PublishAsync(Definition(), Version("version-1", "1.0.0"));

        Assert.Equal(2, store.Reads);
    }

    [Theory]
    [InlineData("TenantId")]
    [InlineData("TemplateHash")]
    [InlineData("SourceReferenceId")]
    [InlineData("Contract")]
    [InlineData("Provider")]
    public async Task A_commit_that_loses_to_a_different_publication_fails_with_its_own_error(string differingMember)
    {
        var failure = AlreadyPublished();
        var command = new LosingCommand(failure);
        var store = new PeerPublicationStore(() => Altered(command.Attempted!, publication =>
        {
            switch (differingMember)
            {
                case "Contract":
                    publication["Contract"]!["Inputs"]![0]!["DisplayName"] = "Other message";
                    break;
                case "Provider":
                    publication["Provider"]!["Payload"] = new JsonObject { ["typeAlias"] = "Acme.Other" };
                    break;
                default:
                    publication[differingMember] = "other";
                    break;
            }
        }));

        var thrown = await Assert.ThrowsAsync<ActivityVersionAlreadyPublishedException>(() =>
            CreatePublisher(command, store).PublishAsync(Definition(), Version("version-1", "1.0.0")));

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task A_commit_that_lost_a_race_with_nothing_published_fails_with_its_own_error()
    {
        var failure = new ActivityVersionAlreadyPublishedException("version-1", "The publication lost a uniqueness race and was rolled back.");
        var store = new PeerPublicationStore(() => null);

        var thrown = await Assert.ThrowsAsync<ActivityVersionAlreadyPublishedException>(() =>
            CreatePublisher(new LosingCommand(failure), store).PublishAsync(Definition(), Version("version-1", "1.0.0")));

        Assert.Same(failure, thrown);
        Assert.Equal(2, store.Reads);
    }

    [Theory]
    [InlineData("misaligned identities")]
    [InlineData("different content authority")]
    public async Task A_commit_that_fails_for_another_reason_fails_even_when_an_identical_publication_is_stored(string reason)
    {
        // Only a lost race may be answered by what another writer stored. Any other failure means this call's own commit
        // is wrong, whatever is stored.
        Exception failure = reason == "misaligned identities"
            ? new ArgumentException("Source activity publication identities do not align.", "commit")
            : new InvalidOperationException("Activity definition 'definition-1' has a different content authority.");
        var command = new LosingCommand(failure);
        var store = new PeerPublicationStore(() => Altered(command.Attempted!, _ => { }));

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() =>
            CreatePublisher(command, store).PublishAsync(Definition(), Version("version-1", "1.0.0")));

        Assert.Same(failure, thrown);
        Assert.Equal(1, store.Reads);
    }

    [Fact]
    public async Task A_lost_race_whose_publication_cannot_be_read_back_fails_with_the_commits_own_error()
    {
        var failure = AlreadyPublished();
        var readFailure = new TimeoutException("The database did not answer.");
        var logger = new RecordingLogger();
        var store = new PeerPublicationStore(() => throw readFailure);

        var thrown = await Assert.ThrowsAsync<ActivityVersionAlreadyPublishedException>(() =>
            CreatePublisher(new LosingCommand(failure), store, logger).PublishAsync(Definition(), Version("version-1", "1.0.0")));

        Assert.Same(failure, thrown);
        // Rethrown, not thrown afresh: the frame that raised it is still on the stack trace.
        Assert.Contains(nameof(LosingCommand), thrown.StackTrace);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Same(readFailure, entry.Exception);
    }

    [Fact]
    public async Task A_cancelled_commit_is_not_taken_for_a_lost_race()
    {
        using var cancellation = new CancellationTokenSource();
        var command = new LosingCommand(new OperationCanceledException(cancellation.Token), cancellation.Cancel);
        var store = new PeerPublicationStore(() => command.Attempted);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CreatePublisher(command, store).PublishAsync(Definition(), Version("version-1", "1.0.0"), cancellation.Token));

        Assert.Equal(1, store.Reads);
    }

    private static SourceOwnedActivityVersionPublisher CreatePublisher(
        ICommitSourceActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference> command,
        IActivityDefinitionVersionPublicationStore? store = null,
        ILogger<SourceOwnedActivityVersionPublisher>? logger = null)
    {
        var types = TestWellKnownTypeRegistry.Create();
        var structure = new DefaultActivityStructureService([]);
        var outputCompiler = new RuntimeOutputCaptureCompiler(
            new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]));
        var nodeCompiler = new ExecutableNodeCompiler(structure, types, new RuntimeInputBindingCompiler(types), outputCompiler);
        return new(
            command,
            store ?? new PeerPublicationStore(() => null),
            nodeCompiler,
            TimeProvider.System,
            logger ?? NullLogger<SourceOwnedActivityVersionPublisher>.Instance);
    }

    private static ActivityVersionAlreadyPublishedException AlreadyPublished() =>
        new("version-1", "Activity version 'version-1' is already published.");

    private static ActivityDefinition Definition() => new()
    {
        Id = "definition-1",
        ActivityTypeKey = "Acme.Send",
        Category = "Tests",
        DisplayName = "Send"
    };

    private static ActivityDefinitionVersion Version(string id, string version) => new(version, "definition-1")
    {
        Id = id,
        ProviderKey = "elsa.clr-activity",
        ProviderSchemaVersion = "1",
        ConsumerKey = WellKnownRuntimeActivityConsumers.ClrActivity,
        ConsumerSchemaVersion = "1",
        DescriptorPayload = JsonSerializer.SerializeToElement(new { typeAlias = "Acme.Send" }),
        SourceKind = "CLR",
        SourceId = "assembly-a",
        Hash = "catalog-hash",
        Definition = Definition(),
        Inputs =
        [
            new("message", "Message", new("String"), null, "Message", null, true)
        ],
        Outputs =
        [
            new("result", "Result", new("String"), null, "Result", null, false)
        ]
    };

    private sealed class RecordingCommand : ICommitSourceActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference>
    {
        public List<SourceActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference>> Commits { get; } = [];

        public Task ExecuteAsync(SourceActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference> commit, CancellationToken cancellationToken = default)
        {
            Commits.Add(commit);
            return Task.CompletedTask;
        }
    }

    /// <summary>Commits nothing and throws <paramref name="failure"/> after <paramref name="onAttempt"/> runs.</summary>
    private sealed class LosingCommand(Exception failure, Action? onAttempt = null)
        : ICommitSourceActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference>
    {
        public ActivityDefinitionVersionPublication? Attempted { get; private set; }

        public async Task ExecuteAsync(SourceActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference> commit, CancellationToken cancellationToken = default)
        {
            Attempted = commit.Publication;
            onAttempt?.Invoke();
            await Task.Yield();
            throw failure;
        }
    }

    private sealed class RecordingLogger : ILogger<SourceOwnedActivityVersionPublisher>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, exception));
    }

    /// <summary>Holds no publication when the publisher first checks, and then whatever <paramref name="afterCheck"/> says a peer committed.</summary>
    private sealed class PeerPublicationStore(Func<ActivityDefinitionVersionPublication?> afterCheck) : IActivityDefinitionVersionPublicationStore
    {
        public int Reads { get; private set; }

        public Task<ActivityDefinitionVersionPublication?> FindAsync(string definitionVersionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Reads++ == 0 ? null : afterCheck());

        public Task<IReadOnlyList<ActivityDefinitionVersionPublication>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionVersionPublication>>([]);
    }

    /// <summary>A copy of <paramref name="publication"/> as it reads back from storage, with <paramref name="alter"/> applied.</summary>
    private static ActivityDefinitionVersionPublication Altered(ActivityDefinitionVersionPublication publication, Action<JsonObject> alter)
    {
        var members = JsonSerializer.SerializeToNode(publication)!.AsObject();
        alter(members);
        return members.Deserialize<ActivityDefinitionVersionPublication>()!;
    }
}
