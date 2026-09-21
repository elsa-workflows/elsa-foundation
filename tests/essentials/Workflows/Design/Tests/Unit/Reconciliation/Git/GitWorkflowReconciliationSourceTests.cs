using Elsa.Workflows.Design.Reconciliation.Git.Options;
using Elsa.Workflows.Design.Reconciliation.Git.Services;
using Elsa.Workflows.Design.Reconciliation.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.Reconciliation.Git;

/// <summary>
/// US2 inbound source: version files authored in git surface as reconciliation models with correct
/// SemVer, committer-date provenance, canonical content hash, and definition.json-sourced metadata; a
/// malformed version file is skipped without sinking its siblings.
/// </summary>
public sealed class GitWorkflowReconciliationSourceTests : GitIntegrationTest
{
    private static string Metadata(string name, string? description = null, bool deleted = false) =>
        new GitDefinitionMetadata(name, description, deleted).ToJson();

    private async Task<IReadOnlyList<WorkflowVersionReconciliationModel>> Seed(Action<GitTestRepo> seed)
    {
        var remote = GitTestRepo.InitWorking(_git);
        _tempPaths.Add(remote.Path);
        seed(remote);
        remote.CommitAll("seed");

        var cachePath = GitTestSupport.NewCachePath();
        _tempPaths.Add(cachePath);

        var options = new GitReconciliationOptions
        {
            RemoteUrl = remote.Path, Branch = "main", WorkflowsPath = "workflows",
            LocalCachePath = cachePath, Role = GitReconciliationRole.Consumer,
        };
        var opts = GitTestSupport.Options(options);
        var workspace = new GitWorkspace(_git, opts, NullLogger<GitWorkspace>.Instance);
        var source = new GitWorkflowReconciliationSource(workspace, _git, new FakePayloadSerializer(), opts, NullLogger<GitWorkflowReconciliationSource>.Instance);

        return (await source.Read(CancellationToken.None)).ToList();
    }


    [Fact]
    public async Task Reads_versions_with_semver_metadata_provenance_and_hash()
    {
        var models = await Seed(r =>
        {
            r.WriteFile("workflows/wf-a/definition.json", Metadata("Alpha", "the alpha flow"));
            r.WriteFile("workflows/wf-a/versions/1.0.0.json", GitTestSupport.EmptyStateJson);
            r.WriteFile("workflows/wf-a/versions/2.0.0.json", GitTestSupport.EmptyStateJson);
        });

        Assert.Equal(2, models.Count);
        Assert.All(models, m => Assert.Equal("wf-a", m.DefinitionId));
        Assert.All(models, m => Assert.Equal("Alpha", m.Name));
        Assert.All(models, m => Assert.Equal("the alpha flow", m.Description));
        Assert.All(models, m => Assert.False(string.IsNullOrEmpty(m.ContentHash)));
        Assert.All(models, m => Assert.NotNull(m.SourceCreatedAt));
        Assert.Contains(models, m => m.Version == "1.0.0");
        Assert.Contains(models, m => m.Version == "2.0.0");
    }

    [Fact]
    public async Task Malformed_version_file_is_skipped_but_siblings_reconcile()
    {
        var models = await Seed(r =>
        {
            r.WriteFile("workflows/wf-b/definition.json", Metadata("Beta"));
            r.WriteFile("workflows/wf-b/versions/1.0.0.json", GitTestSupport.EmptyStateJson);
            r.WriteFile("workflows/wf-b/versions/2.0.0.json", "{ this is not valid json");
        });

        Assert.Single(models);
        Assert.Equal("1.0.0", models[0].Version);
    }

    [Fact]
    public async Task Deleted_flag_from_definition_json_propagates_to_the_model()
    {
        var models = await Seed(r =>
        {
            r.WriteFile("workflows/wf-c/definition.json", Metadata("Gamma", deleted: true));
            r.WriteFile("workflows/wf-c/versions/1.0.0.json", GitTestSupport.EmptyStateJson);
        });

        var model = Assert.Single(models);
        Assert.True(model.Deleted);
    }

    [Fact]
    public async Task Missing_definition_json_falls_back_to_definition_id_as_name()
    {
        var models = await Seed(r =>
        {
            r.WriteFile("workflows/wf-d/versions/1.0.0.json", GitTestSupport.EmptyStateJson);
        });

        var model = Assert.Single(models);
        Assert.Equal("wf-d", model.Name);
        Assert.False(model.Deleted);
    }
}
