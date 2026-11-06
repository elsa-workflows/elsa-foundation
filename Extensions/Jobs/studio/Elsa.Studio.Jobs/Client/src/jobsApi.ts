import { getBackendCapabilities, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import type { Access, Operation } from "./types";

export const jobsCapability = [{ id: "elsa.api.jobs", contractVersion: "1" }];
export type Relation = "jobs" | "queues" | "schedules" | "holds" | "capacity" | "access" | "operations";
export type Query = Record<string, string | number | null | undefined>;
export class JobsFault extends Error { constructor(readonly status: number) { super("Jobs request refused or unavailable."); } }
export function statusOf(error: unknown) {
  return typeof error === "object" && error !== null && "status" in error ? Number(error.status) : 0;
}
export function failureText(error: unknown, operation = false) {
  switch (statusOf(error)) {
    case 401: return "Sign in again to inspect Jobs.";
    case 403: return "Your current permissions do not allow this operation. Refresh access before continuing.";
    case 404: return "This resource is unavailable or you no longer have access.";
    case 409: return "This operation conflicts with current state or different content. Reload and review the current revision before starting another operation.";
    case 400: return "Check the fields and schema. A continuation belongs to its original user, filters and page size; return to the first page if needed.";
    case 413: return "The request exceeds the supported size. Reduce the input before submitting.";
    case 501: return "This backend does not advertise the required Jobs capability.";
    default: return operation ? "Jobs could not confirm this request. Check current server state and retry the original operation when available." : "Jobs state is unavailable. Check the connection and refresh when the service is available.";
  }
}
export function newOperation(reason: string): Operation {
  if (!reason.trim() || reason.length > 256) throw new JobsFault(400);
  return { operationId: crypto.randomUUID(), reason };
}
export function gateQuery(gate: { kind: string; name: string; modelKind?: string | null }): Query {
  return { kind: gate.kind, name: gate.name, modelKind: gate.modelKind };
}

export class JobsClient {
  constructor(readonly api: ElsaStudioModuleApi) { }
  private async path(relation: Relation, segments: string[] = []) {
    const document = this.api.backendSession?.capabilities ?? await getBackendCapabilities(this.api.backend);
    const capability = document.capabilities.find(item => item.id === "elsa.api.jobs" && item.contractVersion === "1" && item.status !== "dormant");
    const href = capability?.links.find(item => item.rel === relation)?.href;
    if (!href) throw new JobsFault(501);
    // The capability owns the shell-relative collection address; resource IDs are encoded segments.
    if (/[:?#\\]/.test(href) || href.startsWith("//") || href.split("/").some(part => part === ".." || part === ".")) throw new JobsFault(501);
    return "/" + href.replace(/^\/+|\/+$/g, "") + segments.map(value => "/" + encodeURIComponent(value)).join("");
  }
  async read<T>(relation: Relation, segments: string[] = [], query: Query = {}): Promise<T> {
    const values = new URLSearchParams();
    for (const [key, value] of Object.entries(query)) if (value !== undefined && value !== null && value !== "") values.set(key, String(value));
    return this.api.backend.http.getJson<T>((await this.path(relation, segments)) + (values.size ? "?" + values.toString() : ""));
  }
  access() { return this.read<Access>("access"); }
  async write<T>(method: "POST" | "PUT", relation: Relation, segments: string[], body: unknown): Promise<T> {
    const path = await this.path(relation, segments);
    return method === "POST" ? this.api.backend.http.postJson<T>(path, body) : this.api.backend.http.putJson<T>(path, body);
  }
}
