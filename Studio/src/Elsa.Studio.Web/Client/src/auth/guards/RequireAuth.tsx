import React, { useEffect } from "react";
import { useAuthContext } from "../AuthContext";
import type { LoginOptions } from "../types";

export interface RequireAuthProps {
  children: React.ReactNode;
  fallback?: React.ReactNode;
  loginOptions?: LoginOptions;
}

export function RequireAuth({ children, fallback = null, loginOptions }: RequireAuthProps) {
  const { session, login } = useAuthContext();

  useEffect(() => {
    if (session.status === "anonymous") {
      void login(loginOptions);
    }
  }, [login, loginOptions, session.status]);

  if (session.status !== "authenticated") {
    return <>{fallback}</>;
  }

  return <>{children}</>;
}
