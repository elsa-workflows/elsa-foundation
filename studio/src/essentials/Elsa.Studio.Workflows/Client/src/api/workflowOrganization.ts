import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { WorkflowFolder } from "../workflowTypes";
import { capabilityIds, resolveCapabilityLink } from "./capabilities";
export { workflowOrganizationSupport } from "./workflowOrganizationCapabilities";

export interface WorkflowOrganizationPermissions {
  canAssignTags?: boolean;
  canManageFolders?: boolean;
  canManageTags?: boolean;
  canShareViews?: boolean;
}
export interface WorkflowTagValue { id: string; name: string; deprecated?: boolean }
export interface WorkflowTag {
  id: string;
  name: string;
  description?: string | null;
  mode: "marker" | "controlled";
  cardinality: "single" | "multiple";
  deprecated: boolean;
  revision: string;
  values: WorkflowTagValue[];
}
export interface WorkflowTagInput {
  name: string;
  description?: string | null;
  mode: WorkflowTag["mode"];
  cardinality: WorkflowTag["cardinality"];
  deprecated?: boolean;
  revision?: string;
  values?: WorkflowTagValue[];
}
export interface WorkflowTagAssignment { tagId: string; valueId?: string | null }
export interface WorkflowTagFilter {
  tagId: string;
  operator: "exists" | "missing" | "any" | "all" | "none";
  valueIds?: string[];
}
export interface WorkflowOrganization {
  definitionId: string;
  folderId: string | null;
  folderBreadcrumb: WorkflowFolder[];
  tags: WorkflowTagAssignment[];
  revision: string;
  permissions?: WorkflowOrganizationPermissions;
}
export interface WorkflowLibraryQuery {
  searchTerm?: string | null;
  state?: "active" | "deleted" | "all";
  folderId?: string | null;
  unfiled?: boolean;
  includeDescendants?: boolean;
  tagFilters?: WorkflowTagFilter[];
  favoritesOnly?: boolean;
  sort?: "name" | "name-desc" | "modified";
  publicationState?: string | null;
}
export interface WorkflowLibraryView {
  id: string;
  name: string;
  shared: boolean;
  ownerId: string;
  query: WorkflowLibraryQuery;
  columns: string[];
  revision: string;
  schemaVersion: number;
  canEdit?: boolean;
  canDelete?: boolean;
}
export interface WorkflowTagPage {
  items: WorkflowTag[];
  nextContinuationToken?: string | null;
  permissions?: WorkflowOrganizationPermissions;
}
export interface WorkflowViewsResponse {
  items: WorkflowLibraryView[];
  permissions?: WorkflowOrganizationPermissions;
}
export interface WorkflowTagBulkOutcome {
  definitionId: string;
  status: string;
  revision?: string | null;
  message?: string | null;
}

const path = (context: StudioEndpointContext, relation: string, parameters: Record<string, string> = {}) =>
  resolveCapabilityLink(context, capabilityIds.workflowDesign, relation, parameters);

export async function listWorkflowTags(context: StudioEndpointContext, search = "", continuationToken?: string | null) {
  const query = new URLSearchParams({ pageSize: "100" });
  if (search.trim()) query.set("search", search.trim());
  if (continuationToken) query.set("continuationToken", continuationToken);
  return context.http.getJson<WorkflowTagPage>(`${await path(context, "workflow-tags")}?${query}`);
}
export async function createWorkflowTag(context: StudioEndpointContext, input: WorkflowTagInput) {
  return context.http.postJson<WorkflowTag>(await path(context, "workflow-tags"), input);
}
export async function getWorkflowTag(context: StudioEndpointContext, tagId: string) {
  return context.http.getJson<WorkflowTag>(await path(context, "workflow-tag", { tagId }));
}
export async function updateWorkflowTag(context: StudioEndpointContext, tagId: string, input: WorkflowTagInput) {
  return context.http.putJson<WorkflowTag>(await path(context, "workflow-tag", { tagId }), input);
}
export async function getWorkflowOrganization(context: StudioEndpointContext, definitionId: string) {
  return context.http.getJson<WorkflowOrganization>(await path(context, "workflow-organization", { definitionId }));
}
export async function saveWorkflowOrganization(context: StudioEndpointContext, organization: WorkflowOrganization, tags: WorkflowTagAssignment[], operationKey: string) {
  return context.http.putJson<WorkflowOrganization>(await path(context, "workflow-organization", { definitionId: organization.definitionId }), {
    tags, revision: organization.revision, operationKey
  });
}
export async function bulkEditWorkflowTags(context: StudioEndpointContext, input: {
  targets: { definitionId: string; revision: string }[];
  add: WorkflowTagAssignment[];
  remove: WorkflowTagAssignment[];
  operationKey: string;
}) {
  return context.http.postJson<{ items?: WorkflowTagBulkOutcome[]; outcomes?: WorkflowTagBulkOutcome[] } | WorkflowTagBulkOutcome[]>(await path(context, "workflow-tags-bulk"), input);
}
export async function listWorkflowViews(context: StudioEndpointContext) {
  return context.http.getJson<WorkflowViewsResponse>(await path(context, "workflow-views"));
}
export async function saveWorkflowView(context: StudioEndpointContext, input: { name: string; query: WorkflowLibraryQuery; shared: boolean; columns: string[]; revision?: string }, id?: string) {
  return id
    ? context.http.putJson<WorkflowLibraryView>(await path(context, "workflow-view", { viewId: id }), input)
    : context.http.postJson<WorkflowLibraryView>(await path(context, "workflow-views"), input);
}
export async function deleteWorkflowView(context: StudioEndpointContext, view: WorkflowLibraryView) {
  const query = new URLSearchParams({ revision: view.revision });
  return context.http.deleteJson<unknown>(`${await path(context, "workflow-view", { viewId: view.id })}?${query}`);
}
export async function setWorkflowFavorite(context: StudioEndpointContext, definitionId: string, favorite: boolean) {
  const target = await path(context, "workflow-favorites", { definitionId });
  return favorite ? context.http.putJson<unknown>(target, {}) : context.http.deleteJson<unknown>(target);
}
export function workflowTagAssignmentKey(assignment: WorkflowTagAssignment) {
  return JSON.stringify([assignment.tagId, assignment.valueId ?? null]);
}
export function workflowOperationKey() {
  return crypto.randomUUID();
}
