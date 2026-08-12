using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Resume-target resolution shared by the invoke and resume scheduler work handlers, which each carried
/// a byte-identical copy: every registration of a stateful suspension must name a resume target that
/// exists on the executable and belongs to the suspending node.
/// </summary>
internal static class StatefulSuspensionSupport
{
    public static void ValidateRegistrations(
        WorkflowExecutable executable,
        ExecutableNode executableNode,
        IStatefulActivitySuspensionTransition suspension)
    {
        foreach (var registration in suspension.Registrations)
            ResolveResumeTarget(executable, executableNode, registration.ResumeTargetKey);
    }

    public static WorkflowExecutableResumeTarget ResolveResumeTarget(
        WorkflowExecutable executable,
        ExecutableNode executableNode,
        string resumeTargetKey)
    {
        var resumeTarget = SchedulerWorkHandlerHelpers.FindResumeTargetForNode(
            executable,
            executableNode.ExecutableNodeId,
            resumeTargetKey);
        if (resumeTarget is null)
        {
            throw new InvalidOperationException(
                $"Stateful activity '{executableNode.ExecutableNodeId}' registered missing resume target '{resumeTargetKey}'.");
        }

        if (!StringComparer.Ordinal.Equals(resumeTarget.ExecutableNodeId, executableNode.ExecutableNodeId))
        {
            throw new InvalidOperationException(
                $"Resume target '{resumeTargetKey}' belongs to executable node '{resumeTarget.ExecutableNodeId}', not '{executableNode.ExecutableNodeId}'.");
        }

        return resumeTarget;
    }
}
