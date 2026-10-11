using Elsa.Workflows.Publishing.Services;
using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Publishing.Api.Services;

/// <summary>Publishing-owned preview adapter; Design receives only provider-neutral diff facts.</summary>
public sealed class ActivityDraftDiffCandidateCompiler(
    IActivityDefinitionStore definitions,
    IActivityDefinitionVersionPublicationStore publications,
    IActivityTemplateCompiler compiler) : IActivityDraftDiffCandidateCompiler
{
    public async ValueTask<ActivityDraftDiffCandidate> CompileAsync(
        ActivityDraftDiffCandidateRequest request,
        CancellationToken cancellationToken = default)
    {
        var definition = await definitions.GetAsync(request.DefinitionId, cancellationToken);
        var baseline = request.BaseVersionId is null
            ? null
            : await publications.FindAsync(request.BaseVersionId, cancellationToken);
        var candidateVersionId = $"preview:{request.DraftId}:{request.Revision}";
        var draft = new ActivityDefinitionDraft
        {
            Id = request.DraftId,
            DefinitionId = request.DefinitionId,
            Revision = request.Revision,
            State = request.State,
            TenantId = definition.TenantId
        };
        var layoutBytes = JsonSerializer.SerializeToUtf8Bytes(request.Layout).LongLength;
        var result = await compiler.CompileAsync(new(
            definition,
            draft,
            candidateVersionId,
            baseline?.Version ?? "0.0.0-preview",
            layoutBytes), cancellationToken);

        var owner = new ActivityDefinitionReference(
            "ActivityDraft",
            request.DefinitionId,
            DraftId: request.DraftId,
            Revision: request.Revision,
            TemplateHash: result.Template?.TemplateHash,
            TenantId: definition.TenantId);
        var dependencies = result.DirectDependencies.Select(dependency =>
        {
            var target = new ActivityDefinitionReference(
                "ActivityVersion",
                dependency.DefinitionId,
                dependency.VersionId,
                dependency.Version,
                TemplateHash: dependency.TemplateHash,
                TenantId: dependency.TenantId,
                Lifecycle: dependency.Lifecycle);
            return new ActivityDependencyItem(
                $"{candidateVersionId}:{dependency.OccurrenceId}",
                owner,
                target,
                new(dependency.OccurrenceId, dependency.NodeOrigin),
                true,
                1,
                [owner, target]);
        }).ToArray();

        return new(
            result.Template?.TemplateHash,
            result.Template?.ProviderFingerprint ?? baseline?.ProviderFingerprint ?? string.Empty,
            result.Template is null ? null : result.Measurements.ClosedNodeCount,
            result.Template?.ResumeTargets.Count,
            result.Template?.RuntimeRequirements
                .Select(x => new ActivityRuntimeRequirementDeclaration(x.ConsumerKey, x.SchemaVersion))
                .ToArray() ?? [],
            dependencies,
            result.ProviderCompatibilityChanges,
            result.Diagnostics);
    }
}
