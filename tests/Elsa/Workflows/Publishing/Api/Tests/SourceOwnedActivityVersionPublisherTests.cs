using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Runtime.Core.Models;
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
    public async Task Rejects_design_owned_content()
    {
        var publisher = CreatePublisher(new RecordingCommand());
        var version = Version("version-1", "1.0.0");
        version.ProviderKey = WellKnownActivityContentAuthorities.Design;

        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(Definition(), version));
    }

    private static SourceOwnedActivityVersionPublisher CreatePublisher(RecordingCommand command)
    {
        var types = TestWellKnownTypeRegistry.Create();
        var structure = new DefaultActivityStructureService([]);
        var nodeCompiler = new ExecutableNodeCompiler(structure, types, new RuntimeInputBindingCompiler(types));
        return new(command, new EmptyPublicationStore(), nodeCompiler, TimeProvider.System);
    }

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

    private sealed class EmptyPublicationStore : IActivityDefinitionVersionPublicationStore
    {
        public Task<ActivityDefinitionVersionPublication?> FindAsync(string definitionVersionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ActivityDefinitionVersionPublication?>(null);

        public Task<IReadOnlyList<ActivityDefinitionVersionPublication>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionVersionPublication>>([]);
    }
}
