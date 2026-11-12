import React, { useCallback, useEffect, useRef, useState } from "react";
import { useAuthContext } from "../AuthContext";
import type { LoginOptions } from "../types";

export interface RequireAuthProps {
  children: React.ReactNode;
  fallback?: React.ReactNode;
  errorFallback?: (props: RequireAuthErrorFallbackProps) => React.ReactNode;
  loginOptions?: LoginOptions;
}

export interface RequireAuthErrorFallbackProps {
  error: unknown;
  retry(): void;
}

export function RequireAuth({ children, fallback = null, errorFallback, loginOptions }: RequireAuthProps) {
  const { session, login } = useAuthContext();
  const loginStartedRef = useRef<{ key: string; login: typeof login } | null>(null);
  const [loginError, setLoginError] = useState<unknown>(null);
  const [retryAttempt, setRetryAttempt] = useState(0);

  const retry = useCallback(() => {
    loginStartedRef.current = null;
    setLoginError(null);
    setRetryAttempt(attempt => attempt + 1);
  }, []);

  useEffect(() => {
    if (session.status === "anonymous") {
      const loginKey = `${getLoginKey(loginOptions)}\n${retryAttempt}`;
      const currentLogin = loginStartedRef.current;
      if (currentLogin?.key === loginKey && currentLogin.login === login) {
        return;
      }

      const loginAttempt = { key: loginKey, login };
      loginStartedRef.current = loginAttempt;
      setLoginError(null);
      void login(loginOptions).catch(error => {
        if (loginStartedRef.current === loginAttempt) {
          loginStartedRef.current = null;
          setLoginError(error);
        }
        console.error("Auth login failed.", error);
      });
    } else {
      loginStartedRef.current = null;
    }
  }, [login, loginOptions, retryAttempt, session.status]);

  if (session.status !== "authenticated") {
    if (loginError !== null && errorFallback) {
      return <>{errorFallback({ error: loginError, retry })}</>;
    }

    return <>{fallback}</>;
  }

  return <>{children}</>;
}

function getLoginKey(options?: LoginOptions) {
  return `${options?.providerId ?? ""}\n${options?.returnUrl ?? ""}`;
}
