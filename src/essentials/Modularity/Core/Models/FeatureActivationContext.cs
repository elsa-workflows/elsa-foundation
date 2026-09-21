namespace Elsa.Modularity.Core.Models;

/// <summary>
/// What an <see cref="Contracts.IFeatureActivationGuard"/> is asked about: the shell as it is stored now,
/// and the request that is about to replace it.
/// </summary>
/// <param name="Shell">The stored shell configuration this request was validated against, before any save.</param>
/// <param name="Request">
/// The request exactly as it would be saved: validated, and with every masked secret already restored to
/// its real value. A guard reads it and must keep those values out of anything it returns, throws or logs.
/// </param>
public sealed record FeatureActivationContext(ShellFeatureConfigurationSnapshot Shell, FeatureApplyRequest Request)
{
    /// <summary>
    /// The features this request leaves enabled, ordered by id. An apply request describes the full desired
    /// feature state — <c>ValidateRequest</c> refuses one that omits a currently-enabled feature — so this is
    /// the complete set of features the shell would be running, not only the ones this request turns on.
    /// </summary>
    public IReadOnlyList<FeatureApplyItem> EnabledFeatures { get; } =
    [
        .. Request.Features
            .Where(feature => feature.Enabled)
            .OrderBy(feature => feature.Id, StringComparer.OrdinalIgnoreCase)
    ];
}

/// <summary>One reason a request must not be saved: the feature it is about, and the operator-facing text.</summary>
/// <param name="Feature">The id of the feature the refusal is about, as the request spells it.</param>
/// <param name="Reason">
/// What to tell the operator, including what to do about it. Never a connection string or any other secret
/// the request carries (FR-061).
/// </param>
public sealed record FeatureActivationRefusal(string Feature, string Reason);

/// <summary>One guard's verdict on a request.</summary>
public sealed record FeatureActivationDecision(IReadOnlyList<FeatureActivationRefusal> Refusals)
{
    /// <summary>Nothing this guard objects to.</summary>
    public static readonly FeatureActivationDecision Allowed = new([]);

    public static FeatureActivationDecision Refused(params FeatureActivationRefusal[] refusals) => new(refusals);

    public bool IsAllowed => Refusals.Count == 0;
}
