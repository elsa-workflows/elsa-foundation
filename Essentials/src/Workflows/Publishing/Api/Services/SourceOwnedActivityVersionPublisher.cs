using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Services;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Exceptions;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Publishing.Api.Services;

/// <summary>
/// Closes source-owned activity catalog versions into the same immutable publication/template model
/// used by Design-owned providers. The source remains sole content authority; customization forks to
/// a new Design-owned definition through the normal authoring API. Nodes reconciling the same source
/// converge: a commit that loses to an identical publication counts as published.
/// <para>
/// Content whose provider key a registered <see cref="IActivityTemplateProviderCompiler"/> owns is judged exactly as a
/// Studio draft is: validated by <see cref="IActivityDraftValidator"/>, compiled through the provider compiler (which runs
/// the provider's own gates, such as a model capability check), and checked for storage and runtime readiness. A version
/// any of that refuses is not written; its diagnostics are returned for the reconciler to record. Content whose provider key
/// no compiler owns (such as <c>elsa.clr-activity</c>) keeps the source-owned root compiled by <see cref="ExecutableNodeCompiler"/>.
/// </para>
/// </summary>
public sealed class SourceOwnedActivityVersionPublisher(
    ICommitSourceActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference> commitCommand,
    IActivityDefinitionVersionPublicationStore publicationStore,
    ExecutableNodeCompiler nodeCompiler,
    TimeProvider timeProvider,
    ILogger<SourceOwnedActivityVersionPublisher> logger,
    IEnumerable<IActivityTemplateProviderCompiler>? providerCompilers = null,
    IActivityTemplateCompiler? templateCompiler = null,
    IActivityDraftValidator? validator = null,
    IEnumerable<IActivityActivationStrategy>? activityActivationStrategies = null,
    IRuntimeDurableValueStorageDriverRegistry? storageDrivers = null,
    IActivityPublicationCandidateEvaluator? publicationEvaluator = null,
    IActivityPublicationReadiness? readiness = null,
    IActivityCatalogProjection? catalogProjection = null) : IActivitySourceVersionPublisher
{
    /// <summary>The diagnostic subject kind of a source-owned version; its id is the definition version id.</summary>
    public const string SubjectKind = "ActivitySourceVersion";

    private readonly IActivityTemplateProviderCompiler[] _providerCompilers = providerCompilers?.ToArray() ?? [];
    private readonly IActivityPublicationReadiness _readiness = readiness ?? new ActivityPublicationReadiness(activityActivationStrategies, storageDrivers);
    private readonly IActivityCatalogProjection _catalogProjection = catalogProjection ?? new ActivityCatalogProjection();
    private IActivityPublicationCandidateEvaluator? _evaluator = publicationEvaluator;

    public async Task<ActivitySourceVersionPublicationResult> PublishAsync(
        IActivityDefinition definition,
        IActivityDefinitionVersion version,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (StringComparer.Ordinal.Equals(version.ProviderKey, WellKnownActivityContentAuthorities.Design) ||
            StringComparer.Ordinal.Equals(version.SourceKind, "ActivityDefinitionDraft"))
            throw new InvalidOperationException("The source-owned publication bridge cannot publish Design-owned activity content.");
        if (await publicationStore.FindAsync(version.Id, cancellationToken) is not null)
            return ActivitySourceVersionPublicationResult.Published;
        var now = timeProvider.GetUtcNow();
        var catalogVersion = ActivityDefinitionVersion.From(version);
        var persistedDefinition = ActivityDefinition.From(definition);
        catalogVersion.Definition = persistedDefinition;
        var contract = ToContract(version);
        var sourceReferenceId = StableId("activity-source-ref", version.Id);

        ExecutableActivityTemplate template;
        ActivityDefinitionVersionPublication publication;
        if (IsProviderBacked(version))
        {
            var compiled = await CompileProviderBackedAsync(persistedDefinition, version, contract, cancellationToken);
            if (!compiled.IsPublishable)
                return ActivitySourceVersionPublicationResult.Refused(compiled.Diagnostics);
            template = compiled.Compilation.Template!;
            publication = CreatePublication(
                catalogVersion,
                persistedDefinition,
                version,
                contract,
                ActivityDefinitionVersionResolutionKind.ReusableTemplateBoundary,
                template,
                sourceReferenceId,
                compiled.Compilation.Measurements,
                now);
        }
        else
        {
            template = CompileSourceOwnedTemplate(catalogVersion, version, now);
            publication = CreatePublication(
                catalogVersion,
                persistedDefinition,
                version,
                contract,
                ActivityDefinitionVersionResolutionKind.AuthorableActivity,
                template,
                sourceReferenceId,
                new(
                    LocalNodeCount: 1,
                    ClosedNodeCount: 1,
                    DependencyCount: 0,
                    MaximumObservedAuthoredDepth: 1,
                    DescriptorBytes: version.DescriptorPayload.GetRawText().Length,
                    LayoutBytes: 0,
                    EstimatedDurableBoundarySlots: version.Inputs.LongCount() + version.Outputs.LongCount()),
                now);
        }

        var sourceReference = new WorkflowExecutableSourceReference(
            sourceReferenceId,
            template.TemplateId,
            version.SourceKind,
            version.SourceId,
            version.Version,
            definition.Id,
            version.Id,
            version.Version,
            now,
            now,
            WorkflowExecutableReferenceScope.Published);
        var authoring = new ActivityDefinitionAuthoringState
        {
            Id = StableId("activity-authority", definition.Id),
            TenantId = catalogVersion.TenantId,
            DefinitionId = definition.Id,
            ContentAuthority = new(ActivityContentAuthorityKind.ProviderSource, version.ProviderKey, version.SourceId),
            HeadVersionId = version.Id,
            RecommendedVersionId = version.Id,
            CreatedAt = now,
            LastModifiedAt = now
        };
        var layout = new ActivityDefinitionVersionLayout
        {
            Id = version.Id,
            TenantId = catalogVersion.TenantId,
            DefinitionVersionId = version.Id,
            Records = [],
            CreatedAt = now,
            LastModifiedAt = now
        };

        try
        {
            await commitCommand.ExecuteAsync(new(
                persistedDefinition,
                authoring,
                catalogVersion,
                publication,
                layout,
                template,
                sourceReference), cancellationToken);
        }
        catch (ActivityVersionAlreadyPublishedException) when (!cancellationToken.IsCancellationRequested)
        {
            // Another node reconciling the same source can commit this publication after the check above, and the
            // commit then finds it already published (#2189). A commit that loses a race reads again before it says so,
            // so a race lost to an unrelated writer never lands here. When what is stored is the publication this call
            // was about to write, the version is published, which is all this call promises. Otherwise this rethrows
            // the commit's own exception with its stack. No other failure reaches this block.
            if (!await IsPublishedIdenticallyAsync(publication, cancellationToken))
                throw;
        }

        return ActivitySourceVersionPublicationResult.Published;
    }

    private bool IsProviderBacked(IActivityDefinitionVersion version) =>
        _providerCompilers.Any(compiler => StringComparer.Ordinal.Equals(compiler.ProviderKey, version.ProviderKey));

    /// <summary>
    /// Validates and compiles provider-backed content through the same verdict as a Studio draft. Content no schema of its
    /// provider owns is refused by the compiler, never sent down the CLR path. A source-owned publication commits no
    /// dependency edges, so content that names a dependency is refused. A validator or compiler that throws refuses this
    /// version only; the reconciliation pass goes on.
    /// </summary>
    private async Task<EvaluatedPublicationCandidate> CompileProviderBackedAsync(
        ActivityDefinition definition,
        IActivityDefinitionVersion version,
        Elsa.Activities.Design.Core.Models.ActivityContract contract,
        CancellationToken cancellationToken)
    {
        var candidate = new ActivityTemplateCandidate(
            SubjectKind,
            version.Id,
            0,
            contract,
            new(version.ProviderKey, version.ProviderSchemaVersion, version.DescriptorPayload));
        // The same subject shape the evaluator and the template compiler give this candidate.
        var subject = new ActivityDiagnosticSubject(SubjectKind, version.Id, definition.Id, Revision: 0);
        if ((_evaluator is null && templateCompiler is null) || validator is null)
            return Refusal(subject, "activity.source.provider-compilation-unavailable",
                "Provider-backed source content needs the activity draft validator and template compiler, and one is not registered.",
                "Enable Activities Design API and Workflows Publishing so source content is validated and compiled like Studio content.");

        EvaluatedPublicationCandidate evaluated;
        try
        {
            var validation = await validator.ValidateAsync(
                new(definition.Id, version.Id, 0, new(contract, candidate.Provider, new Dictionary<string, string>(StringComparer.Ordinal)))
                {
                    SubjectKind = SubjectKind
                },
                cancellationToken);
            evaluated = await (_evaluator ??= new ActivityPublicationCandidateEvaluator(templateCompiler!, _readiness)).EvaluateAsync(
                new(definition, candidate, version.Id, version.Version, 0),
                validation,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Compiling source-owned activity version '{DefinitionVersionId}' failed.", version.Id);
            return Refusal(subject, "activity.source.compilation-failed",
                "The provider-backed source content could not be validated or compiled.",
                "Check the node log for the failure, correct the source content and reconcile again.");
        }

        if (evaluated.IsPublishable && evaluated.Compilation.DirectDependencies.Count > 0)
            return evaluated with
            {
                IsPublishable = false,
                Diagnostics = ActivityDiagnosticOrderer.Order(evaluated.Diagnostics.Append(new(
                    "activity.source.dependencies-unsupported",
                    ActivityDiagnosticSeverity.Error,
                    "Source-owned activity content cannot depend on other activity versions.",
                    subject,
                    Remediation: "Author content with activity dependencies in Studio, or remove the dependencies from the source.",
                    Metadata: new Dictionary<string, string>(StringComparer.Ordinal))))
            };
        // A replaced validator may refuse without saying why; the refusal still names itself, so it is logged and recorded.
        if (!evaluated.IsPublishable && evaluated.Diagnostics.All(x => x.Severity != ActivityDiagnosticSeverity.Error))
            return evaluated with
            {
                Diagnostics = ActivityDiagnosticOrderer.Order(evaluated.Diagnostics.Append(new(
                    "activity.source.publication-refused",
                    ActivityDiagnosticSeverity.Error,
                    "The provider-backed source content was refused without a stated reason.",
                    subject,
                    Remediation: "Validate the source content in Studio or check the node log, correct it and reconcile again.",
                    Metadata: new Dictionary<string, string>(StringComparer.Ordinal))))
            };
        return evaluated;
    }

    private static EvaluatedPublicationCandidate Refusal(ActivityDiagnosticSubject subject, string code, string message, string remediation) => new(
        new(null, new(0, 0, 0, 0, 0, 0, 0), [], []),
        [new(code, ActivityDiagnosticSeverity.Error, message, subject, Remediation: remediation, Metadata: new Dictionary<string, string>(StringComparer.Ordinal))],
        false);

    /// <summary>The single-node template of content no provider compiler owns: its root is the CLR or JSON source's own descriptor.</summary>
    private ExecutableActivityTemplate CompileSourceOwnedTemplate(
        ActivityDefinitionVersion catalogVersion,
        IActivityDefinitionVersion version,
        DateTimeOffset now)
    {
        var root = nodeCompiler.CompileSourceOwnedRoot(catalogVersion);
        var resumeTargets = nodeCompiler.BuildResumeTargets(root)
            .ToDictionary(
                x => x.Key,
                x => x.Value with { LocalResumeTargetId = x.Value.LocalResumeTargetId ?? x.Value.ResumeTargetId },
                StringComparer.Ordinal);
        var providerFingerprint = ExecutableActivityTemplateBehaviorHasher.ComputeCanonicalValueHash(new
        {
            version.ProviderKey,
            version.ProviderSchemaVersion,
            version.ConsumerKey,
            version.ConsumerSchemaVersion,
            descriptor = version.DescriptorPayload
        });
        var templateHash = ExecutableActivityTemplateBehaviorHasher.Compute(
            root,
            resumeTargets,
            [],
            [],
            [new(version.ConsumerKey, version.ConsumerSchemaVersion)],
            [],
            providerFingerprint,
            new Dictionary<string, string>(StringComparer.Ordinal));
        return new ExecutableActivityTemplate(
            $"activity-template-{templateHash["sha256:".Length..]}",
            templateHash,
            root,
            resumeTargets,
            [],
            [],
            [new(version.ConsumerKey, version.ConsumerSchemaVersion)],
            providerFingerprint,
            new Dictionary<string, string>(StringComparer.Ordinal),
            now);
    }

    private ActivityDefinitionVersionPublication CreatePublication(ActivityDefinitionVersion catalogVersion, ActivityDefinition definition, IActivityDefinitionVersion version,
        Elsa.Activities.Design.Core.Models.ActivityContract contract, ActivityDefinitionVersionResolutionKind resolutionKind, ExecutableActivityTemplate template,
        string sourceReferenceId, ActivityResourceMeasurements measurements, DateTimeOffset now) =>
        _catalogProjection.Publication(definition, contract, new(version.ProviderKey, version.ProviderSchemaVersion, version.DescriptorPayload),
            version.Id, version.Version, template.ProviderFingerprint, sourceReferenceId, template, measurements, now, resolutionKind);

    /// <summary>
    /// Reads the publication of the version back and compares it. A read that fails counts as not identical, so the caller
    /// fails with the commit's exception, which says why the publication did not commit; the read failure is logged.
    /// </summary>
    private async Task<bool> IsPublishedIdenticallyAsync(ActivityDefinitionVersionPublication candidate, CancellationToken cancellationToken)
    {
        try
        {
            return IsIdentical(await publicationStore.FindAsync(candidate.DefinitionVersionId, cancellationToken), candidate);
        }
        catch (Exception readFailure) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                readFailure,
                "Could not read back the publication of activity version '{DefinitionVersionId}' after its commit lost to another writer; failing with the commit's error.",
                candidate.DefinitionVersionId);
            return false;
        }
    }

    /// <summary>
    /// Whether <paramref name="stored"/> is the publication <paramref name="candidate"/> describes: every persisted member
    /// is equal except those in <see cref="MembersNotCompared"/>. Those are the clock fields and the row number, which each
    /// node fills in for itself, and the lifecycle status, which moves only after publication. A member added later is
    /// compared unless it is listed there, so a new difference fails rather than passes.
    /// </summary>
    private static bool IsIdentical(ActivityDefinitionVersionPublication? stored, ActivityDefinitionVersionPublication candidate) =>
        stored is not null && StringComparer.Ordinal.Equals(PublishedContent(stored), PublishedContent(candidate));

    private static string PublishedContent(ActivityDefinitionVersionPublication publication)
    {
        var members = JsonSerializer.SerializeToNode(publication)!.AsObject();
        foreach (var member in MembersNotCompared)
            members.Remove(member);
        return CanonicalJson(members);
    }

    /// <summary>Writes <paramref name="node"/> with every object's members in ordinal order, so equal content reads the same.</summary>
    private static string CanonicalJson(JsonNode? node) => node switch
    {
        JsonObject members => "{" + string.Join(',', members
            .OrderBy(member => member.Key, StringComparer.Ordinal)
            .Select(member => JsonSerializer.Serialize(member.Key) + ":" + CanonicalJson(member.Value))) + "}",
        JsonArray items => "[" + string.Join(',', items.Select(CanonicalJson)) + "]",
        null => "null",
        _ => node.ToJsonString()
    };

    /// <summary>The members that do not describe what was published: the clock fields, the row number and the lifecycle status.</summary>
    private static readonly string[] MembersNotCompared =
    [
        nameof(ActivityDefinitionVersionPublication.PublishedAt),
        nameof(ActivityDefinitionVersionPublication.CreatedAt),
        nameof(ActivityDefinitionVersionPublication.LastModifiedAt),
        nameof(ActivityDefinitionVersionPublication.RowNumber),
        nameof(ActivityDefinitionVersionPublication.Lifecycle)
    ];

    private static Elsa.Activities.Design.Core.Models.ActivityContract ToContract(IActivityDefinitionVersion version) => new(
        "1",
        version.Inputs.Select(input => new Elsa.Activities.Design.Core.Models.ActivityInputContract(
            input.ReferenceKey,
            input.Name,
            input.Type,
            input.IsRequired,
            input.IsNullable,
            input.DefaultValue is { } value ? new(input.DefaultSyntax ?? "Literal", value) : null,
            WellKnownRuntimeDurableValueStorageDrivers.Json,
            DisplayName: input.DisplayName,
            Description: input.Description,
            Category: input.Category,
            Order: input.Order,
            UiHint: input.UiHint,
            UiSpecifications: input.UISpecifications)).ToArray(),
        version.Outputs.Select(output => new ActivityOutputContract(
            output.ReferenceKey,
            output.Name,
            output.Type,
            output.IsRequired,
            output.IsNullable,
            WellKnownRuntimeDurableValueStorageDrivers.Json,
            DisplayName: output.DisplayName,
            Description: output.Description,
            Category: output.Category,
            Order: output.Order,
            UiHint: output.UiHint,
            UiSpecifications: output.UISpecifications,
            SourceRepresentation: output.SourceRepresentation)).ToArray(),
        ToOutcomes(version));

    private static IReadOnlyList<ActivityOutcomeContract> ToOutcomes(IActivityDefinitionVersion version)
    {
        var outcomes = new List<ActivityOutcomeContract>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var facet in version.DesignFacets.Where(x =>
                     StringComparer.Ordinal.Equals(x.Kind, "elsa.outcomes") &&
                     x.Payload.ValueKind == JsonValueKind.Object &&
                     x.Payload.TryGetProperty("ports", out var ports) &&
                     ports.ValueKind == JsonValueKind.Array))
        {
            foreach (var port in facet.Payload.GetProperty("ports").EnumerateArray())
            {
                if (port.ValueKind != JsonValueKind.Object ||
                    !port.TryGetProperty("name", out var nameProperty) ||
                    nameProperty.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(nameProperty.GetString()))
                    continue;

                var name = nameProperty.GetString()!;
                var referenceKey = port.TryGetProperty("referenceKey", out var referenceProperty) &&
                                   referenceProperty.ValueKind == JsonValueKind.String &&
                                   !string.IsNullOrWhiteSpace(referenceProperty.GetString())
                    ? referenceProperty.GetString()!
                    : name;
                if (seen.Add(referenceKey))
                    outcomes.Add(new(referenceKey, name, true));
            }
        }

        return outcomes.Count == 0 ? [new("Done", "Done", true)] : outcomes;
    }

    private static string StableId(string prefix, string value) =>
        $"{prefix}-{Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))}";
}
