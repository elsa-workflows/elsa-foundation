import { useAuthContext } from "./AuthContext";

export function useAuthCapabilities() {
  return useAuthContext().capabilities;
}
