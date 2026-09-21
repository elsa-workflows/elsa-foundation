using System.Globalization;
using Elsa.Git;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Reconciliation.Contracts;
using Elsa.Workflows.Design.Reconciliation.Git.Contracts;
using Elsa.Workflows.Design.Reconciliation.Git.Options;
using Elsa.Workflows.Design.Reconciliation.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.Reconciliation.Git.Services;

/// <summary>
/// The inbound git reconciliation source (ADR 0034; <c>SourceKind = "git"</c>). Reads immutable
/// <c>{WorkflowsPath}/{definitionId}/versions/{semver}.json</c> content plus the mutable sibling
/// <c>definition.json</c> from a local working clone, emitting one
/// <see cref="WorkflowVersionReconciliationModel"/> per version file. Read lazily per call, so a re-run
/// picks up new commits. A malformed version file is skipped with a diagnostic; its siblings still
/// reconcile.
/// </summary>
public sealed class GitWorkflowReconciliationSource(
    IGitWorkspace workspace,
    IGitClient gitClient,
    IPayloadSerializer payloadSerializer,
    IOptions<GitReconciliationOptions> options,
    ILogger<GitWorkflowReconciliationSource> logger) : IWorkflowReconciliationSource
{
    private readonly GitReconciliationOptions _options = options.Value;

    public string SourceId => _options.ResolvedSourceId;

    public string SourceKind => "git";

    public async ValueTask<IEnumerable<WorkflowVersionReconciliationModel>> Read(CancellationToken cancellationToken)
    {
        var repoPath = await workspace.EnsureReadyAsync(cancellationToken);
        var workflowsRoot = Path.Combine(repoPath, _options.WorkflowsPath);
        if (!Directory.Exists(workflowsRoot))
            return [];

        var models = new List<WorkflowVersionReconciliationModel>();
        foreach (var definitionDir in Directory.EnumerateDirectories(workflowsRoot))
            ReadDefinition(repoPath, definitionDir, models);

        return models;
    }

    private void ReadDefinition(string repoPath, string definitionDir, List<WorkflowVersionReconciliationModel> models)
    {
        var definitionId = Path.GetFileName(definitionDir);
        var metadata = GitDefinitionMetadata.Read(Path.Combine(definitionDir, "definition.json"));
        if (metadata is null)
            LogMissingMetadata(definitionId);

        var name = string.IsNullOrWhiteSpace(metadata?.Name) ? definitionId : metadata.Name!;
        var description = metadata?.Description;
        var deleted = metadata?.Deleted ?? false;

        var versionsDir = Path.Combine(definitionDir, "versions");
        if (!Directory.Exists(versionsDir))
            return;

        foreach (var file in Directory.EnumerateFiles(versionsDir, "*.json"))
        {
            var model = TryReadVersion(repoPath, definitionId, name, description, deleted, file);
            if (model is not null)
                models.Add(model);
        }
    }

    private WorkflowVersionReconciliationModel? TryReadVersion(
        string repoPath, string definitionId, string name, string? description, bool deleted, string file)
    {
        var version = Path.GetFileNameWithoutExtension(file);
        try
        {
            var fileText = File.ReadAllText(file);
            var state = payloadSerializer.Deserialize<WorkflowDefinitionState>(fileText);
            var contentHash = GitCanonicalJson.HashFile(fileText);
            var sourceCreatedAt = ReadCommitterDate(repoPath, file);

            return new WorkflowVersionReconciliationModel(
                DefinitionId: definitionId,
                Name: name,
                Description: description,
                Version: version,
                State: state,
                SourceCreatedAt: sourceCreatedAt,
                ContentHash: contentHash,
                Deleted: deleted);
        }
        catch (Exception ex)
        {
            LogMalformedVersion(definitionId, version, ex);
            return null;
        }
    }

    /// <summary>The committer date of the commit that introduced the immutable version file (FR-005).</summary>
    private DateTimeOffset? ReadCommitterDate(string repoPath, string file)
    {
        var relativePath = Path.GetRelativePath(repoPath, file).Replace(Path.DirectorySeparatorChar, '/');
        var raw = gitClient.RunOrDefault(repoPath, "log", "-1", "--format=%cI", "--", relativePath);
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when)
            ? when
            : null;
    }

    private void LogMissingMetadata(string definitionId)
    {
        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("No readable definition.json for '{def}'; falling back to the definition id as name", definitionId);
    }

    private void LogMalformedVersion(string definitionId, string version, Exception ex)
    {
        logger.LogWarning(ex, "Skipping malformed git version file '{def}' v{v}", definitionId, version);
    }
}
