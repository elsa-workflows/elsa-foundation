import React, { useCallback, useLayoutEffect, useMemo, useRef, useState } from "react";
import { AuthContext } from "./AuthContext";
import { anonymousAuthSession, unknownAuthSession, type AuthCapabilities, type AuthProviderProps, type AuthSession, type LoginOptions } from "./types";

export function AuthProvider({ manager, children }: AuthProviderProps) {
  const [session, setSession] = useState<AuthSession>(() => manager.getSession() ?? unknownAuthSession);
  const [capabilities, setCapabilities] = useState<AuthCapabilities | null>(null);
  const mountedRef = useRef(false);
  const authOperationRef = useRef(0);

  const shouldCommit = useCallback((operation: number) => mountedRef.current && authOperationRef.current === operation, []);

  useLayoutEffect(() => {
    mountedRef.current = true;
    const operation = ++authOperationRef.current;

    async function initialize() {
      try {
        const nextSession = await manager.initialize();
        if (!shouldCommit(operation)) {
          return;
        }

        setSession(nextSession);
        if (nextSession.status !== "authenticated") {
          setCapabilities(null);
          return;
        }

        try {
          const nextCapabilities = await manager.getCapabilities();
          if (shouldCommit(operation)) {
            setCapabilities(nextCapabilities);
          }
        } catch (error) {
          if (shouldCommit(operation)) {
            console.error("Auth capabilities request failed.", error);
            setCapabilities(null);
          }
        }
      } catch (error) {
        if (shouldCommit(operation)) {
          console.error("Auth initialization failed.", error);
          setSession(anonymousAuthSession);
          setCapabilities(null);
        }
      }
    }

    void initialize();

    return () => {
      mountedRef.current = false;
      authOperationRef.current += 1;
    };
  }, [manager, shouldCommit]);

  const login = useCallback(async (options?: LoginOptions) => {
    const operation = ++authOperationRef.current;
    await manager.login(options);
    if (!shouldCommit(operation)) {
      return;
    }

    const nextSession = manager.getSession();
    setSession(nextSession);
    if (nextSession.status === "authenticated") {
      try {
        const nextCapabilities = await manager.getCapabilities();
        if (shouldCommit(operation)) {
          setCapabilities(nextCapabilities);
        }
      } catch (error) {
        if (shouldCommit(operation)) {
          console.error("Auth capabilities request failed.", error);
          setCapabilities(null);
        }
      }
    } else {
      setCapabilities(null);
    }
  }, [manager, shouldCommit]);

  const logout = useCallback(async () => {
    const operation = ++authOperationRef.current;
    await manager.logout();
    if (!shouldCommit(operation)) {
      return;
    }

    setSession(manager.getSession());
    setCapabilities(null);
  }, [manager, shouldCommit]);

  const refresh = useCallback(async () => {
    const operation = ++authOperationRef.current;
    const nextSession = await manager.refresh();
    if (!shouldCommit(operation)) {
      return nextSession;
    }

    setSession(nextSession);
    if (nextSession.status === "authenticated") {
      try {
        const nextCapabilities = await manager.getCapabilities();
        if (shouldCommit(operation)) {
          setCapabilities(nextCapabilities);
        }
      } catch (error) {
        if (shouldCommit(operation)) {
          console.error("Auth capabilities request failed.", error);
          setCapabilities(null);
        }
      }
    } else {
      setCapabilities(null);
    }
    return nextSession;
  }, [manager, shouldCommit]);

  const value = useMemo(() => ({
    session,
    capabilities,
    login,
    logout,
    refresh
  }), [capabilities, login, logout, refresh, session]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
