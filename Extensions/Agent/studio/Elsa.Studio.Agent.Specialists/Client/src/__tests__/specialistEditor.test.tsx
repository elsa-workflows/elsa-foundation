import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { StudioHttpError, type StudioActivityDefinitionImplementationState, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { SpecialistEditor } from "../SpecialistEditor";
import { initialSpecialistManifest, readSpecialistManifest, secondsToMilliseconds, specialistProblems, unreadableMembers } from "../specialistManifest";

const cleanup = new Set<() => void>();
afterEach(() => { for (const dispose of cleanup) dispose(); cleanup.clear(); });

const temperatureSchema = { type: "object", additionalProperties: false, properties: { temperature: { type: "number", minimum: 0, maximum: 1 } } };
type Connection = { name: string; displayName: string; status: string; settings: Record<string, string> };

interface Fixture {
  temperature?: boolean;
  capabilities?: "unusable" | "denied" | "failed";
  providers?: "denied";
  connections?: Record<string, Connection[]> | "denied";
}

function fakeContext(fixture: Fixture = {}) {
  const getJson = vi.fn(async (path: string) => {
    if (path !== "/agent/specialists/model-providers") throw new Error(`unexpected ${path}`);
    if (fixture.providers === "denied") throw new StudioHttpError(403, "Forbidden");
    return { items: [
      { providerKey: "anthropic", connectionTypeName: "specialist-anthropic", displayName: "Anthropic" },
      { providerKey: "azure-openai", connectionTypeName: "specialist-azure-openai", displayName: "Azure OpenAI" }
    ] };
  });
  const connections = fixture.connections ?? {
    "specialist-anthropic": [
      { name: "writer", displayName: "Writer", status: "Ready", settings: { modelId: "claude-fable-5-1", apiKey: "never-read" } },
      { name: "broken", displayName: "Broken", status: "Ready", settings: {} }
    ],
    "specialist-azure-openai": [{ name: "azure", displayName: "Azure", status: "ready", settings: { modelId: "gpt-x" } }]
  };
  const postJson = vi.fn(async (path: string, body: { typeName?: string; search?: string }) => {
    if (path === "/connections/picker") {
      if (connections === "denied") throw new StudioHttpError(403, "Forbidden");
      return { items: (connections[body.typeName ?? ""] ?? []).filter(item => !body.search || item.name.includes(body.search)) };
    }
    if (path === "/agent/specialists/model-capabilities") {
      if (fixture.capabilities === "unusable") throw new StudioHttpError(422, "Unprocessable");
      if (fixture.capabilities === "denied") throw new StudioHttpError(403, "Forbidden");
      if (fixture.capabilities === "failed") throw new Error("offline");
      return { settingsSchema: fixture.temperature === false ? { type: "object", properties: {} } : temperatureSchema, supportsJsonSchema: true, maximumOutputTokens: 4096 };
    }
    throw new Error(`unexpected ${path}`);
  });
  return { context: { http: { getJson, postJson } } as unknown as StudioEndpointContext, getJson, postJson };
}

async function settle() {
  for (let index = 0; index < 10; index++) await new Promise(resolve => setTimeout(resolve, 0));
}

/** Renders the editor as the generic editor does: it owns the value and passes every change back in. */
async function render(context: StudioEndpointContext, payload: unknown, options: { readOnly?: boolean; schema?: string } = {}) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root: Root = createRoot(container);
  const changes: StudioActivityDefinitionImplementationState[] = [];
  let value: StudioActivityDefinitionImplementationState = { payload, layout: [] };
  const draw = () => flushSync(() => root.render(
    <SpecialistEditor context={context} definitionId="definition-1" draftId="draft-1" revision={1} providerKey="elsa.specialist"
      providerSchemaVersion={options.schema ?? "2"} manifestFingerprint="fingerprint" historyResetKey="draft-1:1" value={value} readOnly={options.readOnly ?? false}
      onChange={next => { changes.push(next); value = next; draw(); }} />));
  draw();
  await settle();
  cleanup.add(() => { root.unmount(); container.remove(); });
  return { container, changes, latest: () => value.payload as any };
}

function type(element: HTMLInputElement | HTMLTextAreaElement, text: string) {
  Object.getOwnPropertyDescriptor(Object.getPrototypeOf(element), "value")!.set!.call(element, text);
  element.dispatchEvent(new Event("input", { bubbles: true }));
}

const connectionSelect = (container: HTMLElement) => field(container, "Model connection") as unknown as HTMLSelectElement;
const field = (container: HTMLElement, label: string) =>
  [...container.querySelectorAll("label.studio-field")].find(x => x.querySelector(".studio-field-label")?.textContent === label)!.querySelector("input, textarea, select") as HTMLInputElement;
const hasField = (container: HTMLElement, label: string) => [...container.querySelectorAll(".studio-field-label")].some(x => x.textContent === label);
const button = (container: HTMLElement, text: string) => [...container.querySelectorAll("button")].find(x => x.textContent?.includes(text));

function choose(select: HTMLSelectElement, text: string) {
  select.value = [...select.options].find(option => option.textContent?.startsWith(text))!.value;
  select.dispatchEvent(new Event("change", { bubbles: true }));
}

const configured = () => ({
  ...initialSpecialistManifest(),
  instructions: "Write short articles.",
  model: { providerKey: "anthropic", connectionName: "writer", modelId: "claude-fable-5-1" },
  inputConstraints: { brief: { maxLength: 200 } },
  allowContinuity: false
});

describe("specialist editor", () => {
  it("links the saved draft revision to the agent playground", async () => {
    const { context } = fakeContext();
    const { container } = await render(context, { ...initialSpecialistManifest(), allowContinuity: false });

    expect(container.querySelector<HTMLAnchorElement>(".agent-specialist-editor__playground a")!.getAttribute("href")).toBe("/agents/drafts/draft-1/playground");
  });

  it("lists every provider's connections in one choice and takes the provider and model from the connection", async () => {
    const { context, postJson } = fakeContext();
    const { container, latest } = await render(context, { ...initialSpecialistManifest(), allowContinuity: false });

    expect(hasField(container, "Provider")).toBe(false);
    const select = connectionSelect(container);
    expect([...select.querySelectorAll("optgroup")].map(x => x.label)).toEqual(["Anthropic", "Azure OpenAI"]);
    expect([...select.options].find(x => x.textContent?.startsWith("Broken"))!.disabled).toBe(true);
    expect(postJson).toHaveBeenCalledWith("/connections/picker", { typeName: "specialist-azure-openai" }, expect.anything());

    choose(select, "Azure");
    await settle();
    expect(latest().model).toEqual({ providerKey: "azure-openai", connectionName: "azure", modelId: "gpt-x" });
    choose(connectionSelect(container), "Writer");
    await settle();

    expect(latest().model).toEqual({ providerKey: "anthropic", connectionName: "writer", modelId: "claude-fable-5-1" });
    expect(JSON.stringify(latest())).not.toContain("never-read");
    expect(postJson).toHaveBeenCalledWith("/agent/specialists/model-capabilities", latest().model, expect.anything());
    expect(container.querySelector("[data-testid='specialist-model']")?.textContent).toBe("Model: claude-fable-5-1 from Anthropic · up to 4,096 output tokens");
    type(field(container, "Temperature"), "0.3");
    expect(latest().settings).toEqual({ temperature: 0.3 });
  });

  it("patches only the member it edits, so unknown and unedited saved values stay as saved", async () => {
    const { context } = fakeContext();
    const saved = { ...configured(), limits: { maximumModelCalls: 4, timeoutMilliseconds: 1500 }, extra: { kept: true } };
    const { container, latest } = await render(context, saved, { schema: "1" });

    expect(container.textContent).toContain("The saved settings contain extra, which agents do not accept");
    expect(field(container, "Timeout (seconds)").value).toBe("1.5");
    expect(field(container, "Timeout (seconds)").getAttribute("aria-invalid")).toBeNull();
    type(field(container, "Instructions"), "Write shorter articles.");
    await settle();

    expect(latest()).toEqual({ ...saved, instructions: "Write shorter articles." });
    button(container, "Remove unknown settings")!.click();
    await settle();
    expect("extra" in latest()).toBe(false);
    expect(latest().allowContinuity).toBe(false);
  });

  it("hides an unsupported setting control but shows the saved value until the author removes it", async () => {
    const { context } = fakeContext({ temperature: false });
    const { container, latest } = await render(context, { ...configured(), settings: { temperature: 0.7 } });

    expect(hasField(container, "Temperature")).toBe(false);
    expect(container.textContent).toContain("does not support the saved temperature (0.7) setting");
    expect(latest().settings).toEqual({ temperature: 0.7 });
    button(container, "Remove unsupported settings")!.click();
    await settle();

    expect(latest().settings).toEqual({});
  });

  it("flags a saved setting named like an inherited property", async () => {
    const { context } = fakeContext();
    const { container } = await render(context, { ...configured(), settings: { constructor: 1 } });

    expect(container.textContent).toContain("does not support the saved constructor (1) setting");
  });

  it("keeps unsaved text in one number field while another field is edited", async () => {
    const { context } = fakeContext();
    const { container, latest } = await render(context, configured());

    type(field(container, "Maximum model calls"), "abc");
    type(field(container, "Maximum output tokens"), "100");
    await settle();

    expect(latest().limits).toMatchObject({ maximumModelCalls: 8, maximumOutputTokens: 100 });
    expect(field(container, "Maximum model calls").value).toBe("abc");
    expect(container.textContent).toContain("Not saved: enter a whole number. The saved value 8 is kept.");
  });

  it("explains a saved temperature outside the model's range", async () => {
    const { context } = fakeContext();
    const { container } = await render(context, { ...configured(), settings: { temperature: 1.4 } });

    expect(field(container, "Temperature").value).toBe("1.4");
    expect(container.textContent).toContain("This model accepts a temperature from 0 up to 1.");
  });

  it("keeps a saved connection the author can no longer see, and finds it with a server search", async () => {
    const many = Array.from({ length: 50 }, (_, index) => ({ name: `conn-${index}`, displayName: `Conn ${index}`, status: "Ready", settings: { modelId: "m" } }));
    const { context, postJson } = fakeContext({ connections: { "specialist-anthropic": many, "specialist-azure-openai": [] } });
    const { container, latest } = await render(context, { ...configured(), model: { providerKey: "anthropic", connectionName: "retired", modelId: "claude-fable-5-1" } });

    expect(container.textContent).toContain("The saved connection “retired” is not available to you");
    expect(connectionSelect(container).selectedOptions[0].textContent).toBe("retired (saved)");
    expect(latest().model.connectionName).toBe("retired");
    type(field(container, "Search connections"), "conn-4");
    await new Promise(resolve => setTimeout(resolve, 300));
    await settle();

    expect(postJson).toHaveBeenCalledWith("/connections/picker", { typeName: "specialist-anthropic", search: "conn-4" }, expect.anything());
    // A narrowed result keeps the search, so it can be changed or cleared.
    expect(field(container, "Search connections").value).toBe("conn-4");
    expect([...connectionSelect(container).options].filter(x => x.value).map(x => x.textContent)).toContain("retired (saved)");
    expect(container.textContent).toContain("is not among the connections matching this search");
    type(field(container, "Search connections"), "");
    await new Promise(resolve => setTimeout(resolve, 300));
    await settle();
    expect(hasField(container, "Search connections")).toBe(true);
    expect(postJson).toHaveBeenLastCalledWith("/connections/picker", { typeName: "specialist-azure-openai" }, expect.anything());
  });

  it("offers the connection's current model when the saved one is stale", async () => {
    const { context } = fakeContext({ capabilities: "unusable" });
    const { container, latest } = await render(context, { ...configured(), model: { providerKey: "anthropic", connectionName: "writer", modelId: "old-model" } });

    expect(container.textContent).toContain("This connection now uses claude-fable-5-1, but the agent still names old-model.");
    expect(container.textContent).toContain("cannot be used by agents right now");
    button(container, "Use claude-fable-5-1")!.click();
    await settle();

    expect(latest().model.modelId).toBe("claude-fable-5-1");
  });

  it("tells a reader without author permission why model choices are missing while showing the saved reference", async () => {
    const { context } = fakeContext({ providers: "denied", capabilities: "denied" });
    const { container, changes } = await render(context, configured(), { readOnly: true });

    expect(container.textContent).toContain("You can see the saved model, but choosing a model needs permission to author agents.");
    expect(container.textContent).toContain("Reading what this model supports needs permission to author agents.");
    expect(container.textContent).not.toContain("cannot be used by agents");
    expect(container.querySelector("[data-testid='specialist-model']")?.textContent).toContain("claude-fable-5-1");
    expect(changes).toHaveLength(0);
  });

  it("says when listing connections needs another permission", async () => {
    const { context } = fakeContext({ connections: "denied" });
    const { container } = await render(context, configured());

    expect(container.textContent).toContain("Listing connections needs permission to read workflow designs or manage connections.");
  });

  it("retries a failed capability read, also in read-only mode", async () => {
    const fixture: Fixture = { capabilities: "failed" };
    const { context, postJson } = fakeContext(fixture);
    const { container } = await render(context, configured(), { readOnly: true });

    expect(container.textContent).toContain("What this model supports could not be read.");
    fixture.capabilities = undefined;
    button(container, "Retry")!.click();
    await settle();

    expect(postJson.mock.calls.filter(call => call[0] === "/agent/specialists/model-capabilities").length).toBeGreaterThan(1);
    expect(container.textContent).toContain("up to 4,096 output tokens");
  });

  it("explains number text it cannot save and never writes it", async () => {
    const { context } = fakeContext();
    const { container, latest } = await render(context, configured());
    const calls = field(container, "Maximum model calls");

    type(calls, "2.5");
    await settle();
    expect(calls.value).toBe("2.5");
    expect(calls.getAttribute("aria-invalid")).toBe("true");
    const message = document.getElementById(calls.getAttribute("aria-describedby")!)!;
    expect(message.textContent).toBe("Not saved: enter a whole number. The saved value 8 is kept.");
    expect(latest().limits.maximumModelCalls).toBe(8);

    type(calls, "");
    await settle();
    expect(latest().limits.maximumModelCalls).toBe(8);
    expect(container.textContent).toContain("Not saved: enter a whole number.");

    type(calls, "40");
    await settle();
    expect(latest().limits.maximumModelCalls).toBe(40);
    expect(container.textContent).toContain("Use between 1 and 32 model calls.");

    type(field(container, "Timeout (seconds)"), "90.5");
    type(field(container, "Maximum output tokens"), "5000");
    await settle();
    expect(latest().limits).toMatchObject({ timeoutMilliseconds: 90500, maximumOutputTokens: 5000 });
    expect(container.textContent).toContain("Use between 1 and 4096 output tokens for this model.");
    type(field(container, "Maximum output tokens"), "");
    await settle();
    expect("maximumOutputTokens" in latest().limits).toBe(false);
  });

  it("says where to create a connection when there is none, and lists no empty provider", async () => {
    const { context } = fakeContext({ connections: {} });
    const { container } = await render(context, initialSpecialistManifest());

    expect(container.textContent).toContain("There are no model connections you can use yet.");
    const select = connectionSelect(container);
    expect(select.getAttribute("aria-invalid")).toBe("true");
    expect(document.getElementById(select.getAttribute("aria-describedby")!)?.textContent).toBe("Choose a model connection.");
    expect(connectionSelect(container).querySelectorAll("optgroup")).toHaveLength(0);
  });

  it("offers continuity only on schema 2", async () => {
    const { context } = fakeContext();
    const { container, latest } = await render(context, configured());
    (container.querySelector(".agent-specialist-continuity input") as HTMLInputElement).click();
    await settle();
    expect(latest().allowContinuity).toBe(true);

    const legacy = await render(fakeContext().context, { ...configured(), allowContinuity: undefined }, { schema: "1" });
    expect(legacy.container.querySelector(".agent-specialist-continuity")).toBeNull();
  });

  it("disables every control when the generic editor is read-only", async () => {
    const { context } = fakeContext();
    const { container } = await render(context, { ...configured(), settings: { temperature: 0.3 } }, { readOnly: true });

    const controls = [...container.querySelectorAll("input, textarea, select")] as HTMLInputElement[];
    expect(controls.length).toBeGreaterThan(4);
    for (const control of controls) expect(control.disabled || control.closest("fieldset")?.disabled).toBe(true);
    expect([...container.querySelectorAll("button")].filter(x => !x.disabled && !x.closest("fieldset[disabled]"))).toHaveLength(0);
    // Read-only still shows what the saved model would need, without offering to change it.
    expect(container.textContent).not.toContain("Remove unsupported settings");
  });
});

describe("specialist manifest", () => {
  it("reads a payload for display and reports field problems", () => {
    expect(readSpecialistManifest({ instructions: "Hi", model: { providerKey: "a", connectionName: "b", modelId: "c" }, settings: {}, inputConstraints: {}, resultConstraints: { r: {} },
      limits: { maximumModelCalls: 2, timeoutMilliseconds: 5000 } }, "1")).toEqual({
      instructions: "Hi", model: { providerKey: "a", connectionName: "b", modelId: "c" }, settings: {}, inputConstraints: {}, resultConstraints: { r: {} },
      limits: { maximumModelCalls: 2, timeoutMilliseconds: 5000 }
    });
    expect(specialistProblems(initialSpecialistManifest())).toEqual({ instructions: "Instructions are required.", model: "Choose a model connection." });
    expect(specialistProblems({ ...configured(), limits: { maximumModelCalls: 1, timeoutMilliseconds: 500 } }).timeout).toBeDefined();
    expect(unreadableMembers({ limits: { maximumModelCalls: "8" }, other: 1 })).toEqual({ unknown: ["other"], malformed: ["limits.maximumModelCalls"] });
    expect(secondsToMilliseconds("1.5")).toBe(1500);
    expect(secondsToMilliseconds("1.0005")).toBeNull();
  });
});
