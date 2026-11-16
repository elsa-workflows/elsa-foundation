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
  managementUnavailableReason?: "identity-coordination-unavailable" | null;
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
export interface IdentityUser { id: string; userName: string; displayName: string | null; status: number }
export interface IdentityUsers {
  tenantId: string; protocolPath: string; ownershipMode: string; canManageUsers: boolean; canRevokeTokens: boolean;
  canReadLinks: boolean; canReadSessions: boolean; linkManagementUnavailableReason: string | null;
  sessionAdministrationUnavailableReason: string | null; items: IdentityUser[]; totalCount: number;
}
export interface IdentityLink {
  provider: string; issuer: string | null; subject: string; linkedAt: string; lastSeenAt: string | null; revision: string | null;
  qualified: boolean; connectionConfigured: boolean; connectionDisplayName: string | null; canRemove: boolean;
}
export interface IdentityLinks {
  userId: string; items: IdentityLink[]; totalCount: number; userRevision: string | null; catalogRevision: string | null; canAttach: boolean;
  attachChoices: { id: string; key: string; displayName: string; effective: boolean; enabled: boolean }[];
}
export interface IdentitySession {
  id: string; userId: string; provider: string; connectionKey: string | null; connectionRecordId: string | null; issuer: string | null;
  createdAt: string; expiresAt: string; revokedAt: string | null; state: "active" | "inactive" | "expired" | "revoked";
  revision: string; current: boolean; canRevoke: boolean;
}
export interface IdentitySessions { userId: string; items: IdentitySession[]; totalCount: number }
export interface LinkAttachment { connectionRecordId: string; subject: string; expectedCatalogRevision: string | null; expectedUserRevision: string }
export interface LinkRemoval { provider: string; issuer: string; subject: string; expectedLinkRevision: string; expectedUserRevision: string; expectedCatalogRevision: string | null }
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
  async users(search = "", skip = 0, signal?: AbortSignal): Promise<IdentityUsers> {
    const result = await this.transport.getJson<IdentityUsers>(`${management}/users?skip=${skip}&take=25&search=${encodeURIComponent(search.trim())}`, { signal });
    boundedPage(result);
    this.protocolPath = protocolPath(result.protocolPath);
    return result;
  }
  async links(userId: string, skip = 0, signal?: AbortSignal) {
    const result = await this.transport.getJson<IdentityLinks>(`${management}/users/${identitySegment(userId)}/links?skip=${skip}&take=25`, { signal });
    boundedPage(result);
    if (result.userId !== userId) throw new ExternalAuthenticationClientError("The backend returned access for a different user. Refresh user access.");
    return result;
  }
  async sessions(userId: string, skip = 0, signal?: AbortSignal) {
    const result = await this.transport.getJson<IdentitySessions>(`${management}/users/${identitySegment(userId)}/sessions?skip=${skip}&take=25`, { signal });
    boundedPage(result);
    if (result.userId !== userId || result.items.some(item => item.userId !== userId))
      throw new ExternalAuthenticationClientError("The backend returned sessions for a different user. Refresh user access.");
    return result;
  }
  attach(userId: string, request: LinkAttachment, signal?: AbortSignal) {
    return this.postIdentity<{ attached: boolean; failure: string | null }>(`/users/${identitySegment(userId)}/links/attach`, request, signal);
  }
  removeLink(userId: string, request: LinkRemoval, signal?: AbortSignal) {
    return this.postIdentity<{ removed: boolean; failure: string | null }>(`/users/${identitySegment(userId)}/links/remove`, request, signal);
  }
  revokeSession(userId: string, session: IdentitySession, signal?: AbortSignal) {
    return this.postIdentity<{ revoked: boolean; alreadyRevoked: boolean; failure: string | null }>(
      `/users/${identitySegment(userId)}/sessions/${identitySegment(session.id)}/revoke`, { expectedRevision: session.revision }, signal);
  }
  private async postIdentity<T>(suffix: string, value: unknown, signal?: AbortSignal): Promise<T> {
    if (this.protocolPath === undefined) await this.users("", 0, signal);
    return this.post<T>(suffix, value, signal);
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

function identitySegment(value: string): string {
  if (!value || value.length > 400 || value === "." || value === ".." || [...value].some(character => character.charCodeAt(0) < 32 || character.charCodeAt(0) === 127))
    throw new ExternalAuthenticationClientError("Select a valid user or session before continuing.");
  return encodeURIComponent(value);
}

function boundedPage(value: { items: unknown[]; totalCount: number }) {
  if (!value || !Array.isArray(value.items) || value.items.length > 25 || !Number.isSafeInteger(value.totalCount) || value.totalCount < value.items.length)
    throw new ExternalAuthenticationClientError("The backend returned an invalid user-access page. Refresh and retry.");
}

/** User access has a separate permission boundary from connection configuration. */
export function identityAccessError(error: unknown): string {
  const value = error && typeof error === "object" ? error as { status?: number; payload?: { failure?: string; error?: string } } : {};
  const code = value.payload?.failure ?? value.payload?.error;
  const messages: Record<string, string> = {
    "final-login-path": "Keep a usable sign-in method for this user and an administrator. Sign in through the remaining method before trying removal again.",
    "revision-conflict": "User access or connections changed. Refresh user access and review the latest state before retrying.",
    "link-delegation-forbidden": "Your access or the identity authority does not permit changing this user's links.",
    "link-administration-unavailable": "This host cannot safely change identity links. Ask the host administrator to finish configuring identity management.",
    "session-administration-unavailable": "Session administration is unavailable on this host. Ask the host administrator to enable it.",
    "link-removal-unavailable": "Removal could not be confirmed. Refresh user access to check the current links before retrying.",
    "link-attachment-unavailable": "Attachment could not be confirmed. Refresh user access to check the current links before retrying.",
    "session-revocation-failed": "Revocation could not be confirmed. Refresh user access before retrying.",
    "link-conflict": "This external identity already belongs to a user or changed during attachment. Refresh and review the links.",
    "link-not-found": "This identity link no longer belongs to the selected user. Refresh user access.",
    "session-not-found": "This session no longer belongs to the selected user. Refresh user access."
  };
  if (typeof code === "string" && Object.hasOwn(messages, code)) return messages[code];
  if (value.status === 403) return "You do not have permission to inspect or change this user access on the selected backend.";
  if (value.status === 404) return "This user or the user-access module is unavailable on the selected backend.";
  return externalAuthenticationError(error);
}

/** Only closed application codes/statuses are shown; upstream bodies are never rendered. */
export function externalAuthenticationError(error: unknown): string {
  if (error instanceof ExternalAuthenticationClientError) return error.message;
  const value = error && typeof error === "object" ? error as { status?: number; payload?: { failure?: string; error?: string } } : {};
  const code = value.payload?.failure ?? value.payload?.error;
  const messages: Record<string, string> = {
    "final-login-path": "Keep another usable administrator sign-in method before making this change.",
    "revision-conflict": "Connections changed on the server. Refresh and review the latest configuration before retrying.",
    "identity-authority-changed": "User access or role definitions changed. Refresh and review this change before retrying.",
    "identity-coordination-unavailable": "Connection changes are unavailable on this host. Ask the host administrator to finish configuring identity management.",
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
