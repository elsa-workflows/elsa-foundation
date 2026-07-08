import React from "react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import {
  StatusChip,
  StatusPill,
  StudioListContainer,
  StudioListRow,
  StudioStatTile
} from "@elsa-workflows/studio-ui";
import "./styles.css";

export function register(api: ElsaStudioModuleApi) {
  api.dashboardWidgets.add({
    id: "dashboard-sample-health",
    title: "Module health",
    order: 100,
    component: ModuleHealthWidget
  });

  api.dashboardWidgets.add({
    id: "dashboard-sample-route",
    title: "Route ownership",
    order: 110,
    component: RouteOwnershipWidget
  });

  api.dashboardWidgets.add({
    id: "dashboard-sample-backend",
    title: "Backend endpoints",
    order: 120,
    component: BackendEndpointsWidget
  });

  api.dashboardWidgets.add({
    id: "dashboard-sample-kit",
    title: "Shared UI kit",
    order: 130,
    component: SharedKitWidget
  });
}

export function ModuleHealthWidget() {
  return (
    <div className="admin-card dashboard-sample-card">
      <h2>Dashboard module</h2>
      <p className="metric">
        <StatusChip tone="success">Loaded</StatusChip>
      </p>
      <p>Runtime widget registration completed through the Studio SDK.</p>
    </div>
  );
}

export function RouteOwnershipWidget() {
  return (
    <div className="admin-card dashboard-sample-card">
      <h2>Route ownership</h2>
      <p className="metric">Host</p>
      <p>Dashboard navigation and routing are provided by Studio.</p>
    </div>
  );
}

export function BackendEndpointsWidget() {
  return (
    <div className="admin-card dashboard-sample-card">
      <h2>Backend endpoints</h2>
      <p className="metric">0</p>
      <p>This widget is contributed by a frontend-only module.</p>
    </div>
  );
}

const KIT_STATS = [
  { label: "Executions", value: "12.4k", delta: "+8.2%", deltaTone: "success" as const, trend: [4, 6, 5, 8, 7, 11, 12] },
  { label: "Faulted", value: "37", delta: "-14%", deltaTone: "danger" as const, trend: [9, 7, 8, 6, 5, 4, 3] }
];

const KIT_MODULES = [
  { name: "Dashboard", kind: "Frontend", tone: "accent" as const, status: "Loaded" },
  { name: "Workflows", kind: "Full-stack", tone: "success" as const, status: "Loaded" },
  { name: "Diagnostics", kind: "Frontend", tone: "neutral" as const, status: "Idle" }
];

/**
 * Living demo of the shared UI kit — a compact stat row over a dense list, both built
 * entirely from `@elsa-workflows/studio-ui` primitives so the sample doubles as usage
 * documentation for module authors.
 */
export function SharedKitWidget() {
  const [selected, setSelected] = React.useState(KIT_MODULES[0]?.name);

  return (
    <div className="admin-card dashboard-sample-card">
      <h2>Shared UI kit</h2>
      <div className="dashboard-sample-stats">
        {KIT_STATS.map(stat => (
          <StudioStatTile
            key={stat.label}
            label={stat.label}
            value={stat.value}
            delta={stat.delta}
            deltaTone={stat.deltaTone}
            trend={stat.trend}
          />
        ))}
      </div>
      <StudioListContainer className="dashboard-sample-list">
        {KIT_MODULES.map(module => (
          <StudioListRow
            key={module.name}
            title={module.name}
            subtitle={module.kind}
            selected={selected === module.name}
            onSelect={() => setSelected(module.name)}
            trailing={<StatusPill tone={module.tone}>{module.status}</StatusPill>}
          />
        ))}
      </StudioListContainer>
    </div>
  );
}
