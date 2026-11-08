import type { StudioAgentClientExecutionResult, StudioAgentContextAttachment, StudioAgentPreparedInvocation } from "@elsa-workflows/studio-sdk";
import { sha256Hex } from "../sha256";
import type { WorkflowDefinitionBrowseLocation } from "./workflowDefinitionBrowseLocation";
import type { CreateWorkflowDraft } from "./editorTypes";

/**
 * The workflow library's client contexts and page working-state changes (spec 1193 FR-020, FR-023, FR-024): the visible
 * list, the open create dialog and the visible template list, each bounded and attributed to the revision the page computes
 * for its visible state and to the page's own client binding. These are pure functions; the pages own the state.
 */
export const workflowsModuleId = "Elsa.Studio.Workflows";
export const libraryKind = "workflows.library";
export const createDialogKind = "workflows.create-dialog";
export const templateListKind = "workflows.template-list";
/** The server's bound on each admitted page context (WorkflowLibraryContext.ContentLimit), well under the host's 32 KiB budget. */
export const libraryContextLimit = 16 * 1024;
const itemLimit = 50;

export interface LibraryContextRow {
  id: string; name: string; folderId?: string | null; tags?: Array<{ tagId: string; valueId?: string | null }>; favorite?: boolean;
  publicationState?: string | null; organizationRevision?: string | null;
}

export interface LibraryContextInput {
  location: WorkflowDefinitionBrowseLocation;
  view?: { id: string; name: string; shared: boolean; revision: string; canEdit: boolean } | null;
  rows: LibraryContextRow[];
  selection: string[];
  totalCount: number;
  permissions: { canAssignTags?: boolean; canManageFolders?: boolean; canManageTags?: boolean; canShareViews?: boolean };
  organizationEnabled: boolean;
}

/** The visible query as the library page expresses it (search, folder, tag and value filters, favorites, sort, publication filter). */
export function libraryQuery(location: WorkflowDefinitionBrowseLocation) {
  const folder = location.folderSelection;
  return {
    // The server admits a search of at most 256 characters, as the list's own query does.
    search: clip(location.search.trim(), 256) || null,
    state: location.listState,
    folderId: typeof folder === "object" ? folder.id : null,
    unfiled: folder === "unfiled",
    includeDescendants: location.includeDescendants === true,
    tagFilters: (location.tagFilters ?? []).slice(0, 16).map(filter => ({ tagId: filter.tagId, operator: filter.operator ?? "exists", valueIds: filter.valueIds ?? [] })),
    favoritesOnly: location.favoritesOnly === true,
    sort: location.sort ?? "name",
    publicationState: location.publicationState ?? null
  };
}

const clip = (value: string | null | undefined, limit: number) => value == null ? value ?? null : value.slice(0, limit);

/** Headroom below the server's bound for differences between this serialization and the server's measure of the raw text. */
const libraryContextMargin = 1024;

/**
 * The `workflows.library` content, bounded: at most 50 visible rows, a selection of disclosed rows only, names clipped.
 * When the rows would take the content over the server's 16 KiB bound, their tags go first, then their names are clipped
 * further, then unselected rows and finally selected ones are left out from the end; `itemsOmitted` counts the rows left out.
 */
export function buildLibraryContent(input: LibraryContextInput) {
  const projected = input.rows.slice(0, itemLimit).map(row => ({ id: row.id, name: clip(row.name, 256), folderId: row.folderId ?? null,
    tags: (row.tags ?? []).slice(0, 16).map(tag => ({ tagId: tag.tagId, valueId: tag.valueId ?? null })),
    favorite: row.favorite === true, publicationState: row.publicationState ?? null, organizationRevision: row.organizationRevision ?? null }));
  const selected = new Set(input.selection);
  const compose = (items: typeof projected) => {
    const visible = new Set(items.map(item => item.id));
    return {
      query: libraryQuery(input.location),
      view: input.view ? { id: input.view.id, name: clip(input.view.name, 256), ownerKind: input.view.shared ? "shared" : "private", revision: input.view.revision, canEdit: input.view.canEdit } : null,
      columns: (input.location.columns ?? ["name"]).slice(0, 16),
      selection: input.selection.filter(id => visible.has(id)).slice(0, 100),
      items,
      ...(items.length < Math.min(input.rows.length, itemLimit) ? { itemsOmitted: Math.min(input.rows.length, itemLimit) - items.length } : {}),
      totalCount: Math.max(0, input.totalCount),
      permissions: { canAssignTags: input.permissions.canAssignTags === true, canManageFolders: input.permissions.canManageFolders === true,
        canManageTags: input.permissions.canManageTags === true, canShareViews: input.permissions.canShareViews === true },
      organization: input.organizationEnabled ? "enabled" : "disabled"
    };
  };
  const over = (items: typeof projected) => JSON.stringify({ revision: "sha256:" + "0".repeat(64), ...compose(items) }).length > libraryContextLimit - libraryContextMargin;
  let items = projected;
  if (over(items)) items = items.map(item => ({ ...item, tags: [] }));
  if (over(items)) items = items.map(item => ({ ...item, name: clip(item.name, 64) }));
  while (over(items) && items.length) {
    const last = items.map(item => selected.has(item.id)).lastIndexOf(false);
    items = items.filter((_, index) => index !== (last >= 0 ? last : items.length - 1));
  }
  const body = compose(items);
  return { revision: pageRevision(body), ...body };
}

/** A page revision: SHA-256 over the canonical visible state, so any visible change is a new revision. */
export function pageRevision(body: unknown) {
  return `sha256:${sha256Hex(canonical(body))}`;
}

function canonical(value: unknown): string {
  if (Array.isArray(value)) return `[${value.map(canonical).join(",")}]`;
  if (value && typeof value === "object")
    return `{${Object.keys(value).sort().map(key => `${JSON.stringify(key)}:${canonical((value as Record<string, unknown>)[key])}`).join(",")}}`;
  return JSON.stringify(value ?? null);
}

function attachment(kind: string, resourceType: string, resourceId: string, content: { revision: string } & Record<string, unknown>, client: { clientId: string; generationId: string },
  label: string): StudioAgentContextAttachment {
  return {
    id: `${kind}:${resourceId}`, moduleId: workflowsModuleId, schemaVersion: "1",
    target: { moduleId: workflowsModuleId, resourceType, resourceId, revision: content.revision },
    source: "workflow-library", sourceId: resourceId, label, contentType: kind, sensitivity: "internal", scope: "screen", origin: "clientSnapshot",
    client, agentScopes: ["workflow-author"], summary: label, references: { revision: content.revision }, content
  } as StudioAgentContextAttachment;
}

export function libraryAttachment(input: LibraryContextInput, client: { clientId: string; generationId: string }) {
  return attachment(libraryKind, "workflow-library", "library", buildLibraryContent(input), client, "Visible workflow library list");
}

export interface CreateDialogTemplate { id: string; name: string; description?: string | null; revision: string }

type DisclosedTemplate = { id: string; name: string | null; description: string | null; revision: string };

const overLimit = (body: Record<string, unknown>) =>
  JSON.stringify({ revision: "sha256:" + "0".repeat(64), ...body }).length > libraryContextLimit - libraryContextMargin;

/**
 * Bounds a disclosed template list to the server's 16 KiB content bound (WorkflowLibraryContext.ContentLimit), as the
 * library list is bounded: descriptions are clipped and then left out, names are clipped, and finally templates are left
 * out from the end, those named in `keep` (the draft's or the list's selected template) last. The server's shapes have no
 * omitted count, so a left-out template is simply not disclosed.
 */
function boundTemplates(templates: DisclosedTemplate[], keep: string | null, compose: (items: DisclosedTemplate[]) => Record<string, unknown>) {
  let items = templates;
  if (overLimit(compose(items))) items = items.map(item => ({ ...item, description: clip(item.description, 120) }));
  if (overLimit(compose(items))) items = items.map(item => ({ ...item, description: null }));
  if (overLimit(compose(items))) items = items.map(item => ({ ...item, name: clip(item.name, 64) }));
  while (overLimit(compose(items)) && items.length) {
    const last = items.map(item => item.id === keep).lastIndexOf(false);
    items = items.filter((_, index) => index !== (last >= 0 ? last : items.length - 1));
  }
  return items;
}

const disclosedTemplate = (template: CreateDialogTemplate): DisclosedTemplate =>
  ({ id: template.id, name: clip(template.name, 256), description: clip(template.description ?? null, 500), revision: template.revision });

export function buildCreateDialogContent(draft: CreateWorkflowDraft, templates: CreateDialogTemplate[]) {
  const fields = {
    name: clip(draft.name, 256), description: clip(draft.description, 2000), rootActivityVersionId: draft.rootActivityVersionId ?? null, templateId: draft.templateId ?? null
  };
  const compose = (items: DisclosedTemplate[]) => ({ ...fields, templates: items });
  const body = compose(boundTemplates(templates.slice(0, 50).map(disclosedTemplate), fields.templateId, compose));
  return { revision: pageRevision(body), ...body };
}

export function createDialogAttachment(draft: CreateWorkflowDraft, templates: CreateDialogTemplate[], client: { clientId: string; generationId: string }) {
  return attachment(createDialogKind, "workflow-create-dialog", "create-dialog", buildCreateDialogContent(draft, templates), client, "Open new-workflow dialog");
}

export function buildTemplateListContent(search: string, selection: string | null, templates: CreateDialogTemplate[]) {
  const listed = templates.slice(0, 100).map(disclosedTemplate);
  const filter = { search: search.trim() ? clip(search.trim(), 256) : null };
  const selected = selection && listed.some(template => template.id === selection) ? selection : null;
  // The selection names a disclosed template only; if even it had to be left out, nothing is selected.
  const compose = (items: DisclosedTemplate[]) => ({ filter, selection: selected && items.some(item => item.id === selected) ? selected : null, templates: items });
  const body = compose(boundTemplates(listed, selected, compose));
  return { revision: pageRevision(body), ...body };
}

export function templateListAttachment(search: string, selection: string | null, templates: CreateDialogTemplate[], client: { clientId: string; generationId: string }) {
  return attachment(templateListKind, "workflow-template-list", "templates", buildTemplateListContent(search, selection, templates), client, "Visible template list");
}

/** The supported list-state change, exactly as the server froze it (WorkflowLibrarySetListStateTool). */
export interface ListStateChange {
  search?: string | null; folderId?: string | null; favoritesOnly?: boolean; sort?: "name" | "name-desc" | "modified"; publicationState?: "published" | null;
  tagFilters?: Array<{ tagId: string; operator?: "exists" | "any" | "all"; valueIds?: string[] }>; columns?: string[];
}

const listStateFields = new Set(["search", "folderId", "favoritesOnly", "sort", "publicationState", "tagFilters", "columns"]);

/** Applies a frozen change to the browse location, or null when the change is not one the list supports. */
export function applyListStateChange(location: WorkflowDefinitionBrowseLocation, change: unknown): WorkflowDefinitionBrowseLocation | null {
  if (!change || typeof change !== "object" || Array.isArray(change)) return null;
  const value = change as ListStateChange;
  if (Object.keys(value).length === 0 || Object.keys(value).some(key => !listStateFields.has(key))) return null;
  const next: WorkflowDefinitionBrowseLocation = { ...location, viewId: undefined };
  if ("search" in value) { if (value.search != null && typeof value.search !== "string") return null; next.search = value.search ?? ""; }
  if ("folderId" in value) {
    if (value.folderId != null && typeof value.folderId !== "string") return null;
    next.folderSelection = value.folderId ? { id: value.folderId } : "all";
  }
  if ("favoritesOnly" in value) { if (typeof value.favoritesOnly !== "boolean") return null; next.favoritesOnly = value.favoritesOnly; }
  if ("sort" in value) { if (!["name", "name-desc", "modified"].includes(value.sort as string)) return null; next.sort = value.sort as WorkflowDefinitionBrowseLocation["sort"]; }
  if ("publicationState" in value) { if (value.publicationState != null && value.publicationState !== "published") return null; next.publicationState = value.publicationState ?? undefined; }
  if ("tagFilters" in value) {
    if (!Array.isArray(value.tagFilters)) return null;
    next.tagFilters = value.tagFilters.map(filter => ({ tagId: filter.tagId, operator: filter.operator ?? "exists", valueIds: filter.valueIds ?? [] })) as WorkflowDefinitionBrowseLocation["tagFilters"];
    next.invalidTagFilters = undefined;
  }
  if ("columns" in value) { if (!Array.isArray(value.columns) || !value.columns.length) return null; next.columns = [...value.columns]; }
  return next;
}

/** Applies a frozen prefill to the open create dialog's draft. Creating the workflow remains the user's Create action. */
export function applyCreateDialogPrefill(draft: CreateWorkflowDraft, change: unknown, templates: Array<CreateDialogTemplate & { content?: CreateWorkflowDraft["templateContent"] }>): CreateWorkflowDraft | null {
  if (!change || typeof change !== "object" || Array.isArray(change)) return null;
  const value = change as { name?: unknown; description?: unknown; rootActivityVersionId?: unknown; templateId?: unknown };
  if (Object.keys(value).some(key => !["name", "description", "rootActivityVersionId", "templateId"].includes(key)) || typeof value.name !== "string" || !value.name.trim()) return null;
  if (value.description !== undefined && typeof value.description !== "string") return null;
  if (value.rootActivityVersionId !== undefined && value.templateId !== undefined) return null;
  const next: CreateWorkflowDraft = { ...draft, name: value.name, ...(typeof value.description === "string" ? { description: value.description } : {}) };
  if (typeof value.rootActivityVersionId === "string") { next.rootActivityVersionId = value.rootActivityVersionId; delete next.templateId; delete next.templateContent; delete next.starter; }
  if (typeof value.templateId === "string") {
    const template = templates.find(item => item.id === value.templateId);
    if (!template?.content) return null;
    next.templateId = template.id; next.templateContent = structuredClone(template.content); next.rootActivityVersionId = null; delete next.starter;
  }
  return next;
}

/** The binding check every page handler makes first: the exact page binding and the revision the change was reviewed at. */
export function pageBindingProblem(invocation: StudioAgentPreparedInvocation, snapshot: StudioAgentContextAttachment | null | undefined, resourceType: string): string | null {
  if (!snapshot) return "The originating page is no longer open, so nothing was applied.";
  if (!invocation.client || snapshot.client?.clientId !== invocation.client.clientId || snapshot.client?.generationId !== invocation.client.generationId)
    return "The originating page was reloaded or reopened, so this change is stale. Nothing was applied.";
  const target = invocation.target;
  if (!target || target.moduleId !== workflowsModuleId || target.resourceType !== resourceType || target.revision !== snapshot.target?.revision)
    return "The page's visible state changed after review, so this change is stale. Nothing was applied.";
  if (Date.parse(invocation.expiresAt) <= Date.now()) return "The reviewed change expired before it was applied. Nothing was applied.";
  return null;
}

/** The receipt a page returns: the after revision and exactly the approved change, with a revert token. */
export function pageReceipt(invocation: StudioAgentPreparedInvocation, typeId: string, afterRevision: string, undoToken: string, summary: string): StudioAgentClientExecutionResult {
  const args = invocation.arguments as { baseRevision?: string; change?: unknown } | null;
  return {
    outcome: "applied", summary, after: { ...invocation.target!, revision: afterRevision },
    result: { typeId, schemaVersion: "1", data: { observed: true, revision: afterRevision, baseRevision: invocation.target!.revision, change: args?.change, undoToken } }
  };
}
