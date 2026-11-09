import { createContext } from "react";
import type { AuthContextValue } from "../../sdk";

/** Stable Studio-host identity, independent of the currently selected backend identity. */
export const HostAuthContext = createContext<AuthContextValue | null>(null);
