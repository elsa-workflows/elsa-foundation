import { getInputPropertyName, readWrappedInput, withLiteralValue, writeInputValue } from "./activityProperties";
import { findWorkflowStartInput, getWorkflowStartEnabled } from "./workflowStart";
import type { ActivityCatalogItem, ActivityNode } from "./workflowTypes";

/** Uses the same authored input and metadata as the inspector, so save/undo follow normal edits. */
export function withWorkflowStartEnabled(activity: ActivityNode, catalogItem: ActivityCatalogItem | undefined, enabled: boolean): ActivityNode {
  const input = findWorkflowStartInput(catalogItem);
  if (!input || getWorkflowStartEnabled(activity, catalogItem) === undefined) return activity;
  const authored = activity[getInputPropertyName(input)];
  const isWrapped = typeof authored === "object" && authored !== null && "expression" in authored;
  return writeInputValue(activity, input, input.isWrapped === false && !isWrapped
    ? enabled
    : withLiteralValue(readWrappedInput(activity, input), enabled));
}
