import { useEffect, useRef } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { ElsaStudioModuleApi } from "../../sdk";
import { shellKeys } from "../queryKeys";

export type HostHealthStatus = "checking" | "ok" | "attention" | "unavailable";

export interface HostHealthEntry {
  status: HostHealthStatus;
  attention: number;
  detail: string;
}

export interface HostHealthSnapshot {
  studio: HostHealthEntry;
  server: HostHealthEntry;
  backend: HostHealthEntry;
}

interface HostHealthRegistry {
  modules: Array<{ status?: string; diagnostics?: Array<{ status?: string }> }>;
  diagnostics?: Array<{ status?: string }>;
}

// How often the host-health strip re-checks. When a host is unavailable we back off exponentially
// from the healthy cadence up to the ceiling so a downed backend isn't hammered every few seconds.
export const healthyHealthPollMs = 15000;
export const maxHealthPollMs = 60000;

const ModulesChangedEventName = "elsa-studio:modules-changed";

// Fetches all three host-health probes in parallel, mirroring the original single `run()` pass so the
// backoff decision (any host unavailable) stays coherent across the strip.
async function fetchHostHealth(api: ElsaStudioModuleApi): Promise<HostHealthSnapshot> {
  const [studio, server, backend] = await Promise.all([
    readHostHealth(api.host.http.getJson<HostHealthRegistry>("/_elsa/module-management/registry"), "Studio"),
    readHostHealth(api.backend.http.getJson<HostHealthRegistry>("/_elsa/module-management/registry"), "Server"),
    readBackendHealth(api.backend.baseUrl)
  ]);

  return { studio, server, backend };
}

function snapshotDegraded(snapshot: HostHealthSnapshot) {
  return [snapshot.studio, snapshot.server, snapshot.backend].some(host => host.status === "unavailable");
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

async function readBackendHealth(baseUrl: string): Promise<HostHealthEntry> {
  // The backend Elsa Server exposes its liveness signal at the origin root (returns
  // `{ "status": "Healthy", ... }` with CORS enabled) — it does not map a `/_elsa/health` route.
  // Probe the root so the tile reflects the backend's real health check rather than a 404.
  const healthUrl = new URL("/", baseUrl).toString();
  // Prefer a readable (CORS) response so we can tell a healthy host apart from one returning 5xx.
  // An opaque no-cors probe settles even for a crashing server, so it can't distinguish the two.
  // Readable CORS request so we can inspect the real status: a rejected fetch means the
  // host is unreachable, while any response (even an error status) means the API is up.
  try {
    const response = await fetch(healthUrl, { cache: "no-store" });
    if (response.ok) {
      // A 2xx can still carry a self-reported "Degraded"/"Unhealthy" status (ASP.NET Core health
      // checks answer 200 for Degraded), so inspect the body before declaring the backend healthy.
      const reported = await readReportedHealthStatus(response);
      if (reported && !isHealthyStatus(reported)) {
        return {
          status: "attention",
          attention: 1,
          detail: `${healthUrl} reported "${reported}".`
        };
      }

      return {
        status: "ok",
        attention: 0,
        detail: healthUrl
      };
    }

    return {
      status: "attention",
      attention: 1,
      detail: `${healthUrl} responded with ${response.status}.`
    };
  } catch {
    // The host may not send CORS headers on its health endpoint; fall back to a reachability-only probe
    // (which can still confirm the socket is up) before declaring the API unavailable.
    try {
      await fetch(healthUrl, { cache: "no-store", mode: "no-cors" });
      return { status: "ok", attention: 0, detail: healthUrl };
    } catch (e) {
      return { status: "unavailable", attention: 0, detail: `${healthUrl} (${getHealthErrorMessage(e)})` };
    }
  }
}

// Best-effort read of the backend's self-reported health status (e.g. `{ "status": "Healthy" }`).
// Returns null for a non-JSON or bodyless response so the caller falls back to treating a 2xx as healthy.
async function readReportedHealthStatus(response: Response): Promise<string | null> {
  if (!(response.headers.get("content-type") ?? "").includes("json")) return null;
  try {
    const body = await response.json();
    const status = (body as { status?: unknown })?.status;
    return typeof status === "string" ? status : null;
  } catch {
    return null;
  }
}

function isHealthyStatus(status: string): boolean {
  return status.trim().toLowerCase() === "healthy";
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

export function labelForHostStatus(status: HostHealthStatus) {
  if (status === "ok")
    return "OK";

  if (status === "attention")
    return "Review";

  if (status === "unavailable")
    return "Unavailable";

  return "Checking";
}
