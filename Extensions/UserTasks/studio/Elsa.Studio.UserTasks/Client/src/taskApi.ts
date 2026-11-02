import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
export interface TaskField { key: string; label: string; kind: string; required: boolean; readOnly: boolean; help?: string; minimum?: number; maximum?: number; maxLength?: number; choices?: string[] }
export interface TaskForm { id: string; version: string; name: string; provider: string; fields: TaskField[] }
export interface FormVersion { tenantId: string; form: TaskForm; createdAt: string; createdBy: string }
export interface TaskActor { userId: string; canRead: boolean; canAct: boolean; canManage: boolean; canManageForms: boolean; canReadForms: boolean }
export interface TaskItem { id: string; title: string; description?: string; assigneeId?: string; groupId?: string; claimantId?: string; actions: string[]; optionalFormActions: string[]; form?: TaskForm; prefill: Record<string, unknown>; status: string; revision: number; history: { operation: string; actorId: string; at: string; action?: string }[]; decision?: { response: { action: string; data: Record<string, unknown> }; actorId: string } }
export interface TaskPage { items: TaskItem[]; next?: string | null }
export interface TaskTargets { users: { id: string; name: string }[]; groups: { id: string; name: string }[]; moreUsers: boolean; moreGroups: boolean }
export const basicProvider = "elsa.basic-forms/1";
export const taskCapability = [{ id: "elsa.api.user-tasks", contractVersion: "1" }];
export const taskPath = (id: string) => `/runtime/user-tasks/${encodeURIComponent(id)}`;
export function actor(api: ElsaStudioModuleApi) { return api.backend.http.getJson<TaskActor>("/runtime/user-task-identity"); }
export function list(api: ElsaStudioModuleApi, view: string, after?: string | null, search?: string) {
  const query = new URLSearchParams({ view, limit: "25" });
  if (after) query.set("after", after); if (search) query.set("search", search);
  return api.backend.http.getJson<TaskPage>(`/runtime/user-tasks?${query}`);
}
export function read(api: ElsaStudioModuleApi, id: string) { return api.backend.http.getJson<TaskItem>(taskPath(id)); }
export function mutate(api: ElsaStudioModuleApi, task: TaskItem, operation: string, request: Record<string, unknown>) {
  return api.backend.http.postJson<TaskItem>(`${taskPath(task.id)}/${operation}`, { revision: task.revision, ...request });
}
export interface FormPage { items: FormVersion[]; next?: string | null }
export function forms(api: ElsaStudioModuleApi, after?: string | null) { return api.backend.http.getJson<FormPage>(`/runtime/user-task-forms?limit=100${after ? `&after=${encodeURIComponent(after)}` : ""}`); }
export function targets(api: ElsaStudioModuleApi, skip = 0) { return api.backend.http.getJson<TaskTargets>(`/runtime/user-task-targets?skip=${skip}&take=100`); }
export function statusOf(error: unknown) { return typeof error === "object" && error !== null && "status" in error ? Number(error.status) : 0; }
export function message(error: unknown) { return error instanceof Error ? error.message : "This request could not be completed."; }
