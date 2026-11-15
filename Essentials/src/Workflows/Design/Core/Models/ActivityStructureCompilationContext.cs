using System.Collections.ObjectModel;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Models;

/// <summary>Exact server-resolved invocation facts captured before template-boundary placement.</summary>
public sealed record ResolvedStructureInvocation(
    string ActivityVersionId,
    ActivityDefinitionVersionResolutionKind ResolutionKind,
    string RuntimeActivityTypeKey,
    string RuntimeConsumerKey,
    string RuntimeDescriptorSchemaVersion,
    ActivityStructureContract? DeclaredStructure,
    string? TemplateId = null,
    string? TemplateHash = null);

/// <summary>
/// Transient source and resolved invocation facts for activity-owned structure compilation.
/// Source payloads cannot supply this context. It contains no catalog lookup or mutable authority.
/// </summary>
public sealed class ActivityStructureCompilationContext
{
    public ActivityStructureCompilationContext(
        ActivityNode physicalRoot,
        IReadOnlyDictionary<string, ResolvedStructureInvocation> invocations,
        IReadOnlyDictionary<string, IReadOnlyCollection<ActivityChildProjection>> childProjections)
    {
        ArgumentNullException.ThrowIfNull(physicalRoot);
        ArgumentNullException.ThrowIfNull(invocations);
        ArgumentNullException.ThrowIfNull(childProjections);
        PhysicalRoot = physicalRoot;
        var facts = new Dictionary<string, ResolvedStructureInvocation>(StringComparer.Ordinal);
        foreach (var (nodeId, invocation) in invocations)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
            ArgumentNullException.ThrowIfNull(invocation);
            ArgumentException.ThrowIfNullOrWhiteSpace(invocation.ActivityVersionId);
            ArgumentException.ThrowIfNullOrWhiteSpace(invocation.RuntimeActivityTypeKey);
            ArgumentException.ThrowIfNullOrWhiteSpace(invocation.RuntimeConsumerKey);
            ArgumentException.ThrowIfNullOrWhiteSpace(invocation.RuntimeDescriptorSchemaVersion);
            if (!Enum.IsDefined(invocation.ResolutionKind) || invocation.ResolutionKind == ActivityDefinitionVersionResolutionKind.Unspecified)
                throw new ArgumentException("Structure compilation requires an exact resolved activity role.");
            facts.Add(nodeId, invocation);
        }
        Invocations = new ReadOnlyDictionary<string, ResolvedStructureInvocation>(facts);
        ChildProjections = new ReadOnlyDictionary<string, IReadOnlyCollection<ActivityChildProjection>>(
            childProjections.ToDictionary(pair => pair.Key, pair => (IReadOnlyCollection<ActivityChildProjection>)pair.Value
                .Select(slot => slot with { Activities = slot.Activities.ToArray(), OutcomeProjections = slot.OutcomeProjections.ToArray() }).ToArray(), StringComparer.Ordinal));
    }

    public ActivityNode PhysicalRoot { get; }
    public IReadOnlyDictionary<string, ResolvedStructureInvocation> Invocations { get; }
    public IReadOnlyDictionary<string, IReadOnlyCollection<ActivityChildProjection>> ChildProjections { get; }
}
