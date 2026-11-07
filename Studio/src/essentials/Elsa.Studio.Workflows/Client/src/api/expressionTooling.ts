import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { capabilityIds, resolveCapabilityLink } from "./capabilities";

export type ExpressionToolingOwnerKind = "workflow" | "activity-draft";

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
  signal?: AbortSignal,
  ownerKind: ExpressionToolingOwnerKind = "workflow"
) {
  const path = await resolveCapabilityLink(context, ownerKind === "activity-draft" ? capabilityIds.activityExpressionTooling : capabilityIds.expressionTooling, expressionToolingRelations.descriptors);
  return context.http.getJson<unknown>(path, { signal });
}

export function getExpressionAuthoringContext(
  context: StudioEndpointContext,
  request: unknown,
  signal?: AbortSignal,
  ownerKind: ExpressionToolingOwnerKind = "workflow"
) {
  return postExpressionTooling(context, expressionToolingRelations.context, request, signal, ownerKind);
}

export function searchExpressionSymbols(
  context: StudioEndpointContext,
  request: unknown,
  signal?: AbortSignal,
  ownerKind: ExpressionToolingOwnerKind = "workflow"
) {
  return postExpressionTooling(context, expressionToolingRelations.symbols, request, signal, ownerKind);
}

export function getExpressionCompletions(
  context: StudioEndpointContext,
  request: unknown,
  signal?: AbortSignal,
  ownerKind: ExpressionToolingOwnerKind = "workflow"
) {
  return postExpressionTooling(context, expressionToolingRelations.completions, request, signal, ownerKind);
}

export function getExpressionHover(
  context: StudioEndpointContext,
  request: unknown,
  signal?: AbortSignal,
  ownerKind: ExpressionToolingOwnerKind = "workflow"
) {
  return postExpressionTooling(context, expressionToolingRelations.hover, request, signal, ownerKind);
}

export function validateExpression(
  context: StudioEndpointContext,
  request: unknown,
  signal?: AbortSignal,
  ownerKind: ExpressionToolingOwnerKind = "workflow"
) {
  return postExpressionTooling(context, expressionToolingRelations.validation, request, signal, ownerKind);
}

async function postExpressionTooling(
  context: StudioEndpointContext,
  relation: string,
  request: unknown,
  signal?: AbortSignal,
  ownerKind: ExpressionToolingOwnerKind = "workflow"
) {
  const path = await resolveCapabilityLink(context, ownerKind === "activity-draft" ? capabilityIds.activityExpressionTooling : capabilityIds.expressionTooling, relation);
  return context.http.postJson<unknown>(path, request, { signal });
}
