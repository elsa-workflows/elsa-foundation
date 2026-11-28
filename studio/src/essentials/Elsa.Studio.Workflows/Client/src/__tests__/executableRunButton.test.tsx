import React from "react";
import { createRoot, type Root } from "react-dom/client";
import { flushSync } from "react-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ExecutableRunButton } from "../workflow-editor/ExecutableRunButton";

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

describe("ExecutableRunButton", () => {
  it("defaults to the default StudioButton and can render the filled primary", () => {
    const onRequest = vi.fn(async () => undefined);
    const target = { artifactId: "artifact-1", definitionVersionId: "version-1", sourceReferenceId: "ref-1" };

    flushSync(() => root.render(<ExecutableRunButton target={target} runningArtifactId={null} onRequest={onRequest} />));
    const defaultButton = container.querySelector("button");
    expect(defaultButton?.classList.contains("studio-button")).toBe(true);
    expect(defaultButton?.getAttribute("data-variant")).toBe("default");
    expect(defaultButton?.textContent).toContain("Run");

    flushSync(() => root.render(
      <ExecutableRunButton target={target} runningArtifactId={null} variant="primary" onRequest={onRequest} />
    ));
    const primary = container.querySelector("button");
    expect(primary?.getAttribute("data-variant")).toBe("primary");
    expect(primary?.disabled).toBe(false);

    flushSync(() => root.render(
      <ExecutableRunButton target={null} runningArtifactId={null} variant="primary" onRequest={onRequest} />
    ));
    expect(container.querySelector("button")?.disabled).toBe(true);
    expect(container.querySelector("button")?.title).toContain("No live published reference");
  });
});
