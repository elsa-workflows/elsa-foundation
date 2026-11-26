import React from "react";
import { usePermissions } from "../usePermissions";

export interface AuthGuardProps {
  requires?: string | readonly string[];
  requireAll?: boolean;
  fallback?: React.ReactNode;
  children: React.ReactNode;
}

export function AuthGuard({ requires, requireAll = true, fallback = null, children }: AuthGuardProps) {
  const permissions = usePermissions();
  const requiredPermissions = typeof requires === "string"
    ? [requires]
    : requires ?? [];
  const authorized = requiredPermissions.length === 0
    || (requireAll ? permissions.hasAll(requiredPermissions) : permissions.hasAny(requiredPermissions));

  return authorized ? <>{children}</> : <>{fallback}</>;
}
