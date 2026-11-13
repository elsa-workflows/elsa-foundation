using System.Text.Json;
using Elsa.Primitives.Models;
using RuntimeActivityInputContract = Elsa.Activities.Runtime.Core.Models.ActivityInputContract;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Primitives.Diagnostics;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// Resolves exact immutable dependencies and turns a provider compilation into one closed,
/// content-addressed Runtime template. Source identity, layout, timestamps, and the identity stamp
/// derived from the hash are deliberately excluded from the behavioral hash.
/// </summary>
public sealed class ActivityTemplateCompiler(
    IActivityTemplateProviderCompilerRegistry providers,
    IActivityTemplateDependencyDiscovererRegistry dependencyDiscoverers,
    IActivityDefinitionVersionPublicationStore publications,
    IActivityDefinitionVersionStore activityVersions,
    IExecutableActivityTemplateReader templates,
    IActivityTemplateAdmissionPolicy admissionPolicy,
    TimeProvider timeProvider,
    IEnumerable<IActivityDataSelectionDocumentProjector> dataProjectors,
    IActivityDataSelectionAnalyzer dataAnalyzer,
    IActivityStructureService? activityStructures = null,
    IExecutableStructureNormalizer? structureNormalizer = null) : IActivityTemplateCompiler
{
    // Preserve the existing binary constructor, including mandatory shared source admission.
    public ActivityTemplateCompiler(IActivityTemplateProviderCompilerRegistry providers,
        IActivityTemplateDependencyDiscovererRegistry dependencyDiscoverers,
        IActivityDefinitionVersionPublicationStore publications, IActivityDefinitionVersionStore activityVersions,
        IExecutableActivityTemplateReader templates, IActivityTemplateAdmissionPolicy admissionPolicy, TimeProvider timeProvider,
        IEnumerable<IActivityDataSelectionDocumentProjector> dataProjectors, IActivityDataSelectionAnalyzer dataAnalyzer,
        IActivityStructureService? activityStructures)
        : this(providers, dependencyDiscoverers, publications, activityVersions, templates, admissionPolicy,
            timeProvider, dataProjectors, dataAnalyzer, activityStructures, null) { }

    // Retain the source-analysis constructor while the owning structure projection is added.
    public ActivityTemplateCompiler(
        IActivityTemplateProviderCompilerRegistry providers,
        IActivityTemplateDependencyDiscovererRegistry dependencyDiscoverers,
        IActivityDefinitionVersionPublicationStore publications,
        IActivityDefinitionVersionStore activityVersions,
        IExecutableActivityTemplateReader templates,
        IActivityTemplateAdmissionPolicy admissionPolicy,
        TimeProvider timeProvider,
        IEnumerable<IActivityDataSelectionDocumentProjector> dataProjectors,
        IActivityDataSelectionAnalyzer dataAnalyzer)
        : this(providers, dependencyDiscoverers, publications, activityVersions, templates, admissionPolicy,
            timeProvider, dataProjectors, dataAnalyzer, null)
    {
    }

    // Preserve the existing binary constructor. Legacy callers must supply the shared authority
    // before compiling; absence fails closed rather than restoring the former admission gap.
    public ActivityTemplateCompiler(
        IActivityTemplateProviderCompilerRegistry providers,
        IActivityTemplateDependencyDiscovererRegistry dependencyDiscoverers,
        IActivityDefinitionVersionPublicationStore publications,
        IActivityDefinitionVersionStore activityVersions,
        IExecutableActivityTemplateReader templates,
        IActivityTemplateAdmissionPolicy admissionPolicy,
        TimeProvider timeProvider)
        : this(providers, dependencyDiscoverers, publications, activityVersions, templates, admissionPolicy,
            timeProvider, [], null!)
    {
    }

    public async ValueTask<ActivityTemplateCompilerResult> CompileAsync(
        ActivityTemplateCompilerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return await CompileCoreAsync(request, cancellationToken);
        }
        catch (ActivityDataSelectionRejectedException exception)
        {
            throw new ActivityDataSelectionRejectedException(exception.Findings)
            {
                ResourceKind = request.Candidate.SubjectKind,
                ResourceId = request.Candidate.Id,
                ProviderKey = request.Candidate.Provider.ProviderKey
            };
        }
    }

    private async ValueTask<ActivityTemplateCompilerResult> CompileCoreAsync(
        ActivityTemplateCompilerRequest request,
        CancellationToken cancellationToken)
    {
        var document = await ValidateDataSelectionAsync(request, cancellationToken);
        var projected = document is not null;
        var subject = Subject(request);
        var diagnostics = new List<ActivityDiagnostic>();
        var resolved = new List<ActivityResolvedDependency>();
        var loadedTemplates = new Dictionary<string, ExecutableActivityTemplate>(StringComparer.Ordinal);
        var loadedActivityVersions = new Dictionary<string, ActivityDefinitionVersion>(StringComparer.Ordinal);
        var structureInvocations = new Dictionary<string, ResolvedStructureInvocation>(StringComparer.Ordinal);
        IReadOnlyList<ActivityTemplateDependencyRequest> authoritativeDependencies;
        IActivityTemplateDependencyDiscoverer discoverer;
        try
        {
            discoverer = dependencyDiscoverers.Resolve(request.Candidate.Provider.ProviderKey, request.Candidate.Provider.SchemaVersion);
        }
        catch (InvalidOperationException)
        {
            diagnostics.Add(new(
                "activity.provider.dependency-discovery-unavailable",
                ActivityDiagnosticSeverity.Error,
                "The requested activity provider has no dependency discovery strategy.",
                subject,
                new(request.Candidate.Provider.ProviderKey),
                Metadata: new Dictionary<string, string>(StringComparer.Ordinal)));
            return Failure(resolved, diagnostics, request.LayoutBytes);
        }

        var discovery = await discoverer.DiscoverDependenciesAsync(new(
            request.Definition.Id,
            request.Candidate.Id,
            request.Candidate.Revision,
            request.Candidate.Provider) { SubjectKind = request.Candidate.SubjectKind }, cancellationToken);
        diagnostics.AddRange(discovery.Diagnostics);
        authoritativeDependencies = discovery.Dependencies.Select(x => new ActivityTemplateDependencyRequest(
            x.DefinitionVersionId,
            x.OccurrenceId,
            x.NodeOrigin,
            x.ParentOccurrenceId,
            x.ChildSlotName,
            x.ChildIndex,
            x.MemberUsage)).ToArray();

        foreach (var dependencyRequest in authoritativeDependencies
                     .OrderBy(x => x.OccurrenceId, StringComparer.Ordinal)
                     .ThenBy(x => x.DefinitionVersionId, StringComparer.Ordinal))
        {
            var publication = await publications.FindAsync(dependencyRequest.DefinitionVersionId, cancellationToken);
            if (publication is null)
            {
                diagnostics.Add(Error(
                    "activity.dependency.version-not-found",
                    $"Exact activity version '{dependencyRequest.DefinitionVersionId}' was not found.",
                    subject,
                    dependencyRequest));
                continue;
            }

            if (publication.Lifecycle != ActivityDefinitionVersionLifecycle.Active)
            {
                diagnostics.Add(Error(
                    "activity.dependency.version-inactive",
                    $"Exact activity version '{dependencyRequest.DefinitionVersionId}' is not active.",
                    subject,
                    dependencyRequest));
                continue;
            }

            if (publication.TenantId is not null && !StringComparer.Ordinal.Equals(publication.TenantId, request.Definition.TenantId))
            {
                diagnostics.Add(Error(
                    "activity.dependency.tenant-invalid",
                    $"Exact activity version '{dependencyRequest.DefinitionVersionId}' is not visible in this tenant.",
                    subject,
                    dependencyRequest));
                continue;
            }

            var template = await templates.FindAsync(publication.TemplateId, cancellationToken);
            if (template is null || !StringComparer.Ordinal.Equals(template.TemplateHash, publication.TemplateHash))
            {
                diagnostics.Add(Error(
                    "activity.dependency.template-unavailable",
                    $"Executable template '{publication.TemplateId}' does not match the exact published activity version.",
                    subject,
                    dependencyRequest));
                continue;
            }

            if (!loadedActivityVersions.TryGetValue(publication.DefinitionVersionId, out var activityVersion))
            {
                try
                {
                    activityVersion = await activityVersions.GetAsync(publication.DefinitionVersionId, cancellationToken);
                    loadedActivityVersions.Add(publication.DefinitionVersionId, activityVersion);
                }
                catch (EntityNotFoundException)
                {
                    diagnostics.Add(Error(
                        "activity.dependency.catalog-version-not-found",
                        $"Exact activity catalog version '{dependencyRequest.DefinitionVersionId}' was not found.",
                        subject,
                        dependencyRequest));
                    continue;
                }
            }

            if (!TryReadDeclaredStructure(
                    activityVersion,
                    subject,
                    dependencyRequest,
                    diagnostics,
                    out var declaredStructure))
                continue;

            loadedTemplates.TryAdd(template.TemplateId, template);
            structureInvocations.TryAdd(dependencyRequest.OccurrenceId, new(publication.DefinitionVersionId,
                publication.ResolveWorkflowResolutionKind(), template.Root.ActivityType, template.Root.Descriptor.ConsumerKey,
                template.Root.Descriptor.SchemaVersion, declaredStructure, template.TemplateId, template.TemplateHash));
            resolved.Add(new(
                publication.DefinitionId,
                publication.DefinitionVersionId,
                publication.Version,
                publication.TemplateId,
                publication.TemplateHash,
                publication.Contract,
                publication.Lifecycle,
                publication.TenantId,
                dependencyRequest.OccurrenceId,
                dependencyRequest.NodeOrigin,
                dependencyRequest.ParentOccurrenceId,
                dependencyRequest.ChildSlotName,
                dependencyRequest.ChildIndex,
                dependencyRequest.MemberUsage,
                declaredStructure));
        }

        diagnostics.AddRange(ValidateOccurrenceRequests(authoritativeDependencies, subject));
        if (diagnostics.Any(IsError))
            return Failure(resolved, diagnostics, request.LayoutBytes);

        var cycle = await FindCycleAsync(request, resolved, loadedTemplates, cancellationToken);
        if (cycle is not null)
        {
            diagnostics.Add(new(
                ActivityErrorCodes.DependencyCycle,
                ActivityDiagnosticSeverity.Error,
                "Publishing this draft would create a dependency cycle.",
                subject,
                new(DependencyPath: cycle),
                "Choose an acyclic exact version or remove the reference.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["cycleLength"] = Math.Max(1, cycle.Count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                }));
            return Failure(resolved, diagnostics, request.LayoutBytes);
        }

        IActivityTemplateProviderCompiler provider;
        try
        {
            provider = providers.Resolve(request.Candidate.Provider.ProviderKey, request.Candidate.Provider.SchemaVersion);
        }
        catch (InvalidOperationException)
        {
            diagnostics.Add(new(
                ActivityErrorCodes.ProviderUnavailable,
                ActivityDiagnosticSeverity.Error,
                "The requested activity provider or manifest schema is unavailable.",
                subject,
                new(request.Candidate.Provider.ProviderKey),
                Metadata: new Dictionary<string, string>(StringComparer.Ordinal)));
            return Failure(resolved, diagnostics, request.LayoutBytes);
        }

        var compilation = await provider.CompileAsync(new(
            request.Definition.Id,
            request.Definition.ActivityTypeKey,
            request.Candidate.Id,
            request.Candidate.Revision,
            request.CandidateVersion,
            request.Candidate.Contract,
            request.Candidate.Provider,
            resolved,
            provider.CompilerFingerprint)
        {
            SubjectKind = request.Candidate.SubjectKind,
            ResolvedStructureInvocations = new System.Collections.ObjectModel.ReadOnlyDictionary<string, ResolvedStructureInvocation>(structureInvocations)
        }, cancellationToken);
        diagnostics.AddRange(compilation.Diagnostics);
        if (!projected && compilation.Occurrences.Count != 0)
            throw new ActivityDataSelectionRejectedException([new("$activity", "ActivityDataSelection/ProviderProjectionMissing",
                "The activity provider must expose its authored graph for source-read validation.")]);
        if (compilation.ExecutableRoot is not null)
            ActivityTemplateDataSelectionGuard.Validate(compilation.ExecutableRoot, projected,
                ActivityTemplatePlacer.DescribeTemplateDataSelectionFacts(compilation.ExecutableRoot,
                    request.Candidate.Contract.Inputs.Select(x => x.Name), document, activityStructures));

        if (!StringComparer.Ordinal.Equals(compilation.ProviderFingerprint, provider.CompilerFingerprint))
        {
            diagnostics.Add(new(
                "activity.provider.contract-invalid",
                ActivityDiagnosticSeverity.Error,
                "The activity provider returned a compiler fingerprint different from its registered fingerprint.",
                subject,
                new(request.Candidate.Provider.ProviderKey),
                "Return the exact compiler fingerprint supplied by the Publishing coordinator.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["contractMember"] = nameof(ActivityTemplateCompilation.ProviderFingerprint)
                }));
        }

        if (!ResolvedDependenciesMatch(compilation.DirectDependencies, resolved))
        {
            diagnostics.Add(new(
                "activity.provider.contract-invalid",
                ActivityDiagnosticSeverity.Error,
                "The activity provider returned direct dependencies that differ from the coordinator-resolved authoritative dependency set.",
                subject,
                new(request.Candidate.Provider.ProviderKey),
                "Provider compilers must echo the exact coordinator-resolved dependencies without adding, removing, or changing occurrences.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["contractMember"] = nameof(ActivityTemplateCompilation.DirectDependencies)
                }));
        }

        if (compilation.ExecutableRoot is null && !diagnostics.Any(IsError))
        {
            diagnostics.Add(new(
                "activity.compilation.root-missing",
                ActivityDiagnosticSeverity.Error,
                "The activity provider did not return an executable root.",
                subject,
                Metadata: new Dictionary<string, string>(StringComparer.Ordinal)));
        }

        if (compilation.ExecutableRoot is not null)
        {
            var declared = compilation.RuntimeRequirements.Distinct().OrderBy(x => x.ConsumerKey, StringComparer.Ordinal).ThenBy(x => x.SchemaVersion, StringComparer.Ordinal);
            var requiredByNodes = compilation.ExecutableRoot.DescendantsAndSelf()
                .Select(x => new RuntimeRequirement(x.Descriptor.ConsumerKey, x.Descriptor.SchemaVersion))
                .Distinct()
                .OrderBy(x => x.ConsumerKey, StringComparer.Ordinal)
                .ThenBy(x => x.SchemaVersion, StringComparer.Ordinal);
            if (!declared.SequenceEqual(requiredByNodes))
            {
                diagnostics.Add(new(
                    "activity.provider.runtime-requirements-invalid",
                    ActivityDiagnosticSeverity.Error,
                    "The activity provider's Runtime consumer requirements do not exactly match its executable nodes.",
                    subject,
                    new(request.Candidate.Provider.ProviderKey),
                    "Declare every and only the stable consumer/schema pairs used by the compiled executable nodes.",
                    new Dictionary<string, string>(StringComparer.Ordinal)));
            }
        }

        var invalidMeasurements = InvalidMeasurementNames(compilation.ResourceMeasurements);
        if (invalidMeasurements.Length > 0)
        {
            diagnostics.Add(new(
                "activity.provider.resource-measurements-invalid",
                ActivityDiagnosticSeverity.Error,
                "The activity provider returned one or more negative resource measurements.",
                subject,
                new(request.Candidate.Provider.ProviderKey),
                "Return non-negative resource measurements for admission-policy evaluation.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["invalidMeasurements"] = string.Join(',', invalidMeasurements)
                }));
        }

        if (diagnostics.Any(IsError) || compilation.ExecutableRoot is null)
            return Failure(resolved, diagnostics, request.LayoutBytes, compilation.ResourceMeasurements);

        var closure = await LoadClosureAsync(loadedTemplates.Values, loadedTemplates, subject, diagnostics, cancellationToken);
        if (diagnostics.Any(IsError))
            return Failure(resolved, diagnostics, request.LayoutBytes, compilation.ResourceMeasurements);

        var resolvedOccurrenceIds = resolved.Select(x => x.OccurrenceId).ToHashSet(StringComparer.Ordinal);
        var invalidOccurrence = compilation.Occurrences.FirstOrDefault(x =>
            string.IsNullOrWhiteSpace(x.OccurrenceId) ||
            x.InputBindings.ValueKind != JsonValueKind.Array);
        var duplicateOccurrence = compilation.Occurrences
            .GroupBy(x => x.OccurrenceId, StringComparer.Ordinal)
            .FirstOrDefault(x => x.Count() > 1)?.Key;
        var compiledOccurrenceIds = compilation.Occurrences
            .Select(x => x.OccurrenceId)
            .ToHashSet(StringComparer.Ordinal);
        if (invalidOccurrence is not null ||
            duplicateOccurrence is not null ||
            compilation.Occurrences.Count > 0 && !compiledOccurrenceIds.SetEquals(resolvedOccurrenceIds))
        {
            diagnostics.Add(new(
                "activity.provider.occurrence-compilation-invalid",
                ActivityDiagnosticSeverity.Error,
                invalidOccurrence is not null
                    ? "The activity provider returned placement content with a blank occurrence identity or a non-array input-binding payload."
                    : duplicateOccurrence is not null
                    ? $"The activity provider returned duplicate placement content for occurrence '{duplicateOccurrence}'."
                    : "The activity provider's placement content does not exactly match the resolved dependency occurrences.",
                subject,
                new(request.Candidate.Provider.ProviderKey),
                "Return at most one placement-content record for every resolved occurrence, without adding or omitting occurrences.",
                new Dictionary<string, string>(StringComparer.Ordinal)));
            return Failure(resolved, diagnostics, request.LayoutBytes, compilation.ResourceMeasurements);
        }

        // Translation owns the entire authored subtree, including native containers whose own
        // kind/schema stays the same. Their variables still need the canonical runtime form.
        var translatedSubtree = compilation.Occurrences
            .Where(occurrence => occurrence.Structure is { } target && occurrence.AuthoredStructure is { } authored &&
                (target.Kind != authored.Kind || target.SchemaVersion != authored.SchemaVersion))
            .Select(occurrence => occurrence.OccurrenceId)
            .ToHashSet(StringComparer.Ordinal);
        var childrenByParent = resolved.Where(dependency => dependency.ParentOccurrenceId is not null)
            .ToLookup(dependency => dependency.ParentOccurrenceId!, dependency => dependency.OccurrenceId, StringComparer.Ordinal);
        var pending = new Stack<string>(translatedSubtree);
        while (pending.TryPop(out var parent))
            foreach (var child in childrenByParent[parent])
                if (translatedSubtree.Add(child)) pending.Push(child);

        var normalizedStructures = new Dictionary<string, ExecutableActivityStructure?>(StringComparer.Ordinal);
        foreach (var occurrence in compilation.Occurrences)
        {
            var structure = occurrence.Structure;
            var normalizeAuthoredStructure = structure is not null && occurrence.AuthoredStructure is not null &&
                translatedSubtree.Contains(occurrence.OccurrenceId);
            if (normalizeAuthoredStructure && structureNormalizer is null)
            {
                diagnostics.Add(new("activity.structure.normalizer-unavailable", ActivityDiagnosticSeverity.Error,
                    "Translated activity structures require the canonical executable structure normalizer.", subject,
                    new(request.Candidate.Provider.ProviderKey, ReferenceKey: occurrence.OccurrenceId)));
                continue;
            }
            try
            {
                var normalized = normalizeAuthoredStructure
                    ? structureNormalizer!.Normalize(occurrence.OccurrenceId, structure)
                    : structure is null ? null : new ExecutableActivityStructure(structure.Kind, structure.SchemaVersion, structure.Payload);
                if (normalizeAuthoredStructure && (normalized is null || normalized.Kind != structure!.Kind || normalized.SchemaVersion != structure.SchemaVersion))
                    throw new ArgumentException("The canonical structure normalizer must retain the translated target identity.");
                normalizedStructures.Add(occurrence.OccurrenceId, normalized);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
            {
                diagnostics.Add(new("activity.structure.normalization-refused", ActivityDiagnosticSeverity.Error,
                    exception.Message, subject, new(request.Candidate.Provider.ProviderKey, ReferenceKey: occurrence.OccurrenceId)));
            }
        }
        if (diagnostics.Any(IsError)) return Failure(resolved, diagnostics, request.LayoutBytes, compilation.ResourceMeasurements);

        var occurrenceCompilations = compilation.Occurrences.ToDictionary(x => x.OccurrenceId, StringComparer.Ordinal);
        var directRuntimeDependencies = resolved
            .OrderBy(x => x.OccurrenceId, StringComparer.Ordinal)
            .ThenBy(x => x.VersionId, StringComparer.Ordinal)
            .Select(x =>
            {
                ExecutableActivityTemplateOccurrenceOverlay? overlay = null;
                if (occurrenceCompilations.TryGetValue(x.OccurrenceId, out var occurrence))
                {
                    var structure = normalizedStructures[x.OccurrenceId];
                    overlay = new(
                        ExecutableActivityTemplateOccurrenceOverlay.SupportedSchemaVersion,
                        occurrence.InputBindings,
                        structure,
                        occurrence.OutputBindings)
                    {
                        InputContracts = CompileOccurrenceInputContracts(occurrence, loadedTemplates[x.TemplateId].Root)
                    };
                }

                return new ExecutableActivityTemplateDependency(
                    x.DefinitionId,
                    x.VersionId,
                    x.Version,
                    x.TemplateId,
                    x.TemplateHash,
                    x.OccurrenceId,
                    ToInvocationOrigin(x.NodeOrigin),
                    x.ParentOccurrenceId,
                    x.ChildSlotName,
                    x.ChildIndex,
                    overlay);
            })
            .ToArray();
        var closedTemplates = closure.Values
            .Select(x => new ExecutableActivityTemplateIdentity(x.TemplateId, x.TemplateHash))
            .Distinct()
            .OrderBy(x => x.TemplateHash, StringComparer.Ordinal)
            .ThenBy(x => x.TemplateId, StringComparer.Ordinal)
            .ToArray();
        var runtimeRequirements = compilation.RuntimeRequirements
            .Concat(closure.Values.SelectMany(x => x.RuntimeRequirements))
            .Distinct()
            .OrderBy(x => x.ConsumerKey, StringComparer.Ordinal)
            .ThenBy(x => x.SchemaVersion, StringComparer.Ordinal)
            .ToArray();
        var storageDriverRequirements = compilation.StorageDriverRequirements
            .Concat(closure.Values.SelectMany(x => x.StorageDriverRequirements))
            .Distinct()
            .OrderBy(x => x.DriverKey, StringComparer.Ordinal)
            .ToArray();
        var compatibilityMetadata = compilation.ProviderCompatibilityChanges
            .OrderBy(x => x.ChangeId, StringComparer.Ordinal)
            .ToDictionary(x => $"provider.change.{x.ChangeId}", x => $"{x.Impact}:{x.RequiredBump}", StringComparer.Ordinal);
        ActivityResourceMeasurements measurements;
        try
        {
            measurements = CloseMeasurements(compilation.ResourceMeasurements, closure.Values, request.LayoutBytes);
        }
        catch (OverflowException)
        {
            diagnostics.Add(new(
                "activity.provider.resource-measurements-invalid",
                ActivityDiagnosticSeverity.Error,
                "The closed activity resource measurements exceed the supported numeric range.",
                subject,
                new(request.Candidate.Provider.ProviderKey),
                "Return bounded local measurements and review the compiled dependency closure.",
                new Dictionary<string, string>(StringComparer.Ordinal)));
            return Failure(resolved, diagnostics, request.LayoutBytes, compilation.ResourceMeasurements);
        }
        var admission = await admissionPolicy.EvaluateAsync(
            measurements,
            new(request.Definition.TenantId, "PublishActivityDefinition"),
            cancellationToken);
        diagnostics.AddRange(admission.Diagnostics);
        if (!admission.IsAccepted && !diagnostics.Any(IsError))
        {
            diagnostics.Add(new(
                "activity.template.admission-rejected",
                ActivityDiagnosticSeverity.Error,
                "The compiled activity template was rejected by the host admission policy.",
                subject,
                Metadata: new Dictionary<string, string>(StringComparer.Ordinal)));
        }

        if (diagnostics.Any(IsError))
            return new(null, measurements, resolved, ActivityDiagnosticOrderer.Order(diagnostics));

        var hash = ExecutableActivityTemplateBehaviorHasher.Compute(
            compilation.ExecutableRoot,
            compilation.TemplateLocalResumeTargets,
            directRuntimeDependencies,
            closedTemplates,
            runtimeRequirements,
            storageDriverRequirements,
            provider.CompilerFingerprint,
            compatibilityMetadata);
        var templateId = $"activity-template-{hash["sha256:".Length..]}";
        var executableTemplate = new ExecutableActivityTemplate(
            templateId,
            hash,
            compilation.ExecutableRoot,
            compilation.TemplateLocalResumeTargets,
            directRuntimeDependencies,
            closedTemplates,
            runtimeRequirements,
            provider.CompilerFingerprint,
            compatibilityMetadata,
            timeProvider.GetUtcNow(),
            storageDriverRequirements);

        return new(executableTemplate, measurements, resolved, ActivityDiagnosticOrderer.Order(diagnostics))
        {
            ProviderCompatibilityChanges = compilation.ProviderCompatibilityChanges
        };
    }

    private async ValueTask<ActivityDataSelectionDocument?> ValidateDataSelectionAsync(ActivityTemplateCompilerRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (dataAnalyzer is null)
            throw new InvalidOperationException("Activity compilation requires the shared activity data selection analyzer.");
        var revision = ExecutableActivityTemplateBehaviorHasher.ComputeCanonicalValueHash(new
        {
            request.Candidate.SubjectKind, request.Candidate.Id, request.Candidate.Revision,
            request.Candidate.Provider, request.Candidate.Contract
        });
        ActivityDataSelectionDocument? document = null;
        foreach (var projector in dataProjectors)
        {
            var candidate = await projector.TryProjectAsync(request.Candidate.Provider, request.Candidate.Contract,
                request.Candidate.Id, revision, cancellationToken);
            if (candidate is null)
                continue;
            if (document is not null)
                throw new InvalidOperationException("Multiple providers claimed the activity data selection document.");
            if (!StringComparer.Ordinal.Equals(candidate.ResourceId, request.Candidate.Id) ||
                !StringComparer.Ordinal.Equals(candidate.ContractRevision, revision))
                throw new InvalidOperationException("The activity data selection document does not match the actual candidate.");
            document = candidate;
        }
        if (document is null)
            return null;
        var analysis = await dataAnalyzer.AnalyzeAsync(document, cancellationToken: cancellationToken);
        if (!StringComparer.Ordinal.Equals(analysis.ContractRevision, revision))
            throw new InvalidOperationException("Activity data selection analysis does not match the actual candidate.");
        var errors = analysis.Findings.Where(x => StringComparer.Ordinal.Equals(x.Severity, ValidationSeverities.Error)).ToArray();
        if (errors.Length != 0)
            throw new ActivityDataSelectionRejectedException(errors);
        return document;
    }

    private static bool TryReadDeclaredStructure(
        ActivityDefinitionVersion activityVersion,
        ActivityDiagnosticSubject subject,
        ActivityTemplateDependencyRequest dependency,
        ICollection<ActivityDiagnostic> diagnostics,
        out ActivityStructureContract? contract)
    {
        contract = null;
        if (ActivityStructureDesignFacetReader.TryReadSingle(
                activityVersion.DesignFacets,
                out var structureFacet))
        {
            if (structureFacet is not null)
            {
                ActivityStructureDesignFacetReader.TryReadContract(structureFacet, out var parsed);
                contract = parsed;
            }
            return true;
        }

        diagnostics.Add(Error(
            "activity.dependency.structure-contract-invalid",
            $"Exact activity version '{dependency.DefinitionVersionId}' declares more than one structure contract.",
            subject,
            dependency));
        return false;
    }

    private async ValueTask<IReadOnlyList<ActivityDependencyPathItem>?> FindCycleAsync(
        ActivityTemplateCompilerRequest request,
        IReadOnlyList<ActivityResolvedDependency> directDependencies,
        Dictionary<string, ExecutableActivityTemplate> loadedTemplates,
        CancellationToken cancellationToken)
    {
        var candidate = new ActivityDependencyPathItem(
            request.Definition.Id,
            request.CandidateDefinitionVersionId,
            request.CandidateVersion,
            "pending");
        var stack = new Stack<(ExecutableActivityTemplateDependency Dependency, IReadOnlyList<ActivityDependencyPathItem> Path)>();
        foreach (var dependency in directDependencies.OrderByDescending(x => x.OccurrenceId, StringComparer.Ordinal))
        {
            var template = loadedTemplates[dependency.TemplateId];
            stack.Push((new(
                dependency.DefinitionId,
                dependency.VersionId,
                dependency.Version,
                dependency.TemplateId,
                dependency.TemplateHash,
                dependency.OccurrenceId,
                ToInvocationOrigin(dependency.NodeOrigin)), [candidate]));
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (stack.TryPop(out var current))
        {
            var item = new ActivityDependencyPathItem(
                current.Dependency.DefinitionId,
                current.Dependency.DefinitionVersionId,
                current.Dependency.Version,
                current.Dependency.TemplateHash);
            var path = current.Path.Append(item).ToArray();
            if (StringComparer.Ordinal.Equals(current.Dependency.DefinitionId, request.Definition.Id))
                return path.Append(candidate).ToArray();
            if (!visited.Add(current.Dependency.TemplateHash))
                continue;

            if (!loadedTemplates.TryGetValue(current.Dependency.TemplateId, out var template))
            {
                template = await templates.FindAsync(current.Dependency.TemplateId, cancellationToken);
                if (template is null || !StringComparer.Ordinal.Equals(template.TemplateHash, current.Dependency.TemplateHash))
                    continue;
                loadedTemplates[template.TemplateId] = template;
            }

            foreach (var child in template.DirectDependencies
                         .OrderByDescending(x => x.OccurrenceId, StringComparer.Ordinal)
                         .ThenByDescending(x => x.DefinitionVersionId, StringComparer.Ordinal))
                stack.Push((child, path));
        }

        return null;
    }

    private async ValueTask<IReadOnlyDictionary<string, ExecutableActivityTemplate>> LoadClosureAsync(
        IEnumerable<ExecutableActivityTemplate> direct,
        Dictionary<string, ExecutableActivityTemplate> loaded,
        ActivityDiagnosticSubject subject,
        ICollection<ActivityDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var closure = new Dictionary<string, ExecutableActivityTemplate>(StringComparer.Ordinal);
        var pending = new Stack<ExecutableActivityTemplate>(direct.OrderByDescending(x => x.TemplateHash, StringComparer.Ordinal));
        while (pending.TryPop(out var template))
        {
            if (!closure.TryAdd(template.TemplateHash, template))
                continue;

            foreach (var identity in template.ClosedTemplates.OrderByDescending(x => x.TemplateHash, StringComparer.Ordinal))
            {
                if (closure.ContainsKey(identity.TemplateHash))
                    continue;
                if (!loaded.TryGetValue(identity.TemplateId, out var child))
                {
                    child = await templates.FindAsync(identity.TemplateId, cancellationToken);
                    if (child is not null)
                        loaded[child.TemplateId] = child;
                }

                if (child is null || !StringComparer.Ordinal.Equals(child.TemplateHash, identity.TemplateHash))
                {
                    diagnostics.Add(new(
                        "activity.dependency.closure-incomplete",
                        ActivityDiagnosticSeverity.Error,
                        $"Closed executable template '{identity.TemplateId}' is unavailable or has a different hash.",
                        subject,
                        Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["templateId"] = identity.TemplateId,
                            ["templateHash"] = identity.TemplateHash
                        }));
                    continue;
                }

                pending.Push(child);
            }
        }

        return closure;
    }

    private static IReadOnlyList<RuntimeActivityInputContract>? CompileOccurrenceInputContracts(ActivityTemplateOccurrenceCompilation occurrence, ExecutableNode source)
    {
        if (occurrence.StructureInputs is not { } projection || projection.Inputs.Count == 0 && projection.ConsumedInputKeys.Count == 0)
            return null;
        var sourceInputs = source.ActivityContract?.Inputs
            ?? throw new InvalidOperationException("An occurrence with structure inputs requires an exact pinned source contract.");
        if (projection.ConsumedInputKeys.Any(key => !sourceInputs.ContainsKey(key)))
            throw new InvalidOperationException("An occurrence structure consumes an undeclared source input.");
        var result = sourceInputs.Values.Where(input => !projection.ConsumedInputKeys.Contains(input.Key, StringComparer.Ordinal)).ToDictionary(input => input.Key, StringComparer.Ordinal);
        foreach (var input in projection.Inputs)
        {
            RuntimeActivityInputContract contract;
            if (input.Definition is { } definition)
            {
                if (definition.ReferenceKey != input.ReferenceKey) throw new InvalidOperationException("An occurrence structure input does not match its definition.");
                contract = new(input.ReferenceKey, definition.Name, new ValueTypeDescriptor(definition.Type.Alias, definition.Type.CollectionKind),
                    definition.IsRequired, definition.IsNullable, definition.DefaultValue.HasValue, definition.DefaultValue,
                    RuntimeInputBindingCompiler.EffectivePolicy(occurrence.OccurrenceId, definition, input.State),
                    isCredential: definition.IsCredential == true, connectionType: definition.ConnectionType) { IsStructureInput = true };
            }
            else
            {
                var original = sourceInputs.GetValueOrDefault(input.ReferenceKey)
                    ?? throw new InvalidOperationException("An occurrence structure input has no source declaration.");
                var policy = input.State is null ? original.Policy : ValuePolicyCombiner.CombineAuthoredInput(original.Policy,
                    input.State.StorageDriverType, input.State.IsSensitive, $"Occurrence '{occurrence.OccurrenceId}' input '{input.ReferenceKey}'");
                contract = new(original.Key, original.Name, original.Type, original.IsRequired, original.IsNullable, original.HasDefault,
                    original.DefaultValue, policy, original.EditorMetadata, original.IsCredential, original.ConnectionType) { IsStructureInput = true };
            }
            if (!result.TryAdd(input.ReferenceKey, contract)) throw new InvalidOperationException("An occurrence structure declares a duplicate input key.");
        }
        return result.Values.OrderBy(input => input.Key, StringComparer.Ordinal).ToArray();
    }

    private static IEnumerable<ActivityDiagnostic> ValidateOccurrenceRequests(
        IReadOnlyList<ActivityTemplateDependencyRequest> dependencies,
        ActivityDiagnosticSubject subject)
    {
        foreach (var duplicate in dependencies
                     .GroupBy(x => x.OccurrenceId, StringComparer.Ordinal)
                     .Where(x => x.Count() > 1)
                     .OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            yield return new(
                "activity.dependency.occurrence-duplicate",
                ActivityDiagnosticSeverity.Error,
                $"Dependency occurrence '{duplicate.Key}' is declared more than once.",
                subject,
                new(ReferenceKey: duplicate.Key),
                Metadata: new Dictionary<string, string>(StringComparer.Ordinal));
        }

        var occurrences = dependencies.Select(x => x.OccurrenceId).ToHashSet(StringComparer.Ordinal);
        foreach (var dependency in dependencies.OrderBy(x => x.OccurrenceId, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(dependency.ChildSlotName) || dependency.ChildIndex < 0 ||
                dependency.ParentOccurrenceId is not null && !occurrences.Contains(dependency.ParentOccurrenceId))
                yield return new(
                    "activity.dependency.structure-invalid",
                    ActivityDiagnosticSeverity.Error,
                    $"Dependency occurrence '{dependency.OccurrenceId}' has an invalid parent/slot/order relationship.",
                    subject,
                    new(ReferenceKey: dependency.OccurrenceId, NodeOrigin: dependency.NodeOrigin),
                    Metadata: new Dictionary<string, string>(StringComparer.Ordinal));
        }
        foreach (var duplicatePosition in dependencies
                     .GroupBy(x => (x.ParentOccurrenceId, x.ChildSlotName, x.ChildIndex))
                     .Where(x => x.Count() > 1))
            yield return new(
                "activity.dependency.structure-position-duplicate",
                ActivityDiagnosticSeverity.Error,
                "Two dependency occurrences claim the same authored parent/slot/order position.",
                subject,
                Metadata: new Dictionary<string, string>(StringComparer.Ordinal));
    }

    private static bool ResolvedDependenciesMatch(
        IReadOnlyList<ActivityResolvedDependency> providerDependencies,
        IReadOnlyList<ActivityResolvedDependency> authoritative)
    {
        static string Key(ActivityResolvedDependency value) =>
            $"{value.DefinitionId}\u001f{value.VersionId}\u001f{value.Version}\u001f{value.TemplateId}\u001f{value.TemplateHash}\u001f{value.OccurrenceId}\u001f{value.ParentOccurrenceId}\u001f{value.ChildSlotName}\u001f{value.ChildIndex}\u001f{string.Join("\u001e", value.NodeOrigin.Select(x => $"{x.Kind}\u001d{x.Id}"))}\u001f{string.Join("\u001e", value.MemberUsage.OrderBy(x => x.MemberKind, StringComparer.Ordinal).ThenBy(x => x.ReferenceKey, StringComparer.Ordinal).ThenBy(x => x.UsageKind, StringComparer.Ordinal).Select(x => $"{x.MemberKind}\u001d{x.ReferenceKey}\u001d{x.UsageKind}"))}\u001f{value.DeclaredStructure?.Kind}\u001d{value.DeclaredStructure?.SchemaVersion}";
        return providerDependencies.Select(Key).Order(StringComparer.Ordinal)
            .SequenceEqual(authoritative.Select(Key).Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    private static ActivityResourceMeasurements CloseMeasurements(
        ActivityResourceMeasurements local,
        IEnumerable<ExecutableActivityTemplate> closure,
        long layoutBytes)
    {
        var closed = closure.ToArray();
        checked
        {
            return local with
            {
                ClosedNodeCount = local.LocalNodeCount + closed.Sum(x => (long)x.NodesById.Count),
                DescriptorBytes = local.DescriptorBytes + closed.Sum(x => x.NodesById.Values.Sum(node => (long)JsonSerializer.SerializeToUtf8Bytes(node.Descriptor).Length)),
                LayoutBytes = layoutBytes
            };
        }
    }

    private static string[] InvalidMeasurementNames(ActivityResourceMeasurements measurements)
    {
        var invalid = new List<string>(7);
        if (measurements.LocalNodeCount < 0) invalid.Add(nameof(measurements.LocalNodeCount));
        if (measurements.ClosedNodeCount < 0) invalid.Add(nameof(measurements.ClosedNodeCount));
        if (measurements.DependencyCount < 0) invalid.Add(nameof(measurements.DependencyCount));
        if (measurements.MaximumObservedAuthoredDepth < 0) invalid.Add(nameof(measurements.MaximumObservedAuthoredDepth));
        if (measurements.DescriptorBytes < 0) invalid.Add(nameof(measurements.DescriptorBytes));
        if (measurements.LayoutBytes < 0) invalid.Add(nameof(measurements.LayoutBytes));
        if (measurements.EstimatedDurableBoundarySlots < 0) invalid.Add(nameof(measurements.EstimatedDurableBoundarySlots));
        return invalid.ToArray();
    }

    private static ActivityInvocationOrigin ToInvocationOrigin(IReadOnlyList<ActivityNodeOrigin> origin) => new(
        origin.Select(x => new ActivityInvocationOriginSegment(MapOrigin(x.Kind), x.Id)).ToArray());

    private static ActivityInvocationOriginSegmentKind MapOrigin(string kind) => kind switch
    {
        "WorkflowRoot" => ActivityInvocationOriginSegmentKind.WorkflowRoot,
        "TemplateBoundary" => ActivityInvocationOriginSegmentKind.TemplateBoundary,
        "NestedPlacement" => ActivityInvocationOriginSegmentKind.NestedPlacement,
        _ => ActivityInvocationOriginSegmentKind.AuthoredNode
    };

    private static ActivityDiagnostic Error(
        string code,
        string message,
        ActivityDiagnosticSubject subject,
        ActivityTemplateDependencyRequest dependency) => new(
        code,
        ActivityDiagnosticSeverity.Error,
        message,
        subject,
        new(ReferenceKey: dependency.OccurrenceId, NodeOrigin: dependency.NodeOrigin),
        Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["definitionVersionId"] = dependency.DefinitionVersionId
        });

    private static ActivityDiagnosticSubject Subject(ActivityTemplateCompilerRequest request) => new(
        request.Candidate.SubjectKind,
        request.Candidate.Id,
        request.Definition.Id,
        Revision: request.Candidate.Revision);

    private static bool IsError(ActivityDiagnostic diagnostic) => diagnostic.Severity == ActivityDiagnosticSeverity.Error;

    private static ActivityTemplateCompilerResult Failure(
        IReadOnlyList<ActivityResolvedDependency> resolved,
        IEnumerable<ActivityDiagnostic> diagnostics,
        long layoutBytes,
        ActivityResourceMeasurements? measurements = null) => new(
        null,
        (measurements ?? new(0, 0, resolved.Count, 0, 0, 0, 0)) with { LayoutBytes = layoutBytes },
        resolved,
        ActivityDiagnosticOrderer.Order(diagnostics));
}
