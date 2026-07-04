import { useCallback } from "react";
import { useMutation, useQuery, useQueryClient, type UseQueryResult } from "@tanstack/react-query";
import {
  createModuleManagementHosts,
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
  const hosts = createModuleManagementHosts(api);
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

  const mutation = useMutation({
    mutationFn: (operation: () => Promise<unknown>) => operation(),
    onSuccess: async () => {
      await invalidate();
      for (const delay of onSettledDelaysMs) {
        window.setTimeout(() => void invalidate(), delay);
      }
    }
  });

  return {
    mutateAsync: operation => mutation.mutateAsync(operation),
    isPending: mutation.isPending
  };
}
