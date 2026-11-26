using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IRuntimePayloadCapturePolicy
{
    RuntimePayloadCaptureDecision Decide(RuntimePayloadCaptureRequest request);
}
