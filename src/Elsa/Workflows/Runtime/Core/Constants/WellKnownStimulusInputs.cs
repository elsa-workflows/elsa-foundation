namespace Elsa.Workflows.Runtime.Core.Constants;

/// <summary>
/// Well-known workflow-input keys used by the stimulus routing start path (spec 089, FR-001).
/// </summary>
public static class WellKnownStimulusInputs
{
    /// <summary>
    /// The workflow-input key under which <c>IStimulusRouter</c> forwards <c>StimulusDispatchRequest.Input</c>
    /// when starting matching triggers, so a started instance observes the live stimulus payload through the
    /// ordinary seed-input channel (<c>input.*</c> projection). The start-path counterpart of the resume path's
    /// <c>BookmarkResumeDispatchRequest.Input</c>, which continues to deliver the payload directly to the resumed
    /// activity.
    /// </summary>
    public const string StimulusInput = "stimulusInput";
}
