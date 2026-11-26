import { useEffect, useRef } from "react";

export type ExecutionFocusOperation = { token: unknown; isCurrent(): boolean; isReady?(): boolean; signal?: AbortSignal };
export type ExecutionFocusIntent = {
  targetId: string;
  direction?: "previous" | "next";
  scope: string;
  identity: string;
  ownerBound: boolean;
  selectionStarted: boolean;
  isReady(): boolean;
  from?: string;
  origin: HTMLElement | null;
  operationToken: unknown;
  isCurrent(): boolean;
  disposeAbort?: () => void;
};
export type ExecutionFocusRequest = { current: string | null; intent?: ExecutionFocusIntent };

export function clearExecutionFocus(request: ExecutionFocusRequest, expected?: ExecutionFocusIntent) {
  if (expected && request.intent !== expected) return;
  request.intent?.disposeAbort?.();
  request.current = null;
  request.intent = undefined;
}

/** Lives at the owner of the ref, including a deliberate child-navigator remount gap. */
export function useExecutionFocusRequest(): ExecutionFocusRequest {
  const request = useRef<ExecutionFocusRequest>({ current: null }).current;
  useEffect(() => {
    const laterFocus = (event: FocusEvent) => {
      const intent = request.intent;
      if (intent && event.target !== intent.origin && event.target !== document.body) clearExecutionFocus(request, intent);
    };
    document.addEventListener("focusin", laterFocus);
    return () => { clearExecutionFocus(request); document.removeEventListener("focusin", laterFocus); };
  }, [request]);
  return request;
}

export function requestExecutionFocus(request: ExecutionFocusRequest, targetId: string | null | undefined,
  details: Pick<ExecutionFocusIntent, "direction" | "scope" | "identity" | "from" | "origin">, operation?: ExecutionFocusOperation) {
  clearExecutionFocus(request);
  if (!targetId) return;
  request.current = targetId;
  request.intent = { ...details, targetId, ownerBound: false, selectionStarted: false, isReady: () => false, operationToken: operation?.token, isCurrent: operation?.isCurrent ?? (() => true) };
}

/** Call before the existing owner selection callback, then bind its newly started operation. */
export function continueExecutionFocus(request: ExecutionFocusRequest) {
  const intent = request.intent;
  if (!intent || !intent.isCurrent()) {
    if (intent) clearExecutionFocus(request, intent);
    return (_operation?: ExecutionFocusOperation) => {};
  }
  intent.selectionStarted = true;
  return (operation?: ExecutionFocusOperation) => {
    if (request.intent !== intent) return;
    intent.ownerBound = Boolean(operation);
    intent.isReady = operation && operation.token !== intent.operationToken ? operation.isReady ?? (() => true) : () => true;
    if (!operation) return;
    if (!operation.isCurrent()) { clearExecutionFocus(request, intent); return; }
    intent.isCurrent = operation.isCurrent;
    // Do not bind the already finished controller from before this new selection.
    if (operation.token !== intent.operationToken && operation.signal) {
      const signal = operation.signal;
      intent.isCurrent = () => operation.isCurrent() && !signal.aborted;
      if (signal.aborted) { clearExecutionFocus(request, intent); return; }
      const abort = () => clearExecutionFocus(request, intent);
      signal.addEventListener("abort", abort, { once: true });
      intent.disposeAbort = () => signal.removeEventListener("abort", abort);
    }
    intent.operationToken = operation.token;
  };
}
