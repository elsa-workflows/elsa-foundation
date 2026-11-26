import type { ActivityCatalogItem, ActivityNode, CreateDefinitionRequest } from "../workflowTypes";
import { createActivityNode } from "../workflowAdapter";
import { createHttpStarterState } from "./httpWorkflowStarter";
import { createNodeId, getCreateRootActivity } from "./editorHelpers";
import type { CreateWorkflowDraft } from "./editorTypes";

export function getCreateInitialState(draft: CreateWorkflowDraft, activities: ActivityCatalogItem[]) {
  if (draft.starter === "http") return createHttpStarterState(activities);
  const activity = getCreateRootActivity(draft.rootActivityVersionId, activities);
  if (!activity?.authoringTemplate) return null;

  const rootActivity = createActivityNode(activity, createNodeId(activity));
  return {
    rootActivity: {
      ...rootActivity,
      inputs: toAuthoredArguments(activity.authoringTemplate.inputs) as unknown as ActivityNode["inputs"],
      outputs: toAuthoredArguments(activity.authoringTemplate.outputs) as unknown as ActivityNode["outputs"]
    }
  };
}

function toAuthoredArguments(value: unknown) {
  if (!Array.isArray(value)) return structuredClone(value ?? {});
  return Object.fromEntries(value.flatMap(item => {
    if (!item || typeof item !== "object") return [];
    const argument = item as { referenceKey?: unknown; value?: unknown };
    return typeof argument.referenceKey === "string"
      ? [[argument.referenceKey, structuredClone(argument.value)]]
      : [];
  }));
}

export function getCreateInitialDocument(draft: CreateWorkflowDraft, activities: ActivityCatalogItem[]): Pick<CreateDefinitionRequest, "initialState" | "layout" | "activityPresentation"> {
  if (draft.templateContent) return { initialState: structuredClone(draft.templateContent.state), layout: structuredClone(draft.templateContent.layout), activityPresentation: structuredClone(draft.templateContent.activityPresentation ?? []) };
  return { initialState: getCreateInitialState(draft, activities) };
}