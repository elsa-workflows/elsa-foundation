import React, { useState } from "react";
import { createRoot } from "react-dom/client";
import { AnonymousAuthProvider, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { ThemeProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import { writeAppearancePreferences } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/appearance";
import { DecisionsPage } from "../../../extensions/Decisions/studio/Elsa.Studio.Decisions/Client/src/DecisionsPage";
import { NamedDashboardsRoute } from "../../../extensions/Dashboard/studio/Elsa.Studio.Dashboard/Client/src/native/NamedDashboards";
import { ConnectionList } from "../../../extensions/Connections/studio/Elsa.Studio.Connections/Client/src/ConnectionList";
import { OpenTelemetryPage, register as registerTelemetry } from "../../../extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/src/module";
import "../../../extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/src/styles.css";
import "../../../extensions/Connections/studio/Elsa.Studio.Connections/Client/src/styles.css";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";
import "../../../extensions/Dashboard/studio/Elsa.Studio.Dashboard/Client/src/native/native.css";
import "../../../extensions/Decisions/studio/Elsa.Studio.Decisions/Client/src/styles.css";

const params = new URLSearchParams(location.search);
const view = params.get("view") ?? "decisions";
writeAppearancePreferences({ themeId: "classic", mode: params.get("theme") === "dark" ? "dark" : "light" });
const api = {
  backend: { baseUrl: location.origin, http: { getJson: async () => ({ items: [{ id: "approval", kind: view === "questions" ? "question" : "dashboard", name: "Purchase approval", description: "Review purchase approvals", updatedAt: "2026-11-18T10:00:00Z", capabilities: { canRead: true, canEdit: true, canShare: true } }] }) } },
  backendSession: { identity: { status: "authenticated", subject: "row-review", roles: [], permissions: ["decisions:read"] }, capabilities: { capabilities: [{ id: "elsa.api.decisions", contractVersion: "1", links: [{ rel: "decisions", href: "decisions" }] }] } },
  runtime: { identity: { subject: "row-review" } }
} as unknown as ElsaStudioModuleApi;

const traces = ["Purchase approval", "Order processing"].map((name, index) => ({ traceId: `trace-${index}`, name, startTime: `2026-11-18T10:0${1 - index}:00Z`, duration: "00:00:00.250", spanCount: 1, status: "Ok" }));
if (view === "traces") registerTelemetry({
  ...api,
  navigation: { add() {} }, routes: { add() {} }, panels: { add() {} },
  backend: { baseUrl: location.origin, http: {
    getJson: async (path: string) => path.endsWith("/storage") ? { traceCount: 2, spanCount: 2, metricInstrumentCount: 0, metricPointCount: 0, logRecordCount: 0 } : { trace: traces.find(trace => path.endsWith(trace.traceId)), spans: [], resources: [] },
    postJson: async (path: string) => ({ items: path.includes("traces/search") ? traces : [] })
  } }
} as unknown as ElsaStudioModuleApi);

function Fixture() {
  const [destination, navigate] = useState("");
  return <AnonymousAuthProvider><ThemeProvider><main style={{ padding: 24 }}>
    <output aria-label="Opened destination">{destination}</output>
    {view === "traces" ? <OpenTelemetryPage /> : view === "connections" ? <section className="connections-shell"><ConnectionList items={[{ tenantId: "row-review", name: "customer-api", displayName: "Customer API", typeName: "http", authScheme: "bearer", settings: {}, secretFieldNames: [], owner: "tenant", managedBy: "", status: "Ready", createdAt: "2026-11-18T10:00:00Z", tags: [] }]} descriptors={null} loading={false} error={false} searched={false} onRetry={() => {}} onClear={() => {}} onOpen={navigate} /></section> : view === "decisions" ? <DecisionsPage api={api} navigate={navigate} /> : <NamedDashboardsRoute api={api} kind={view === "questions" ? "question" : "dashboard"} view="list" navigate={navigate} />}
  </main></ThemeProvider></AnonymousAuthProvider>;
}
createRoot(document.getElementById("root")!).render(<Fixture />);
