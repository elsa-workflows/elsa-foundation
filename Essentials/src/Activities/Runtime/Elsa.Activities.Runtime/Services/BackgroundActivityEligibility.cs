using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Background;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// The background runtime's keys, and its thin calls into <see cref="BackgroundEligibility"/>, the one component that
/// decides eligibility (ADR 0093 decision 3).
/// </summary>
internal static class BackgroundActivityEligibility
{
    public const string WorkIdMetadataKey = RuntimeMetadataKeys.BackgroundActivityWorkId;
    public const string ResultReason = "BackgroundActivityResult";

    /// <summary>The run-time re-check of a pinned node, with its CLR type resolved from the pinned activation capability.</summary>
    public static void EnsureEligible(ExecutableNode node, IWellKnownTypeRegistry typeRegistry) =>
        Throw(BackgroundEligibility.CheckRunTime(BackgroundEligibilitySubject.ForPinnedNode(node, typeRegistry)));

    /// <summary>The run-time re-check against the activated object, and C6 once the body ran.</summary>
    public static void EnsureEligible(ExecutableNode node, IActivity activity, bool? stagedCoordinatorEffects = null) =>
        Throw(BackgroundEligibility.CheckRunTime(BackgroundEligibilitySubject.ForPinnedNode(node, activity.GetType()) with
        {
            StagedCoordinatorEffects = stagedCoordinatorEffects
        }));

    private static void Throw(BackgroundEligibilityRefusal? refusal)
    {
        if (refusal is not null)
            throw new BackgroundActivityValidationException(refusal.Message) { Criterion = refusal.Criterion };
    }
}
