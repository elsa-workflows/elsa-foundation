import type { StudioActivityInputDescriptor } from "@elsa-workflows/studio-sdk";
import { getInputPropertyName, readWrappedInput, withLiteralValue, writeInputValue } from "./activityProperties";
import type { ActivityCatalogItem, ActivityNode } from "./workflowTypes";

function startInput(catalogItem: ActivityCatalogItem | undefined) {
  return catalogItem?.inputs.find((input): input is StudioActivityInputDescriptor => {
    if (typeof input !== "object" || input === null || !("name" in input) || !("typeName" in input)) return false;
    return [input.name, "referenceKey" in input ? input.referenceKey : undefined]
      .some(key => typeof key === "string" && key.toLowerCase() === "canstartworkflow");
  });
}

/** Only offer the shortcut for a declared, literal start setting; never replace an expression. */
export function getWorkflowStartEnabled(activity: ActivityNode, catalogItem: ActivityCatalogItem | undefined): boolean | undefined {
  const input = startInput(catalogItem);
  if (!input) return undefined;
  const { expression } = readWrappedInput(activity, input);
  if (expression.type !== "Literal") return undefined;
  if (expression.value === true || expression.value === "true") return true;
  if (expression.value === false || expression.value === "false" || expression.value === "") return false;
  return undefined;
}

/** Uses the same authored input and metadata as the inspector, so save/undo follow normal edits. */
export function withWorkflowStartEnabled(activity: ActivityNode, catalogItem: ActivityCatalogItem | undefined, enabled: boolean): ActivityNode {
  const input = startInput(catalogItem);
  if (!input || getWorkflowStartEnabled(activity, catalogItem) === undefined) return activity;
  const authored = activity[getInputPropertyName(input)];
  const isWrapped = typeof authored === "object" && authored !== null && "expression" in authored;
  return writeInputValue(activity, input, input.isWrapped === false && !isWrapped
    ? enabled
    : withLiteralValue(readWrappedInput(activity, input), enabled));
}
