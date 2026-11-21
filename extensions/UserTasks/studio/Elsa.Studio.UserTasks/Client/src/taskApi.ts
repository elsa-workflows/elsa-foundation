import { getBackendCapabilities } from "@elsa-workflows/studio-sdk";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
export type TaskFieldDisplay = "checkbox" | "dropdown" | "radio";
export interface TaskFieldPresentation { display: TaskFieldDisplay; trueLabel?: string; falseLabel?: string; unansweredLabel?: string }
export interface TaskField { key: string; label: string; kind: string; required: boolean; readOnly: boolean; help?: string; minimum?: number; maximum?: number; maxLength?: number; choices?: string[]; presentation?: TaskFieldPresentation; mustBeTrue?: boolean; controlId?: string; configuration?: Record<string, unknown> }
export interface TaskForm { id: string; version: string; name: string; provider: string; fields: TaskField[] }
export interface FormVersion { tenantId: string; form: TaskForm; createdAt: string; createdBy: string }
export interface FormDraft { tenantId: string; id: string; form: TaskForm | null; revision: number; updatedAt: string; updatedBy: string }
export interface FormDefinition { tenantId: string; id: string; latestPublished: FormVersion | null; draft: FormDraft | null; revision: number; deleted?: boolean }
export interface TaskActor { userId: string; canRead: boolean; canAct: boolean; canManage: boolean; canManageForms: boolean; canReadForms: boolean }
export interface TaskItem { id: string; title: string; description?: string; assigneeId?: string; assigneeName?: string | null; groupId?: string; groupName?: string | null; claimantId?: string; claimantName?: string | null; actions: string[]; optionalFormActions: string[]; form?: TaskForm; prefill: Record<string, unknown>; status: string; revision: number; history: { operation: string; actorId: string; actorName?: string | null; at: string; action?: string }[]; decision?: { response: { action: string; data: Record<string, unknown> }; actorId: string } }
export interface TaskPage { items: TaskItem[]; next?: string | null }
export interface TaskTargets { users: { id: string; name: string }[]; groups: { id: string; name: string }[]; moreUsers: boolean; moreGroups: boolean }
export const basicProvider = "elsa.basic-forms/1";
export const taskCapability = [{ id: "elsa.api.user-tasks", contractVersion: "1" }];
const draftFormRelations = ["identity", "form-definitions", "form-definition", "form-history", "save-form-draft", "discard-form-draft", "publish-form"];
const legacyFormRelations = ["identity", "forms"];
export type FormsPageMode = "lifecycle" | "legacy" | "unavailable";
type TaskCapabilityDocument = NonNullable<NonNullable<ElsaStudioModuleApi["backendSession"]>["capabilities"]>;
function userTaskCapability(document: TaskCapabilityDocument) {
  return document.capabilities.find(item => item.id === "elsa.api.user-tasks" && item.contractVersion === "1" && item.status !== "dormant");
}
export function formsPageModeFromCapabilities(document: TaskCapabilityDocument): FormsPageMode {
  const capability = userTaskCapability(document);
  const relations = new Set(capability?.links.map(item => item.rel) ?? []);
  if (draftFormRelations.every(relation => relations.has(relation))) return "lifecycle";
  if (legacyFormRelations.every(relation => relations.has(relation))) return "legacy";
  return "unavailable";
}
export function formsPageModeFromSession(api: ElsaStudioModuleApi): FormsPageMode | null {
  const document = api.backendSession?.capabilities;
  return document ? formsPageModeFromCapabilities(document) : null;
}
export async function formsPageMode(api: ElsaStudioModuleApi): Promise<FormsPageMode> {
  const document = api.backendSession?.capabilities ?? await getBackendCapabilities(api.backend);
  return formsPageModeFromCapabilities(document);
}
export async function supportsFormDeletion(api: ElsaStudioModuleApi) {
  try {
    const document = api.backendSession?.capabilities ?? await getBackendCapabilities(api.backend);
    return userTaskCapability(document)?.links.some(item => item.rel === "delete-form") ?? false;
  } catch {
    return false;
  }
}
async function route(api: ElsaStudioModuleApi, relation: string, variables: Record<string, string> = {}) {
  const document = api.backendSession?.capabilities ?? await getBackendCapabilities(api.backend);
  const capability = userTaskCapability(document);
  const link = capability?.links.find(item => item.rel === relation);
  if (!link) throw new Error("This backend does not advertise the required User Task operation.");
  const path = link.href.replace(/\{([^}]+)\}/g, (_match, key: string) => {
    if (!Object.hasOwn(variables, key)) throw new Error("A task operation address is incomplete.");
    return encodeURIComponent(variables[key]);
  });
  return `/${path.replace(/^\/+/, "")}`;
}
export async function actor(api: ElsaStudioModuleApi) { return api.backend.http.getJson<TaskActor>(await route(api, "identity")); }
export async function list(api: ElsaStudioModuleApi, view: string, after?: string | null, search?: string) {
  const query = new URLSearchParams({ view, limit: "25" });
  if (after) query.set("after", after); if (search) query.set("search", search);
  return api.backend.http.getJson<TaskPage>(`${await route(api, "tasks")}?${query}`);
}
export async function read(api: ElsaStudioModuleApi, id: string) { return api.backend.http.getJson<TaskItem>(await route(api, "task", { id })); }
export async function mutate(api: ElsaStudioModuleApi, task: TaskItem, operation: string, request: Record<string, unknown>) {
  return api.backend.http.postJson<TaskItem>(await route(api, operation, { id: task.id }), { revision: task.revision, ...request });
}
export interface FormPage { items: FormVersion[]; next?: string | null }
export async function forms(api: ElsaStudioModuleApi, after?: string | null) { return api.backend.http.getJson<FormPage>(`${await route(api, "forms")}?limit=100${after ? `&after=${encodeURIComponent(after)}` : ""}`); }
export async function createForm(api: ElsaStudioModuleApi, form: TaskForm) { return api.backend.http.postJson<FormVersion>(await route(api, "forms"), form); }
export async function formDefinitions(api: ElsaStudioModuleApi, after?: string | null) { return api.backend.http.getJson<{ items: FormDefinition[]; next?: string | null }>(`${await route(api, "form-definitions")}?limit=25${after ? `&after=${encodeURIComponent(after)}` : ""}`); }
export async function formDefinition(api: ElsaStudioModuleApi, id: string) { return api.backend.http.getJson<FormDefinition>(await route(api, "form-definition", { id })); }
export async function formVersions(api: ElsaStudioModuleApi, id: string, after?: string | null) { return api.backend.http.getJson<FormPage>(`${await route(api, "form-history", { id })}?limit=25${after ? `&after=${encodeURIComponent(after)}` : ""}`); }
export async function saveFormDraft(api: ElsaStudioModuleApi, id: string, form: TaskForm, revision: number) { return api.backend.http.postJson<FormDefinition>(await route(api, "save-form-draft", { id }), { form, revision }); }
export async function discardFormDraft(api: ElsaStudioModuleApi, id: string, revision: number) { return api.backend.http.postJson<FormDefinition>(await route(api, "discard-form-draft", { id }), { revision }); }
export async function publishForm(api: ElsaStudioModuleApi, id: string, form: TaskForm, revision: number) { return api.backend.http.postJson<FormVersion>(await route(api, "publish-form", { id }), { form, revision }); }
export async function deleteForm(api: ElsaStudioModuleApi, id: string, revision: number) { return api.backend.http.postJson<FormDefinition>(await route(api, "delete-form", { id }), { revision }); }
export async function targets(api: ElsaStudioModuleApi, skip = 0) { return api.backend.http.getJson<TaskTargets>(`${await route(api, "targets")}?skip=${skip}&take=100`); }
export function statusOf(error: unknown) { return typeof error === "object" && error !== null && "status" in error ? Number(error.status) : 0; }
export function message(error: unknown) { return error instanceof Error ? error.message : "This request could not be completed."; }
