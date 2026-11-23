import { useState } from "react";
import { qualifyStudioBackendPath, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { getWorkflowDefinitionVersion } from "../api/workflowDesign";
import { listExecutables, runExecutable } from "../api/runtime";
import type {
  WorkflowExecutableReference,
  WorkflowExecutableRunResponse,
  WorkflowExecutableSummary,
  WorkflowExecutionInputs,
  WorkflowInput
} from "../workflowTypes";
import { readWorkflowInputs } from "../workflowReferenceAuthoring";
import { formatExecutableRunError, getExecutableReferenceStatus, readExecutableRunWorkflowExecutionId } from "./executableRunHelpers";
import type { ExecutableRunState } from "./editorTypes";
import { getNotifications } from "./notifications";

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

export async function resolvePublishedDefinitionRunTarget(
  context: StudioEndpointContext,
  definitionId: string
): Promise<ExecutableWorkflowRunTarget | null> {
  const executables = await listExecutables(context, { scope: "published", includeRetired: false });
  const candidates = executables.flatMap(executable => {
    const references = (executable.references ?? []).filter(reference => reference.definitionId === definitionId);
    const reference = findPublishedRunReference(references);
    const target = reference ? createExecutableWorkflowRunTarget({ ...executable, references }) : null;
    return target && reference ? [{ target, reference }] : [];
  });
  candidates.sort((left, right) => {
    const leftDate = Date.parse(left.reference.publishedAt ?? left.reference.createdAt);
    const rightDate = Date.parse(right.reference.publishedAt ?? right.reference.createdAt);
    const leftTimestamp = Number.isFinite(leftDate) ? leftDate : Number.NEGATIVE_INFINITY;
    const rightTimestamp = Number.isFinite(rightDate) ? rightDate : Number.NEGATIVE_INFINITY;
    return rightTimestamp - leftTimestamp
      || left.target.artifactId.localeCompare(right.target.artifactId)
      || left.target.sourceReferenceId.localeCompare(right.target.sourceReferenceId);
  });
  return candidates[0]?.target ?? null;
}

export function notifyPublishedRunStarted(name: string, result: WorkflowExecutableRunResponse): void {
  const workflowExecutionId = readExecutableRunWorkflowExecutionId(result);
  getNotifications().notify({
    level: "success",
    title: "Run started",
    message: `Started ${name}.`,
    ...(workflowExecutionId ? {
      link: {
        label: "Open run",
        path: qualifyStudioBackendPath(`/workflows/instances/${encodeURIComponent(workflowExecutionId)}`)
      }
    } : {})
  });
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
  opener: HTMLElement | null;
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
    // Loading the input contract disables Run and can move browser focus to body.
    // Retain the invoking control before that asynchronous state change.
    const opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    setRunningArtifactId(target.artifactId);
    try {
      const version = await getWorkflowDefinitionVersion(context, target.definitionVersionId);
      const inputs = readWorkflowInputs(version.state?.inputs);
      setRunningArtifactId(null);
      if (inputs.length > 0) setPending({ target, inputs, opener });
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
