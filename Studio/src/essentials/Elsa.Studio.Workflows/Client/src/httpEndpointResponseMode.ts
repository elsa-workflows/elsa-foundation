import type { StudioActivityInputDescriptor } from "@elsa-workflows/studio-sdk";
import { getInputPropertyName, readWrappedInput, withLiteralValue, writeInputValue, type WrappedActivityInputValue } from "./activityProperties";
import type { ActivityCatalogItem, ActivityNode } from "./workflowTypes";

export type HttpEndpointResponseMode = "Sync" | "Async";

const defaultResponseMode: HttpEndpointResponseMode = "Sync";

/**
 * Read the authored response mode from the endpoint's catalog contract. A missing input uses the
 * current contract default (Sync), while expressions and unrecognized values remain unknown so the
 * canvas and warning never guess their behavior.
 */
export function getHttpEndpointResponseMode(
  activity: ActivityNode,
  catalogItem: ActivityCatalogItem | undefined
): HttpEndpointResponseMode | null {
  if (!isHttpEndpoint(activity, catalogItem)) return null;

  const input = findResponseModeInput(catalogItem);
  const propertyName = input ? getInputPropertyName(input) : "responseMode";
  const authored = readAuthoredInput(activity, propertyName, input);
  if (authored === undefined || authored === null) {
    return normalizeResponseMode(input?.defaultValue) ?? defaultResponseMode;
  }

  // The workflow wire adapter expands every canonical ArgumentState into the wrapped authoring shape,
  // including inputs whose catalog describes a plain CLR enum (isWrapped: false). Read that actual
  // authored Literal before interpreting the descriptor's editor shape.
  if (isWrappedInputValue(authored)) {
    if (authored.expression.type !== "Literal") return null;
    return normalizeResponseMode(authored.expression.value);
  }

  if (input?.isWrapped === false) return normalizeResponseMode(authored);
  if (typeof authored === "string") return normalizeResponseMode(authored);
  return null;
}

/** The input-specific update used by the correction action; preserves wrapped argument metadata. */
export function withHttpEndpointResponseMode(
  activity: ActivityNode,
  catalogItem: ActivityCatalogItem | undefined,
  mode: HttpEndpointResponseMode
): ActivityNode {
  if (!isHttpEndpoint(activity, catalogItem)) return activity;
  const input = findResponseModeInput(catalogItem);
  if (!input) return activity;

  const authored = readAuthoredInput(activity, getInputPropertyName(input), input);
  if (isWrappedInputValue(authored)) {
    return writeInputValue(activity, input, withLiteralValue(authored, mode));
  }

  if (input.isWrapped === false) {
    return writeInputValue(activity, input, mode);
  }

  return writeInputValue(activity, input, withLiteralValue(readWrappedInput(activity, input), mode));
}

/** Label shown on the endpoint canvas node. Unknown expression modes deliberately have no badge. */
export function httpEndpointResponseBadge(mode: HttpEndpointResponseMode | null): string | undefined {
  if (mode === "Sync") return "Waits for response";
  if (mode === "Async") return "Runs in background";
  return undefined;
}

function findResponseModeInput(catalogItem: ActivityCatalogItem | undefined): StudioActivityInputDescriptor | undefined {
  return catalogItem?.inputs.find(isResponseModeInput) as StudioActivityInputDescriptor | undefined;
}

function isResponseModeInput(input: unknown): input is StudioActivityInputDescriptor {
  if (!isRecord(input)) return false;
  return [input.name, input.referenceKey].some(value => typeof value === "string" && value.toLowerCase() === "responsemode");
}

function readAuthoredInput(
  activity: ActivityNode,
  propertyName: string,
  descriptor: StudioActivityInputDescriptor | undefined
): unknown {
  if (Object.prototype.hasOwnProperty.call(activity, propertyName)) return activity[propertyName];

  const referenceKey = descriptor?.referenceKey?.trim() || propertyName;
  if (!Array.isArray(activity.inputs)) return undefined;
  const wireInput = activity.inputs.find(value => isRecord(value) && value.referenceKey === referenceKey);
  if (!isRecord(wireInput) || !isRecord(wireInput.value)) return undefined;
  const wireValue = wireInput.value;
  if (typeof wireValue.expressionType !== "string") return undefined;
  const argumentExtras = Object.fromEntries(
    Object.entries(wireInput).filter(([key]) => key !== "referenceKey" && key !== "value" && key !== "conversion")
  );
  return {
    typeName: descriptor?.typeName ?? "",
    expression: { type: wireValue.expressionType, value: wireValue.value },
    ...(wireInput.conversion != null ? { conversion: wireInput.conversion } : {}),
    ...(Object.keys(argumentExtras).length > 0 ? { argumentExtras } : {})
  };
}

function isWrappedInputValue(value: unknown): value is WrappedActivityInputValue {
  return isRecord(value)
    && isRecord(value.expression)
    && typeof value.expression.type === "string";
}

function normalizeResponseMode(value: unknown): HttpEndpointResponseMode | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim().toLowerCase();
  if (normalized === "sync") return "Sync";
  if (normalized === "async") return "Async";
  return null;
}

function isHttpEndpoint(activity: ActivityNode, catalogItem: ActivityCatalogItem | undefined) {
  return catalogItem?.activityTypeKey.endsWith(".HttpEndpoint") ?? false;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
