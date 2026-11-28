using System.Reflection;

namespace Kernel.ReviewFixtures;

// The design experiments stand in for the issuer while exercising the real store.
// Apply this only to the final ticket authored by the fixture's modeled kernel.
internal static class ModeledTicket
{
    private static readonly MethodInfo IssueMethod = typeof(Message).GetMethod("AsKernelTicket", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new MissingMethodException(nameof(Message), "AsKernelTicket");

    internal static Message Issue(Message message) => (Message)IssueMethod.Invoke(message, null)!;
}
