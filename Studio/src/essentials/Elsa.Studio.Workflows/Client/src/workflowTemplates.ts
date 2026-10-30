import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ApiCapabilityUnavailableError, capabilityIds, resolveCapabilityLink } from "./api/capabilities";
import { canonicalizeStateForWire, expandStateFromWire } from "./activityInputWire";
import type { ActivityCatalogItem, WorkflowDraft, WorkflowDefinitionState } from "./workflowTypes";
import { getChildSlots } from "./workflowAdapter";

export interface WorkflowTemplateContent { state: WorkflowDefinitionState; layout: WorkflowDraft["layout"]; activityPresentation?: WorkflowDraft["activityPresentation"] }
export interface WorkflowTemplate { id: string; name: string; description?: string | null; revision: string; createdAt: string; lastModifiedAt: string; content: WorkflowTemplateContent }
export const builtInWorkflowTemplates = [
  { id: "blank", name: "Blank workflow", description: "Choose a root activity and start with an empty graph." },
  { id: "http", name: "HTTP request and response", description: "An HTTP endpoint connected to an HTTP response." }
] as const;

export async function workflowTemplatesPath(context: StudioEndpointContext) {
  try { return await resolveCapabilityLink(context, capabilityIds.workflowDesign, "workflow-templates"); }
  catch (error) { if (error instanceof ApiCapabilityUnavailableError) return null; throw error; }
}
async function requiredPath(context: StudioEndpointContext) {
  const path = await workflowTemplatesPath(context);
  if (!path) throw new Error("This server does not support saved workflow templates.");
  return path;
}
const normalize = (template: WorkflowTemplate): WorkflowTemplate => ({ ...template, content: { ...template.content, state: expandStateFromWire(template.content.state), layout: template.content.layout ?? [], activityPresentation: template.content.activityPresentation ?? [] } });
export async function listWorkflowTemplates(context: StudioEndpointContext): Promise<WorkflowTemplate[] | null> {
  const path = await workflowTemplatesPath(context);
  return path ? (await context.http.getJson<WorkflowTemplate[]>(path)).map(normalize) : null;
}
export async function getWorkflowTemplate(context: StudioEndpointContext, id: string) {
  const path = await resolveCapabilityLink(context, capabilityIds.workflowDesign, "workflow-template", { templateId: id });
  return normalize(await context.http.getJson<WorkflowTemplate>(path));
}
export async function saveWorkflowTemplate(context: StudioEndpointContext, input: {
  id?: string; name: string; description?: string | null; content: WorkflowTemplateContent; expectedRevision?: string;
}) {
  const { id, ...body } = input;
  const wire = { ...body, operationKey: crypto.randomUUID(), content: { ...body.content, state: canonicalizeStateForWire(body.content.state) } };
  const path = id ? await resolveCapabilityLink(context, capabilityIds.workflowDesign, "workflow-template", { templateId: id }) : await requiredPath(context);
  return normalize(id ? await context.http.requestJson<WorkflowTemplate>(path, { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify(wire) }) : await context.http.postJson<WorkflowTemplate>(path, wire));
}
export async function deleteWorkflowTemplate(context: StudioEndpointContext, template: WorkflowTemplate) {
  const path = await resolveCapabilityLink(context, capabilityIds.workflowDesign, "workflow-template-delete", { templateId: template.id });
  await context.http.postJson(path, { expectedRevision: template.revision, operationKey: crypto.randomUUID() });
}
export function templateContent(draft: WorkflowDraft): WorkflowTemplateContent {
  return structuredClone({ state: draft.state, layout: draft.layout, activityPresentation: draft.activityPresentation ?? [] });
}
export function templateAvailabilityReason(template: WorkflowTemplate, catalog: ActivityCatalogItem[]) {
  if (!template.content.state.rootActivity) return "This template has no root activity yet. Edit it before creating a workflow.";
  const byVersion = new Map(catalog.map(activity => [activity.activityVersionId, activity]));
  const missing = new Set<string>();
  const visit = (node: NonNullable<WorkflowDefinitionState["rootActivity"]>) => {
    const descriptor = byVersion.get(node.activityVersionId);
    if (!descriptor || descriptor.available === false) missing.add(descriptor?.displayName ?? node.activityVersionId);
    getChildSlots(node, catalog).forEach(slot => slot.activities.forEach(visit));
  };
  visit(template.content.state.rootActivity);
  return missing.size ? `Unavailable activities: ${[...missing].join(", ")}.` : undefined;
}
