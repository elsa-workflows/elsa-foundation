namespace Elsa.Activities.Runtime.Core.Attributes;

/// <summary>
/// Declares a stable runtime resume target ID for an activity handler.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ResumeTargetAttribute : Attribute
{
    public ResumeTargetAttribute(string resumeTargetId)
    {
        if (string.IsNullOrWhiteSpace(resumeTargetId))
            throw new ArgumentException("A resume target ID is required.", nameof(resumeTargetId));

        ResumeTargetId = resumeTargetId;
    }

    public string ResumeTargetId { get; }
}
