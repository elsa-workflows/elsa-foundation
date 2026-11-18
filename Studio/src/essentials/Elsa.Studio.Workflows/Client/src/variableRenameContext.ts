import { createContext, useContext } from "react";

/**
 * Lets a variable editor report a committed rename to the workflow editor, which plans and applies the script follow-up
 * (spec 1222 FR-021). `scopeId` is the declaring scope of the variables the nearest editor shows: the workflow sentinel
 * or the container node's id. Without a provider, renaming a variable changes only its declaration.
 */
export interface VariableRenameScope {
  scopeId: string;
  onVariableRenamed(scopeId: string, referenceKey: string, oldName: string, newName: string): void;
}

export const VariableRenameContext = createContext<VariableRenameScope | null>(null);

export function useVariableRenameScope(): VariableRenameScope | null {
  return useContext(VariableRenameContext);
}
