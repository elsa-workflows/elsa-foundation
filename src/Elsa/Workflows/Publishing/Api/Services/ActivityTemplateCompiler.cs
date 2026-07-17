using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Services;

namespace Elsa.Workflows.Publishing.Api.Services;

public sealed record ActivityTemplateDependencyRequest(
    string DefinitionVersionId,
    string OccurrenceId,
    IReadOnlyList<ActivityNodeOrigin> NodeOrigin,
    string? ParentOccurrenceId = null,
    string ChildSlotName = "activity-graph",
    int ChildIndex = 0);

public sealed record ActivityTemplateCompilerRequest(
    ActivityDefinition Definition,
    ActivityDefinitionDraft Draft,
    string CandidateDefinitionVersionId,
    string CandidateVersion,
    long LayoutBytes);

public sealed record ActivityTemplateCompilerResult(
    ExecutableActivityTemplate? Template,
    ActivityResourceMeasurements Measurements,
    IReadOnlyList<ActivityResolvedDependency> DirectDependencies,
    IReadOnlyList<ActivityDiagnostic> Diagnostics)
{
    public bool IsSuccessful => Template is not null && Diagnostics.All(x => x.Severity != ActivityDiagnosticSeverity.Error);
    public IReadOnlyList<ActivityVersionChange> ProviderCompatibilityChanges { get; init; } = [];
}

public interface IActivityTemplateCompiler
{
    ValueTask<ActivityTemplateCompilerResult> CompileAsync(
        ActivityTemplateCompilerRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves exact immutable dependencies and turns a provider compilation into one closed,
/// content-addressed Runtime template. Source identity, layout, timestamps, and the identity stamp
/// derived from the hash are deliberately excluded from the behavioral hash.
/// </summary>
public sealed class ActivityTemplateCompiler(
    IActivityTemplateProviderCompilerRegistry providers,
    IActivityTemplateDependencyDiscovererRegistry dependencyDiscoverers,
    IActivityDefinitionVersionPublicationStore publications,
    IExecutableActivityTemplateReader templates,
    IActivityTemplateAdmissionPolicy admissionPolicy,
    TimeProvider timeProvider) : IActivityTemplateCompiler
{
    public async ValueTask<ActivityTemplateCompilerResult> CompileAsync(
        ActivityTemplateCompilerRequest request,
        CancellationToken cancellationToken = default)
    {
        var subject = Subject(request);
        var diagnostics = new List<ActivityDiagnostic>();
        var resolved = new List<ActivityResolvedDependency>();
        var loadedTemplates = new Dictionary<string, ExecutableActivityTemplate>(StringComparer.Ordinal);
        IReadOnlyList<ActivityTemplateDependencyRequest> authoritativeDependencies;
        IActivityTemplateDependencyDiscoverer discoverer;
        try
        {
            discoverer = dependencyDiscoverers.Resolve(request.Draft.State.Provider.ProviderKey, request.Draft.State.Provider.SchemaVersion);
        }
        catch (InvalidOperationException)
        {
            diagnostics.Add(new(
                "activity.provider.dependency-discovery-unavailable",
                ActivityDiagnosticSeverity.Error,
                "The requested activity provider has no dependency discovery strategy.",
                subject,
                new(request.Draft.State.Provider.ProviderKey),
                Metadata: new Dictionary<string, string>(StringComparer.Ordinal)));
            return Failure(resolved, diagnostics, request.LayoutBytes);
        }

        var discovery = await discoverer.DiscoverDependenciesAsync(new(
            request.Definition.Id,
            request.Draft.Id,
            request.Draft.Revision,
            request.Draft.State.Provider), cancellationToken);
        diagnostics.AddRange(discovery.Diagnostics);
        authoritativeDependencies = discovery.Dependencies.Select(x => new ActivityTemplateDependencyRequest(
            x.DefinitionVersionId,
            x.OccurrenceId,
            x.NodeOrigin,
            x.ParentOccurrenceId,
            x.ChildSlotName,
            x.ChildIndex)).ToArray();

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

            loadedTemplates.TryAdd(template.TemplateId, template);
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
                dependencyRequest.ChildIndex));
        }

        diagnostics.AddRange(ValidateOccurrenceRequests(authoritativeDependencies, subject));
        if (diagnostics.Any(IsError))
            return Failure(resolved, diagnostics, request.LayoutBytes);

        var cycle = await FindCycleAsync(request, resolved, loadedTemplates, cancellationToken);
        if (cycle is not null)
        {
            diagnostics.Add(new(
                "activity.dependency.cycle",
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
            provider = providers.Resolve(request.Draft.State.Provider.ProviderKey, request.Draft.State.Provider.SchemaVersion);
        }
        catch (InvalidOperationException)
        {
            diagnostics.Add(new(
                "activity.provider.unavailable",
                ActivityDiagnosticSeverity.Error,
                "The requested activity provider or manifest schema is unavailable.",
                subject,
                new(request.Draft.State.Provider.ProviderKey),
                Metadata: new Dictionary<string, string>(StringComparer.Ordinal)));
            return Failure(resolved, diagnostics, request.LayoutBytes);
        }

        var compilation = await provider.CompileAsync(new(
            request.Definition.Id,
            request.Definition.ActivityTypeKey,
            request.Draft.Id,
            request.Draft.Revision,
            request.CandidateVersion,
            request.Draft.State.Contract,
            request.Draft.State.Provider,
            resolved,
            provider.CompilerFingerprint), cancellationToken);
        diagnostics.AddRange(compilation.Diagnostics);

        if (!StringComparer.Ordinal.Equals(compilation.ProviderFingerprint, provider.CompilerFingerprint))
        {
            diagnostics.Add(new(
                "activity.provider.contract-invalid",
                ActivityDiagnosticSeverity.Error,
                "The activity provider returned a compiler fingerprint different from its registered fingerprint.",
                subject,
                new(request.Draft.State.Provider.ProviderKey),
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
                new(request.Draft.State.Provider.ProviderKey),
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
            var requiredByNodes = Flatten(compilation.ExecutableRoot)
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
                    new(request.Draft.State.Provider.ProviderKey),
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
                new(request.Draft.State.Provider.ProviderKey),
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

        var directRuntimeDependencies = resolved
            .OrderBy(x => x.OccurrenceId, StringComparer.Ordinal)
            .ThenBy(x => x.VersionId, StringComparer.Ordinal)
            .Select(x => new ExecutableActivityTemplateDependency(
                x.DefinitionId,
                x.VersionId,
                x.Version,
                x.TemplateId,
                x.TemplateHash,
                x.OccurrenceId,
                ToInvocationOrigin(x.NodeOrigin),
                x.ParentOccurrenceId,
                x.ChildSlotName,
                x.ChildIndex))
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
                new(request.Draft.State.Provider.ProviderKey),
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
            $"{value.DefinitionId}\u001f{value.VersionId}\u001f{value.Version}\u001f{value.TemplateId}\u001f{value.TemplateHash}\u001f{value.OccurrenceId}\u001f{value.ParentOccurrenceId}\u001f{value.ChildSlotName}\u001f{value.ChildIndex}\u001f{string.Join("\u001e", value.NodeOrigin.Select(x => $"{x.Kind}\u001d{x.Id}"))}";
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

    private static IEnumerable<ExecutableNode> Flatten(ExecutableNode root)
    {
        var pending = new Stack<ExecutableNode>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            yield return node;
            foreach (var child in node.ChildSlots.SelectMany(x => x.Activities).Reverse())
                pending.Push(child);
        }
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
        "ActivityDraft",
        request.Draft.Id,
        request.Definition.Id,
        Revision: request.Draft.Revision);

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
