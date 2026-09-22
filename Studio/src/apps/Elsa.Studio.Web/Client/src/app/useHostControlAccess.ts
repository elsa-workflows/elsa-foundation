import { useContext, useMemo } from "react";
import { AuthContext } from "../auth/AuthContext";
import {
  hostControlPermissions,
  moduleManagementReadKeys,
  extensionBuilderReadKeys
} from "./hostControlPermissions";

// Whether the signed-in user may perform each host-control action, from their session permissions. Used to PROACTIVELY
// hide/disable affordances the user can't use; the server-side bridge gate remains the authority (#249, ADR 0037).
//
// Demo/auth-disabled posture: when Studio auth is disabled the session is never "authenticated" (it stays anonymous),
// the bridge allows every request anonymously, and the session carries no permissions. Gating purely on permission
// presence would then wrongly hide affordances in demo mode, so every check short-circuits to `true` unless the session
// is actually authenticated. Only an authenticated user is held to their permission set. The AuthContext is read
// optionally so host-control pages render outside an <AuthProvider> (e.g. isolated tests) with full access.
export interface HostControlAccess {
  canReadModuleManagement: boolean;
  canManageModuleManagement: boolean;
  canReadExtensionBuilder: boolean;
}

export function useHostControlAccess(): HostControlAccess {
  const auth = useContext(AuthContext);

  return useMemo(() => {
    const session = auth?.session;
    const enforced = session?.status === "authenticated";
    const permissions = new Set(session?.permissions ?? []);
    const hasAny = (keys: readonly string[]) => keys.some(key => permissions.has(key));

    return {
      canReadModuleManagement: !enforced || hasAny(moduleManagementReadKeys),
      canManageModuleManagement: !enforced || permissions.has(hostControlPermissions.moduleManagementManage),
      canReadExtensionBuilder: !enforced || hasAny(extensionBuilderReadKeys)
    };
  }, [auth]);
}
