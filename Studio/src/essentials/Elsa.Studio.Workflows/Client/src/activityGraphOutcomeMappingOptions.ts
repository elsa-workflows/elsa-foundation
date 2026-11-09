import type { StudioActivityDefinitionContract } from "@elsa-workflows/studio-sdk";
import type { ActivityCatalogItem, ActivityNode } from "./workflowTypes";

export interface ActivityGraphOutcomeChoice {
  referenceKey: string;
  name: string;
}

/** Reads only stable outcome identities advertised by the root or its catalog descriptor. */
export function getActivityGraphOutcomeMappingOptions(
  root: ActivityNode,
  catalogItem: ActivityCatalogItem | undefined,
  contract: Pick<StudioActivityDefinitionContract, "outcomes">
) {
  return {
    sourceOutcomes: uniqueOutcomeChoices([
      ...readStableOutcomeChoices((root as { outcomes?: unknown }).outcomes),
      ...readStableFlowOutcomeChoices(catalogItem?.ports),
      ...readStableFlowOutcomeChoices(catalogItem?.outputs),
      ...readStableFlowOutcomeChoices(catalogItem?.designFacets)
    ]),
    boundaryOutcomes: contract.outcomes
      .filter(outcome => outcome.isEmitted && outcome.referenceKey.trim())
      .map(outcome => ({ referenceKey: outcome.referenceKey, name: outcome.name || outcome.referenceKey }))
  };
}

/**
 * A creation-time default is safe only when there is one advertised source outcome, or one
 * unambiguous canonical Done outcome. Other multi-outcome roots must leave the choice explicit.
 */
export function defaultActivityGraphOutcomeChoice(sourceOutcomes: ActivityGraphOutcomeChoice[]) {
  if (sourceOutcomes.length === 1) return sourceOutcomes[0];
  const canonicalDone = sourceOutcomes.filter(outcome =>
    outcome.name.trim().toLowerCase() === "done" || outcome.referenceKey.trim().toLowerCase() === "done"
  );
  return canonicalDone.length === 1 ? canonicalDone[0] : null;
}

function readStableOutcomeChoices(value: unknown): ActivityGraphOutcomeChoice[] {
  if (!Array.isArray(value)) return [];
  return value.flatMap(item => {
    if (!item || typeof item !== "object") return [];
    const record = item as Record<string, unknown>;
    const referenceKey = typeof record.referenceKey === "string" ? record.referenceKey.trim() : "";
    if (!referenceKey) return [];
    const name = typeof record.displayName === "string" && record.displayName.trim()
      ? record.displayName
      : typeof record.name === "string" && record.name.trim()
        ? record.name
        : referenceKey;
    return [{ referenceKey, name }];
  });
}

function readStableFlowOutcomeChoices(value: unknown): ActivityGraphOutcomeChoice[] {
  if (!Array.isArray(value)) return [];
  const choices: ActivityGraphOutcomeChoice[] = [];
  for (const item of value) {
    if (!item || typeof item !== "object") continue;
    const record = item as Record<string, unknown>;
    if (Array.isArray(record.ports)) choices.push(...readStableFlowOutcomeChoices(record.ports));
    const type = typeof record.type === "string" ? record.type : typeof record.portType === "string" ? record.portType : "";
    const referenceKey = typeof record.referenceKey === "string" ? record.referenceKey.trim() : "";
    if (!["flow", "outcome"].includes(type.toLowerCase()) || !referenceKey) continue;
    const name = typeof record.displayName === "string" && record.displayName.trim()
      ? record.displayName
      : typeof record.name === "string" && record.name.trim()
        ? record.name
        : referenceKey;
    choices.push({ referenceKey, name });
  }
  return choices;
}

function uniqueOutcomeChoices(choices: ActivityGraphOutcomeChoice[]) {
  const byReferenceKey = new Map<string, ActivityGraphOutcomeChoice>();
  for (const choice of choices) {
    if (!byReferenceKey.has(choice.referenceKey)) byReferenceKey.set(choice.referenceKey, choice);
  }
  return [...byReferenceKey.values()];
}
