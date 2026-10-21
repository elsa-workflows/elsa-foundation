using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Graph.Design.Models;
using Elsa.Activities.Graph.Design.Services;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

/// <summary>
/// Builds a test-only published Graph wrapper through the production graph compiler and activity-template placer.
/// The wrapper publication itself is an in-memory catalog overlay because the host has no in-memory publication
/// writer; its source-owned child publications are committed by the normal publisher into the configured EF stores.
/// </summary>
internal static class NativeEtlActivityBoundary
{
    private const string WrapperVersion = "1.0.0";
    private static readonly DateTimeOffset Timestamp = DateTimeOffset.UnixEpoch;
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public static async Task<ActivityTemplatePlacement> PlaceAsync(
        IServiceProvider services,
        ActivityNode authoredComposite,
        IReadOnlyCollection<(ActivityDefinition Definition, ActivityDefinitionVersion Version)> sourceCatalog,
        string parentWorkflowVersion,
        string authoredBoundaryNodeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(authoredComposite);
        ArgumentNullException.ThrowIfNull(sourceCatalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentWorkflowVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(authoredBoundaryNodeId);

        var sourcePublisher = services.GetRequiredService<IActivitySourceVersionPublisher>();
        var catalogByVersion = sourceCatalog
            .ToDictionary(item => item.Version.Id, item => item, StringComparer.Ordinal);
        var occurrenceIds = CollectOccurrences(authoredComposite, services.GetRequiredService<IActivityStructureService>());
        var missing = occurrenceIds.Where(id => !catalogByVersion.ContainsKey(id)).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
            throw new ArgumentException($"The immutable source catalog is missing exact activity version(s): {string.Join(", ", missing)}.", nameof(sourceCatalog));

        foreach (var sourceVersionId in occurrenceIds.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (sourceDefinition, version) = catalogByVersion[sourceVersionId];
            if (!StringComparer.Ordinal.Equals(sourceDefinition.Id, version.DefinitionId))
                throw new ArgumentException($"Source catalog version '{sourceVersionId}' is not bound to its exact definition.", nameof(sourceCatalog));
            await sourcePublisher.PublishAsync(sourceDefinition, version, cancellationToken);
        }

        var publications = services.GetRequiredService<IActivityDefinitionVersionPublicationStore>();
        if (await publications.FindAsync(authoredComposite.ActivityVersionId, cancellationToken) is null)
            throw new InvalidOperationException("The source-owned graph entry was not published.");

        var manifest = new ActivityGraphManifest(
            ActivityGraphManifest.SchemaVersion,
            JsonSerializer.SerializeToElement(authoredComposite, WebJson),
            [],
            [],
            []);
        using var manifestDocument = JsonDocument.Parse(manifest.ToCanonicalUtf8Bytes());
        var provider = new ActivityProviderManifest(
            GraphActivityProvider.Key,
            ActivityGraphManifest.SchemaVersion,
            manifestDocument.RootElement.Clone());
        var contract = new Elsa.Activities.Design.Core.Models.ActivityContract(
            "1",
            [],
            [],
            [new ActivityOutcomeContract("done", "Done", true)]);

        var definitionId = "etl-native-graph-" + HashPrefix(manifest.ToCanonicalUtf8Bytes());
        var definition = new ActivityDefinition
        {
            Id = definitionId,
            ActivityTypeKey = definitionId,
            Category = "ETL test boundary",
            DisplayName = "ETL native composite boundary"
        };
        var draft = new ActivityDefinitionDraft
        {
            Id = definitionId + "-draft",
            DefinitionId = definitionId,
            Revision = 1,
            Status = ActivityDefinitionDraftStatus.Active,
            State = new(contract, provider, new Dictionary<string, string>(StringComparer.Ordinal))
        };
        var versionId = definitionId + "-v1";

        var compilation = await services.GetRequiredService<IActivityTemplateCompiler>().CompileAsync(
            new(definition, draft, versionId, WrapperVersion, LayoutBytes: 0), cancellationToken);
        var template = compilation.Template;
        if (!compilation.IsSuccessful || template is null)
        {
            var codes = string.Join(", ", compilation.Diagnostics.Select(diagnostic => diagnostic.Code));
            throw new InvalidOperationException($"The production Graph activity compiler rejected the ETL test boundary ({codes}).");
        }

        var sourceReferenceId = "etl-native-graph-source-" + HashPrefix(template.TemplateHash);
        var publication = new ActivityDefinitionVersionPublication
        {
            Id = versionId,
            DefinitionVersionId = versionId,
            DefinitionId = definitionId,
            Version = WrapperVersion,
            ActivityTypeKey = definition.ActivityTypeKey,
            ResolutionKind = ActivityDefinitionVersionResolutionKind.ReusableTemplateBoundary,
            SourceDraftId = draft.Id,
            Contract = contract,
            Provider = provider,
            TemplateId = template.TemplateId,
            TemplateHash = template.TemplateHash,
            SourceReferenceId = sourceReferenceId,
            ProviderFingerprint = template.ProviderFingerprint,
            DirectDependencyCount = template.DirectDependencies.Count,
            ClosedTemplateCount = template.ClosedTemplates.Count,
            RuntimeRequirements = template.RuntimeRequirements
                .Select(requirement => new ActivityRuntimeRequirementDeclaration(requirement.ConsumerKey, requirement.SchemaVersion))
                .ToArray(),
            ResourceMeasurements = compilation.Measurements,
            ResumeTargetCount = template.ResumeTargets.Count,
            Lifecycle = ActivityDefinitionVersionLifecycle.Active,
            PublishedAt = Timestamp
        };
        var reference = new WorkflowExecutableSourceReference(
            sourceReferenceId,
            template.TemplateId,
            "ActivityDefinitionVersion",
            versionId,
            WrapperVersion,
            definitionId,
            versionId,
            WrapperVersion,
            Timestamp,
            Timestamp,
            WorkflowExecutableReferenceScope.Published);

        var templateStore = services.GetRequiredService<IExecutableActivityTemplateStore>();
        var referenceStore = services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>();
        await templateStore.SaveAsync(template, cancellationToken);
        await referenceStore.SaveAsync(reference, cancellationToken);

        var placer = new ActivityTemplatePlacer(
            new PublicationOverlay(publications, publication),
            services.GetRequiredService<IExecutableActivityTemplateReader>(),
            services.GetRequiredService<IWorkflowExecutableSourceReferenceReader>(),
            services.GetRequiredService<IActivityPlacementHasher>(),
            services.GetRequiredService<IActivityStructureService>(),
            services.GetRequiredService<RuntimeInputBindingCompiler>());

        return await placer.PlaceAsync(new(
            publication,
            template,
            reference,
            new ActivityInvocationOrigin([
                new(ActivityInvocationOriginSegmentKind.WorkflowRoot, parentWorkflowVersion),
                new(ActivityInvocationOriginSegmentKind.AuthoredNode, authoredBoundaryNodeId),
                new(ActivityInvocationOriginSegmentKind.TemplateBoundary, versionId)
            ]),
            definition.ActivityTypeKey,
            new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal),
            new Dictionary<string, RuntimeOutputCapture>(StringComparer.Ordinal),
            BindBoundaryRootToAuthoredNode: true), cancellationToken);
    }

    /// <summary>Finds the placed node created for an authored graph occurrence, independent of generated node IDs.</summary>
    public static string FindOccurrenceNodeId(ActivityTemplatePlacement placement, string authoredNodeId)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentException.ThrowIfNullOrWhiteSpace(authoredNodeId);

        var matches = placement.Root.DescendantsAndSelf()
            .Where(node => ReadOrigin(node).Segments
                .LastOrDefault(segment => segment.Kind == ActivityInvocationOriginSegmentKind.NestedPlacement) is { } segment &&
                StringComparer.Ordinal.Equals(segment.Id, authoredNodeId))
            .ToArray();
        return matches.Length == 1
            ? matches[0].ExecutableNodeId
            : throw new InvalidOperationException($"Expected exactly one placed node for graph occurrence '{authoredNodeId}', found {matches.Length}.");
    }

    private static HashSet<string> CollectOccurrences(ActivityNode root, IActivityStructureService structures)
    {
        var versionIds = new HashSet<string>(StringComparer.Ordinal);
        var visitedNodes = new HashSet<ActivityNode>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<ActivityNode>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            if (!visitedNodes.Add(node))
                continue;
            versionIds.Add(node.ActivityVersionId);
            foreach (var child in structures.ProjectChildren(node).SelectMany(slot => slot.Activities))
                pending.Push(child);
        }

        return versionIds;
    }

    private static ActivityInvocationOrigin ReadOrigin(ExecutableNode node)
    {
        if (!node.Metadata.TryGetValue("activity.invocationOrigin", out var value))
            return new([]);
        var segments = JsonSerializer.Deserialize<ActivityInvocationOriginSegment[]>(value, WebJson) ?? [];
        return new(segments);
    }

    private static string HashPrefix(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant()[..24];

    private static string HashPrefix(string value) => HashPrefix(System.Text.Encoding.UTF8.GetBytes(value));

    private sealed class PublicationOverlay(
        IActivityDefinitionVersionPublicationStore inner,
        ActivityDefinitionVersionPublication boundary) : IActivityDefinitionVersionPublicationStore
    {
        public Task<ActivityDefinitionVersionPublication?> FindAsync(string definitionVersionId, CancellationToken cancellationToken = default) =>
            StringComparer.Ordinal.Equals(definitionVersionId, boundary.DefinitionVersionId)
                ? Task.FromResult<ActivityDefinitionVersionPublication?>(boundary)
                : inner.FindAsync(definitionVersionId, cancellationToken);

        public async Task<IReadOnlyList<ActivityDefinitionVersionPublication>> ListByDefinitionAsync(
            string definitionId,
            CancellationToken cancellationToken = default)
        {
            var existing = await inner.ListByDefinitionAsync(definitionId, cancellationToken);
            return StringComparer.Ordinal.Equals(definitionId, boundary.DefinitionId)
                ? existing.Append(boundary).ToArray()
                : existing;
        }
    }
}
