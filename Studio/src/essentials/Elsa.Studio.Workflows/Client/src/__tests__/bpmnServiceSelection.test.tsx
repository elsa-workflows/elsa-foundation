import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { beforeEach, expect, it, vi } from "vitest";
import type { StudioBackendSession } from "@elsa-workflows/studio-sdk";
import { BpmnServiceBindingEditor } from "../bpmn/BpmnServiceBindingEditor";
import { directBindingProperty, directServiceBinding } from "../bpmn/bpmnDirectBinding";
import { hasCapabilityLink } from "../api/capabilities";
import { listPublicationSlots } from "../api/publishing";
import { getExecutable } from "../api/runtime";
import type { BpmnElement } from "../bpmn/bpmnTypes";
import type { WorkflowExecutableDetails, WorkflowInput } from "../workflowTypes";

vi.mock("../api/capabilities", () => ({ capabilityIds: { workflowDesign: "design", runtime: "runtime", bpmnPublishing: "bpmn-publishing" }, hasCapabilityLink: vi.fn() }));
vi.mock("../api/publishing", () => ({ listPublicationSlots: vi.fn() }));
vi.mock("../api/runtime", () => ({ getExecutable: vi.fn() }));
vi.mock("../workflow-editor/RunWorkflowPicker", () => ({ RunWorkflowPicker: ({ onChange }: { onChange(id: string): void }) =>
  <><button type="button" onClick={() => onChange("definition-1")}>Choose worker</button><button type="button" onClick={() => onChange("definition-2")}>Choose other worker</button></> }));

function executable(): WorkflowExecutableDetails {
  return { artifactId: "artifact-1", artifactHash: "a".repeat(64), createdAt: "2026-01-01T00:00:00Z", rootActivityType: "Elsa.Etl.RunEtl", rootActivityVersion: "1.0.0", nodeCount: 1, resumeTargetCount: 1,
    rootActivity: { executableNodeId: "root", authoredActivityId: "root", activityType: "Elsa.Etl.RunEtl", activityTypeVersion: "1.0.0", inputBindings: [], childSlots: [] },
    chosenReference: { sourceReferenceId: "source-1", selection: "requested", layout: [] },
    references: [{ sourceReferenceId: "source-1", artifactId: "artifact-1", definitionId: "definition-1", definitionVersionId: "version-1", artifactVersion: "1.0.0", scope: "Published", createdAt: "2026-01-01T00:00:00Z" }] };
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(hasCapabilityLink).mockResolvedValue(true);
  vi.mocked(listPublicationSlots).mockResolvedValue({ available: true, slots: [{ slotId: "slot-1", definitionId: "definition-1", slotName: "Published",
    activeActivationId: "publication-1", sourceKind: "publishing", sourceId: "publication-1", revision: 1, updatedAt: "2026-01-01T00:00:00Z",
    publication: { publicationId: "publication-1", definitionId: "definition-1", versionId: "version-1", artifactId: "artifact-1", artifactHash: "a".repeat(64),
      slotName: "Published", sourceReferenceId: "source-1", status: "active" } }] });
  vi.mocked(getExecutable).mockResolvedValue(executable());
});

function fixture(initialModelInputs: WorkflowInput[] = []) {
  let documentId = "process-draft-1";
  let session = { key: "session-1", profile: { id: "backend-1", baseUrl: "https://backend.example" }, context: { baseUrl: "https://backend.example" },
    identity: { status: "authenticated", subject: "author", tenantId: "tenant-1" }, disposed: false } as StudioBackendSession;
  let element: BpmnElement = { elementId: "service", elementType: "task", properties: { vendor: "keep" } };
  const onChange = vi.fn();
  let modelInputs = initialModelInputs;
  const container = document.createElement("div"); document.body.appendChild(container); const root = createRoot(container);
  const render = (options: { session?: StudioBackendSession; element?: BpmnElement; readOnly?: boolean; documentId?: string; modelInputs?: WorkflowInput[] } = {}) => {
    session = options.session ?? session; element = options.element ?? element;
    documentId = options.documentId ?? documentId;
    modelInputs = options.modelInputs ?? modelInputs;
    flushSync(() => root.render(<BpmnServiceBindingEditor session={session} documentId={documentId} element={element} onChange={onChange} readOnly={options.readOnly} modelInputs={modelInputs} />));
  };
  render();
  const click = (text: string) => flushSync(() => [...container.querySelectorAll("button")].find(button => button.textContent === text)!.click());
  const choose = async () => {
    await vi.waitFor(() => expect(container.textContent).toContain("Choose worker")); click("Choose worker");
    await vi.waitFor(() => expect(container.querySelector("[aria-label='Service publication'] option[value='slot-1']")).not.toBeNull());
    const select = container.querySelector<HTMLSelectElement>("[aria-label='Service publication']")!;
    flushSync(() => { select.value = "slot-1"; select.dispatchEvent(new Event("change", { bubbles: true })); });
  };
  return { container, onChange, render, choose, click, get session() { return session; }, get element() { return element; },
    dispose: () => { flushSync(() => root.unmount()); container.remove(); } };
}

it("uses the exact selected live publication and preserves unrelated authored properties", async () => {
  const view = fixture();
  try {
    await view.choose(); view.click("Use this service publication");
    await vi.waitFor(() => expect(view.onChange).toHaveBeenCalledOnce());
    expect(getExecutable).toHaveBeenCalledWith(view.session.context, "artifact-1", "source-1");
    const updated = { ...view.element, ...view.onChange.mock.calls[0][0] };
    expect(updated.properties.vendor).toBe("keep");
    expect(directServiceBinding(updated)).toMatchObject({ sourceReferenceId: "source-1", executable: { definitionVersionId: "version-1", artifactHash: "a".repeat(64) },
      faultPolicy: { disposition: "HoldForRetry", maxAttempts: 3 } });
    const create = view.container.querySelector<HTMLAnchorElement>("a")!;
    expect(create.getAttribute("href")).toContain("homeAction=create"); expect(create.target).toBe("_blank");
    expect(view.container.textContent).toContain("select a published pipeline");
  } finally { view.dispose(); }
});

it.each(["artifact", "hash", "chosen", "missing-reference", "definition", "version", "test", "retired", "expired", "invalid-expiry"])("refuses a stale or inconsistent publication: %s", async reason => {
  const detail = executable(), reference = detail.references[0];
  if (reason === "artifact") detail.artifactId = "other";
  if (reason === "hash") detail.artifactHash = "b".repeat(64);
  if (reason === "chosen") detail.chosenReference!.sourceReferenceId = "other";
  if (reason === "missing-reference") detail.references = [];
  if (reason === "definition") reference.definitionId = "other";
  if (reason === "version") reference.definitionVersionId = "other";
  if (reason === "test") reference.scope = "TestRun";
  if (reason === "retired") reference.deletedAt = "2026-01-01T00:00:00Z";
  if (reason === "expired") reference.expiresAt = "2000-01-01T00:00:00Z";
  if (reason === "invalid-expiry") reference.expiresAt = "not a date";
  vi.mocked(getExecutable).mockResolvedValue(detail);
  const view = fixture();
  try {
    await view.choose(); view.click("Use this service publication");
    await vi.waitFor(() => expect(view.container.querySelector("[role='alert']")?.textContent).toContain("exact live published reference"));
    expect(view.onChange).not.toHaveBeenCalled();
  } finally { view.dispose(); }
});

it.each(["backend", "subject", "tenant", "disposed", "readOnly", "selection", "document", "element-data", "transport"])("does not apply an outstanding read after its scope retires: %s", async change => {
  let finish!: (value: WorkflowExecutableDetails) => void;
  vi.mocked(getExecutable).mockImplementation(() => new Promise(resolve => { finish = resolve; }));
  const view = fixture();
  try {
    await view.choose(); view.click("Use this service publication");
    await vi.waitFor(() => expect(getExecutable).toHaveBeenCalledOnce());
    if (change === "backend") view.render({ session: { ...view.session, key: "session-2" } });
    if (change === "subject") view.render({ session: { ...view.session, identity: { ...view.session.identity, subject: "other" } } });
    if (change === "tenant") view.render({ session: { ...view.session, identity: { ...view.session.identity, tenantId: "other" } } });
    if (change === "disposed") Object.defineProperty(view.session, "disposed", { value: true });
    if (change === "readOnly") view.render({ readOnly: true });
    if (change === "selection") view.render({ element: { ...view.element, elementId: "other-task" } });
    if (change === "document") view.render({ documentId: "another-process-draft" });
    if (change === "element-data") view.render({ element: { ...view.element, properties: { vendor: "new author edit" } } });
    if (change === "transport") Object.defineProperty(view.session, "context", { value: { ...view.session.context } });
    finish(executable()); await new Promise(resolve => setTimeout(resolve, 0));
    expect(view.onChange).not.toHaveBeenCalled();
  } finally { view.dispose(); }
});

it("keeps a saved binding visible when read-only and does not open catalogs", async () => {
  const view = fixture();
  try {
    view.render({ readOnly: true, element: { ...view.element, elementType: "serviceTask", properties: { [directBindingProperty]: JSON.stringify({
      formatVersion: 1, kind: "nativeService", sourceReferenceId: "source-1", executable: { artifactId: "artifact-1", artifactHash: "a".repeat(64), definitionId: "definition-1", definitionVersionId: "version-1", artifactVersion: "1.0.0" },
      businessErrorCode: "service-failure", faultPolicy: { formatVersion: 1, disposition: "HoldForRetry", maxAttempts: 3 }
    }) } } });
    await vi.waitFor(() => expect(view.container.textContent).toContain("Retained service version 1.0.0"));
    expect(view.container.textContent).toContain("read-only");
    expect(view.container.querySelector("button")).toBeNull(); expect(listPublicationSlots).not.toHaveBeenCalled(); expect(getExecutable).not.toHaveBeenCalled();
  } finally { view.dispose(); }
});

it("does not fall back to an unbounded workflow catalog when paged reads are absent", async () => {
  vi.mocked(hasCapabilityLink).mockResolvedValue(false);
  const view = fixture();
  try {
    await vi.waitFor(() => expect(view.container.textContent).toContain("does not advertise"));
    expect(view.container.textContent).not.toContain("Choose worker"); expect(listPublicationSlots).not.toHaveBeenCalled();
  } finally { view.dispose(); }
});

it("does not offer service selection on a human-only direct-publication backend", async () => {
  vi.mocked(hasCapabilityLink).mockImplementation(async (_context, _capability, relation) => relation !== "bpmn-authored-service-publish");
  const view = fixture();
  try {
    await vi.waitFor(() => expect(view.container.textContent).toContain("does not advertise authored service publication"));
    expect(view.container.textContent).not.toContain("Choose worker"); expect(listPublicationSlots).not.toHaveBeenCalled();
  } finally { view.dispose(); }
});

it("uses a single final-fault attempt even after an invalid retry budget was entered", async () => {
  const view = fixture();
  try {
    await view.choose();
    const attempts = view.container.querySelector<HTMLInputElement>("[aria-label='Service maximum attempts']")!;
    flushSync(() => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(attempts, "33");
      attempts.dispatchEvent(new Event("input", { bubbles: true }));
    });
    const disposition = view.container.querySelector<HTMLSelectElement>("[aria-label='Service technical failure']")!;
    flushSync(() => { disposition.value = "FinalFault"; disposition.dispatchEvent(new Event("change", { bubbles: true })); });
    view.click("Use this service publication");
    await vi.waitFor(() => expect(view.onChange).toHaveBeenCalledOnce());
    expect(directServiceBinding({ ...view.element, ...view.onChange.mock.calls[0][0] })?.faultPolicy).toEqual({ formatVersion: 1, disposition: "FinalFault", maxAttempts: 1 });
  } finally { view.dispose(); }
});

function modelInput(name: string): WorkflowInput { return { referenceKey: name, name, displayName: name, description: "", category: "", uiHint: "", storageDriverType: null, type: { alias: "String", collectionKind: "Single" }, isNullable: false, isRequired: true }; }
function select(view: ReturnType<typeof fixture>, label: string, value: string) { const control = view.container.querySelector<HTMLSelectElement>(`[aria-label='${label}']`)!; flushSync(() => { control.value = value; control.dispatchEvent(new Event("change", { bubbles: true })); }); }
function mappedExecutable() { return Object.assign(executable(), { inputContract: { version: 1, inputs: [{ name: "AccountId", type: { alias: "String", collectionKind: "Single" }, isRequired: true, defaultValue: null }] } }); }

it("maps an actual working-document input to a declared worker input and restores it after reopening", async () => {
  vi.mocked(getExecutable).mockResolvedValue(mappedExecutable());
  const view = fixture([modelInput("ModelAccountId")]);
  try {
    await view.choose(); view.click("Use this service publication");
    await vi.waitFor(() => expect(view.container.textContent).toContain("Map service workflow inputs"));
    expect(view.onChange).not.toHaveBeenCalled();
    select(view, "Service input AccountId source", "Input"); select(view, "Process input for service AccountId", "ModelAccountId");
    view.click("Apply service input mapping");
    await vi.waitFor(() => expect(view.onChange).toHaveBeenCalledOnce());
    const updated = { ...view.element, ...view.onChange.mock.calls[0][0] };
    expect(directServiceBinding(updated)).toMatchObject({ formatVersion: 2, inputs: { AccountId: { kind: "Input", name: "ModelAccountId" } }, sourceReferenceId: "source-1", businessErrorCode: "service-failure" });
    view.render({ element: updated, readOnly: true });
    expect(view.container.textContent).toContain("Process input: ModelAccountId");
    expect(view.container.querySelector("button")).toBeNull();
  } finally { view.dispose(); }
});

it("rejects missing/invalid mappings, cancels without mutation and restores publication-button focus", async () => {
  vi.mocked(getExecutable).mockResolvedValue(mappedExecutable());
  const view = fixture([modelInput("ModelAccountId")]);
  try {
    await view.choose(); view.click("Use this service publication");
    await vi.waitFor(() => expect(view.container.textContent).toContain("Map service workflow inputs"));
    expect(document.activeElement?.textContent).toBe("Map service workflow inputs");
    view.click("Apply service input mapping");
    await vi.waitFor(() => expect(view.container.textContent).toContain("Map this required service input"));
    expect(view.onChange).not.toHaveBeenCalled();
    select(view, "Service input AccountId source", "Literal");
    const literal = view.container.querySelector<HTMLTextAreaElement>("[aria-label='JSON value for service AccountId']")!;
    flushSync(() => { Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, "value")!.set!.call(literal, "{invalid"); literal.dispatchEvent(new Event("input", { bubbles: true })); });
    view.click("Apply service input mapping");
    await vi.waitFor(() => expect(view.container.textContent).toContain("Enter a valid JSON literal"));
    view.click("Cancel input mapping");
    await vi.waitFor(() => expect(document.activeElement?.textContent).toBe("Use this service publication"));
    expect(view.onChange).not.toHaveBeenCalled();
  } finally { view.dispose(); }
});

it("writes configured JSON literals as v2 and retains a previous v2 mapping when inspected", async () => {
  vi.mocked(getExecutable).mockResolvedValue(mappedExecutable());
  const view = fixture();
  try {
    await view.choose(); view.click("Use this service publication");
    await vi.waitFor(() => expect(view.container.textContent).toContain("Map service workflow inputs"));
    select(view, "Service input AccountId source", "Literal");
    const literal = view.container.querySelector<HTMLTextAreaElement>("[aria-label='JSON value for service AccountId']")!;
    flushSync(() => { Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, "value")!.set!.call(literal, '"account-42"'); literal.dispatchEvent(new Event("input", { bubbles: true })); });
    view.click("Apply service input mapping");
    await vi.waitFor(() => expect(view.onChange).toHaveBeenCalledOnce());
    const updated = { ...view.element, ...view.onChange.mock.calls[0][0] };
    expect(directServiceBinding(updated)).toMatchObject({ formatVersion: 2, inputs: { AccountId: { kind: "Literal", value: "account-42" } } });
    view.render({ element: updated });
    expect(view.container.textContent).toContain('Configured JSON: "account-42"');
  } finally { view.dispose(); }
});
