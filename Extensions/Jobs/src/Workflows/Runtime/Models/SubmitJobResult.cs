using System.Text.Json;
using Elsa.Jobs.Core;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Jobs.Workflows.Runtime.Models;

/// <summary>A durable model submission with a stable JobId. This is not a Jobs-store acceptance receipt.</summary>
/// <remarks>PendingAdmission means the committed native handoff still owns admission. Submission is the bounded opaque handoff retained by the checkpoint; applications use JobId and AdmissionState.</remarks>
public sealed record SubmitJobResult(int FormatVersion, string JobId, string AdmissionState, JsonElement Submission);

internal sealed record JobNativeSubmission(int FormatVersion, JobScope Scope, WorkflowExecutableIdentity Executable,
    string WorkflowExecutionId, string InvocationId, string OperationKey, string InitialAttemptId, string NodeId,
    ActivityInvocationContractIdentity Contract, JobWorkflowHandlerPin HandlerPin, JobPayload Input,
    DateTimeOffset? NotBefore, JobWorkflowOwner Owner);

internal sealed record JobNativeSubmissionAdmission(int FormatVersion, JobNativeSubmission Submission,
    JobRequest Request, string RequestSha256, JobOrigin Origin, string ProposedJobId, string ProducerKey,
    DateTimeOffset CommittedAt);

internal sealed record JobNativeSubmissionIntent(int FormatVersion, JobNativeSubmissionAdmission Admission);
