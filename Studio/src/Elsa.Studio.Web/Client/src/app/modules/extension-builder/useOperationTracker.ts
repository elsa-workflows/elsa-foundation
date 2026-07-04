import { useCallback, useState } from "react";
import { getErrorMessage } from "../moduleManagementApi";

// Operations are grouped into independent scopes so that running one action (e.g. staging a file)
// does not disable unrelated actions (e.g. Save or Build). Each scope tracks its own in-flight
// count; the previous single global `operationBusy` flag disabled every action at once.
export type OperationScope = "home" | "explorer" | "save" | "build" | "source" | "promote" | "runtime";

export interface OperationTracker {
  status: string | null;
  error: string | null;
  setStatus(value: string | null): void;
  setError(value: string | null): void;
  // True while any operation in the given scope is running.
  isBusy(scope: OperationScope): boolean;
  // True while any operation at all is running (used only for hard, page-wide guards).
  anyBusy: boolean;
  // Runs `operation` under `scope`, mirroring the previous runOperation semantics (clears error/
  // status, sets success status, surfaces errors) but scoping the busy flag to `scope`.
  runOperation<T>(scope: OperationScope, operation: () => Promise<T>, success: string): Promise<T | null>;
}

export function useOperationTracker(): OperationTracker {
  const [busyCounts, setBusyCounts] = useState<Partial<Record<OperationScope, number>>>({});
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const adjust = useCallback((scope: OperationScope, delta: number) => {
    setBusyCounts(current => {
      const next = (current[scope] ?? 0) + delta;
      const updated = { ...current };
      if (next <= 0) delete updated[scope];
      else updated[scope] = next;
      return updated;
    });
  }, []);

  const runOperation = useCallback(async <T,>(scope: OperationScope, operation: () => Promise<T>, success: string): Promise<T | null> => {
    adjust(scope, 1);
    setError(null);
    setStatus(null);
    try {
      const result = await operation();
      setStatus(success);
      return result;
    } catch (e) {
      setError(getErrorMessage(e));
      return null;
    } finally {
      adjust(scope, -1);
    }
  }, [adjust]);

  const isBusy = useCallback((scope: OperationScope) => (busyCounts[scope] ?? 0) > 0, [busyCounts]);
  const anyBusy = Object.keys(busyCounts).length > 0;

  return { status, error, setStatus, setError, isBusy, anyBusy, runOperation };
}
