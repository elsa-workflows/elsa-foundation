import { createContext, useContext } from "react";
import type { StudioActivityDefinitionImplementationState } from "@elsa-workflows/studio-sdk";
import type { NativeBlockValidation } from "./NativeBlockCommands";

/** Resource-owned state outside the graph is required to validate a complete Activity Definition proposal. */
const NativeBlockAuthorityContext = createContext<{
  sourceKey: string;
  canEdit: boolean;
  canApply(): boolean;
  validate(candidate: StudioActivityDefinitionImplementationState): Promise<NativeBlockValidation>;
} | null>(null);
export const NativeBlockAuthorityProvider = NativeBlockAuthorityContext.Provider;
export const useNativeBlockAuthority = () => useContext(NativeBlockAuthorityContext);
