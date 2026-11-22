import { useEffect, useRef } from "react";
import type { StudioEndpointContext, StudioWorkflowRunInputEditorContribution } from "@elsa-workflows/studio-sdk";
import type { WorkflowDefinitionSummary } from "../workflowTypes";
import { formatExecutableRunError } from "./editorHelpers";
import { getNotifications, useNotificationGuard } from "./notifications";
import { WorkflowRunInputDialog } from "./WorkflowRunInputDialog";
import { notifyPublishedRunStarted, resolvePublishedDefinitionRunTarget, useExecutableWorkflowRun } from "./useExecutableWorkflowRun";

export function WorkflowDefinitionRunAction({ context, definition, requestId, runInputEditors }: {
  context: StudioEndpointContext;
  definition: WorkflowDefinitionSummary;
  requestId: number;
  runInputEditors: StudioWorkflowRunInputEditorContribution[];
}) {
  const captureNotificationGuard = useNotificationGuard(context);
  const pendingRunFeedback = useRef<{ canNotify: (() => boolean) | null; name: string }>({ canNotify: null, name: "" });
  const runAction = useExecutableWorkflowRun({
    context,
    onDispatchStart: () => undefined,
    onStarted: (_target, result) => {
      const feedback = pendingRunFeedback.current;
      if (feedback.canNotify?.()) notifyPublishedRunStarted(feedback.name, result);
    },
    onError: error => {
      const feedback = pendingRunFeedback.current;
      if (feedback.canNotify?.()) getNotifications().notify({
        level: "error",
        title: `Couldn't start ${feedback.name}`,
        message: formatExecutableRunError(error)
      });
    }
  });
  const request = useRef({ context, definition, requestId, captureNotificationGuard, run: runAction.request });
  request.current = { context, definition, requestId, captureNotificationGuard, run: runAction.request };
  const startedRequestId = useRef<number | null>(null);

  useEffect(() => {
    if (startedRequestId.current === requestId) return;
    startedRequestId.current = requestId;
    const current = request.current;
    const canNotify = current.captureNotificationGuard();
    pendingRunFeedback.current = { canNotify, name: current.definition.name };
    void (async () => {
      try {
        const target = await resolvePublishedDefinitionRunTarget(current.context, current.definition.id);
        if (!canNotify()) return;
        if (!target) {
          getNotifications().notify({
            level: "error",
            title: `Couldn't start ${current.definition.name}`,
            message: "No live published version is available to run."
          });
          return;
        }
        await current.run(target);
      } catch (error) {
        if (canNotify()) getNotifications().notify({
          level: "error",
          title: `Couldn't start ${current.definition.name}`,
          message: formatExecutableRunError(error)
        });
      }
    })();
  }, [requestId]);

  return runAction.pending ? <WorkflowRunInputDialog
    inputs={runAction.pending.inputs}
    editors={runInputEditors}
    busy={!!runAction.runningArtifactId}
    onSubmit={inputs => { void runAction.confirm(inputs); }}
    onCancel={runAction.cancel}
  /> : null;
}