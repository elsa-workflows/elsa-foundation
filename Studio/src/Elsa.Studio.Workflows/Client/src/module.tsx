import "@xyflow/react/dist/style.css";
import { lazy, type ReactNode } from "react";
import { authSessionEndedEvent, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { setDialogs } from "./workflow-editor/dialogs";
import { createObjectExpressionEditorContribution } from "./objectExpressionEditor";
import "./styles.css";
import { registerVariableReferenceContribution } from "./variableReferenceContribution";
import { registerInputReferenceContribution } from "./inputReferenceContribution";
import { WorkflowLazyBoundary } from "./WorkflowLazyBoundary";
import { activityGraphImplementationEditorContribution } from "./activityGraphContribution";
import { clearActivityDefinitionRecoveryForIdentity } from "./activityDefinitionRecovery";

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

// Re-exported for the test suite (src/__tests__/module.test.tsx), which imports these connect-end
// helpers directly alongside register().
export { isConnectEndOverExistingWorkflowNode, resolveConnectEndSource } from "./workflow-editor/editorHelpers";
export { capabilityIds, clearApiCapabilityCache, resolveCapabilityLink } from "./api/capabilities";
// WorkflowDesignerPanelContext is the TContext behind the workflow.designer.panels slot — exported so
// contributed-panel authors can type their `context` prop instead of hand-copying the shape.
export type { WorkflowConnectSource, WorkflowDesignerPanelContext } from "./workflow-editor/editorTypes";
export { createEnumWorkflowRunInputEditorContribution } from "./workflowRunInputEditorContributions";
export type { EnumWorkflowRunInputEditorOptions } from "./workflowRunInputEditorContributions";
export { activityDefinitionsObservationEvent } from "./activityDefinitionObservability";
export type { ActivityDefinitionsObservation } from "./activityDefinitionObservability";

export function register(api: ElsaStudioModuleApi) {
  setDialogs(api.dialogs);
  registerVariableReferenceContribution(api.expressionEditors);
  api.expressionEditors.add(createObjectExpressionEditorContribution(() => api.propertyEditors.list()));
  registerInputReferenceContribution(api.expressionEditors);
  api.activityEditors.add(activityGraphImplementationEditorContribution);
  if (api.runtime.identity?.subject && api.runtime.identity.tenantId && typeof window !== "undefined") {
    window.addEventListener(authSessionEndedEvent, () => clearActivityDefinitionRecoveryForIdentity(api.runtime.identity), { once: true });
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
      items: [
        { id: "workflows-definitions", title: "Workflow Definitions", path: "/workflows/definitions", iconColor: "#0ea5e9" },
        { id: "workflows-activity-definitions", title: "Activity Definitions", path: "/workflows/activity-definitions", iconColor: "#0ea5e9" },
        { title: "Executables", path: "/workflows/executables", iconColor: "#0ea5e9" },
        { title: "Runs", path: "/workflows/instances", iconColor: "#0ea5e9" },
        { title: "Runtime Diagnostics", path: "/workflows/runtime-diagnostics", iconColor: "#0ea5e9" },
        { title: "Activity Availability", path: "/workflows/activity-availability", iconColor: "#0ea5e9" }
      ]
    },
    routes: [
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
        component: () => deferred("Activity Definition upgrades", <ActivityUpgradeWorkbenchPage context={api.backend} />)
      },
      {
        id: "workflows-activity-definitions",
        path: "/workflows/activity-definitions",
        label: "Activity Definitions",
        component: ({ navigate }) => deferred("activity definitions", <ActivityDefinitionsPage context={api.backend} activityEditors={() => api.activityEditors.list()} inputEditors={runInputEditors} runtime={api.runtime} navigateToStudioPath={navigate} />)
      },
      {
        id: "workflows-definitions",
        path: "/workflows/definitions",
        label: "Workflow definitions",
        component: () => deferred("workflow definitions", <WorkflowManagementPage context={api.backend} ai={api.ai} propertyEditors={api.propertyEditors.list()} expressionEditors={api.expressionEditors?.list() ?? []} runInputEditors={runInputEditors()} workflowDesignerPanels={api.workflowDesigner.panels.list()} autosaveEnabledByDefault={api.runtime.workflows?.autosaveEnabledByDefault ?? true} />)
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => deferred("workflow executables", <WorkflowExecutablesPage context={api.backend} ai={api.ai} runInputEditors={runInputEditors()} />)
      },
      {
        id: "workflows-executable-inspector",
        path: "/workflows/executables/:artifactId",
        label: "Executable Inspector",
        component: () => deferred("executable inspector", <WorkflowExecutableInspectorPage context={api.backend} ai={api.ai} runInputEditors={runInputEditors()} />)
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
        component: ({ navigate }) => deferred("workflow run", <WorkflowInstanceDetailsPage context={api.backend} ai={api.ai} expressionEditors={api.expressionEditors.list()} navigate={navigate} />)
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
        label: "Runtime diagnostics",
        component: () => deferred("runtime diagnostics", <RuntimeDiagnosticsSettingsPage context={api.backend} />)
      }
    ]
  });
}
