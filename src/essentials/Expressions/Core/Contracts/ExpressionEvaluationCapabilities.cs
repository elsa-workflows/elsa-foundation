using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.Core.Contracts;

/// <summary>
/// Immutable capability grant resolved from a serialized expression capability profile.
/// Evaluators inspect this contract instead of discovering host capabilities from an execution context.
/// </summary>
public sealed record ExpressionEvaluationCapabilities
{
    private static readonly ExpressionEvaluationCapabilities BindingPure = new(
        ExpressionCapabilityProfiles.BindingPureV1, true, false, false, false, false);

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
    public bool AllowsNondeterminism { get; }

    public bool IsBindingPure =>
        AllowsDeterministicTransforms &&
        !AllowsAmbientWorkflowState &&
        !AllowsMutation &&
        !AllowsServiceLocation &&
        !AllowsNondeterminism;

    public static ExpressionEvaluationCapabilities Resolve(string profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);
        return StringComparer.Ordinal.Equals(profile, ExpressionCapabilityProfiles.BindingPureV1)
            ? BindingPure
            : new ExpressionEvaluationCapabilities(profile, false, false, false, false, false);
    }
}
