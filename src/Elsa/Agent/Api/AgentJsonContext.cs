using System.Text.Json.Serialization;
using System.Text.Json;
using Elsa.Agent.Api.Models;
using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Api;

internal sealed class AgentCamelCaseEnumConverter : JsonStringEnumConverter
{
    public AgentCamelCaseEnumConverter() : base(JsonNamingPolicy.CamelCase) { }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, Converters = [typeof(AgentCamelCaseEnumConverter)], GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AgentApiResponse<AgentBootstrapResponse>))]
[JsonSerializable(typeof(AgentApiResponse<AgentCreateSessionResponse>))]
[JsonSerializable(typeof(AgentApiResponse<AgentSessionDetailsResponse>))]
[JsonSerializable(typeof(AgentApiResponse<AgentMessageAcceptedResponse>))]
[JsonSerializable(typeof(AgentApiResponse<AgentTurnCancelResponse>))]
[JsonSerializable(typeof(AgentApiResponse<AgentFeedback>))]
[JsonSerializable(typeof(AgentApiResponse<AgentActionProposal>))]
[JsonSerializable(typeof(AgentApiResponse<AgentProposalExecutionResult>))]
[JsonSerializable(typeof(AgentApiResponse<IReadOnlyCollection<AgentAuditEvent>>))]
[JsonSerializable(typeof(AgentCreateSessionRequest))]
[JsonSerializable(typeof(AgentMessageRequest))]
[JsonSerializable(typeof(AgentTurnCancelRequest))]
[JsonSerializable(typeof(AgentFeedbackApiRequest))]
[JsonSerializable(typeof(AgentProposalDecisionRequest))]
public partial class AgentJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AgentStreamEvent))]
public partial class AgentSseJsonContext : JsonSerializerContext;
