import { useState } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { getWorkflowDefinitionVersion } from "../api/workflowDesign";
import { runExecutable } from "../api/runtime";
import type { WorkflowExecutableRunResponse, WorkflowExecutionInputs, WorkflowInput } from "../workflowTypes";
import { readWorkflowInputs } from "../workflowReferenceAuthoring";

export interface ExecutableWorkflowRunTarget {
  artifactId: string;
  definitionVersionId?: string | null;
}

interface PendingExecutableRun {
  target: ExecutableWorkflowRunTarget;
  inputs: WorkflowInput[];
}

export function useExecutableWorkflowRun({ context, onDispatchStart, onStarted, onError }: {
  context: StudioEndpointContext;
  onDispatchStart(): void;
  onStarted(target: ExecutableWorkflowRunTarget, result: WorkflowExecutableRunResponse): void;
  onError(error: unknown): void;
}) {
  const [pending, setPending] = useState<PendingExecutableRun | null>(null);
  const [runningArtifactId, setRunningArtifactId] = useState<string | null>(null);

  const dispatch = async (target: ExecutableWorkflowRunTarget, inputs: WorkflowExecutionInputs) => {
    setRunningArtifactId(target.artifactId);
    onDispatchStart();
    try {
      onStarted(target, await runExecutable(context, target.artifactId, inputs));
    } catch (error) {
      onError(error);
    } finally {
      setRunningArtifactId(null);
    }
  };

  const request = async (target: ExecutableWorkflowRunTarget) => {
    if (runningArtifactId) return;
    if (!target.definitionVersionId) {
      await dispatch(target, {});
      return;
    }

    setRunningArtifactId(target.artifactId);
    try {
      const version = await getWorkflowDefinitionVersion(context, target.definitionVersionId);
      const inputs = readWorkflowInputs(version.state?.inputs);
      setRunningArtifactId(null);
      if (inputs.length > 0) setPending({ target, inputs });
      else await dispatch(target, {});
    } catch (error) {
      setRunningArtifactId(null);
      onError(error);
    }
  };

  const confirm = async (inputs: WorkflowExecutionInputs) => {
    const run = pending;
    if (!run) return;
    setPending(null);
    await dispatch(run.target, inputs);
  };

  return {
    pending,
    runningArtifactId,
    request,
    confirm,
    cancel: () => setPending(null)
  };
}
