import { createContext, useContext } from "react";
import type { AuthContextValue } from "./types";

export const AuthContext = createContext<AuthContextValue | null>(null);

export function useAuthContext() {
  const value = useContext(AuthContext);
  if (!value) {
    throw new Error("Auth SDK hooks must be used within <AuthProvider>.");
  }

  return value;
}
