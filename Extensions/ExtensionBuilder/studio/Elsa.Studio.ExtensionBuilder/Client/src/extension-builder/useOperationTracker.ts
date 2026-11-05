import { useCallback, useEffect, useRef, useState } from "react";
import type { StudioNotificationApi } from "@elsa-workflows/studio-sdk";
import { describeExtensionBuilderError } from "./helpers";

// Operations are grouped into independent scopes so that running one action (e.g. staging a file)
// does not disable unrelated actions (e.g. Save or Build). Each scope tracks its own in-flight
// count; the previous single global `operationBusy` flag disabled every action at once.
export type OperationScope = "home" | "explorer" | "save" | "build" | "source" | "promote" | "runtime";

export interface OperationTracker {
  error: string | null;
  setError(value: string | null): void;
  // True while any operation in the given scope is running.
  isBusy(scope: OperationScope): boolean;
  // True while any operation at all is running (used only for hard, page-wide guards).
  anyBusy: boolean;
  // Runs `operation` under `scope`, toasts completion and keeps errors inline.
  runOperation<T>(scope: OperationScope, operation: () => Promise<T>, success: string | ((result: T) => string)): Promise<T | null>;
}

export function useOperationTracker(notifications?: StudioNotificationApi): OperationTracker {
  const [busyCounts, setBusyCounts] = useState<Partial<Record<OperationScope, number>>>({});
  const mounted = useRef(false);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
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

  const runOperation = useCallback(async <T,>(scope: OperationScope, operation: () => Promise<T>, success: string | ((result: T) => string)): Promise<T | null> => {
    adjust(scope, 1);
    setError(null);
    try {
      const result = await operation();
      const title = typeof success === "function" ? success(result) : success;
      if (mounted.current && title) notifications?.notify({ level: "success", title });
      return result;
    } catch (e) {
      // Every scoped operation error goes through the shared Extension Builder formatter so bridge infrastructure
      // failures name the backend-management state (with the bridge's detail — e.g. a relay timeout's "may still have
      // completed") and Studio 403s name the missing permission, instead of a generic message.
      setError(describeExtensionBuilderError(e));
      return null;
    } finally {
      adjust(scope, -1);
    }
  }, [adjust, notifications]);

  const isBusy = useCallback((scope: OperationScope) => (busyCounts[scope] ?? 0) > 0, [busyCounts]);
  const anyBusy = Object.keys(busyCounts).length > 0;

  return { error, setError, isBusy, anyBusy, runOperation };
}
