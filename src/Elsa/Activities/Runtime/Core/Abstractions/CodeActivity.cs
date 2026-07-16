namespace Elsa.Activities.Runtime.Core.Abstractions;

/// <summary>
/// Base class for custom activities with auto-complete behavior.
/// </summary>
public abstract class CodeActivity : ActivityBase
{
    protected CodeActivity()
    {
    }

    protected CodeActivity(string activityType, string version = "1.0.0")
        : base(activityType, version)
    {
    }
}
