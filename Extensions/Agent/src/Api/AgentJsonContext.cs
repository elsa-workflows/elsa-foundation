using Elsa.Agent.Api.Models;
using Elsa.Agent.Core.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Agent.Api;

internal sealed class AgentCamelCaseEnumConverter : JsonStringEnumConverter
{
    public AgentCamelCaseEnumConverter() : base(JsonNamingPolicy.CamelCase) { }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, Converters = [typeof(AgentCamelCaseEnumConverter)], GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AgentApiResponse<AgentBootstrapResponse>))]
[JsonSerializable(typeof(AgentPrivilegePolicyView))]
[JsonSerializable(typeof(AgentApiResponse<AgentCreateSessionResponse>))]
[JsonSerializable(typeof(AgentApiResponse<AgentSessionDetailsResponse>))]
[JsonSerializable(typeof(AgentApiResponse<AgentSessionPolicyResponse>))]
[JsonSerializable(typeof(AgentApiResponse<AgentMessageAcceptedResponse>))]
[JsonSerializable(typeof(AgentApiResponse<AgentTurnCancelResponse>))]
[JsonSerializable(typeof(AgentApiResponse<AgentFeedback>))]
[JsonSerializable(typeof(AgentApiResponse<AgentActionProposal>))]
[JsonSerializable(typeof(AgentApiResponse<AgentProposalExecutionResult>))]
[JsonSerializable(typeof(AgentApiResponse<IReadOnlyCollection<AgentAuditEvent>>))]
[JsonSerializable(typeof(AgentApiResponse<AgentSessionListResponse>))]
[JsonSerializable(typeof(AgentApiResponse<IReadOnlyCollection<AgentActionProposal>>))]
[JsonSerializable(typeof(AgentApiResponse<AgentMessageViewModelResponse>))]
[JsonSerializable(typeof(AgentBindingErrorResponse))]
[JsonSerializable(typeof(AgentCreateSessionRequest))]
[JsonSerializable(typeof(AgentMessageRequest))]
[JsonSerializable(typeof(AgentSessionAutonomyRequest))]
[JsonSerializable(typeof(AgentTurnCancelRequest))]
[JsonSerializable(typeof(AgentFeedbackApiRequest))]
[JsonSerializable(typeof(AgentProposalDecisionRequest))]
[JsonSerializable(typeof(AgentClientAcknowledgement))]
[JsonSerializable(typeof(AgentAuditQueryRequest))]
[JsonSerializable(typeof(AgentSessionListQueryRequest))]
[JsonSerializable(typeof(AgentClientInvocationResolutionRequest))]
public partial class AgentJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, Converters = [typeof(AgentCamelCaseEnumConverter)], GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AgentStreamEvent))]
[JsonSerializable(typeof(AgentContextContribution))]
[JsonSerializable(typeof(AgentActionProposal))]
[JsonSerializable(typeof(AgentToolResult))]
[JsonSerializable(typeof(AgentPayload))]
[JsonSerializable(typeof(AgentEffectReceipt))]
[JsonSerializable(typeof(AgentPreparedToolInvocation))]
[JsonSerializable(typeof(AgentTurnStarted))]
[JsonSerializable(typeof(AgentStepDescriptor))]
[JsonSerializable(typeof(AgentToolCallRequest))]
[JsonSerializable(typeof(AgentToolCallOutcome))]
[JsonSerializable(typeof(AgentPlanUpdate))]
[JsonSerializable(typeof(AgentProviderNotice))]
public partial class AgentSseJsonContext : JsonSerializerContext;
