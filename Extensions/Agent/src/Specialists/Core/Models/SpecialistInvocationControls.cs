namespace Elsa.Agent.Specialists.Core.Models;

internal static class SpecialistInvocationControls
{
    internal const string Profile = "elsa.specialist.invocation/2";
    internal const string LegacyProfile = "elsa.specialist.continuation/1";
    internal const string InputKey = "$specialist.options";
    internal const string InputName = "AgentOptions";
    internal const string OutputKey = "$specialist.conversation";
    internal const string OutputName = "Conversation";
    internal const string JsonType = "System.Text.Json.JsonElement";
}
