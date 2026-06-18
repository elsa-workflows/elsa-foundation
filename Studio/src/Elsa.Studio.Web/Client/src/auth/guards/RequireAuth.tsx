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
  const loginStartedRef = useRef<{ key: string; login: typeof login } | null>(null);

  useEffect(() => {
    if (session.status === "anonymous") {
      const loginKey = getLoginKey(loginOptions);
      const currentLogin = loginStartedRef.current;
      if (currentLogin?.key === loginKey && currentLogin.login === login) {
        return;
      }

      const loginAttempt = { key: loginKey, login };
      loginStartedRef.current = loginAttempt;
      void login(loginOptions).catch(error => {
        if (loginStartedRef.current === loginAttempt) {
          loginStartedRef.current = null;
        }
        console.error("Auth login failed.", error);
      });
    } else {
      loginStartedRef.current = null;
    }
  }, [login, loginOptions, session.status]);

  if (session.status !== "authenticated") {
    return <>{fallback}</>;
  }

  return <>{children}</>;
}

function getLoginKey(options?: LoginOptions) {
  return `${options?.providerId ?? ""}\n${options?.returnUrl ?? ""}`;
}
