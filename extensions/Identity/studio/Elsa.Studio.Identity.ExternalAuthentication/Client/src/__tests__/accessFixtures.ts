import type { IdentityLink, IdentityLinks, IdentitySession, IdentitySessions, IdentityUsers } from "../sdk";

export const users: IdentityUsers = { tenantId: "default", protocolPath: "/sso", ownershipMode: "FoundationOwned", canManageUsers: true,
  canRevokeTokens: true, canReadLinks: true, canReadSessions: true, linkManagementUnavailableReason: null, sessionAdministrationUnavailableReason: null,
  items: [{ id: "user-1", userName: "alice", displayName: "Alice", status: 0 }, { id: "user-2", userName: "bob", displayName: "Bob", status: 1 }], totalCount: 2 };
export const link: IdentityLink = { provider: "company", issuer: "https://idp.example", subject: "opaque-subject", linkedAt: "2026-11-04T10:00:00Z",
  lastSeenAt: null, revision: "l1", qualified: true, connectionConfigured: true, connectionDisplayName: "Company", canRemove: true };
export const links: IdentityLinks = { userId: "user-1", items: [link], totalCount: 1, userRevision: "u1", catalogRevision: "c1", canAttach: true,
  attachChoices: [{ id: "deployment:company", key: "company", displayName: "Company", effective: true, enabled: true }] };
export const session: IdentitySession = { id: "a".repeat(64), userId: "user-1", provider: "external:company", connectionKey: "company",
  connectionRecordId: "deployment:company", issuer: "https://idp.example", createdAt: "2026-11-04T10:00:00Z", expiresAt: "2026-11-04T18:00:00Z",
  revokedAt: null, state: "active", revision: "s1", current: false, canRevoke: true };
export const sessions: IdentitySessions = { userId: "user-1", items: [session], totalCount: 1 };
