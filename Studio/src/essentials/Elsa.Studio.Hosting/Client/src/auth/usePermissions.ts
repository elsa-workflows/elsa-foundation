import { useMemo } from "react";
import { useAuthSession } from "./useAuthSession";
import type { PermissionSet } from "./types";

export function usePermissions(): PermissionSet {
  const { permissions } = useAuthSession();

  return useMemo(() => {
    const permissionSet = new Set(permissions);

    return {
      has: key => permissionSet.has(key),
      hasAny: keys => keys.some(key => permissionSet.has(key)),
      hasAll: keys => keys.every(key => permissionSet.has(key))
    };
  }, [permissions]);
}
