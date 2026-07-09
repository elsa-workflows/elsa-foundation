import { StudioHttpError } from "../sdk";

// Host-control permission keys (ADR 0037 / #249). These mirror the backend feature-contributed identity permissions
// (Elsa.Modularity.Api.Authorization.ModuleManagementPermissionKeys and
// Elsa.Modularity.ExtensionBuilder.Authorization.ExtensionBuilderPermissionKeys) and the Studio bridge policies that
// gate the management surfaces. The frontend uses them with `usePermissions` to PROACTIVELY hide/disable affordances a
// user cannot use; the server-side bridge check remains the authority (an out-of-date session never grants access).
export const hostControlPermissions = {
  moduleManagementRead: "module-management.read",
  moduleManagementManage: "module-management.manage",
  extensionBuilderRead: "extension-builder.read",
  extensionBuilderManage: "extension-builder.manage"
} as const;

// A management-surface read a user may hold the read permission for satisfies it directly; a `manage` holder satisfies
// the read too (the backend catalog declares manage ⇒ read, and Studio expands it locally in usePermissions terms).
export const moduleManagementReadKeys = [
  hostControlPermissions.moduleManagementRead,
  hostControlPermissions.moduleManagementManage
] as const;

export const extensionBuilderReadKeys = [
  hostControlPermissions.extensionBuilderRead,
  hostControlPermissions.extensionBuilderManage
] as const;

// True when a failed request was a Studio authorization failure (403): the caller is signed in but lacks the required
// host-control permission. This is DISTINCT from a 401 (unauthenticated — the http layer refreshes/redirects) and from
// the bridge's backend-status states (unconfigured/unreachable/unauthorized/degraded, which the bridge reports as data,
// not as an HTTP error). Callers map this to an explicit "you lack permission" surface, never to "backend unavailable".
export function isPermissionDenied(error: unknown): boolean {
  return error instanceof StudioHttpError && error.status === 403;
}
