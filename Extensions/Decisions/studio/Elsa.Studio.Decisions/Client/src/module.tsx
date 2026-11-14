import { Scale } from "lucide-react";
import React, { lazy, Suspense } from "react";
import { useStudioRouteLocation, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { decisionsCapability } from "./decisionsApi";
import { collectEvaluateDecisionValidation } from "./decisionDraftValidation";
import { createDecisionInputsEditorContribution } from "./DecisionInputsEditor";
import { DecisionVersionEditor } from "./DecisionVersionEditor";
import { newDecisionHandoffParameter } from "./newDecisionHandoff";
import "./styles.css";
const Page = lazy(() => import("./DecisionsPage").then(module => ({ default: module.DecisionsPage })));
function DecisionsRoute({ api, navigate }: { api: ElsaStudioModuleApi; navigate(path: string): void }) {
  const location = useStudioRouteLocation();
  return <Suspense fallback={<p role="status">Loading decisions…</p>}><Page key={`${api.backend.baseUrl}:${api.runtime.identity?.tenantId}:${api.runtime.identity?.subject}`} api={api} navigate={navigate} assetId={location.searchParams.get("decision")} versionId={location.searchParams.get("version")} isNew={location.searchParams.has("new")} handoff={location.searchParams.get(newDecisionHandoffParameter)} /></Suspense>;
}
export function register(api: ElsaStudioModuleApi) {
  api.featureAreas.add({ id: "decisions", title: "Decisions", description: "Reusable business policies, decision tables and saved cases.", navGroup: "Workspace", ownedPaths: ["/decisions"], required: false, defaultEnabled: true, order: 35,
    nav: { icon: Scale, title: "Decisions", path: "/decisions", placement: "build", order: 35, requiresBackend: decisionsCapability, requiresPermission: ["decisions:read"] },
    routes: [{ id: "decisions", path: "/decisions", label: "Decisions", requiresBackend: decisionsCapability, workspace: { resourceKey: location => { const id = location.searchParams.get("decision"); return id ? `decision:${id}:${location.searchParams.get("version") ?? "draft"}` : null; } }, component: ({ navigate }) => <DecisionsRoute api={api} navigate={navigate} /> }]
  });
  api.propertyEditors.add({ id: "decisions.version", requiresBackend: decisionsCapability, order: 50, supportsValidationReporting: true,
    collectDraftValidation: (activity, context) => collectEvaluateDecisionValidation(api, activity, context),
    supports: descriptor => descriptor.uiHint === "decision-version", component: props => <DecisionVersionEditor {...props} api={api} /> });
  api.expressionEditors.add(createDecisionInputsEditorContribution(api));
}
