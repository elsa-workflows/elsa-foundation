import type { ActivityDefinitionVersionView } from "../activityDefinitionTypes";
import type { ReadyBlockPromotion } from "./blockPromotionTypes";

/** Key-sorted JSON. An absent and a null member are the same: the server's records write omitted optional members as null. */
export function promotionMaterialJson(value: unknown): string {
  return JSON.stringify(normalize(value));
}

function normalize(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(normalize);
  if (value === null || typeof value !== "object") return value;
  return Object.fromEntries(Object.entries(value as Record<string, unknown>)
    .filter(([, member]) => member !== null && member !== undefined)
    .sort(([left], [right]) => left < right ? -1 : left > right ? 1 : 0)
    .map(([key, member]) => [key, normalize(member)]));
}

export type PromotionTargetReview = { status: "matches" } | { status: "refused"; reasons: string[] };

/**
 * Compares a freshly read published version with the freshly analyzed candidate. Only an active version whose
 * provider, implementation and public contract are the reviewed material can replace the block; a matching name,
 * head or public signature alone is not enough.
 */
export function reviewPromotionTarget(plan: ReadyBlockPromotion, versionId: string, version: ActivityDefinitionVersionView): PromotionTargetReview {
  const reasons: string[] = [];
  const provider = plan.candidate.provider;
  if (version.versionId !== versionId) reasons.push("The server returned a different version than the one selected.");
  if (version.lifecycle !== "Active") reasons.push(`This version is ${version.lifecycle.toLowerCase()}. Choose an active published version.`);
  if (version.provider.providerKey !== provider.providerKey || version.provider.schemaVersion !== provider.schemaVersion)
    reasons.push("This version uses a different implementation provider than the proposed reusable activity.");
  else if (version.provider.payload === undefined)
    reasons.push("Studio cannot read this version's implementation, so it cannot confirm that it matches this block.");
  else if (promotionMaterialJson(version.provider.payload) !== promotionMaterialJson(provider.payload))
    reasons.push("This version's implementation differs from the block as it is now.");
  if (promotionMaterialJson(version.contract) !== promotionMaterialJson(plan.candidate.contract))
    reasons.push("This version's public inputs, outputs or outcomes differ from the reviewed proposal.");
  return reasons.length ? { status: "refused", reasons } : { status: "matches" };
}
