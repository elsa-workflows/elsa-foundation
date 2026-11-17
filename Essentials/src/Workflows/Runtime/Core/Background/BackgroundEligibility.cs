using System.Reflection;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Background;

/// <summary>The background execution eligibility criteria of ADR 0093 decision 2.</summary>
public enum BackgroundEligibilityCriterion
{
    /// <summary>A leaf: a CLR activity that is no intrinsic, no reusable boundary, has no children and is not structural.</summary>
    C1 = 1,
    /// <summary>Completes in one go: not stateful and no checkpoint participant.</summary>
    C2,
    /// <summary>Typed result: a pinned, typed result contract that is not <c>object</c> or <c>Any</c>.</summary>
    C3,
    /// <summary>Durable result: no stream, transient-resource or non-persistable result or projection.</summary>
    C4,
    /// <summary>No hidden result: no sensitive or encryption-required result or projection.</summary>
    C5,
    /// <summary>No coordinator effects from the body (checked on the worker, after the body ran).</summary>
    C6,
    /// <summary>Tolerates running more than once (declared by the activity author).</summary>
    C7,
    /// <summary>Cancels cooperatively (declared by the activity author).</summary>
    C8,
    /// <summary>Variables only through inputs and results: the body does not read or write variables by name.</summary>
    C9,
    /// <summary>Worker-bound inputs are supported: durable, materializable on the worker and protected.</summary>
    C10
}

/// <summary>
/// What the eligibility criteria read about one node. Each caller builds it from what it knows; a criterion whose fact
/// is absent does not run (C6 runs only when <see cref="StagedCoordinatorEffects"/> is set, on the worker).
/// </summary>
public sealed record BackgroundEligibilitySubject(string NodeId)
{
    /// <summary>The node's execution placement; only a defined <see cref="ActivityExecutionPlacement.Background"/> is checked.</summary>
    public ActivityExecutionPlacement Placement { get; init; } = ActivityExecutionPlacement.Background;

    /// <summary>The CLR activity type, or null when the node's type is not a registered CLR activity.</summary>
    public Type? ActivityType { get; init; }

    /// <summary>The node is an engine intrinsic.</summary>
    public bool IsIntrinsic { get; init; }

    /// <summary>The node is a reusable activity boundary (a placed activity definition).</summary>
    public bool IsReusableBoundary { get; init; }

    /// <summary>The node has an executable structure.</summary>
    public bool HasStructure { get; init; }

    /// <summary>The node has child slots.</summary>
    public bool HasChildren { get; init; }

    /// <summary>The pinned activity contract, or null when the node has none.</summary>
    public ActivityContract? Contract { get; init; }

    /// <summary>The node's compiled input bindings, read by C10.</summary>
    public IReadOnlyDictionary<string, RuntimeInputBinding> InputBindings { get; init; } =
        new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal);

    /// <summary>Whether the body staged a workflow dispatch or published stimuli. Null before the body ran.</summary>
    public bool? StagedCoordinatorEffects { get; init; }

    /// <summary>
    /// Builds the run-time subject of a pinned node, resolving its CLR activity type from the pinned activation
    /// capability as the worker does. An unresolvable capability leaves the type unknown, which C1 refuses.
    /// </summary>
    public static BackgroundEligibilitySubject ForPinnedNode(ExecutableNode node, IWellKnownTypeRegistry typeRegistry)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(typeRegistry);
        var activityType = node.ActivityContract is { } contract &&
                           typeRegistry.TryGetTypeOrDefault(contract.Activation.Capability, out var resolved) &&
                           typeof(IActivity).IsAssignableFrom(resolved)
            ? resolved
            : null;
        return ForPinnedNode(node, activityType);
    }

    /// <summary>Builds the run-time subject of a pinned node and, when known, its CLR activity type.</summary>
    public static BackgroundEligibilitySubject ForPinnedNode(ExecutableNode node, Type? activityType)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new BackgroundEligibilitySubject(node.ExecutableNodeId)
        {
            Placement = node.ExecutionPlacement,
            ActivityType = activityType,
            IsIntrinsic = node.IntrinsicKind is not null,
            HasStructure = node.Structure is not null,
            HasChildren = node.ChildSlots.Count > 0,
            Contract = node.ActivityContract,
            InputBindings = node.InputBindings
        };
    }
}

/// <summary>One background eligibility refusal: <c>VF-BG-001</c>, the failed criterion and a plain reason.</summary>
public sealed record BackgroundEligibilityRefusal(BackgroundEligibilityCriterion? Criterion, string NodeId, string Reason)
{
    /// <summary>The diagnostic code every background eligibility refusal carries.</summary>
    public const string Code = "VF-BG-001";

    /// <summary>The rendered refusal: <c>VF-BG-001 (C9): Background execution is not eligible for activity node '…': …</c>.</summary>
    public string Message => Criterion is { } criterion
        ? $"{Code} ({criterion}): Background execution is not eligible for activity node '{NodeId}': {Reason}."
        : $"{Code}: Background execution is not eligible for activity node '{NodeId}': {Reason}.";

    /// <inheritdoc />
    public override string ToString() => Message;
}

/// <summary>
/// The one place that decides background execution eligibility (ADR 0093 decision 3). Publication, background
/// admission and the background worker call it; no other code decides eligibility. Each entry point returns the first
/// failing criterion in the order C1 … C10, or null.
/// </summary>
/// <remarks>
/// C7 and C8 cannot be checked by code: the activity author promises them by declaring
/// <see cref="BackgroundExecutionSafeAttribute"/>. That declaration is checked after the engine-checked criteria, so a
/// refusal names the concrete criterion a node fails before it reports a missing declaration.
/// </remarks>
public static class BackgroundEligibility
{
    private delegate string? Criterion(BackgroundEligibilitySubject subject);

    private static readonly (BackgroundEligibilityCriterion Id, Criterion Check)[] PlacementCriteria =
    [
        (BackgroundEligibilityCriterion.C1, IsLeaf),
        (BackgroundEligibilityCriterion.C2, CompletesInOneGo),
        (BackgroundEligibilityCriterion.C3, HasTypedResult),
        (BackgroundEligibilityCriterion.C4, HasDurableResult),
        (BackgroundEligibilityCriterion.C5, HasNoHiddenResult),
        (BackgroundEligibilityCriterion.C9, UsesVariablesOnlyThroughInputsAndResults),
        (BackgroundEligibilityCriterion.C10, HasSupportedWorkerBoundInputs)
    ];

    /// <summary>
    /// The publication check of a node placed in the background: C1–C5, C9, C10 and the declaration. Publication
    /// knows the authored node, its CLR type and the contract it is about to pin.
    /// </summary>
    public static BackgroundEligibilityRefusal? CheckPublication(BackgroundEligibilitySubject subject) =>
        Check(subject, includeCoordinatorEffects: false);

    /// <summary>
    /// The run-time re-check at admission and on the worker: the same criteria as publication, and C6 when the subject
    /// says whether the body staged coordinator effects.
    /// </summary>
    public static BackgroundEligibilityRefusal? CheckRunTime(BackgroundEligibilitySubject subject) =>
        Check(subject, includeCoordinatorEffects: true);

    /// <summary>
    /// C9 as one predicate over the CLR type: the activity reads variables by name. ADR 0090's
    /// <c>[AccessesUndeclaredVariables]</c> marker and writable <c>VariableReference&lt;T&gt;</c> inputs join it here
    /// when they exist in code.
    /// </summary>
    public static bool AccessesVariablesByName(Type activityType)
    {
        ArgumentNullException.ThrowIfNull(activityType);
        return typeof(IRuntimeScopedVariableReader).IsAssignableFrom(activityType);
    }

    private static BackgroundEligibilityRefusal? Check(BackgroundEligibilitySubject subject, bool includeCoordinatorEffects)
    {
        ArgumentNullException.ThrowIfNull(subject);
        // Preconditions outside C1–C10: the placement is defined and is the background.
        if (!Enum.IsDefined(subject.Placement))
            return new BackgroundEligibilityRefusal(null, subject.NodeId, $"execution placement '{subject.Placement}' is not defined");
        if (subject.Placement != ActivityExecutionPlacement.Background)
            return new BackgroundEligibilityRefusal(null, subject.NodeId, "the node is not placed in the background");

        foreach (var (id, check) in PlacementCriteria)
        {
            if (check(subject) is { } reason)
                return new BackgroundEligibilityRefusal(id, subject.NodeId, reason);

            // C6 sits between C5 and C9 in criterion order; it is a run-time fact about the body.
            if (id == BackgroundEligibilityCriterion.C5 && includeCoordinatorEffects && subject.StagedCoordinatorEffects == true)
                return new BackgroundEligibilityRefusal(BackgroundEligibilityCriterion.C6, subject.NodeId,
                    "the body staged a workflow dispatch or published stimuli; those effects belong to the coordinator");
        }

        return DeclaresBackgroundExecutionSafe(subject)
            ? null
            : new BackgroundEligibilityRefusal(null, subject.NodeId, "the activity type does not declare BackgroundExecutionSafe");
    }

    private static string? IsLeaf(BackgroundEligibilitySubject subject)
    {
        if (subject.IsIntrinsic)
            return "engine intrinsics cannot run as background activities";
        if (subject.IsReusableBoundary)
            return "reusable activity boundaries are not supported";
        if (subject.ActivityType is not { } type)
            return "the activity type is not a registered CLR activity";
        if (subject.HasStructure || subject.HasChildren ||
            typeof(IRuntimeStructuralActivity).IsAssignableFrom(type) ||
            typeof(IRuntimeActivityChildCompletionHandler).IsAssignableFrom(type) ||
            typeof(IRuntimeActivityChildFaultHandler).IsAssignableFrom(type) ||
            typeof(IRuntimeActivityChildOutcomeHandler).IsAssignableFrom(type) ||
            typeof(IRuntimeActivityChildNotificationHandler).IsAssignableFrom(type))
            return "structural activities and nodes with children are not supported";
        return null;
    }

    private static string? CompletesInOneGo(BackgroundEligibilitySubject subject)
    {
        var type = subject.ActivityType!;
        if (typeof(IStatefulActivity).IsAssignableFrom(type))
            return "stateful activities are not supported";
        if (typeof(IRuntimeActivityCheckpointParticipant).IsAssignableFrom(type))
            return "checkpoint-participating activities are not supported";
        return null;
    }

    private static string? HasTypedResult(BackgroundEligibilitySubject subject)
    {
        if (subject.Contract is not { } contract)
            return "the activity has no pinned typed result contract";
        if (FindResultType(subject.ActivityType!) == typeof(object) ||
            contract.Result.Type.Alias is "Object" or "System.Object" or "Any" or "Elsa.Any")
            return "untyped object results are not supported";
        return null;
    }

    private static string? HasDurableResult(BackgroundEligibilitySubject subject)
    {
        var result = subject.Contract!.Result;
        var resultType = FindResultType(subject.ActivityType!);
        if ((resultType is not null && typeof(Stream).IsAssignableFrom(resultType)) ||
            result.Projections.Values.Select(projection => projection.EffectiveSourceRepresentation)
                .Prepend(result.EffectiveSourceRepresentation)
                .Any(representation => representation == ValueRepresentation.TransientResource) ||
            ResultPolicies(result).Any(policy => !policy.IsPersistable))
            return "transient result values are not supported";
        return null;
    }

    private static string? HasNoHiddenResult(BackgroundEligibilitySubject subject) =>
        ResultPolicies(subject.Contract!.Result).Any(policy => policy.HidesValue())
            ? "sensitive or encryption-required result values are not supported"
            : null;

    private static string? UsesVariablesOnlyThroughInputsAndResults(BackgroundEligibilitySubject subject) =>
        AccessesVariablesByName(subject.ActivityType!)
            ? "the activity reads variables by name; variables reach a background step only through its inputs and output captures"
            : null;

    private static string? HasSupportedWorkerBoundInputs(BackgroundEligibilitySubject subject)
    {
        foreach (var input in subject.Contract!.Inputs.Values.OrderBy(input => input.Key, StringComparer.Ordinal))
        {
            if (input.IsStructureInput)
                continue;
            var binding = subject.InputBindings.GetValueOrDefault(input.Key);
            // A secret reference travels as a withheld reference and resolves at worker activation; nothing travels for
            // an input that is neither bound nor defaulted.
            if (binding?.Source == RuntimeInputBindingSource.SecretRead || (binding is null && !input.HasDefault))
                continue;
            if (!input.Policy.IsPersistable)
                return $"input '{input.Key}' has no durable representation to carry it to a worker";
            if (ValueRepresentationDefaults.Infer(input.Type) == ValueRepresentation.TransientResource)
                return $"input '{input.Key}' is a live resource that cannot be materialized on a worker";
            if (input.Policy.HidesValue() || binding?.EffectivePolicy.HidesValue() == true)
                return $"input '{input.Key}' is sensitive or encryption-required and would lose its protection on the way to a worker; bind it to a secret reference instead";
        }

        return null;
    }

    private static bool DeclaresBackgroundExecutionSafe(BackgroundEligibilitySubject subject) =>
        subject.Contract?.SupportsBackgroundExecution == true &&
        subject.ActivityType?.GetCustomAttribute<BackgroundExecutionSafeAttribute>() is not null;

    private static IEnumerable<ActivityValuePolicy> ResultPolicies(ActivityResultContract result) =>
        result.Projections.Values.Select(projection => projection.Policy).Prepend(result.Policy);

    private static Type? FindResultType(Type activityType)
    {
        var results = activityType.GetInterfaces()
            .Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IActivityResult<>))
            .Select(candidate => candidate.GetGenericArguments()[0])
            .Distinct()
            .ToArray();
        return results.Length == 1 ? results[0] : null;
    }
}
