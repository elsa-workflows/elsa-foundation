import { useState } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { getWorkflowDefinitionVersion } from "../api/workflowDesign";
import { runExecutable } from "../api/runtime";
import type {
  WorkflowExecutableReference,
  WorkflowExecutableRunResponse,
  WorkflowExecutableSummary,
  WorkflowExecutionInputs,
  WorkflowInput
} from "../workflowTypes";
import { readWorkflowInputs } from "../workflowReferenceAuthoring";
import { formatExecutableRunError, getExecutableReferenceStatus, readExecutableRunWorkflowExecutionId } from "./editorHelpers";
import type { ExecutableRunState } from "./editorTypes";

export interface ExecutableWorkflowRunTarget {
  artifactId: string;
  definitionVersionId: string;
  sourceReferenceId: string;
}

export function createExecutableWorkflowRunTarget(executable: WorkflowExecutableSummary): ExecutableWorkflowRunTarget | null {
  const reference = findPublishedRunReference(executable.references);
  if (!reference) return null;
  return {
    artifactId: executable.artifactId,
    definitionVersionId: reference.definitionVersionId,
    sourceReferenceId: reference.sourceReferenceId
  };
}

export function findPublishedRunReference(
  references: WorkflowExecutableReference[] | undefined,
  now: Date = new Date()
): WorkflowExecutableReference | null {
  return (references ?? [])
    .filter(reference => reference.scope.trim().toLowerCase() === "published" && getExecutableReferenceStatus(reference, now) === "live")
    .sort((left, right) => {
      const rightDate = Date.parse(right.publishedAt ?? right.createdAt);
      const leftDate = Date.parse(left.publishedAt ?? left.createdAt);
      const rightTimestamp = Number.isFinite(rightDate) ? rightDate : Number.NEGATIVE_INFINITY;
      const leftTimestamp = Number.isFinite(leftDate) ? leftDate : Number.NEGATIVE_INFINITY;
      return rightTimestamp - leftTimestamp || left.sourceReferenceId.localeCompare(right.sourceReferenceId);
    })[0] ?? null;
}

interface PendingExecutableRun {
  target: ExecutableWorkflowRunTarget;
  inputs: WorkflowInput[];
}

export function createExecutableWorkflowRunFeedback({ setStatus, setLastRun, setError }: {
  setStatus(value: string): void;
  setLastRun(value: ExecutableRunState | null): void;
  setError(value: string): void;
}) {
  return {
    onDispatchStart: () => {
      setStatus("");
      setLastRun(null);
      setError("");
    },
    onStarted: (target: ExecutableWorkflowRunTarget, result: WorkflowExecutableRunResponse) => {
      setLastRun({ artifactId: target.artifactId, workflowExecutionId: readExecutableRunWorkflowExecutionId(result) });
      setStatus(`Started ${target.artifactId}`);
    },
    onError: (error: unknown) => setError(formatExecutableRunError(error))
  };
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
      onStarted(target, await runExecutable(context, target.artifactId, inputs, target.sourceReferenceId));
    } catch (error) {
      onError(error);
    } finally {
      setRunningArtifactId(null);
    }
  };

  const request = async (target: ExecutableWorkflowRunTarget) => {
    if (runningArtifactId) return;
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
