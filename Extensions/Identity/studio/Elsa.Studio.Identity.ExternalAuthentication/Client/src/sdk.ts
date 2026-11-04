/** React-independent client. All requests use the caller's selected backend transport. */
export interface ExternalAuthenticationTransport {
  getJson<T>(path: string, init?: RequestInit): Promise<T>;
  postJson<T>(path: string, body: unknown, init?: RequestInit): Promise<T>;
}
export interface SecretReference { name: string; typeName?: string | null; scope?: string | null }
export interface AdmissionPolicy { type: string; defaultRoleIds: string[]; matcher?: string | null }
export interface ConnectionDocument {
  id: string; key: string; displayName: string; protocol: string;
  settings: Record<string, string>; secrets: Record<string, SecretReference>; admission: AdmissionPolicy;
  enabled: boolean; preferred: boolean; displayOrder: number; overridesDeployment: boolean; archivedAt?: string | null;
}
export interface Readiness { ready: boolean; failure: number; protocolFailure: number }
export interface ConnectionView {
  document: ConnectionDocument; source: 0 | 1; effective: boolean; shadowedById?: string | null;
  overridePermitted: boolean; materialRevision: string; configurationEditable: boolean; configurationSupported: boolean;
  signInCallback?: string | null; previewCallback?: string | null; readiness?: Readiness | null;
}
export interface ConnectionCatalog {
  tenantId: string; protocolPath: string; revision: string | null; databaseManagement: boolean; canManageConnections: boolean;
  provisioningPolicies: string[];
  capabilities: { canManageUsers: boolean; canManageRoles: boolean; canMapGroups: boolean; canRevokeTokens: boolean };
  connections: ConnectionView[];
}
export const connectionChanges = {
  create: 0, update: 1, createOverride: 2, promoteOverride: 3, enable: 4, disable: 5, archive: 6, restore: 7, remove: 8
} as const;
export type ConnectionChange = typeof connectionChanges[keyof typeof connectionChanges];
export interface ConnectionCommand { change: ConnectionChange; expectedRevision: string | null; record: ConnectionDocument }
export interface ConnectionAssessment { canApply: boolean; failure?: string | null; proposed?: ConnectionCatalog | null }
export interface ConnectionChanged { saved: boolean; revision?: string | null; recordId?: string | null; failure?: string | null }
export interface ConnectionTest {
  connectionRecordId: string; materialRevision: string; observedAt: string; readiness: Readiness; protocolFailure: number; stale: boolean;
}
export interface ConnectionPreview {
  connectionKey: string; connectionRecordId: string; materialRevision: string; issuer: string; subject: string;
  admission: number; existingUserId?: string | null; userName?: string | null; displayName?: string | null; email?: string | null;
  roleIds: string[]; permissions: string[]; admissionFailure: number; observedAt: string; stale: boolean;
}
export interface RoleChoice { id: string; name: string; canDelegate: boolean }
export interface SecretChoice { name: string; displayName: string | null; typeName: "text"; scope: string | null }
export interface PreviewSubmission { action: string; fields: Record<string, string> }
const management = "/_elsa/identity/external-connections";
export class ExternalAuthenticationClientError extends Error { }

export class ExternalAuthenticationClient {
  private protocolPath: string | undefined;
  constructor(private readonly transport: ExternalAuthenticationTransport, readonly baseUrl: string) {
    const base = new URL(baseUrl);
    if (!/^https?:$/.test(base.protocol) || base.username || base.password || base.search || base.hash)
      throw new ExternalAuthenticationClientError("Select a valid backend before managing external sign-in.");
  }
  async list(signal?: AbortSignal): Promise<ConnectionCatalog> {
    const result = await this.transport.getJson<ConnectionCatalog>(management, { signal });
    this.protocolPath = protocolPath(result.protocolPath);
    return result;
  }
  detail(id: string, signal?: AbortSignal) {
    return this.transport.getJson<{ revision: string | null; connection: ConnectionView }>(`${management}/${encodeURIComponent(id)}`, { signal });
  }
  assess(command: ConnectionCommand, signal?: AbortSignal) { return this.post<ConnectionAssessment>("/assess", command, signal); }
  apply(command: ConnectionCommand, signal?: AbortSignal) { return this.post<ConnectionChanged>("/commands", command, signal); }
  test(id: string, revision: string | null, signal?: AbortSignal) {
    return this.post<ConnectionTest>("/test", { connectionRecordId: id, expectedRevision: revision }, signal);
  }
  roles(skip = 0, signal?: AbortSignal) {
    return this.transport.getJson<{ items: RoleChoice[]; totalCount: number }>(`${management}/lookups/roles?skip=${skip}&take=100`, { signal });
  }
  async secrets(search: string, signal?: AbortSignal): Promise<{ items: SecretChoice[]; capped: boolean }> {
    const result = await this.transport.postJson<{ items?: unknown[] }>("/secrets/picker",
      { search: search.trim(), typeNames: ["text"], storeNames: [], activeOnly: true }, { signal });
    const raw = Array.isArray(result.items) ? result.items : [];
    const items = raw.flatMap(item => {
      if (!item || typeof item !== "object") return [];
      const value = item as Record<string, unknown>;
      if (typeof value.name !== "string" || !value.name.trim() || value.typeName !== "text" ||
        typeof value.status !== "string" || value.status.toLowerCase() !== "active") return [];
      if (value.expiresAt != null && (typeof value.expiresAt !== "string" || !Number.isFinite(Date.parse(value.expiresAt)) || Date.parse(value.expiresAt) <= Date.now())) return [];
      // Resolved values and arbitrary picker fields are never retained in UI state.
      return [{ name: value.name, displayName: typeof value.displayName === "string" ? value.displayName : null,
        typeName: "text" as const, scope: typeof value.scope === "string" ? value.scope : null }];
    });
    return { items, capped: raw.length >= 50 };
  }
  async previewSubmission(view: ConnectionView, returnUrl: string, signal?: AbortSignal): Promise<PreviewSubmission> {
    if (!view.previewCallback?.endsWith("/callback")) throw new ExternalAuthenticationClientError("Configure the public backend callback URL before previewing.");
    const action = new URL(view.previewCallback.slice(0, -"/callback".length));
    const base = new URL(this.baseUrl);
    const prefix = base.pathname.replace(/\/+$/, "") + "/";
    if (action.origin !== base.origin || action.username || action.password || !action.pathname.startsWith(prefix) || action.search || action.hash)
      throw new ExternalAuthenticationClientError("The preview callback must belong to the selected backend.");
    const token = await this.csrf(signal, true);
    return { action: action.href, fields: { [token.formFieldName]: token.requestToken, connectionRecordId: view.document.id, returnUrl } };
  }
  async readPreview(key: string, handle: string, signal?: AbortSignal) {
    if (!/^[a-z0-9][a-z0-9._-]{0,79}$/.test(key) || !/^[a-zA-Z0-9_-]{43,128}$/.test(handle))
      throw new ExternalAuthenticationClientError("The preview result is invalid or expired. Start another preview.");
    await this.ensureProtocol(signal);
    return this.transport.getJson<ConnectionPreview>(`${this.protocolPath}/preview/${encodeURIComponent(key)}/results/${encodeURIComponent(handle)}`,
      { signal, credentials: "include" });
  }
  private async post<T>(suffix: string, value: unknown, signal?: AbortSignal): Promise<T> {
    const token = await this.csrf(signal);
    if (!token.headerName) throw new ExternalAuthenticationClientError("The backend must enable antiforgery request headers before changing connections.");
    return this.transport.postJson<T>(management + suffix, value, { signal, credentials: "include", headers: { [token.headerName]: token.requestToken } });
  }
  private async ensureProtocol(signal?: AbortSignal) { if (this.protocolPath === undefined) await this.list(signal); }
  private async csrf(signal?: AbortSignal, preview = false) {
    await this.ensureProtocol(signal);
    const result = await this.transport.getJson<{ requestToken: string; formFieldName: string; headerName: string | null }>(`${this.protocolPath}/antiforgery${preview ? "?purpose=preview" : ""}`, { signal, credentials: "include" });
    if (typeof result.requestToken !== "string" || !result.requestToken || typeof result.formFieldName !== "string" || !result.formFieldName ||
      (result.headerName !== null && typeof result.headerName !== "string")) throw new ExternalAuthenticationClientError("The backend could not prepare a protected request. Refresh and retry.");
    return result;
  }
}

function protocolPath(value: unknown): string {
  if (value === "") return "";
  if (typeof value !== "string" || !value.startsWith("/") || value.startsWith("//") || /[\\?#\s]/.test(value))
    throw new ExternalAuthenticationClientError("The backend did not provide valid external sign-in routes.");
  const path = value.replace(/\/+$/, "");
  for (const segment of path.split("/")) {
    let decoded = segment;
    for (let index = 0; index < 3; index++) { try { decoded = decodeURIComponent(decoded); } catch { throw new ExternalAuthenticationClientError("Invalid external sign-in route."); } }
    if (decoded === "." || decoded === ".." || decoded.includes("/") || decoded.includes("\\")) throw new ExternalAuthenticationClientError("Invalid external sign-in route.");
  }
  return path;
}

/** Only closed application codes/statuses are shown; upstream bodies are never rendered. */
export function externalAuthenticationError(error: unknown): string {
  if (error instanceof ExternalAuthenticationClientError) return error.message;
  const value = error && typeof error === "object" ? error as { status?: number; payload?: { failure?: string; error?: string } } : {};
  const code = value.payload?.failure ?? value.payload?.error;
  const messages: Record<string, string> = {
    "final-login-path": "Keep another usable administrator sign-in method before making this change.",
    "revision-conflict": "Connections changed on the server. Refresh and review the latest configuration before retrying.",
    "role-delegation-forbidden": "Choose existing Elsa roles within your permission and deployment limits.",
    "deployment-override-forbidden": "This deployment does not permit an override for this connection.",
    "database-management-disabled": "Connections are managed by this deployment and are read-only here.",
    "connection-not-ready": "Complete the connection settings, secret reference and provisioning policy before enabling it.",
    "provider-test-timeout": "The provider did not respond in time. Check its availability and retry.",
    "antiforgery-required": "Your request protection expired. Refresh and retry."
  };
  if (typeof code === "string" && Object.hasOwn(messages, code)) return messages[code];
  switch (value.status) {
    case 401: return "Sign in to this backend again, then retry.";
    case 403: return "You do not have permission to perform this action on the selected backend.";
    case 404: return "This connection or the external sign-in module is unavailable on the selected backend.";
    case 409: return "Connections changed or another usable sign-in method is required. Refresh and review the change.";
    case 413: return "The connection document is too large. Reduce its settings or role selections.";
    case 503: return "The backend cannot provide external sign-in management right now. Check its configuration and retry.";
    default: return "The request could not be completed. Check the connection and retry.";
  }
}
