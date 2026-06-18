import React, { useEffect, useRef } from "react";
import { useAuthContext } from "../AuthContext";
import type { LoginOptions } from "../types";

export interface RequireAuthProps {
  children: React.ReactNode;
  fallback?: React.ReactNode;
  loginOptions?: LoginOptions;
}

export function RequireAuth({ children, fallback = null, loginOptions }: RequireAuthProps) {
  const { session, login } = useAuthContext();
  const loginStartedRef = useRef(false);

  useEffect(() => {
    if (session.status === "anonymous") {
      if (loginStartedRef.current) {
        return;
      }

      loginStartedRef.current = true;
      void login(loginOptions).catch(error => {
        loginStartedRef.current = false;
        console.error("Auth login failed.", error);
      });
    } else {
      loginStartedRef.current = false;
    }
  }, [login, loginOptions, session.status]);

  if (session.status !== "authenticated") {
    return <>{fallback}</>;
  }

  return <>{children}</>;
}
