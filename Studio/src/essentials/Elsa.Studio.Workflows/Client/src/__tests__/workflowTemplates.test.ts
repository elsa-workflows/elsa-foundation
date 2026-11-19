import { describe, expect, it, vi, beforeEach } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { templateAvailabilityReason, templateContent, type WorkflowTemplate } from "../workflowTemplates";
import { getCreateInitialDocument } from "../workflow-editor/editorHelpers";
import { createTemplateDocumentAdapter } from "../workflow-editor/templateDocumentAdapter";
import { getWorkflowTemplate, saveWorkflowTemplate } from "../workflowTemplates";
import { sequenceActivity, sequenceNode, leafNode, writeLine } from "./fixtures";
import type { WorkflowDraft } from "../workflowTypes";

vi.mock("../workflowTemplates", async original => ({ ...(await original<typeof import("../workflowTemplates")>()), getWorkflowTemplate: vi.fn(), saveWorkflowTemplate: vi.fn() }));
const context = {} as StudioEndpointContext;
const draft: WorkflowDraft = { id: "template-1", definitionId: "template-1", state: {
  rootActivity: sequenceNode("root", [leafNode("write")]),
  variables: [{ referenceKey: "message", name: "Message", type: { alias: "String", collectionKind: "Single" }, default: { expressionType: "Literal", value: "Hello" } }],
  inputs: [], outputs: []
}, layout: [{ nodeId: "write", x: 12, y: 34 }], activityPresentation: [{ nodeId: "write", displayName: "Greeting" }], validationErrors: [] };
const template: WorkflowTemplate = { id: draft.id, name: "Starter", description: "Description", revision: "r1", createdAt: "2026-10-30T00:00:00Z", lastModifiedAt: "2026-10-30T00:00:00Z", content: templateContent(draft) };
beforeEach(() => { vi.mocked(getWorkflowTemplate).mockReset().mockResolvedValue(structuredClone(template)); vi.mocked(saveWorkflowTemplate).mockReset(); });

describe("workflow templates", () => {
  it("copies authored content, layout and labels without changing the source", () => {
    const content = templateContent(draft);
    const created = getCreateInitialDocument({ name: "New workflow", description: "", rootActivityVersionId: null, templateId: template.id, templateContent: content }, []);
    expect(created.initialState).toEqual(draft.state);
    expect(created.layout).toEqual(draft.layout);
    created.layout![0].x = 999;
    created.activityPresentation![0].displayName = "Changed";
    created.initialState!.variables![0].name = "Changed";
    expect(content.layout[0].x).toBe(12);
    expect(content.activityPresentation![0].displayName).toBe("Greeting");
    expect(draft.state.variables![0].name).toBe("Message");
  });
  it("explains missing dependencies and incomplete roots", () => {
    expect(templateAvailabilityReason(template, [sequenceActivity, writeLine])).toBeUndefined();
    expect(templateAvailabilityReason(template, [sequenceActivity])).toContain(writeLine.activityVersionId);
    expect(templateAvailabilityReason({ ...template, content: { ...template.content, state: { rootActivity: null } } }, [])).toContain("no root activity");
  });
  it("serializes metadata and document writes against the latest revision", async () => {
    vi.mocked(saveWorkflowTemplate).mockImplementation(async (_context, input) => ({ ...template, ...input, id: template.id, revision: input.expectedRevision + "-next" }));
    const adapter = createTemplateDocumentAdapter(); await adapter.load(context, template.id);
    await Promise.all([adapter.save(context, draft), adapter.metadata(context, template.id, { name: "Renamed", description: "New" })]);
    expect(vi.mocked(saveWorkflowTemplate).mock.calls[0][1].expectedRevision).toBe("r1");
    expect(vi.mocked(saveWorkflowTemplate).mock.calls[1][1].expectedRevision).toBe("r1-next");
    expect(vi.mocked(saveWorkflowTemplate).mock.calls[1][1].content).toEqual(template.content);
  });
  it("preserves a failed local name edit during the next document save", async () => {
    vi.mocked(saveWorkflowTemplate).mockRejectedValueOnce(new Error("offline")).mockImplementation(async (_context, input) => ({ ...template, ...input, id: template.id, revision: "r2" }));
    const adapter = createTemplateDocumentAdapter(); await adapter.load(context, template.id);
    await expect(adapter.metadata(context, template.id, { name: "Local name" })).rejects.toThrow("offline");
    await adapter.save(context, draft);
    expect(vi.mocked(saveWorkflowTemplate).mock.calls[1][1].name).toBe("Local name");
  });
  it("does not reload or retry over a conflict, preserving the editable draft", async () => {
    vi.mocked(saveWorkflowTemplate).mockRejectedValue(new Error("changed in another session"));
    const adapter = createTemplateDocumentAdapter(); const loaded = await adapter.load(context, template.id);
    loaded.draft!.layout[0].x = 99;
    await expect(adapter.save(context, loaded.draft!)).rejects.toThrow("changed in another session");
    expect(loaded.draft!.layout[0].x).toBe(99);
    expect(saveWorkflowTemplate).toHaveBeenCalledTimes(1);
    expect(getWorkflowTemplate).toHaveBeenCalledTimes(1);
  });
});
