using Elsa.Weaver.Api.Models;
using Elsa.Weaver.Core.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Weaver.Api;

internal sealed class WeaverCamelCaseEnumConverter : JsonStringEnumConverter
{
    public WeaverCamelCaseEnumConverter() : base(JsonNamingPolicy.CamelCase) { }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, Converters = [typeof(WeaverCamelCaseEnumConverter)], GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(WeaverApiResponse<WeaverBootstrapResponse>))]
[JsonSerializable(typeof(WeaverPrivilegePolicyView))]
[JsonSerializable(typeof(WeaverApiResponse<WeaverSupportResponse>))]
[JsonSerializable(typeof(WeaverApiResponse<WeaverCreateSessionResponse>))]
[JsonSerializable(typeof(WeaverApiResponse<WeaverSessionDetailsResponse>))]
[JsonSerializable(typeof(WeaverApiResponse<WeaverSessionPolicyResponse>))]
[JsonSerializable(typeof(WeaverApiResponse<WeaverMessageAcceptedResponse>))]
[JsonSerializable(typeof(WeaverApiResponse<WeaverTurnCancelResponse>))]
[JsonSerializable(typeof(WeaverApiResponse<WeaverFeedback>))]
[JsonSerializable(typeof(WeaverApiResponse<WeaverActionProposal>))]
[JsonSerializable(typeof(WeaverApiResponse<WeaverProposalExecutionResult>))]
[JsonSerializable(typeof(WeaverApiResponse<IReadOnlyCollection<WeaverAuditEvent>>))]
[JsonSerializable(typeof(WeaverApiResponse<WeaverSessionListResponse>))]
[JsonSerializable(typeof(WeaverApiResponse<IReadOnlyCollection<WeaverActionProposal>>))]
[JsonSerializable(typeof(WeaverApiResponse<WeaverMessageViewModelResponse>))]
[JsonSerializable(typeof(WeaverBindingErrorResponse))]
[JsonSerializable(typeof(WeaverCreateSessionRequest))]
[JsonSerializable(typeof(WeaverMessageRequest))]
[JsonSerializable(typeof(WeaverSessionAutonomyRequest))]
[JsonSerializable(typeof(WeaverTurnCancelRequest))]
[JsonSerializable(typeof(WeaverFeedbackApiRequest))]
[JsonSerializable(typeof(WeaverProposalDecisionRequest))]
[JsonSerializable(typeof(WeaverClientAcknowledgement))]
[JsonSerializable(typeof(WeaverAuditQueryRequest))]
[JsonSerializable(typeof(WeaverSessionListQueryRequest))]
[JsonSerializable(typeof(WeaverClientInvocationResolutionRequest))]
public partial class WeaverJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, Converters = [typeof(WeaverCamelCaseEnumConverter)], GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(WeaverStreamEvent))]
[JsonSerializable(typeof(WeaverContextContribution))]
[JsonSerializable(typeof(WeaverActionProposal))]
[JsonSerializable(typeof(WeaverToolResult))]
[JsonSerializable(typeof(WeaverPayload))]
[JsonSerializable(typeof(WeaverEffectReceipt))]
[JsonSerializable(typeof(WeaverPreparedToolInvocation))]
[JsonSerializable(typeof(WeaverTurnStarted))]
[JsonSerializable(typeof(WeaverStepDescriptor))]
[JsonSerializable(typeof(WeaverToolCallRequest))]
[JsonSerializable(typeof(WeaverToolCallOutcome))]
[JsonSerializable(typeof(WeaverPlanUpdate))]
[JsonSerializable(typeof(WeaverProviderNotice))]
public partial class WeaverSseJsonContext : JsonSerializerContext;
