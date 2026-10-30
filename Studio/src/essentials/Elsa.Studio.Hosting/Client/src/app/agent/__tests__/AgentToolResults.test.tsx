import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { expect, it, vi } from "vitest";
import { AgentToolResults } from "../AgentToolResults";
import { AgentProposalReview } from "../AgentProposalReview";

it.each(["supports", "component"])("keeps owner evidence and shared review usable when contributed %s fails", failure => {
  const error = vi.spyOn(console, "error").mockImplementation(() => {});
  const container = document.createElement("div"); const root = createRoot(container);
  const target = { moduleId: "Sample", resourceType: "sample", resourceId: "one", revision: "r2" };
  const payload = { typeId: "sample.changed", schemaVersion: "1", data: { value: 42 } };
  const approved = vi.fn();
  try {
    flushSync(() => root.render(<><AgentToolResults results={[{ toolCallId: "call", toolName: "sample_change", succeeded: true, content: "Observed owner result.", receipt: { invocationId: "i", before: { ...target, revision: "r1" }, after: target, scope: "serverPersisted", evidence: payload } }]} renderers={[{ id: "sample", moduleId: "Sample", displayName: "Sample", supports: () => { if (failure === "supports") throw new Error("Broken module predicate"); return true; }, component: () => { throw new Error("Broken module renderer"); } }]} /><AgentProposalReview proposals={[{ id: "pending", title: "Review next change", summary: "The next change still needs review.", risk: "review-required", status: "awaiting-approval", reviewReady: true, revision: "r2", resourceTarget: target }]} onApprove={approved} onDeny={() => {}} onExecute={() => {}} /></>));
    expect(container.textContent).toContain("Observed target: one"); expect(container.textContent).toContain("r2");
    expect(container.textContent).toContain("Structured evidence remains available"); expect(container.textContent).toContain('"value": 42');
    const approve = [...container.querySelectorAll("button")].find(button => button.textContent?.includes("Approve"))!;
    expect(approve.disabled).toBe(false); flushSync(() => approve.click()); expect(approved).toHaveBeenCalledTimes(1);
  } finally { flushSync(() => root.unmount()); error.mockRestore(); }
});
