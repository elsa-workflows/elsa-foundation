import React from "react";
import { Boxes } from "lucide-react";
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
  return (
    <>
      <StudioAlert tone={status === "unconfigured" ? "warning" : "danger"}>
        {host.label} module registry is unavailable — {labelForBackendRegistryStatus(status)}. {detail}
      </StudioAlert>
      <EmptyState icon={<Boxes size={22} />}>
        The {host.label} module registry could not be loaded. It is served through the Studio management bridge; check the backend management configuration.
      </EmptyState>
    </>
  );
}
