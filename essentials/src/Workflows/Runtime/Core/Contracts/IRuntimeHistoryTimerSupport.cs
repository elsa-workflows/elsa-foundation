namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Optional qualification of the installed timer producer, physical owner and recurring pump.</summary>
public interface IRuntimeHistoryTimerSupport
{
    bool CanCapture => false;
}
