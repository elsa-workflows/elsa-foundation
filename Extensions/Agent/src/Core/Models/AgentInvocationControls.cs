namespace Elsa.Agent.Core.Models;

internal static class AgentInvocationControls
{
    internal const string Profile = "elsa.agent.invocation/2";
    internal const string LegacyProfile = "elsa.agent.continuation/1";
    internal const string InputKey = "$agent.options";
    internal const string InputName = "AgentOptions";
    internal const string OutputKey = "$agent.conversation";
    internal const string OutputName = "Conversation";
    internal const string JsonType = "System.Text.Json.JsonElement";
}
