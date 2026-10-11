import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { capabilityIds, resolveCapabilityLink } from "./capabilities";

export interface ActivityExecutionValuePayload {
  evidenceId: string;
  captureMode: string;
  payload: unknown;
}

const maxConcurrentValuePayloadResolutions = 3;
let activeValuePayloadResolutions = 0;
const queuedValuePayloadResolutions: Array<{
  signal?: AbortSignal;
  run(): Promise<void>;
  reject(error: unknown): void;
  cancel(): void;
}> = [];

/** Resolve one captured value through the separately advertised, permission-checked Runtime relation. */
export async function getActivityExecutionValuePayload(
  context: StudioEndpointContext,
  workflowExecutionId: string,
  activityExecutionId: string,
  evidenceId: string,
  expectedCaptureMode: string,
  signal?: AbortSignal
) {
  throwIfAborted(signal);
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.runtime,
    "activity-execution-value-payload",
    { workflowExecutionId, activityExecutionId, evidenceId });
  throwIfAborted(signal);

  return withValuePayloadResolutionSlot(signal, async () => {
    const response = signal
      ? await context.http.getJson<ActivityExecutionValuePayload>(path, { signal })
      : await context.http.getJson<ActivityExecutionValuePayload>(path);
    throwIfAborted(signal);

    if (!response || typeof response !== "object" || Array.isArray(response)) {
      throw new Error("Runtime returned an invalid value evidence payload response.");
    }
    if (response.evidenceId !== evidenceId) {
      throw new Error("Runtime returned value evidence for a different evidence ID.");
    }
    if (normalizeCaptureMode(response.captureMode) !== normalizeCaptureMode(expectedCaptureMode)) {
      throw new Error("Runtime returned value evidence with a different capture mode.");
    }
    if (!Object.hasOwn(response, "payload")) {
      throw new Error("Runtime value evidence response did not include a payload.");
    }

    return response;
  });
}

function normalizeCaptureMode(mode: string) {
  return mode.replace(/[^a-z]/gi, "").toLowerCase();
}

function withValuePayloadResolutionSlot<T>(signal: AbortSignal | undefined, operation: () => Promise<T>): Promise<T> {
  if (signal?.aborted) return Promise.reject(abortError());

  return new Promise<T>((resolve, reject) => {
    let onAbort: () => void = () => {};
    const queued = {
      signal,
      reject,
      cancel: () => onAbort(),
      run: async () => {
        try {
          throwIfAborted(signal);
          resolve(await operation());
        } catch (error) {
          reject(error);
        } finally {
          signal?.removeEventListener("abort", onAbort);
          activeValuePayloadResolutions--;
          pumpValuePayloadResolutions();
        }
      }
    };
    onAbort = () => {
      signal?.removeEventListener("abort", onAbort);
      const index = queuedValuePayloadResolutions.indexOf(queued);
      if (index < 0) return;
      queuedValuePayloadResolutions.splice(index, 1);
      reject(abortError());
      pumpValuePayloadResolutions();
    };

    signal?.addEventListener("abort", onAbort, { once: true });
    queuedValuePayloadResolutions.push(queued);
    pumpValuePayloadResolutions();
  });
}

function pumpValuePayloadResolutions() {
  while (activeValuePayloadResolutions < maxConcurrentValuePayloadResolutions && queuedValuePayloadResolutions.length > 0) {
    const queued = queuedValuePayloadResolutions.shift()!;
    if (queued.signal?.aborted) {
      queued.cancel();
      queued.reject(abortError());
      continue;
    }
    activeValuePayloadResolutions++;
    void queued.run();
  }
}

function throwIfAborted(signal: AbortSignal | undefined) {
  if (signal?.aborted) throw abortError();
}

function abortError() {
  const error = new Error("Runtime value resolution was cancelled.");
  error.name = "AbortError";
  return error;
}
