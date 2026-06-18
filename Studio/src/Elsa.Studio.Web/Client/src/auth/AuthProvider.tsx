import React, { useCallback, useEffect, useMemo, useState } from "react";
import { AuthContext } from "./AuthContext";
import { unknownAuthSession, type AuthCapabilities, type AuthProviderProps, type AuthSession, type LoginOptions } from "./types";

export function AuthProvider({ manager, children }: AuthProviderProps) {
  const [session, setSession] = useState<AuthSession>(() => manager.getSession() ?? unknownAuthSession);
  const [capabilities, setCapabilities] = useState<AuthCapabilities | null>(null);

  useEffect(() => {
    let disposed = false;

    async function initialize() {
      const nextSession = await manager.initialize();
      if (disposed) {
        return;
      }

      setSession(nextSession);
      if (nextSession.status === "authenticated") {
        const nextCapabilities = await manager.getCapabilities();
        if (!disposed) {
          setCapabilities(nextCapabilities);
        }
      }
    }

    void initialize();

    return () => {
      disposed = true;
    };
  }, [manager]);

  const login = useCallback((options?: LoginOptions) => manager.login(options), [manager]);

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
