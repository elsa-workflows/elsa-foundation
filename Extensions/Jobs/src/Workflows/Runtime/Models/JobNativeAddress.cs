using System.Text.Json;
using Elsa.Jobs.Core;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Jobs.Workflows.Runtime.Models;

/// <summary>Trusted owning execution; a model worker retains its actual root origin and model-owned address.</summary>
public sealed record JobWorkflowOwner(int FormatVersion, JobScope Scope, string ModelKind,
    string DefinitionId, string DefinitionVersion, string ExecutionId, string InvocationId,
    string MaterialId, string MaterialSha256, RuntimeModelNativeWorkOrigin? Origin, JsonElement? ModelAddress);

/// <summary>The real native wait captured by the bookmark checkpoint, including its full executable and contract pins.</summary>
public sealed record JobNativeAddress(int FormatVersion, JobScope Scope, WorkflowExecutableIdentity Executable,
    string WorkflowExecutionId, string InvocationId, string OperationKey, string InitialAttemptId,
    string NodeId, ActivityInvocationContractIdentity Contract, string BookmarkId, string RegistrationId,
    string ResumeTargetId, string StimulusType, string StimulusHash, string ProviderId,
    ValueTypeDescriptor PayloadType, ActivityTriggerDeduplicationPolicy DeduplicationPolicy, JobWorkflowOwner Owner);

internal sealed record RunJobInvocation(int FormatVersion, JobScope Scope, WorkflowExecutableIdentity Executable,
    string WorkflowExecutionId, string InvocationId, string OperationKey, string InitialAttemptId,
    string NodeId, ActivityInvocationContractIdentity Contract, string ResumeTargetId,
    JobWorkflowHandlerPin HandlerPin, JobPayload Input, DateTimeOffset? NotBefore, JobWorkflowOwner Owner);

internal sealed record JobNativeAdmission(int FormatVersion, RunJobInvocation Invocation, JobNativeAddress Address,
    JobRequest Request, string RequestSha256, JobOrigin Origin, string ProposedJobId, string ProducerKey, DateTimeOffset CommittedAt);

internal sealed record JobNativeIntent(int FormatVersion, JobNativeAdmission Admission);

internal sealed record JobNativeDeliveryBinding(int FormatVersion, RunJobCompletion Completion,
    string RegistrationId, string DeliveryId, string DeduplicationKey, string AttemptId,
    ActivityInvocationContractIdentity Contract);
