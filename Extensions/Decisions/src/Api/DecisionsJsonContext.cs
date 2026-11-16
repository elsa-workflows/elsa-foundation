using System.Text.Json.Serialization;
using Elsa.Decisions.Core;
using Elsa.Decisions.Design;
using Elsa.Decisions.Persistence.Core;

namespace Elsa.Decisions.Api;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(DecisionAssetsResponse))]
[JsonSerializable(typeof(DecisionVersionsResponse))]
[JsonSerializable(typeof(DecisionCasesResponse))]
[JsonSerializable(typeof(DecisionRunsResponse))]
[JsonSerializable(typeof(DecisionAssetView))]
[JsonSerializable(typeof(DecisionVersionView))]
[JsonSerializable(typeof(DecisionCaseView))]
[JsonSerializable(typeof(DecisionValidation))]
[JsonSerializable(typeof(DecisionTestReceipt))]
[JsonSerializable(typeof(DecisionEvaluation))]
[JsonSerializable(typeof(DecisionProblem))]
[JsonSerializable(typeof(SaveDecisionRequest))]
[JsonSerializable(typeof(PublishDecisionRequest))]
[JsonSerializable(typeof(CloneDecisionRequest))]
[JsonSerializable(typeof(ValidateDecisionRequest))]
[JsonSerializable(typeof(EvaluateDecisionRequest))]
[JsonSerializable(typeof(SaveDecisionCaseRequest))]
[JsonSerializable(typeof(TestDecisionRequest))]
internal partial class DecisionsJsonContext : JsonSerializerContext;
