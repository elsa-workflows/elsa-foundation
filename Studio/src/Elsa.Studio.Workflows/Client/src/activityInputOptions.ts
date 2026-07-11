import { useEffect, useMemo, useRef, useState } from "react";
import {
  readActivityInputOptionsProvider,
  type StudioActivityDescriptor,
  type StudioActivityInputDescriptor,
  type StudioActivityInputOption,
  type StudioEndpointContext
} from "@elsa-workflows/studio-sdk";
import { readInputValue, readWrappedInput } from "./activityProperties";
import { getActivityInputOptions } from "./api/workflows";
import type { ActivityNode, WorkflowDefinitionState } from "./workflowTypes";

export type ActivityInputOptionsStatus = "idle" | "loading" | "ready" | "error";

export interface ActivityInputOptionsState {
  status: ActivityInputOptionsStatus;
  options: StudioActivityInputOption[];
  retry(): void;
}

export const readOptionsProvider = readActivityInputOptionsProvider;

export function useActivityInputOptions(
  context: StudioEndpointContext,
  workflowState: WorkflowDefinitionState,
  activity: ActivityNode,
  descriptor: StudioActivityDescriptor,
  input: StudioActivityInputDescriptor
): ActivityInputOptionsState {
  const provider = readOptionsProvider(input);
  const dependencySignature = useMemo(() => provider
    ? JSON.stringify(provider.dependsOn.map(name => {
      const dependency = descriptor.inputs.find(candidate => candidate.name === name);
      if (!dependency) return null;
      if (dependency.isWrapped === false) return readInputValue(activity, dependency);
      const wrapped = readWrappedInput(activity, dependency);
      return { syntax: wrapped.expression.type, value: wrapped.expression.value };
    }))
    : "", [activity, descriptor.inputs, provider?.key, provider?.dependsOn.join("\u0000")]);
  const [retryVersion, setRetryVersion] = useState(0);
  const [result, setResult] = useState<Omit<ActivityInputOptionsState, "retry">>({ status: provider ? "loading" : "idle", options: [] });
  const previousDependencySignature = useRef<string | null>(null);
  const previousRetryVersion = useRef(retryVersion);

  useEffect(() => {
    if (!provider) {
      setResult({ status: "idle", options: [] });
      return;
    }

    const controller = new AbortController();
    let active = true;
    const isRetry = previousRetryVersion.current !== retryVersion;
    const isDependencyRefresh = previousDependencySignature.current !== null && previousDependencySignature.current !== dependencySignature;
    previousRetryVersion.current = retryVersion;
    previousDependencySignature.current = dependencySignature;
    setResult(previous => ({ ...previous, status: "loading" }));

    const timeout = globalThis.setTimeout(() => {
      getActivityInputOptions(context, activity.activityVersionId, input.name, activity.nodeId, workflowState, controller.signal).then(
        options => { if (active && !controller.signal.aborted) setResult({ status: "ready", options }); },
        error => {
          if (!active || controller.signal.aborted || (error instanceof DOMException && error.name === "AbortError")) return;
          setResult({ status: "error", options: [] });
        }
      );
    }, isDependencyRefresh && !isRetry ? 150 : 0);

    return () => {
      active = false;
      globalThis.clearTimeout(timeout);
      controller.abort();
    };
    // workflowState is intentionally omitted: declared dependencies, selection, and retry govern refreshes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activity.activityVersionId, activity.nodeId, context, dependencySignature, input.name, provider?.key, retryVersion]);

  return { ...result, retry: () => setRetryVersion(version => version + 1) };
}
