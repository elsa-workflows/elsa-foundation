namespace Elsa.Workflows.Runtime.Core.Services;

internal static class RuntimeFailureMessages
{
    public static string For(Exception exception) =>
        string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;
}
