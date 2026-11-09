import { useOptionalAuthContext } from "@elsa-workflows/studio-sdk";
import { useHostControlAccess } from "../useHostControlAccess";
import type { NavigationAccess } from "./placement";

export function useShellNavigationAccess(enabledFeatures?: ReadonlySet<string> | readonly string[]): NavigationAccess {
  const auth = useOptionalAuthContext();
  const hostAccess = useHostControlAccess();
  const session = auth?.session;
  return {
    enabledFeatures,
    roles: session?.roles,
    permissions: session?.permissions,
    authenticated: session?.status === "authenticated",
    // Anonymous/demo: host-control read is true because the server allows admin calls when sign-in is off.
    canReadAdmin: hostAccess.canReadModuleManagement
  };
}
