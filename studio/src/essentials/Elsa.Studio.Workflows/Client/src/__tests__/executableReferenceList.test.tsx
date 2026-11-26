import React from "react";
import { createRoot, type Root } from "react-dom/client";
import { flushSync } from "react-dom";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import type { WorkflowExecutableReference } from "../workflowTypes";
import { ExecutableReferenceList } from "../workflow-editor/executableShared";

function reference(overrides: Partial<WorkflowExecutableReference> = {}): WorkflowExecutableReference {
  return {
    sourceReferenceId: "ref-1",
    artifactId: "artifact-1",
    definitionId: "definition-1",
    definitionVersionId: "version-1",
    artifactVersion: "1.0.0",
    createdAt: "2026-06-18T01:00:00Z",
    publishedAt: "2026-06-18T01:10:00Z",
    scope: "Published",
    ...overrides
  };
}

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

function renderList(props: React.ComponentProps<typeof ExecutableReferenceList>) {
  flushSync(() => root.render(<ExecutableReferenceList {...props} />));
}

describe("ExecutableReferenceList collection variant", () => {
  it("shows expiry and the retirement reason", () => {
    renderList({
      variant: "collection",
      definitionNames: new Map([["definition-1", "Invoice settlement"]]),
      activeReferenceId: "ref-1",
      onSelect: () => {},
      references: [reference({
        expiresAt: "2026-07-01T00:00:00Z",
        deletedAt: "2026-06-20T00:00:00Z",
        deletedReason: "Replaced by a newer publication"
      })]
    });

    const labels = [...container.querySelectorAll(".wf-reference-collection-label")].map(label => label.textContent);
    expect(labels).toEqual(["Version", "Scope", "Published", "Expires", "Reason"]);
    expect(container.textContent).toContain("Expires");
    expect(container.textContent).toContain("Replaced by a newer publication");
    expect(container.textContent).toContain("Retired");
    expect(container.querySelector("dl")).toBeNull();
    expect(container.querySelector("button.wf-reference-select")?.getAttribute("aria-label"))
      .toBe("Show Invoice settlement version 1.0.0");
  });

  it("uses Created when publishedAt is missing and hides the date row when both dates are missing", () => {
    renderList({
      variant: "collection",
      references: [reference({ publishedAt: null })]
    });
    expect([...container.querySelectorAll(".wf-reference-collection-label")].map(label => label.textContent))
      .toEqual(["Version", "Scope", "Created"]);

    renderList({
      variant: "collection",
      references: [reference({ publishedAt: null, createdAt: "" })]
    });
    expect([...container.querySelectorAll(".wf-reference-collection-label")].map(label => label.textContent))
      .toEqual(["Version", "Scope"]);
    expect(container.textContent).not.toContain("Created");
  });

  it("falls back to the raw definition id, Untitled workflow, and an empty collection message", () => {
    renderList({
      variant: "collection",
      references: [reference()]
    });
    expect(container.querySelector(".wf-reference-origin")?.textContent).toBe("definition-1");

    renderList({
      variant: "collection",
      references: [reference({ definitionId: "" })]
    });
    expect(container.querySelector(".wf-reference-origin")?.textContent).toBe("Untitled workflow");

    renderList({
      variant: "collection",
      references: []
    });
    expect(container.querySelector(".wf-reference-list")).toBeNull();
    expect(container.textContent).toContain("No source references.");
  });

  it("keeps the scope chip tone on collection rows", () => {
    renderList({
      variant: "collection",
      references: [reference({ scope: "TestRun" })]
    });
    expect(container.querySelector(".wf-chip.wf-reference-scope-testrun")?.textContent).toBe("Test run");
  });
});
