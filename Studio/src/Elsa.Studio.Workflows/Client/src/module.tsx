import "@xyflow/react/dist/style.css";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { setDialogs } from "./workflow-editor/dialogs";
import { ActivityAvailabilityPage } from "./ActivityAvailabilityPage";
import { RuntimeDiagnosticsSettingsPage } from "./RuntimeDiagnosticsSettingsPage";
import { createObjectExpressionEditorContribution } from "./objectExpressionEditor";
import {
  WorkflowExecutableInspectorPage,
  WorkflowExecutablesPage,
  WorkflowInstanceDetailsPage,
  WorkflowInstancesPage,
  WorkflowManagementPage
} from "./workflow-editor/pages";
import "./styles.css";
import { registerVariableReferenceContribution } from "./variableReferenceContribution";
import { registerInputReferenceContribution } from "./inputReferenceContribution";

// Re-exported for the test suite (src/__tests__/module.test.tsx), which imports these connect-end
// helpers directly alongside register().
export { isConnectEndOverExistingWorkflowNode, resolveConnectEndSource } from "./workflow-editor/editorHelpers";
export { capabilityIds, clearApiCapabilityCache, resolveCapabilityLink } from "./api/capabilities";
// WorkflowDesignerPanelContext is the TContext behind the workflow.designer.panels slot — exported so
// contributed-panel authors can type their `context` prop instead of hand-copying the shape.
export type { WorkflowConnectSource, WorkflowDesignerPanelContext } from "./workflow-editor/editorTypes";

export function register(api: ElsaStudioModuleApi) {
  setDialogs(api.dialogs);
  registerVariableReferenceContribution(api.expressionEditors);
  api.expressionEditors.add(createObjectExpressionEditorContribution(() => api.propertyEditors.list()));
  registerInputReferenceContribution(api.expressionEditors);
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
        { title: "Definitions", path: "/workflows/definitions", iconColor: "#0ea5e9" },
        { title: "Executables", path: "/workflows/executables", iconColor: "#0ea5e9" },
        { title: "Runs", path: "/workflows/instances", iconColor: "#0ea5e9" },
        { title: "Runtime Diagnostics", path: "/workflows/runtime-diagnostics", iconColor: "#0ea5e9" },
        { title: "Activity Availability", path: "/workflows/activity-availability", iconColor: "#0ea5e9" }
      ]
    },
    routes: [
      {
        id: "workflows-definitions",
        path: "/workflows/definitions",
        label: "Workflow definitions",
        component: () => <WorkflowManagementPage context={api.backend} ai={api.ai} propertyEditors={api.propertyEditors.list()} expressionEditors={api.expressionEditors?.list() ?? []} workflowDesignerPanels={api.workflowDesigner.panels.list()} autosaveEnabledByDefault={api.runtime.workflows?.autosaveEnabledByDefault ?? true} />
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => <WorkflowExecutablesPage context={api.backend} ai={api.ai} />
      },
      {
        id: "workflows-executable-inspector",
        path: "/workflows/executables/:artifactId",
        label: "Executable Inspector",
        component: () => <WorkflowExecutableInspectorPage context={api.backend} ai={api.ai} />
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow runs",
        component: () => <WorkflowInstancesPage context={api.backend} />
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow run",
        component: ({ navigate }) => <WorkflowInstanceDetailsPage context={api.backend} ai={api.ai} navigate={navigate} />
      },
      {
        id: "workflows-activity-availability",
        path: "/workflows/activity-availability",
        label: "Activity availability",
        component: () => <ActivityAvailabilityPage context={api.backend} />
      },
      {
        id: "workflows-runtime-diagnostics",
        path: "/workflows/runtime-diagnostics",
        label: "Runtime diagnostics",
        component: () => <RuntimeDiagnosticsSettingsPage context={api.backend} />
      }
    ]
  });
}
