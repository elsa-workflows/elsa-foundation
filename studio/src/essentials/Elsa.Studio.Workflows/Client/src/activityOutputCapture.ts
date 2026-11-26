import type { StudioActivityOutputDescriptor } from "@elsa-workflows/studio-sdk";
import { readStringField, referenceKeyKeys } from "./workflowProperties";
import { readVariableReference } from "./scopedVariables";
import type { ActivityNode, VariableReference } from "./workflowTypes";

/**
 * In-memory authored model for capturing a regular activity's output into a workflow variable — the
 * output-side mirror of {@link import("./activityProperties").WrappedActivityInputValue}.
 *
 * The editor keeps a capture as a top-level property on the activity node, keyed by the output's stable
 * `referenceKey` verbatim (the same key the backend's output `ArgumentState` carries). The wire layer
 * ({@link import("./activityInputWire")}) folds these into `node.outputs[]` on save and expands them back on
 * load, so an unbound output leaves no property/entry and a capture round-trips losslessly. A capture is a
 * variable target plus an optional authored conversion request; every other `ArgumentState` field survives
 * in `argumentExtras`, exactly as on the input side.
 */
export interface ActivityOutputCapture {
  /** The variable the produced output value is written into (workflow-scope for v1). */
  target: VariableReference;
  /** Authored conversion request (backend AuthoredValueConversionRequest); absent = default (Auto). */
  conversion?: unknown;
  /** Passthrough ArgumentState fields the editor doesn't model; owned by activityInputWire. */
  argumentExtras?: Record<string, unknown>;
}

/**
 * The in-memory property key for an authored output capture, and the referenceKey the wire folds it under:
 * the descriptor's stable `referenceKey` verbatim, falling back to the output `name` verbatim (its stable
 * backend identity, e.g. "Result") for descriptors that don't carry one. Unlike the input side, the output
 * name is NOT camelized — output identities are PascalCase and must ride the wire untransformed.
 * `referenceKey` is optional so descriptors from older catalogs can continue using their output name.
 */
export function getOutputPropertyName(descriptor: StudioActivityOutputDescriptor): string {
  const referenceKey = readStringField(descriptor as unknown as Record<string, unknown>, referenceKeyKeys).trim();
  return referenceKey ? referenceKey : descriptor.name;
}

export function readOutputCapture(activity: ActivityNode, descriptor: StudioActivityOutputDescriptor): ActivityOutputCapture | null {
  const key = getOutputPropertyName(descriptor);
  const capture = readOutputCaptureValue(activity[key]);
  if (capture || key === descriptor.name) return capture;
  // A descriptor can gain a stable referenceKey after the workflow was authored with an older catalog
  // that only exposed the output name. Keep showing that capture until the author changes or clears it.
  return readOutputCaptureValue(activity[descriptor.name]);
}

/** Reads a stored output-capture value into the structured model, or null when nothing is bound. */
export function readOutputCaptureValue(value: unknown): ActivityOutputCapture | null {
  if (!isRecord(value)) return null;
  const target = readVariableReference(value.target);
  if (!target) return null;
  return {
    target,
    ...(value.conversion != null ? { conversion: value.conversion } : {}),
    ...(isRecord(value.argumentExtras) ? { argumentExtras: value.argumentExtras } : {})
  };
}

/** Writes (target set) or, with `null`, clears the output capture on the node for `descriptor`. */
export function writeOutputCapture(
  activity: ActivityNode,
  descriptor: StudioActivityOutputDescriptor,
  capture: ActivityOutputCapture | null
): ActivityNode {
  const key = getOutputPropertyName(descriptor);
  const legacyName = descriptor.name === key ? null : descriptor.name;
  const hasLegacyCapture = legacyName !== null && readOutputCaptureValue(activity[legacyName]) !== null;
  if (capture) {
    const next = { ...activity, [key]: capture };
    if (legacyName !== null && hasLegacyCapture) delete next[legacyName];
    return next;
  }
  if (!(key in activity) && !hasLegacyCapture) return activity;
  const next = { ...activity };
  delete next[key];
  if (legacyName !== null && hasLegacyCapture) delete next[legacyName];
  return next;
}

/** Sets or (with `null`) clears the capture's authored conversion request. Mirrors `withConversion`. */
export function withCaptureConversion(capture: ActivityOutputCapture, conversion: unknown): ActivityOutputCapture {
  if (conversion == null) {
    const { conversion: _cleared, ...rest } = capture;
    void _cleared;
    return rest;
  }
  return { ...capture, conversion };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
