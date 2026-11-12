import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { expect, it, vi } from "vitest";
import { WeaverToolResults } from "../WeaverToolResults";
import type { StudioWeaverEffectScope, StudioWeaverToolResult } from "../../../sdk";
import { WeaverProposalReview } from "../WeaverProposalReview";

it("keeps a terminal refusal's canonical reason visible in the shared result and review without replacing owner presentation", () => {
  const container = document.createElement("div"); const root = createRoot(container);
  const message = "Current host policy disables a privilege required by this operation.";
  const result: StudioWeaverToolResult = { toolCallId: "call", toolName: "owner_test", succeeded: false,
    content: "Execution admission refused the operation before dispatch. No effect occurred.",
    error: { code: "weaver.proposal.privilege_disabled", message, statusCode: 403 } };
  try {
    flushSync(() => root.render(<><WeaverToolResults results={[result]} renderers={[]} />
      <WeaverProposalReview proposals={[{ id: "failed", title: "Test resource", summary: result.content,
        risk: "review-required", status: "failed", toolResult: result }]} onApprove={() => {}} onDeny={() => {}} onExecute={() => {}} /></>));
    expect([...container.querySelectorAll("p")].filter(item => item.textContent === message)).toHaveLength(2);
    expect(container.textContent).toContain("No effect occurred");
    expect(container.querySelector("article[data-succeeded]")?.getAttribute("data-succeeded")).toBe("false");
    flushSync(() => root.render(<WeaverToolResults results={[{ ...result, content: message }]} renderers={[]} />));
    expect([...container.querySelectorAll("p")].filter(item => item.textContent === message)).toHaveLength(1);
    expect(container.textContent).not.toContain("Persisted resource");
  } finally { flushSync(() => root.unmount()); }
});

it.each(["supports", "component"])("keeps owner evidence and shared review usable when contributed %s fails", failure => {
  const error = vi.spyOn(console, "error").mockImplementation(() => {});
  const container = document.createElement("div"); const root = createRoot(container);
  const target = { moduleId: "Sample", resourceType: "sample", resourceId: "one", revision: "r2" };
  const payload = { typeId: "sample.changed", schemaVersion: "1", data: { value: 42 } };
  const approved = vi.fn();
  try {
    flushSync(() => root.render(<><WeaverToolResults results={[{ toolCallId: "call", toolName: "sample_change", succeeded: true, content: "Observed owner result.", receipt: { invocationId: "i", before: { ...target, revision: "r1" }, after: target, scope: "serverPersisted", evidence: payload } }]} renderers={[{ id: "sample", moduleId: "Sample", displayName: "Sample", supports: () => { if (failure === "supports") throw new Error("Broken module predicate"); return true; }, component: () => { throw new Error("Broken module renderer"); } }]} /><WeaverProposalReview proposals={[{ id: "pending", title: "Review next change", summary: "The next change still needs review.", risk: "review-required", status: "awaiting-approval", reviewReady: true, revision: "r2", resourceTarget: target }]} onApprove={approved} onDeny={() => {}} onExecute={() => {}} /></>));
    expect(container.textContent).toContain("Observed target: one"); expect(container.textContent).toContain("r2");
    expect(container.textContent).toContain("Structured evidence remains available"); expect(container.textContent).toContain('"value": 42');
    const approve = [...container.querySelectorAll("button")].find(button => button.textContent?.includes("Approve"))!;
    expect(approve.disabled).toBe(false); flushSync(() => approve.click()); expect(approved).toHaveBeenCalledTimes(1);
  } finally { flushSync(() => root.unmount()); error.mockRestore(); }
});

// Spec 1193 FR-019: a renderer contributed by another package presents a resource module's results through
// resourceModuleIds, and the resource-owning module's result controls render after it, for the matching result type only.
it("matches renderers by resource module and renders the owning module's result controls after them", () => {
  const container = document.createElement("div"); const root = createRoot(container);
  const target = { moduleId: "Owner", resourceType: "doc", resourceId: "one", revision: "r2" };
  const payload = { typeId: "owner.changed", schemaVersion: "1", data: {} };
  const result = { toolCallId: "call", toolName: "owner_change", succeeded: true, content: "Changed.", payload,
    receipt: { invocationId: "i", before: { ...target, revision: "r1" }, after: target, scope: "clientWorkingState" as const, evidence: payload } };
  const renderers = [
    { id: "presenter", moduleId: "Presenter", resourceModuleIds: ["Owner"], displayName: "Presenter", resultTypes: ["owner.changed"], component: () => <span>Presented by another package</span> },
    { id: "foreign", moduleId: "Presenter", resourceModuleIds: ["Someone.Else"], displayName: "Foreign", resultTypes: ["owner.changed"], component: () => <span>Foreign renderer</span> }
  ];
  const controls = [
    { id: "undo", moduleId: "Owner", displayName: "Undo", resultTypes: ["owner.changed"], component: () => <button type="button">Owner undo</button> },
    { id: "other-type", moduleId: "Owner", displayName: "Other", resultTypes: ["owner.other"], component: () => <span>Wrong type control</span> },
    { id: "other-module", moduleId: "Stranger", displayName: "Stranger", resultTypes: ["owner.changed"], component: () => <span>Stranger control</span> }
  ];
  try {
    flushSync(() => root.render(<><WeaverToolResults results={[result]} renderers={renderers} controls={controls} />
      <WeaverProposalReview proposals={[{ id: "p", title: "Change", summary: "Changed", risk: "review-required", status: "executed", revision: "r2",
        resourceTarget: target, resultRendererId: "presenter", toolResult: result }]} resultRenderers={renderers} resultControls={controls}
        onApprove={() => {}} onDeny={() => {}} onExecute={() => {}} /></>));
    expect(container.textContent?.match(/Presented by another package/g)).toHaveLength(2);
    expect(container.textContent).not.toContain("Foreign renderer");
    expect(container.textContent?.match(/Owner undo/g)).toHaveLength(2);
    expect(container.textContent).not.toContain("Wrong type control");
    expect(container.textContent).not.toContain("Stranger control");
  } finally { flushSync(() => root.unmount()); }
});

const scopeCases = [
  { scope: "clientWorkingState", label: "Local working state" },
  { scope: "ClientWorkingState", label: "Local working state" },
  { scope: 0, label: "Local working state" },
  { scope: "client-working-state", label: "Local working state" },
  { scope: "serverPersisted", label: "Persisted resource" },
  { scope: "ServerPersisted", label: "Persisted resource" },
  { scope: 1, label: "Persisted resource" },
  { scope: "server-persisted", label: "Persisted resource" },
  { scope: "noPersistedEffect", label: "No persisted resource change" },
  { scope: "NoPersistedEffect", label: "No persisted resource change" },
  { scope: 2, label: "No persisted resource change" },
  { scope: "no-persisted-effect", label: "No persisted resource change" },
  ...["future-scope", 3, -1, 0.5, null].map(scope => ({ scope, label: "Effect scope unavailable" }))
];
it.each(scopeCases)("labels owning receipt scope $scope as $label without interpreting the module verdict", ({ scope, label }) => {
  const container = document.createElement("div"); const root = createRoot(container);
  const target = { moduleId: "Owner", resourceType: "sample", resourceId: "one", revision: "r1" };
  const payload = { typeId: "owner.test-result", schemaVersion: "1", data: { passed: false } };
  try {
    flushSync(() => root.render(<WeaverToolResults results={[{ toolCallId: "call", toolName: "owner_test", succeeded: true,
      content: "The owner ran the test and reported a negative verdict.", payload,
      receipt: { invocationId: "invocation", before: target, after: target, scope: scope as StudioWeaverEffectScope, evidence: payload }
    }]} renderers={[]} />));
    expect(container.querySelector("article small")?.textContent).toBe(label);
    expect(container.querySelector("article")?.getAttribute("data-succeeded")).toBe("true");
    expect(container.textContent).toContain("negative verdict");
    expect(container.textContent).toContain('"passed": false');
  } finally { flushSync(() => root.unmount()); }
});
