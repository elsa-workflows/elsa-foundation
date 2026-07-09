import { useEffect, useRef } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  studioBackendManagementStatusPath,
  type ElsaStudioModuleApi,
  type StudioBackendManagementStatus,
  type StudioBackendManagementStatusKind
} from "../../sdk";
import { shellKeys } from "../queryKeys";

export type HostHealthStatus = "checking" | "ok" | "attention" | "unavailable";

export interface HostHealthEntry {
  status: HostHealthStatus;
  attention: number;
  detail: string;
}

export interface HostHealthSnapshot {
  studio: HostHealthEntry;
  // Backend management availability, read from the Studio management bridge (ADR 0037) rather than by probing
  // backend host-control endpoints directly. `kind` is the raw bridge status so the strip can render the explicit
  // unconfigured/unreachable/unauthorized states instead of inferring them from a failed fetch.
  backend: HostHealthEntry & { kind: BackendHealthKind };
}

// "unknown" covers the case where the bridge status endpoint itself is unreachable (a Studio-origin failure, not a
// backend one) — distinct from the backend-management statuses the bridge reports.
export type BackendHealthKind = StudioBackendManagementStatusKind | "unknown";

interface HostHealthRegistry {
  modules: Array<{ status?: string; diagnostics?: Array<{ status?: string }> }>;
  diagnostics?: Array<{ status?: string }>;
}

// How often the host-health strip re-checks. When a host is unavailable we back off exponentially
// from the healthy cadence up to the ceiling so a downed backend isn't hammered every few seconds.
export const healthyHealthPollMs = 15000;
export const maxHealthPollMs = 60000;

const ModulesChangedEventName = "elsa-studio:modules-changed";

// Fetches the Studio host registry and the backend management bridge status in parallel. Both reads target the Studio
// origin (`api.host`): the registry is the local host's own module registry, and the bridge status is Studio's report
// of backend availability. Neither read touches backend host-control endpoints from the browser.
async function fetchHostHealth(api: ElsaStudioModuleApi): Promise<HostHealthSnapshot> {
  const [studio, backend] = await Promise.all([
    readHostHealth(api.host.http.getJson<HostHealthRegistry>("/_elsa/module-management/registry"), "Studio"),
    readBackendManagementHealth(api)
  ]);

  return { studio, backend };
}

function snapshotDegraded(snapshot: HostHealthSnapshot) {
  return snapshot.studio.status === "unavailable" || snapshot.backend.status === "unavailable";
}

// Host-health polling, ported from the App.tsx `HostHealthStrip` useEffect onto TanStack Query while
// preserving its semantics exactly:
//  - Exponential backoff on failure: a ref-tracked consecutive-degraded counter (incremented after a
//    degraded snapshot, reset to 0 after a clean one — identical to the original `failures` variable)
//    feeds the `refetchInterval` delay: 15s while healthy, doubling up to the 60s ceiling while down.
//  - Pause while the tab is hidden: `refetchIntervalInBackground` defaults to false, so the interval
//    stops when `document.hidden`.
//  - Refetch on return: `refetchOnWindowFocus: true` re-checks when the tab regains focus, so status
//    is never stale after the tab was backgrounded.
//  - Re-check on module changes: the `elsa-studio:modules-changed` event invalidates the query.
export function useHostHealth(api: ElsaStudioModuleApi) {
  const queryClient = useQueryClient();
  // Consecutive degraded polls, mirroring the original mutable `failures` counter. A ref (not state)
  // so updating it from the query result doesn't itself trigger a re-render/refetch loop.
  const failuresRef = useRef(0);

  const query = useQuery({
    queryKey: shellKeys.hostHealth,
    queryFn: () => fetchHostHealth(api),
    refetchOnWindowFocus: true,
    refetchInterval: () => {
      const failures = failuresRef.current;
      return failures === 0
        ? healthyHealthPollMs
        : Math.min(maxHealthPollMs, healthyHealthPollMs * 2 ** (failures - 1));
    }
  });

  // Advance/reset the backoff counter from each settled poll. `isFetching` gates it to completed
  // fetches so we only count once per poll, matching the original per-`run()` increment.
  const { data, isFetching } = query;
  useEffect(() => {
    if (isFetching) return;
    if (!data) return;
    failuresRef.current = snapshotDegraded(data) ? failuresRef.current + 1 : 0;
  }, [data, isFetching]);

  useEffect(() => {
    const onModulesChanged = () => void queryClient.invalidateQueries({ queryKey: shellKeys.hostHealth });
    window.addEventListener(ModulesChangedEventName, onModulesChanged);
    return () => window.removeEventListener(ModulesChangedEventName, onModulesChanged);
  }, [queryClient]);

  return query;
}

async function readHostHealth(registryRequest: Promise<HostHealthRegistry>, hostLabel: string): Promise<HostHealthEntry> {
  try {
    const registry = await registryRequest;
    const attention = countRegistryAttention(registry);

    return {
      status: attention === 0 ? "ok" : "attention",
      attention,
      detail: attention === 0 ? `${hostLabel} is reachable.` : `${attention} item${attention === 1 ? "" : "s"} need review.`
    };
  } catch (e) {
    return {
      status: "unavailable",
      attention: 0,
      detail: `${hostLabel} module registry is unavailable: ${getHealthErrorMessage(e)}`
    };
  }
}

// Reads the Studio management bridge status from the Studio origin and maps it to a host-health entry. The bridge
// answers with an explicit backend-management state, so the browser renders the real reason (unconfigured / unreachable
// / unauthorized / degraded) rather than inferring it from a failed backend fetch. A failure to reach the bridge itself
// (a Studio-origin problem) surfaces as "unknown".
async function readBackendManagementHealth(api: ElsaStudioModuleApi): Promise<HostHealthEntry & { kind: BackendHealthKind }> {
  try {
    const status = await api.host.http.getJson<StudioBackendManagementStatus>(studioBackendManagementStatusPath);
    return mapBackendManagementStatus(status);
  } catch (e) {
    return {
      kind: "unknown",
      status: "unavailable",
      attention: 0,
      detail: `Backend management status is unavailable: ${getHealthErrorMessage(e)}`
    };
  }
}

function mapBackendManagementStatus(status: StudioBackendManagementStatus): HostHealthEntry & { kind: BackendHealthKind } {
  const detail = status.detail?.trim() || defaultBackendDetail(status.status);
  switch (status.status) {
    case "available":
      return { kind: status.status, status: "ok", attention: 0, detail };
    case "unconfigured":
      // Fail-closed-by-design, not an outage: no attention weight and no backoff.
      return { kind: status.status, status: "attention", attention: 0, detail };
    case "degraded":
      return { kind: status.status, status: "attention", attention: 1, detail };
    case "unauthorized":
    case "unreachable":
    default:
      return { kind: status.status as BackendHealthKind, status: "unavailable", attention: 0, detail };
  }
}

function defaultBackendDetail(kind: StudioBackendManagementStatusKind): string {
  switch (kind) {
    case "available": return "Backend management is reachable.";
    case "unconfigured": return "Backend management is not configured.";
    case "unauthorized": return "Backend rejected the Studio management credential.";
    case "unreachable": return "Backend management could not be reached.";
    case "degraded": return "Backend management is degraded.";
    default: return "Backend management status is unknown.";
  }
}

export function checkingHostHealth(detail: string): HostHealthEntry {
  return { status: "checking", attention: 0, detail };
}

export function getHealthErrorMessage(error: unknown) {
  return error instanceof Error && error.message.length > 0 ? error.message : "request failed";
}

function countRegistryAttention(registry: HostHealthRegistry) {
  const attentionStatuses = new Set(["failed", "incompatible", "disabled"]);
  const moduleStatuses = registry.modules.filter(module => module.status && attentionStatuses.has(module.status)).length;
  const moduleDiagnostics = registry.modules.flatMap(module => module.diagnostics ?? []).filter(diagnostic => diagnostic.status && attentionStatuses.has(diagnostic.status)).length;
  const hostDiagnostics = (registry.diagnostics ?? []).filter(diagnostic => diagnostic.status && attentionStatuses.has(diagnostic.status)).length;

  return moduleStatuses + moduleDiagnostics + hostDiagnostics;
}

// The label shown for a backend management tile. Unlike the generic host status, the bridge distinguishes
// unconfigured/unreachable/unauthorized/degraded, so the tile names the real state instead of a generic "Unavailable".
export function labelForBackendHealth(kind: BackendHealthKind): string {
  switch (kind) {
    case "available": return "Connected";
    case "unconfigured": return "Not configured";
    case "unauthorized": return "Unauthorized";
    case "unreachable": return "Unreachable";
    case "degraded": return "Degraded";
    default: return "Unknown";
  }
}

export function labelForHostStatus(status: HostHealthStatus) {
  if (status === "ok")
    return "OK";

  if (status === "attention")
    return "Review";

  if (status === "unavailable")
    return "Unavailable";

  return "Checking";
}
