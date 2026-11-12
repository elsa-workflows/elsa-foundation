import React from "react";
import { createRoot, type Root } from "react-dom/client";
import { flushSync } from "react-dom";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { WorkflowExecutableCanvasHeader } from "../workflow-editor/WorkflowExecutableCanvasHeader";

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  flushSync(() => root.unmount());
  container.remove();
});

describe("WorkflowExecutableCanvasHeader", () => {
  it("renders the workflow display name, version, and a copyable artifact ID", () => {
    flushSync(() => root.render(
      <WorkflowExecutableCanvasHeader
        displayName="Invoice settlement"
        version="1.0.0"
        artifactId="artifact-95bc4f13586a"
        selectionLabel="Showing requested reference activation-ref:publication-invoice"
      />
    ));

    expect(container.querySelector("h3")?.textContent).toContain("Invoice settlement");
    expect(container.querySelector("h3 small")?.textContent).toBe("Version 1.0.0");
    expect(container.textContent).not.toContain("artifact-95bc4f13586a Version");
    expect(container.querySelector('[aria-label="Artifact ID: artifact-95bc4f13586a"]')).toBeTruthy();
    expect(container.querySelector('[aria-label="Copy Artifact ID"]')).toBeTruthy();
    const selection = container.querySelector(".wf-executable-selection");
    expect(selection?.textContent).toBe("Showing requested reference activation-ref:publication-invoice");
    expect(selection?.getAttribute("title")).toBe("Showing requested reference activation-ref:publication-invoice");
  });

  it("omits the version chip when the reference has no version and still copies the artifact ID", () => {
    flushSync(() => root.render(
      <WorkflowExecutableCanvasHeader
        displayName="Executable artifact-1"
        version={null}
        artifactId="artifact-1"
        selectionLabel="No source reference; automatic layout"
      />
    ));

    expect(container.querySelector("h3")?.textContent).toBe("Executable artifact-1");
    expect(container.querySelector("h3 small")).toBeNull();
    expect(container.querySelector('[aria-label="Copy Artifact ID"]')).toBeTruthy();
  });
});
