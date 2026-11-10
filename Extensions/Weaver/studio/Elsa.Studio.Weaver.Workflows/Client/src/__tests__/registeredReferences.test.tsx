import { describe, expect, it } from "vitest";
import type { ElsaStudioModuleApi, StudioAgentRegistry } from "@elsa-workflows/studio-sdk";
import { createAgentRegistry } from "@elsa-workflows/studio-sdk";
import { register } from "../module";
// The Workflows module's own registration, composed beside this package's as the Studio host composes both.
import { registerWorkflowAgent } from "../../../../../../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflowAgent";
import server from "./workflowAgentRegistrations.json";

// Spec 1193 SC-007 (FR-019): over the composed Studio contributions of both Workflows packages and the server registrations
// of Elsa.Weaver.Workflows (workflowAgentRegistrations.json, which Elsa.Weaver.Tests holds equal to the composed server), every
// prompt, guidance text and renderer references only registered tools and result kinds, and each contribution is registered
// under its own package's module identity.
const workflowsModule = "Elsa.Studio.Workflows";
const weaverWorkflowsModule = "Elsa.Studio.Weaver.Workflows";
// The Design-side module (Elsa.Weaver.Workflows) and the Runtime-side package (Elsa.Weaver.Workflows.Runtime, spec 1193 slices F
// and G, with its Executable and Run sources) register under the same Studio module identity; a combined host composes both.
const allTools = [...server.tools, ...server.runtime.tools];
const toolNames = new Set(allTools.map(tool => tool.name));
const resultTypes = new Set(allTools.map(tool => tool.resultType));
const capabilities = new Set([...server.capabilities, ...server.runtime.capabilities]);
const contextKinds = new Set([...server.contextKinds, ...server.librarySource.contextKinds, ...server.versionSource.contextKinds, ...server.runtime.contextKinds,
  ...server.runtime.runSource.contextKinds]);

function compose() {
  const workflows = createAgentRegistry();
  registerWorkflowAgent(workflows);
  const weaver = createAgentRegistry();
  register({ agent: weaver } as unknown as ElsaStudioModuleApi);
  return { workflows, weaver };
}

function contributions(agent: StudioAgentRegistry) {
  return [
    ...agent.promptActions.list(), ...agent.promptStarters.list(), ...agent.capabilities.list(), ...agent.contextProviders.list(),
    ...agent.toolSlots.list(), ...agent.toolContracts.list(), ...agent.resultRenderers.list(), ...(agent.resultControls?.list() ?? [])
  ] as Array<{ id: string; moduleId: string }>;
}

/** Each tool a text names: snake_case tool names, and any "<name> tool" phrase (as the removed get-executable-detail prompt had). */
function namedTools(text: string) {
  const snake = [...text.matchAll(/\b[a-z]+(?:_[a-z]+)+\b/g)].map(match => match[0]);
  const phrased = [...text.matchAll(/\b([a-z][a-z0-9]*(?:[-_][a-z0-9]+)+)\s+tool\b/gi)].map(match => match[1]);
  return [...snake, ...phrased];
}

const samples: unknown[] = [{ id: "definition-1", name: "Onboard customer" }, { definition: { id: "definition-1" }, draft: { id: "draft-1" } },
  { intent: "Send a welcome email", draft: { name: "", description: "" } }, { instanceId: "instance-1" },
  { artifactId: "artifact-1", artifactHash: "sha256:abc", sourceReferenceId: "ref-1" }, { artifactId: "artifact-1", artifactHash: "sha256:abc", attached: true },
  { definitionId: "definition-1", versionId: "version-1", version: "1.0.0" }, { definitionId: "definition-1", versionId: "version-1", attached: true },
  { workflowExecutionId: "run-1", activityExecutionId: "act-1" }, { workflowExecutionId: "run-1", activityExecutionId: null }, { workflowExecutionId: "run-1", attached: true }];

describe("registered references of the Workflows packages (spec 1193 SC-007)", () => {
  it("registers each contribution under its own package's module identity", () => {
    const { workflows, weaver } = compose();
    expect(contributions(workflows).length).toBeGreaterThan(0);
    expect(contributions(weaver).length).toBeGreaterThan(0);
    for (const item of contributions(workflows)) expect([item.id, item.moduleId]).toEqual([item.id, workflowsModule]);
    for (const item of contributions(weaver)) expect([item.id, item.moduleId]).toEqual([item.id, weaverWorkflowsModule]);
  });

  it("renders only result kinds a registered tool produces, for the module that owns them", () => {
    const { workflows, weaver } = compose();
    const renderers = [...workflows.resultRenderers.list(), ...weaver.resultRenderers.list()];
    for (const renderer of renderers) {
      for (const type of renderer.resultTypes) expect([renderer.id, type, resultTypes.has(type)]).toEqual([renderer.id, type, true]);
      // A renderer contributed for another module's results says so; otherwise it presents its own module's results.
      expect(renderer.resourceModuleIds ?? [renderer.moduleId]).toEqual([workflowsModule]);
    }
    for (const control of workflows.resultControls?.list() ?? [])
      for (const type of control.resultTypes) expect([control.id, resultTypes.has(type)]).toEqual([control.id, true]);
    // Every renderer a server tool names is registered, for its own result kind.
    for (const tool of server.tools.filter(item => item.rendererId)) {
      const renderer = renderers.find(item => item.id === tool.rendererId);
      expect(renderer?.resultTypes, tool.name).toContain(tool.resultType);
    }
  });

  it("binds every client tool contract to a registered server tool, its slot and registered renderers", () => {
    const { workflows, weaver } = compose();
    const rendererIds = new Set([...workflows.resultRenderers.list(), ...weaver.resultRenderers.list()].map(item => item.id));
    const slots = new Set(workflows.toolSlots.list().map(item => item.id));
    expect(weaver.toolContracts.list()).toEqual([]);
    for (const contract of workflows.toolContracts.list()) {
      const tool = server.tools.find(item => item.name === contract.id);
      expect(tool?.executionLocation, contract.id).toBe(contract.executionLocation);
      expect(slots.has(contract.slotId)).toBe(true);
      for (const id of contract.resultRendererIds ?? []) expect([contract.id, id, rendererIds.has(id)]).toEqual([contract.id, id, true]);
    }
    // And every server tool that runs in the browser's working state has the client contract that executes it.
    const contracts = new Set(workflows.toolContracts.list().map(item => item.id));
    for (const tool of server.tools.filter(item => item.executionLocation === "clientWorkingState"))
      expect([tool.name, contracts.has(tool.name)]).toEqual([tool.name, true]);
  });

  it("names only registered tools, capabilities and context kinds in every prompt and starter", () => {
    const { workflows, weaver } = compose();
    for (const capability of workflows.capabilities.list()) expect(capabilities.has(capability.id), capability.id).toBe(true);
    for (const starter of workflows.promptStarters.list()) {
      for (const name of namedTools(starter.prompt)) expect([starter.id, name, toolNames.has(name)]).toEqual([starter.id, name, true]);
      for (const required of starter.requiredCapabilities ?? []) expect([starter.id, capabilities.has(required)]).toEqual([starter.id, true]);
    }
    const actions = weaver.promptActions.list();
    expect(actions.length).toBeGreaterThan(0);
    for (const action of actions) {
      for (const text of [action.label, action.description ?? ""])
        for (const name of namedTools(text)) expect([action.id, name, toolNames.has(name)]).toEqual([action.id, name, true]);
      for (const sample of samples) {
        const prompt = action.createPrompt(sample);
        for (const name of namedTools(prompt.message)) expect([action.id, name, toolNames.has(name)]).toEqual([action.id, name, true]);
        for (const attachment of prompt.attachments ?? []) {
          expect(attachment.moduleId).toBe(workflowsModule);
          expect([action.id, attachment.contentType, contextKinds.has(attachment.contentType)]).toEqual([action.id, attachment.contentType, true]);
        }
        expect(prompt.source?.moduleId).toBe(weaverWorkflowsModule);
      }
    }
  });

  it("explains an Executable and a version only through their registered read tools, attaching each exact identity once (slice F)", () => {
    const { weaver } = compose();
    const executable = weaver.promptActions.list().find(item => item.id === "weaver.workflows.explain-executable")!;
    const version = weaver.promptActions.list().find(item => item.id === "weaver.workflows.explain-version")!;
    expect(executable).toMatchObject({ placement: "selection", contextKind: "workflow-executable", moduleId: weaverWorkflowsModule });
    expect(version).toMatchObject({ placement: "selection", contextKind: "workflow-version", moduleId: weaverWorkflowsModule });

    const fromList = executable.createPrompt({ artifactId: "artifact-1", artifactHash: "sha256:abc", definitionId: "definition-1" });
    expect(new Set(namedTools(fromList.message))).toEqual(new Set(["workflow_read_executable"]));
    expect(fromList.attachments).toEqual([expect.objectContaining({
      id: "workflows.executable:artifact-1", moduleId: workflowsModule, contentType: "workflows.executable", schemaVersion: "1",
      target: { moduleId: workflowsModule, resourceType: "workflow-executable", resourceId: "artifact-1", revision: "sha256:abc" },
      content: { artifactId: "artifact-1", artifactHash: "sha256:abc", sourceReferenceId: null }
    })]);
    expect(server.runtime.contextKinds).toContain(fromList.attachments![0].contentType);
    // The inspector contributes the context itself; a context without the hash is never sent half-formed.
    expect(executable.createPrompt({ artifactId: "artifact-1", artifactHash: "sha256:abc", attached: true }).attachments).toEqual([]);
    expect(executable.createPrompt({ artifactId: "artifact-1" }).attachments).toEqual([]);

    const fromHistory = version.createPrompt({ definitionId: "definition-1", versionId: "version-1", version: "1.0.0" });
    expect(new Set(namedTools(fromHistory.message))).toEqual(new Set(["workflow_read_version"]));
    expect(fromHistory.message).toContain("compareToVersionId");
    expect(fromHistory.attachments).toEqual([expect.objectContaining({
      id: "workflows.version:version-1", moduleId: workflowsModule, contentType: "workflows.version",
      target: { moduleId: workflowsModule, resourceType: "workflow-version", resourceId: "version-1", revision: "version-1" },
      content: { definitionId: "definition-1", versionId: "version-1" }
    })]);
    expect(server.versionSource.contextKinds).toContain(fromHistory.attachments![0].contentType);
    expect(version.createPrompt({ definitionId: "definition-1", versionId: "version-1", attached: true }).attachments).toEqual([]);
    expect(version.createPrompt({ versionId: "version-1" }).attachments).toEqual([]);
  });

  it("explains one exact Run only through the registered Run read, attaching the Run and its selection once (slice G)", () => {
    const { weaver } = compose();
    const run = weaver.promptActions.list().find(item => item.id === "weaver.workflows.explain-instance")!;
    expect(run).toMatchObject({ placement: "selection", contextKind: "workflow-instance", moduleId: weaverWorkflowsModule });
    const selected = run.createPrompt({ workflowExecutionId: "run-1", activityExecutionId: "act-1", status: "Faulted" });
    expect(new Set(namedTools(selected.message))).toEqual(new Set(["workflow_read_run"]));
    expect(selected.attachments).toEqual([expect.objectContaining({
      id: "workflows.run:run-1", moduleId: workflowsModule, contentType: "workflows.run", schemaVersion: "1",
      target: { moduleId: workflowsModule, resourceType: "workflow-run", resourceId: "run-1", revision: "act-1" },
      content: { workflowExecutionId: "run-1", activityExecutionId: "act-1" }
    })]);
    expect(server.runtime.runSource.contextKinds).toContain(selected.attachments![0].contentType);
    expect(run.createPrompt({ workflowExecutionId: "run-1", activityExecutionId: null }).attachments).toEqual([expect.objectContaining({
      target: { moduleId: workflowsModule, resourceType: "workflow-run", resourceId: "run-1", revision: "run-1" },
      content: { workflowExecutionId: "run-1", activityExecutionId: null }
    })]);
    // The run page contributes the context itself; a Run reference never names "the latest run" or a definition instead.
    expect(run.createPrompt({ workflowExecutionId: "run-1", attached: true }).attachments).toEqual([]);
    expect(run.createPrompt({ definitionId: "definition-1" }).attachments).toEqual([]);
    expect(run.createPrompt({ workflowExecutionId: "run-1", activityExecutionId: "run-1" }).attachments).toEqual([]);
    expect(toolNames.has("workflow_read_run")).toBe(true);
    // No run control is a registered tool: retry, cancel, delete and dispatch stay with the run page.
    expect([...toolNames].filter(name => /retry|cancel|delete|dispatch/.test(name))).toEqual([]);
  });

  it("contributes the Executable Inspector's, the version history's and the run page's context under the Workflows module", () => {
    const { workflows } = compose();
    const ids = workflows.contextProviders.list().map(item => item.id);
    expect(ids).toEqual(expect.arrayContaining(["workflows.executable-inspector", "workflows.version-history", "workflows.run-inspector"]));
  });

  it("fails on the dangling references this slice removed", () => {
    expect(namedTools("call the get-executable-detail tool with this artifact id")).toContain("get-executable-detail");
    expect(toolNames.has("get-executable-detail")).toBe(false);
    expect(toolNames.has("workflow_read_executable")).toBe(true);
    expect(resultTypes.has("workflows.proposal")).toBe(false);
  });
});
