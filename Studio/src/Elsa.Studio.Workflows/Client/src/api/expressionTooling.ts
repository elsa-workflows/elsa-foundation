import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { capabilityIds, resolveCapabilityLink } from "./capabilities";

export const expressionToolingRelations = {
  descriptors: "expression-tooling-descriptors",
  context: "expression-tooling-context",
  validation: "expression-tooling-validate",
  symbols: "expression-tooling-symbols",
  completions: "expression-tooling-completions",
  hover: "expression-tooling-hover"
} as const;

export async function getExpressionToolingDescriptors(
  context: StudioEndpointContext,
  signal?: AbortSignal
) {
  const path = await resolveCapabilityLink(context, capabilityIds.expressionTooling, expressionToolingRelations.descriptors);
  return context.http.getJson<unknown>(path, { signal });
}

export function getExpressionAuthoringContext(
  context: StudioEndpointContext,
  request: unknown,
  signal?: AbortSignal
) {
  return postExpressionTooling(context, expressionToolingRelations.context, request, signal);
}

export function searchExpressionSymbols(
  context: StudioEndpointContext,
  request: unknown,
  signal?: AbortSignal
) {
  return postExpressionTooling(context, expressionToolingRelations.symbols, request, signal);
}

export function getExpressionCompletions(
  context: StudioEndpointContext,
  request: unknown,
  signal?: AbortSignal
) {
  return postExpressionTooling(context, expressionToolingRelations.completions, request, signal);
}

export function getExpressionHover(
  context: StudioEndpointContext,
  request: unknown,
  signal?: AbortSignal
) {
  return postExpressionTooling(context, expressionToolingRelations.hover, request, signal);
}

export function validateExpression(
  context: StudioEndpointContext,
  request: unknown,
  signal?: AbortSignal
) {
  return postExpressionTooling(context, expressionToolingRelations.validation, request, signal);
}

async function postExpressionTooling(
  context: StudioEndpointContext,
  relation: string,
  request: unknown,
  signal?: AbortSignal
) {
  const path = await resolveCapabilityLink(context, capabilityIds.expressionTooling, relation);
  return context.http.postJson<unknown>(path, request, { signal });
}
