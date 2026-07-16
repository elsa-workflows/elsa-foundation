using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Services;

public interface IActivityDraftTestRunPublisher
{
    Task<ActivityDraftTestRunView> StartAsync(StartActivityDraftTestRun request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Compiles an exact activity draft revision, places it as the root of a synthetic workflow artifact, and uses
/// ordinary Runtime start dispatch. Both the activity template and wrapper artifact live in their normal
/// content-addressed stores; expiring Source References are their only test-run lifetime mechanism.
/// </summary>
public sealed class ActivityDraftTestRunPublisher(
    IActivityDefinitionStore definitions,
    IActivityDefinitionDraftStore drafts,
    IActivityDefinitionLayoutStore layouts,
    IActivityTemplateCompiler compiler,
    IActivityPublishingAuthorizationContext authorization,
    IExecutableActivityTemplateStore activityTemplates,
    IWorkflowExecutableSourceReferenceStore sourceReferences,
    ActivityTemplatePlacer placer,
    WorkflowExecutableHasher hasher,
    IWorkflowExecutableStore workflowExecutables,
    IWorkflowStartDispatcher startDispatcher,
    IIdentityGenerator identityGenerator,
    TimeProvider timeProvider) : IActivityDraftTestRunPublisher
{
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromMinutes(30);
    private static readonly IReadOnlyDictionary<string, RuntimeOutputCapture> NoOutputCaptures =
        new Dictionary<string, RuntimeOutputCapture>(StringComparer.Ordinal);

    public async Task<ActivityDraftTestRunView> StartAsync(
        StartActivityDraftTestRun request,
        CancellationToken cancellationToken = default)
    {
        var draft = await drafts.FindAsync(request.DraftId, cancellationToken)
                    ?? throw Reject("activity.draft.not-found", "The requested activity draft was not found.");
        EnsureAuthorized(draft.TenantId);
        if (draft.Status != ActivityDefinitionDraftStatus.Active || draft.Revision != request.ExpectedRevision)
            throw Reject("activity.draft.stale-revision", "The requested activity draft revision is no longer active.", conflict: true);
        var definition = await definitions.GetAsync(draft.DefinitionId, cancellationToken);
        EnsureAuthorized(definition.TenantId);
        var layout = await layouts.FindDraftLayoutAsync(draft.Id, cancellationToken)
                     ?? throw Reject("activity.draft.layout-not-found", "The activity draft layout was not found.", conflict: true);
        EnsureAuthorized(layout.TenantId);
        if (layout.Revision != draft.Revision)
            throw Reject("activity.draft.stale-layout", "The activity draft layout does not match the requested revision.", conflict: true);

        var candidateVersionId = $"activity-test-version-{StableHash($"{draft.Id}\u001f{draft.Revision}")}";
        var candidateVersion = $"draft-{draft.Revision}";
        var compilation = await compiler.CompileAsync(new(
            definition,
            draft,
            candidateVersionId,
            candidateVersion,
            Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(layout.Records))), cancellationToken);
        if (!compilation.IsSuccessful || compilation.Template is null)
            throw Reject("activity.test-run.invalid", "The activity draft could not be compiled for a test run.", compilation.Diagnostics);

        var now = timeProvider.GetUtcNow();
        var expiresAt = now.Add(DefaultRetention);
        var testRunId = $"activity-test-run-{identityGenerator.Generate()}";
        var templateReferenceId = $"activity-test-template-ref-{StableHash(compilation.Template.TemplateHash)}";
        var templateReference = CreateTemplateReference(
            templateReferenceId,
            definition,
            draft,
            candidateVersionId,
            candidateVersion,
            compilation.Template,
            layout.Records.ToArray(),
            now,
            expiresAt);
        var publication = CreateEphemeralPublication(
            definition,
            draft,
            candidateVersionId,
            candidateVersion,
            compilation.Template,
            templateReferenceId,
            compilation.Measurements,
            now);

        await activityTemplates.SaveAsync(compilation.Template, cancellationToken);
        await sourceReferences.SaveAsync(templateReference, cancellationToken);

        var origin = new ActivityInvocationOrigin([
            new(ActivityInvocationOriginSegmentKind.WorkflowRoot, $"activity-test-wrapper:{definition.Id}"),
            new(ActivityInvocationOriginSegmentKind.AuthoredNode, "activity-under-test"),
            new(ActivityInvocationOriginSegmentKind.TemplateBoundary, candidateVersionId)
        ]);
        var suppliedInputs = CompileInputs(draft.State.Contract, request.Inputs);
        var placement = await placer.PlaceAsync(new(
            publication,
            compilation.Template,
            templateReference,
            origin,
            definition.ActivityTypeKey,
            suppliedInputs,
            NoOutputCaptures), cancellationToken);

        var artifactHash = hasher.ComputeHash(placement.Root);
        var artifactId = hasher.CreateArtifactId("artifact-", artifactHash);
        var wrapperDefinitionId = $"activity-test-wrapper:{definition.Id}";
        var wrapperVersionId = $"activity-test-wrapper:{draft.Id}:{draft.Revision}";
        var executable = new WorkflowExecutable(
            new(artifactId, wrapperDefinitionId, wrapperVersionId, "draft", artifactHash),
            placement.Root,
            placement.ResumeTargets,
            now,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["runtime.scope"] = "activity-draft-test-run",
                ["activity.templateHash"] = compilation.Template.TemplateHash
            });
        await workflowExecutables.SaveAsync(executable, cancellationToken);

        var wrapperReference = new WorkflowExecutableSourceReference(
            $"activity-test-source-ref-{identityGenerator.Generate()}",
            artifactId,
            "ActivityDraftTestRun",
            draft.Id,
            draft.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            wrapperDefinitionId,
            wrapperVersionId,
            "draft",
            now,
            null,
            WorkflowExecutableReferenceScope.TestRun,
            expiresAt,
            LayoutSidecar: placement.LayoutSidecar);
        await sourceReferences.SaveAsync(wrapperReference, cancellationToken);

        var inputValues = (request.Inputs ?? new Dictionary<string, ActivityDraftTestRunInput>())
            .Where(x => StringComparer.OrdinalIgnoreCase.Equals(x.Value.State, "Present") && x.Value.Value.HasValue)
            .ToDictionary(x => x.Key, x => (object?)x.Value.Value!.Value.Clone(), StringComparer.Ordinal);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["runtime.scope"] = "test-run",
            ["runtime.testRunId"] = testRunId,
            ["runtime.sourceReferenceId"] = wrapperReference.SourceReferenceId,
            ["activity.draftId"] = draft.Id,
            ["activity.draftRevision"] = draft.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrWhiteSpace(request.CorrelationId))
            metadata["runtime.correlationId"] = request.CorrelationId;
        var dispatch = await startDispatcher.DispatchAsync(
            new WorkflowExecutionStartDispatchRequest(
                artifactId,
                "activity-designer-test-run",
                metadata: metadata,
                inputs: inputValues,
                runKind: WorkflowRunKind.TestRun,
                sourceSelection: new WorkflowExecutableSourceSelection(sourceReferenceId: wrapperReference.SourceReferenceId)),
            WorkflowExecutableReferenceScope.TestRun,
            cancellationToken: cancellationToken);

        return new(
            testRunId,
            draft.Id,
            draft.Revision,
            artifactId,
            wrapperReference.SourceReferenceId,
            dispatch.WorkflowExecutionId,
            null,
            "DispatchAccepted",
            dispatch.CommandDispatch.Status.ToString(),
            dispatch.CommandDispatch.Reason,
            expiresAt);
    }

    private static IReadOnlyDictionary<string, RuntimeInputBinding> CompileInputs(
        ActivityContract contract,
        IReadOnlyDictionary<string, ActivityDraftTestRunInput>? supplied)
    {
        supplied ??= new Dictionary<string, ActivityDraftTestRunInput>(StringComparer.Ordinal);
        var known = contract.Inputs.Select(x => x.ReferenceKey).ToHashSet(StringComparer.Ordinal);
        var unknown = supplied.Keys.Where(x => !known.Contains(x)).Order(StringComparer.Ordinal).ToArray();
        if (unknown.Length != 0)
            throw Reject("activity.test-run.input-unknown", $"Unknown activity input '{unknown[0]}'.");

        var result = new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal);
        foreach (var input in contract.Inputs)
        {
            if (!supplied.TryGetValue(input.ReferenceKey, out var value) || StringComparer.OrdinalIgnoreCase.Equals(value.State, "Absent"))
                continue;
            if (!StringComparer.OrdinalIgnoreCase.Equals(value.State, "Present") || !value.Value.HasValue)
                throw Reject("activity.test-run.input-state-invalid", $"Activity input '{input.ReferenceKey}' has an invalid state/value combination.");
            result.Add(input.Name, new(
                input.Name,
                RuntimeInputBindingSource.Literal,
                literalValue: value.Value.Value.Clone(),
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["typeName"] = typeof(object).AssemblyQualifiedName!,
                    ["referenceKey"] = input.ReferenceKey
                }));
        }
        return result;
    }

    private static ActivityDefinitionVersionPublication CreateEphemeralPublication(
        Elsa.Activities.Design.Persistence.Core.Entities.ActivityDefinition definition,
        ActivityDefinitionDraft draft,
        string versionId,
        string version,
        ExecutableActivityTemplate template,
        string sourceReferenceId,
        ActivityResourceMeasurements measurements,
        DateTimeOffset now) => new()
    {
        Id = versionId,
        TenantId = definition.TenantId,
        DefinitionVersionId = versionId,
        DefinitionId = definition.Id,
        Version = version,
        ActivityTypeKey = definition.ActivityTypeKey,
        SourceDraftId = draft.Id,
        SourceVersionId = draft.SourceVersionId,
        Contract = draft.State.Contract,
        Provider = draft.State.Provider,
        TemplateId = template.TemplateId,
        TemplateHash = template.TemplateHash,
        SourceReferenceId = sourceReferenceId,
        ProviderFingerprint = template.ProviderFingerprint,
        DirectDependencyCount = template.DirectDependencies.Count,
        ClosedTemplateCount = template.ClosedTemplates.Count,
        RuntimeRequirements = template.RuntimeRequirements.Select(x => new ActivityRuntimeRequirementDeclaration(x.ConsumerKey, x.SchemaVersion)).ToArray(),
        ResourceMeasurements = measurements,
        ResumeTargetCount = template.ResumeTargets.Count,
        Lifecycle = ActivityDefinitionVersionLifecycle.Active,
        PublishedAt = now,
        CreatedAt = now,
        LastModifiedAt = now
    };

    private static WorkflowExecutableSourceReference CreateTemplateReference(
        string sourceReferenceId,
        Elsa.Activities.Design.Persistence.Core.Entities.ActivityDefinition definition,
        ActivityDefinitionDraft draft,
        string versionId,
        string version,
        ExecutableActivityTemplate template,
        IReadOnlyCollection<ActivityLayoutRecord> layout,
        DateTimeOffset now,
        DateTimeOffset expiresAt)
    {
        var records = layout.Select(x => new ExecutableActivityLayoutRecord(
            x.NodeId,
            x.NodeId,
            x.NodeId,
            0,
            0,
            AdditionalProperties: x.Data.Clone())).ToArray();
        var origin = new ActivityInvocationOrigin([new(ActivityInvocationOriginSegmentKind.TemplateBoundary, versionId)]);
        return new(
            sourceReferenceId,
            template.TemplateId,
            "ActivityDraft",
            draft.Id,
            draft.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            definition.Id,
            versionId,
            version,
            now,
            null,
            WorkflowExecutableReferenceScope.TestRun,
            expiresAt,
            LayoutSidecar: new([new(versionId, origin, template.TemplateHash, records, [])]));
    }

    private static ActivityPublicationRejectedException Reject(
        string code,
        string message,
        IReadOnlyList<ActivityDiagnostic>? diagnostics = null,
        bool conflict = false) =>
        new(code, message, ActivityDiagnosticOrderer.Order(diagnostics ?? []), conflict);

    private void EnsureAuthorized(string? tenantId)
    {
        if (!authorization.CanAccessTenant(tenantId))
            throw Reject(
                "activity.tenant.reference-denied",
                "The requested activity identity is outside the caller's authorized scope.");
    }

    private static string StableHash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
