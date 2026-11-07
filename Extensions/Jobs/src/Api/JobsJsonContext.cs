using System.Text.Json.Serialization;
using Elsa.Jobs.Api.Models;
using Elsa.Jobs.Core;

namespace Elsa.Jobs.Api;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DictionaryKeyPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(NoJobRequest))]
[JsonSerializable(typeof(JobPageQuery))]
[JsonSerializable(typeof(JobListRequest))]
[JsonSerializable(typeof(JobHoldQuery))]
[JsonSerializable(typeof(SubmitJobRequest))]
[JsonSerializable(typeof(JobOperationRequest))]
[JsonSerializable(typeof(RetryJobRequest))]
[JsonSerializable(typeof(PauseJobsRequest))]
[JsonSerializable(typeof(ReleaseJobHoldRequest))]
[JsonSerializable(typeof(SetJobCapacityRequest))]
[JsonSerializable(typeof(SaveJobScheduleRequest))]
[JsonSerializable(typeof(EnableJobScheduleRequest))]
[JsonSerializable(typeof(JobApiPage<JobView>))]
[JsonSerializable(typeof(JobApiPage<JobAttemptView>))]
[JsonSerializable(typeof(JobApiPage<JobPhysicalReceiptView>))]
[JsonSerializable(typeof(JobApiPage<JobQueueSnapshot>))]
[JsonSerializable(typeof(JobApiPage<JobScheduleView>))]
[JsonSerializable(typeof(JobApiPage<JobHold>))]
[JsonSerializable(typeof(JobApiPage<JobCapacity>))]
[JsonSerializable(typeof(JobView))]
[JsonSerializable(typeof(JobOperationAuditView))]
[JsonSerializable(typeof(JobApiPage<JobOperationAuditView>))]
[JsonSerializable(typeof(JobInputView))]
[JsonSerializable(typeof(JobResultView))]
[JsonSerializable(typeof(JobScheduleView))]
[JsonSerializable(typeof(JobHoldsView))]
[JsonSerializable(typeof(JobApiAccessView))]
[JsonSerializable(typeof(JobAcceptance))]
[JsonSerializable(typeof(JobTransition))]
[JsonSerializable(typeof(JobsApiProblem))]
internal partial class JobsJsonContext : JsonSerializerContext;

internal sealed record NoJobRequest;
