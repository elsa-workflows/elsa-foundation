import { getBackendCapabilities } from "@elsa-workflows/studio-sdk";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
export interface TaskField { key: string; label: string; kind: string; required: boolean; readOnly: boolean; help?: string; minimum?: number; maximum?: number; maxLength?: number; choices?: string[] }
export interface TaskForm { id: string; version: string; name: string; provider: string; fields: TaskField[] }
export interface FormVersion { tenantId: string; form: TaskForm; createdAt: string; createdBy: string }
export interface TaskActor { userId: string; canRead: boolean; canAct: boolean; canManage: boolean; canManageForms: boolean; canReadForms: boolean }
export interface TaskItem { id: string; title: string; description?: string; assigneeId?: string; assigneeName?: string | null; groupId?: string; groupName?: string | null; claimantId?: string; claimantName?: string | null; actions: string[]; optionalFormActions: string[]; form?: TaskForm; prefill: Record<string, unknown>; status: string; revision: number; history: { operation: string; actorId: string; actorName?: string | null; at: string; action?: string }[]; decision?: { response: { action: string; data: Record<string, unknown> }; actorId: string } }
export interface TaskPage { items: TaskItem[]; next?: string | null }
export interface TaskTargets { users: { id: string; name: string }[]; groups: { id: string; name: string }[]; moreUsers: boolean; moreGroups: boolean }
export const basicProvider = "elsa.basic-forms/1";
export const taskCapability = [{ id: "elsa.api.user-tasks", contractVersion: "1" }];
async function route(api: ElsaStudioModuleApi, relation: string, variables: Record<string, string> = {}) {
  const document = api.backendSession?.capabilities ?? await getBackendCapabilities(api.backend);
  const capability = document.capabilities.find(item => item.id === "elsa.api.user-tasks" && item.contractVersion === "1" && item.status !== "dormant");
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
export async function targets(api: ElsaStudioModuleApi, skip = 0) { return api.backend.http.getJson<TaskTargets>(`${await route(api, "targets")}?skip=${skip}&take=100`); }
export function statusOf(error: unknown) { return typeof error === "object" && error !== null && "status" in error ? Number(error.status) : 0; }
export function message(error: unknown) { return error instanceof Error ? error.message : "This request could not be completed."; }
