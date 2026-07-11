namespace Elsa.Server.Readiness;

public enum ShellReadinessStatus
{
    NotStarted,
    Starting,
    Ready,
    Failed,
    Disabled
}
