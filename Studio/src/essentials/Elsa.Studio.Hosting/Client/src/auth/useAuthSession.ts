import { useAuthContext } from "./AuthContext";

export function useAuthSession() {
  return useAuthContext().session;
}
