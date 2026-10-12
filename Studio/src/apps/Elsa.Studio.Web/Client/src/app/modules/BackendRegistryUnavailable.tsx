import React from "react";
import { Boxes, ShieldAlert } from "lucide-react";
import { EmptyState, StudioAlert } from "../ui";
import { labelForBackendRegistryStatus, type BackendRegistryUnavailableKind, type HostModel } from "./moduleManagementApi";

// The explicit backend-management-unavailable state shared by the module-management and package-feeds Server tabs
// (#246, ADR 0037). Rendered instead of the registry surface when the Studio management bridge reports the backend
// registry is unconfigured/unreachable/unauthorized/degraded — or when the bridge itself is unreachable ("unknown").
// It names the real state and reason rather than blanking the page or leaking a raw fetch error, and it never affects
// the Studio host tab, which reads its own registry directly and keeps working when the backend is down.
export function BackendRegistryUnavailable({
  host,
  status,
  detail
}: {
  host: HostModel;
  status: BackendRegistryUnavailableKind;
  detail: string;
}) {
  // A 403 is a Studio authorization failure, not a backend/config problem: name it as a permission problem and do NOT
  // point the user at backend configuration or a login (#249, ADR 0037).
  if (status === "forbidden") {
    return (
      <>
        <StudioAlert tone="warning">
          {host.label} module registry — {labelForBackendRegistryStatus(status)}. {detail}
        </StudioAlert>
        <EmptyState icon={<ShieldAlert size={22} />}>
          You are signed in but do not have permission to view {host.label} module management. Ask an administrator for the
          module-management.read permission.
        </EmptyState>
      </>
    );
  }

  return (
    <>
      <StudioAlert tone={status === "unconfigured" ? "warning" : "danger"}>
        {host.label} module registry is unavailable — {labelForBackendRegistryStatus(status)}. {detail}
      </StudioAlert>
      <EmptyState icon={<Boxes size={22} />}>
        {status === "unconfigured"
          ? <>Privileged host management is optional, but required to manage {host.label} modules. Set <code>Studio:BackendServerBaseUrl</code> (or <code>Studio:BackendBaseUrl</code> for a shared URL) and <code>Studio:BackendModuleManagementApiKey</code> on the Studio host to enable it.</>
          : <>The {host.label} module registry could not be loaded. It is served through Studio's privileged host-management integration.</>}
      </EmptyState>
    </>
  );
}

// Explains that module-management MUTATIONS are unavailable for a host whose writes would go directly to backend
// host-control endpoints (the Server tab). After ADR 0037 / #248 the browser holds no host management key, so those
// direct writes can no longer be authorized; bridging them is a later slice. Read-only registry data still renders —
// this note replaces only the write affordances (upload/delete/feeds/reconcile/prune/retention), so visiting the page
// issues zero doomed backend requests.
export function HostMutationsUnavailable({ host }: { host: HostModel }) {
  return (
    <StudioAlert tone="info">
      {host.label} host management is read-only in Studio. Package uploads, feed edits, reconcile, prune, and retention
      changes for the {host.label} host run directly against its host-control API, which the browser cannot authorize
      without a host management key. Manage the {host.label} host from that host directly, or use a trusted
      server-side/CLI tool.
    </StudioAlert>
  );
}
