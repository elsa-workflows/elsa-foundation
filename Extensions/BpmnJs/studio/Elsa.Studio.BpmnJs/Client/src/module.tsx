import type { ElsaStudioModuleApi, StudioWorkflowDesignerCanvasContribution } from "@elsa-workflows/studio-sdk";

export const bpmnJsCanvasEditor: StudioWorkflowDesignerCanvasContribution = {
  id: "bpmn-js", title: "bpmn-js", order: 100,
  protocol: { id: "elsa.workflows.bpmn-document-canvas", version: 1 },
  supportedResourceKinds: ["workflow-definition", "activity-definition-graph"],
  supportedStructures: [{ kind: "elsa.bpmn.structure", schemaVersions: ["2.0.0"] }],
  load: () => import("./BpmnDocumentJsCanvas").then(module => ({ default: module.BpmnDocumentJsCanvasEditor }))
};

export function register(api: ElsaStudioModuleApi) {
  if (!api.workflowDesigner.canvasEditors) throw new Error("bpmn-js requires a host with the Workflows canvas editor registry.");
  api.workflowDesigner.canvasEditors.add(bpmnJsCanvasEditor);
}
