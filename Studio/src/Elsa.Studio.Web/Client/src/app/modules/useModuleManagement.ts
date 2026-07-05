import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient, type UseQueryResult } from "@tanstack/react-query";
import {
  createModuleManagementHosts,
  getErrorMessage,
  moduleManagementKeys,
  normalizeModuleManagementRegistry,
  type HostId,
  type HostModel,
  type ModuleManagementRegistryResponse
} from "./moduleManagementApi";
import type { ElsaStudioModuleApi } from "../../sdk";

export interface ModuleManagementRegistries {
  hosts: HostModel[];
  studio: UseQueryResult<ModuleManagementRegistryResponse>;
  server: UseQueryResult<ModuleManagementRegistryResponse>;
  byHost(hostId: HostId): UseQueryResult<ModuleManagementRegistryResponse>;
}

// Registry read for a single host, keyed on the host id so the Studio and Server tabs cache
// independently. Always returns a normalized registry so callers never re-normalize. Mirrors the
// Workflows module's `useQuery` reads (Elsa.Studio.Workflows/Client/src/api/workflows.ts).
function useModuleManagementRegistry(host: HostModel) {
  return useQuery({
    queryKey: moduleManagementKeys.registry(host.id),
    queryFn: async () => {
      const registry = await host.context.http.getJson<Partial<ModuleManagementRegistryResponse>>("/_elsa/module-management/registry");
      return normalizeModuleManagementRegistry(registry);
    }
  });
}

// Both hosts' registries as parallel queries. Studio and Server always both exist, so the two
// `useQuery` calls are unconditional (Rules of Hooks safe). Callers pick the active host via `byHost`.
export function useModuleManagementRegistries(api: ElsaStudioModuleApi): ModuleManagementRegistries {
  // Memoized so `activeHost` (found by identity below) stays stable across renders, avoiding needless
  // re-renders of children keyed on the host object.
  const hosts = useMemo(() => createModuleManagementHosts(api), [api]);
  const studioHost = hosts.find(host => host.id === "studio") ?? hosts[0];
  const serverHost = hosts.find(host => host.id === "server") ?? hosts[1] ?? hosts[0];
  const studio = useModuleManagementRegistry(studioHost);
  const server = useModuleManagementRegistry(serverHost);

  return {
    hosts,
    studio,
    server,
    byHost: (hostId: HostId) => (hostId === "server" ? server : studio)
  };
}

export interface HostMutationRunner {
  mutateAsync(operation: () => Promise<unknown>): Promise<unknown>;
  isPending: boolean;
}

// Runs an arbitrary write against the given host and invalidates that host's registry on success so
// reads refetch the post-write state — the Query analogue of the old manual `refreshHost` calls, and
// the same `invalidateQueries`-on-success pattern the Workflows mutations use.
//
// `onSettledDelaysMs` schedules extra invalidations after the initial one to poll async server work
// (Nuplane reconciliation) that completes after the write returns — replacing the page's former
// `scheduleFollowUpRefreshes` timers.
export function useHostMutationRunner(host: HostModel, onSettledDelaysMs: number[] = []): HostMutationRunner {
  const queryClient = useQueryClient();
  const invalidate = useCallback(
    () => queryClient.invalidateQueries({ queryKey: moduleManagementKeys.host(host.id) }),
    [queryClient, host.id]
  );

  // Follow-up invalidation timers are tracked so they can be cancelled on unmount; otherwise a delayed
  // invalidate fires against a dead surface after the page has navigated away, refetching for nothing.
  const followUpTimers = useRef<number[]>([]);
  useEffect(
    () => () => {
      followUpTimers.current.forEach(id => window.clearTimeout(id));
      followUpTimers.current = [];
    },
    []
  );

  const mutation = useMutation({
    mutationFn: (operation: () => Promise<unknown>) => operation(),
    onSuccess: async () => {
      await invalidate();
      for (const delay of onSettledDelaysMs) {
        const id = window.setTimeout(() => {
          followUpTimers.current = followUpTimers.current.filter(pending => pending !== id);
          void invalidate();
        }, delay);
        followUpTimers.current.push(id);
      }
    }
  });

  return {
    mutateAsync: operation => mutation.mutateAsync(operation),
    isPending: mutation.isPending
  };
}

export interface HostOperations {
  isPending: boolean;
  // A local write error takes precedence over a stale read error; otherwise the registry query's failure.
  activeError: string | null;
  activeStatus: string | null;
  runHostOperation(operation: () => Promise<unknown>, success: string): Promise<void>;
  confirmAndRun(confirm: () => Promise<boolean>, operation: () => Promise<unknown>, success: string): Promise<void>;
  // Surface a client-side validation error on the active host without running an operation.
  reportError(message: string): void;
}

// Shared write-orchestration for the per-host module-management pages: runs an operation through the
// mutation runner, mirrors success/validation banners into transient per-host local state, and surfaces
// the active host's error/status. Extracted from the copy-pasted `runHostOperation`/`statusByHost`/
// `localErrorByHost`/confirm-wrapper that had already drifted between the two pages.
export function useHostOperations(
  activeHost: HostModel,
  queryError: unknown,
  onSettledDelaysMs: number[] = []
): HostOperations {
  // Success/validation banners are transient UI, kept local; query/mutation errors surface via `activeError`.
  const [statusByHost, setStatusByHost] = useState<Partial<Record<HostId, string | null>>>({});
  const [localErrorByHost, setLocalErrorByHost] = useState<Partial<Record<HostId, string | null>>>({});
  const mutation = useHostMutationRunner(activeHost, onSettledDelaysMs);

  // The banners are keyed per host so switching tabs preserves each host's transient state; these two
  // closures centralize that keying so the update shape lives in one place.
  const hostId = activeHost.id;
  const setStatus = (status: string | null) => setStatusByHost(current => ({ ...current, [hostId]: status }));
  const setLocalError = (error: string | null) => setLocalErrorByHost(current => ({ ...current, [hostId]: error }));

  async function runHostOperation(operation: () => Promise<unknown>, success: string) {
    setLocalError(null);
    setStatus(null);
    try {
      // mutateAsync invalidates + (optionally) schedules follow-up refetches on success (see useHostMutationRunner).
      await mutation.mutateAsync(operation);
      setStatus(success);
    } catch (e) {
      setLocalError(getErrorMessage(e));
    }
  }

  // Runs `operation` only if `confirm` resolves truthy; cancel is a no-op. For destructive/irreversible
  // writes (feed deletion, staged-package removal) the caller passes an `api.dialogs.confirm(...)` thunk.
  async function confirmAndRun(confirm: () => Promise<boolean>, operation: () => Promise<unknown>, success: string) {
    if (!(await confirm())) return;
    await runHostOperation(operation, success);
  }

  function reportError(message: string) {
    setLocalError(message);
    setStatus(null);
  }

  return {
    isPending: mutation.isPending,
    activeError: localErrorByHost[hostId] ?? errorMessageFor(queryError),
    activeStatus: statusByHost[hostId] ?? null,
    runHostOperation,
    confirmAndRun,
    reportError
  };
}

function errorMessageFor(error: unknown): string | null {
  return error === undefined || error === null ? null : getErrorMessage(error);
}
