import { lazy, type ReactNode } from "react";
import { authSessionEndedEvent, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { setDialogs } from "./workflow-editor/dialogs";
import { createObjectExpressionEditorContribution } from "./objectExpressionEditor";
import "./styles.css";
import "./workspaceAppearance.css";
import { registerVariableReferenceContribution } from "./variableReferenceContribution";
import { registerInputReferenceContribution } from "./inputReferenceContribution";
import { WorkflowLazyBoundary } from "./WorkflowLazyBoundary";
import { activityGraphImplementationEditorContribution, activityGraphSchema2ImplementationEditorContribution } from "./activityGraphContribution";
import { clearActivityDefinitionRecoveryForIdentity } from "./activityDefinitionRecovery";
import {
  eventWorkflowTestStimulusEditor,
  httpWorkflowTestStimulusEditor,
  schedulingWorkflowTestStimulusEditor
} from "./workflowTestStimulusEditors";
import { registerWorkflowAgent } from "./workflowAgent";
import { compensationTargetPropertyEditor } from "./CompensationTargetEditor";

const WorkflowTemplatesPage = lazy(() => import("./workflow-editor/WorkflowTemplatesPage").then(module => ({ default: module.WorkflowTemplatesRoute })));
const WorkflowManagementPage = lazy(() => import("./workflow-editor/pages").then(module => ({ default: module.WorkflowManagementPage })));
const WorkflowExecutablesPage = lazy(() => import("./workflow-editor/pages").then(module => ({ default: module.WorkflowExecutablesPage })));
const WorkflowExecutableInspectorPage = lazy(() => import("./workflow-editor/pages").then(module => ({ default: module.WorkflowExecutableInspectorPage })));
const WorkflowInstancesPage = lazy(() => import("./workflow-editor/pages").then(module => ({ default: module.WorkflowInstancesPage })));
const WorkflowInstanceDetailsPage = lazy(() => import("./workflow-editor/pages").then(module => ({ default: module.WorkflowInstanceDetailsPage })));
const ActivityDefinitionsPage = lazy(() => import("./ActivityDefinitionsPage").then(module => ({ default: module.ActivityDefinitionsPage })));
const ActivityUpgradeWorkbenchPage = lazy(() => import("./ActivityUpgradeWorkbenchPage").then(module => ({ default: module.ActivityUpgradeWorkbenchPage })));
const Elsa3ReusableImportPage = lazy(() => import("./Elsa3ReusableImportPage").then(module => ({ default: module.Elsa3ReusableImportPage })));
const ActivityAvailabilityPage = lazy(() => import("./ActivityAvailabilityPage").then(module => ({ default: module.ActivityAvailabilityPage })));
const RuntimeDiagnosticsSettingsPage = lazy(() => import("./RuntimeDiagnosticsSettingsPage").then(module => ({ default: module.RuntimeDiagnosticsSettingsPage })));

// register() has no host-provided teardown to unregister this listener, and it can run more than once
// (module reloads, tests). Keeping the handler as a stable module-level reference makes repeat
// registration a no-op for addEventListener instead of piling up one listener per call; the handler
// reads the most recently registered module's identity so it always reacts on behalf of the current
// registration.
let latestModuleApi: ElsaStudioModuleApi | null = null;

function clearActivityDefinitionRecoveryOnAuthSessionEnded() {
  const identity = latestModuleApi?.runtime.identity;
  if (identity?.subject && identity.tenantId) {
    clearActivityDefinitionRecoveryForIdentity(identity);
  }
}

// WorkflowDesignerPanelContext is the TContext behind the workflow.designer.panels slot — exported so
// contributed-panel authors can type their `context` prop instead of hand-copying the shape.
export type { GraphPaletteElement } from "./graph-authoring/graphPaletteElements";
export type { WorkflowConnectSource, WorkflowDesignerPanelContext } from "./workflow-editor/editorTypes";
export type { EnumWorkflowRunInputEditorOptions } from "./workflowRunInputEditorContributions";
export type { ActivityDefinitionsObservation } from "./activityDefinitionObservability";
export { capabilityIds, resolveCapabilityLink } from "./api/capabilities";

export function register(api: ElsaStudioModuleApi) {
  setDialogs(api.dialogs);
  const weaver = registerWorkflowAgent(api.agent);
  const expressionToolingIdentity = {
    backend: api.backend.baseUrl || "same-origin",
    subject: api.runtime.identity?.subject?.trim() || "anonymous",
    tenantId: api.runtime.identity?.tenantId?.trim() || "default"
  };
  registerVariableReferenceContribution(api.expressionEditors);
  api.expressionEditors.add(createObjectExpressionEditorContribution(() => api.propertyEditors.list()));
  registerInputReferenceContribution(api.expressionEditors);
  api.propertyEditors.add(compensationTargetPropertyEditor);
  api.workflowTestStimulusEditors?.add(httpWorkflowTestStimulusEditor);
  api.workflowTestStimulusEditors?.add(eventWorkflowTestStimulusEditor);
  api.workflowTestStimulusEditors?.add(schedulingWorkflowTestStimulusEditor);
  api.activityEditors.add(activityGraphImplementationEditorContribution);
  api.activityEditors.add(activityGraphSchema2ImplementationEditorContribution);
  latestModuleApi = api;
  if (typeof window !== "undefined") {
    window.addEventListener(authSessionEndedEvent, clearActivityDefinitionRecoveryOnAuthSessionEnded);
  }
  const runInputEditors = () => api.workflowRunInputEditors?.list() ?? [];
  const deferred = (label: string, content: ReactNode) =>
    <WorkflowLazyBoundary label={label}>{content}</WorkflowLazyBoundary>;
  api.featureAreas.add({
    id: "workflows",
    title: "Workflows",
    description: "Design, publish and run workflow definitions and inspect runs.",
    navGroup: "Workspace",
    ownedPaths: ["/workflows"],
    required: true,
    defaultEnabled: true,
    order: 20,
    nav: {
      title: "Workflows",
      path: "/workflows/definitions",
      iconColor: "#0ea5e9",
      placement: "main",
      order: 20,
      items: [
        { id: "workflows-definitions", title: "Workflows", path: "/workflows/definitions", iconColor: "#0ea5e9", placement: "main", order: 20 },
        { id: "workflows-runs", title: "Runs", path: "/workflows/instances", iconColor: "#0ea5e9", placement: "main", order: 30 },
        { id: "workflows-executables", title: "Executables", path: "/workflows/executables", iconColor: "#d97706", placement: "more", order: 10 },
        { id: "workflows-activity-definitions", title: "Activity definitions", path: "/workflows/activity-definitions", iconColor: "#8b5cf6", placement: "more", order: 20 },
        { id: "workflows-templates", title: "Templates", path: "/workflows/templates", iconColor: "#0ea5e9", placement: "more", order: 25 },
        { id: "workflows-runtime-diagnostics", title: "Runtime evidence settings", path: "/workflows/runtime-diagnostics", iconColor: "#0ea5e9", placement: "more", order: 40 },
        { id: "workflows-activity-availability", title: "Activity availability", path: "/workflows/activity-availability", iconColor: "#0d9488", placement: "more", order: 50 }
      ]
    },
    routes: [
      { id: "workflows-templates", path: "/workflows/templates", label: "Workflow templates",
        component: ({ navigate }) => deferred("workflow templates", <WorkflowTemplatesPage api={api} navigate={navigate} />) },
      {
        id: "workflows-elsa3-import",
        path: "/workflows/activity-definitions/import-elsa3",
        label: "Elsa 3 import",
        component: ({ navigate }) => deferred("Elsa 3 import", <Elsa3ReusableImportPage context={api.backend} navigate={navigate} />)
      },
      {
        id: "workflows-activity-upgrades",
        path: "/workflows/activity-definitions/upgrades",
        label: "Activity Definition upgrades",
        component: ({ navigate }) => deferred("Activity Definition upgrades", <ActivityUpgradeWorkbenchPage context={api.backend} navigateToStudioPath={navigate} />)
      },
      {
        id: "workflows-activity-definitions",
        path: "/workflows/activity-definitions",
        label: "Activity Definitions",
        component: ({ navigate }) => deferred("activity definitions", <ActivityDefinitionsPage context={api.backend} activityEditors={() => api.activityEditors.list()} graphExtensions={() => ({ properties: api.propertyEditors.list(), expressions: api.expressionEditors.list(), panels: api.workflowDesigner.panels.list() })} inputEditors={runInputEditors} runtime={api.runtime} navigateToStudioPath={navigate} />)
      },
      {
        id: "workflows-definitions",
        path: "/workflows/definitions",
        label: "Workflow definitions",
        component: () => deferred("workflow definitions", <WorkflowManagementPage context={api.backend} ai={api.agent} weaver={weaver} propertyEditors={api.propertyEditors.list()} expressionEditors={api.expressionEditors?.list() ?? []} expressionToolingIdentity={expressionToolingIdentity} runInputEditors={runInputEditors()} testStimulusEditors={api.workflowTestStimulusEditors?.list() ?? []} workflowDesignerPanels={api.workflowDesigner.panels.list()} autosaveEnabledByDefault={api.runtime.workflows?.autosaveEnabledByDefault ?? true} />)
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => deferred("workflow executables", <WorkflowExecutablesPage context={api.backend} ai={api.agent} runInputEditors={runInputEditors()} />)
      },
      {
        id: "workflows-executable-inspector",
        path: "/workflows/executables/:artifactId",
        label: "Executable Inspector",
        component: () => deferred("executable inspector", <WorkflowExecutableInspectorPage context={api.backend} ai={api.agent} runInputEditors={runInputEditors()} />)
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow runs",
        component: ({ navigate }) => deferred("workflow runs", <WorkflowInstancesPage context={api.backend} navigate={navigate} />)
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow run",
        component: ({ navigate }) => deferred("workflow run", <WorkflowInstanceDetailsPage context={api.backend} ai={api.agent} expressionEditors={api.expressionEditors.list()} navigate={navigate} />)
      },
      {
        id: "workflows-activity-availability",
        path: "/workflows/activity-availability",
        label: "Activity availability",
        component: () => deferred("activity availability", <ActivityAvailabilityPage context={api.backend} />)
      },
      {
        id: "workflows-runtime-diagnostics",
        path: "/workflows/runtime-diagnostics",
        label: "Runtime evidence settings",
        component: () => deferred("Runtime evidence settings", <RuntimeDiagnosticsSettingsPage context={api.backend} />)
      }
    ]
  });
}
