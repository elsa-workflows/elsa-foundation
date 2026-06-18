import React, { useCallback, useEffect, useMemo, useState } from "react";
import { AuthContext } from "./AuthContext";
import { anonymousAuthSession, unknownAuthSession, type AuthCapabilities, type AuthProviderProps, type AuthSession, type LoginOptions } from "./types";

export function AuthProvider({ manager, children }: AuthProviderProps) {
  const [session, setSession] = useState<AuthSession>(() => manager.getSession() ?? unknownAuthSession);
  const [capabilities, setCapabilities] = useState<AuthCapabilities | null>(null);

  useEffect(() => {
    let disposed = false;

    async function initialize() {
      try {
        const nextSession = await manager.initialize();
        if (disposed) {
          return;
        }

        setSession(nextSession);
        if (nextSession.status !== "authenticated") {
          setCapabilities(null);
          return;
        }

        try {
          const nextCapabilities = await manager.getCapabilities();
          if (!disposed) {
            setCapabilities(nextCapabilities);
          }
        } catch (error) {
          if (!disposed) {
            console.error("Auth capabilities request failed.", error);
            setCapabilities(null);
          }
        }
      } catch (error) {
        if (!disposed) {
          console.error("Auth initialization failed.", error);
          setSession(anonymousAuthSession);
          setCapabilities(null);
        }
      }
    }

    void initialize();

    return () => {
      disposed = true;
    };
  }, [manager]);

  const login = useCallback(async (options?: LoginOptions) => {
    await manager.login(options);
    const nextSession = manager.getSession();
    setSession(nextSession);
    if (nextSession.status === "authenticated") {
      try {
        setCapabilities(await manager.getCapabilities());
      } catch (error) {
        console.error("Auth capabilities request failed.", error);
        setCapabilities(null);
      }
    } else {
      setCapabilities(null);
    }
  }, [manager]);

  const logout = useCallback(async () => {
    await manager.logout();
    setSession(manager.getSession());
    setCapabilities(null);
  }, [manager]);

  const refresh = useCallback(async () => {
    const nextSession = await manager.refresh();
    setSession(nextSession);
    if (nextSession.status === "authenticated") {
      setCapabilities(await manager.getCapabilities());
    } else {
      setCapabilities(null);
    }
    return nextSession;
  }, [manager]);

  const value = useMemo(() => ({
    session,
    capabilities,
    login,
    logout,
    refresh
  }), [capabilities, login, logout, refresh, session]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
