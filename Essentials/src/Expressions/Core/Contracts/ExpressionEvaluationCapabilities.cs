using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.Core.Contracts;

/// <summary>
/// Immutable capability grant resolved from a serialized expression capability profile.
/// Evaluators inspect this contract instead of discovering host capabilities from an execution context.
/// </summary>
public sealed record ExpressionEvaluationCapabilities
{
    // binding-readonly-v1 (ADR 0083, spec 1223 FR-001, FR-031): transforms plus the clock, randomness and GUIDs; no
    // ambient workflow state beyond the declared reads, no mutation, no service location.
    private static readonly ExpressionEvaluationCapabilities BindingReadonly = new(
        ExpressionCapabilityProfiles.BindingReadonlyV1, true, false, false, false, true);

    private ExpressionEvaluationCapabilities(
        string profile,
        bool allowsDeterministicTransforms,
        bool allowsAmbientWorkflowState,
        bool allowsMutation,
        bool allowsServiceLocation,
        bool allowsNondeterminism)
    {
        Profile = profile;
        AllowsDeterministicTransforms = allowsDeterministicTransforms;
        AllowsAmbientWorkflowState = allowsAmbientWorkflowState;
        AllowsMutation = allowsMutation;
        AllowsServiceLocation = allowsServiceLocation;
        AllowsNondeterminism = allowsNondeterminism;
    }

    public string Profile { get; }
    public bool AllowsDeterministicTransforms { get; }
    public bool AllowsAmbientWorkflowState { get; }
    public bool AllowsMutation { get; }
    public bool AllowsServiceLocation { get; }

    /// <summary>Whether expressions may read the clock and produce random values and GUIDs (ADR 0045 §5 D-1).</summary>
    public bool AllowsNondeterminism { get; }

    /// <summary>The <c>binding-readonly-v1</c> grant: transforms, time and randomness; no mutation or service location.</summary>
    public bool IsBindingReadonly =>
        AllowsDeterministicTransforms &&
        !AllowsAmbientWorkflowState &&
        !AllowsMutation &&
        !AllowsServiceLocation &&
        AllowsNondeterminism;

    public static ExpressionEvaluationCapabilities Resolve(string profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);
        return StringComparer.Ordinal.Equals(profile, ExpressionCapabilityProfiles.BindingReadonlyV1)
            ? BindingReadonly
            : new ExpressionEvaluationCapabilities(profile, false, false, false, false, false);
    }
}
