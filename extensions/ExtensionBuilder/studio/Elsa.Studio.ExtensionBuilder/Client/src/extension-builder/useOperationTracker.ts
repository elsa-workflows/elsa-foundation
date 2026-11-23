import { useCallback, useLayoutEffect, useMemo, useRef, useState } from "react";
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

export function useOperationTracker(notifications: StudioNotificationApi | undefined, context: object, selection: string): OperationTracker {
  const [busyCounts, setBusyCounts] = useState<Partial<Record<OperationScope, number>>>({});
  const owner = useMemo(() => ({ context, selection }), [context, selection]);
  const currentOwner = useRef(owner);
  currentOwner.current = owner;
  const visit = useMemo(() => ({ owner, notifications, active: false, generation: 0 }), [owner, notifications]);
  const currentVisit = useRef(visit);
  currentVisit.current = visit;
  useLayoutEffect(() => {
    visit.active = true;
    return () => { visit.active = false; visit.generation++; };
  }, [visit]);
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
    const generation = visit.generation;
    const isCurrent = () => visit.active && currentVisit.current === visit && visit.generation === generation;
    if (!isCurrent()) return null;
    adjust(scope, 1);
    setError(null);
    try {
      const result = await operation();
      // Retained tabs retire confirmations when hidden, but accepted work still belongs to
      // the same backend/selection and must reconcile its build, file or runtime state.
      if (currentOwner.current !== owner) return null;
      if (isCurrent()) {
        const title = typeof success === "function" ? success(result) : success;
        if (title) notifications?.notify({ level: "success", title });
      }
      return result;
    } catch (e) {
      // Every scoped operation error goes through the shared Extension Builder formatter so bridge infrastructure
      // failures name the backend-management state (with the bridge's detail — e.g. a relay timeout's "may still have
      // completed") and Studio 403s name the missing permission, instead of a generic message.
      if (currentOwner.current === owner) setError(describeExtensionBuilderError(e));
      return null;
    } finally {
      adjust(scope, -1);
    }
  }, [adjust, notifications, owner, visit]);

  const isBusy = useCallback((scope: OperationScope) => (busyCounts[scope] ?? 0) > 0, [busyCounts]);
  const anyBusy = Object.keys(busyCounts).length > 0;

  return { error, setError, isBusy, anyBusy, runOperation };
}
