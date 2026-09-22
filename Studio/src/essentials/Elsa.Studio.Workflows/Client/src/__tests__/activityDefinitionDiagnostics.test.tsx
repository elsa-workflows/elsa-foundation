import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioActivityDiagnostic } from "@elsa-workflows/studio-sdk";
import { ActivityDefinitionDiagnosticsPanel } from "../ActivityDefinitionDiagnosticsPanel";

const mounted: Array<() => void> = [];

afterEach(() => {
  for (const unmount of mounted.splice(0)) unmount();
});

describe("ActivityDefinitionDiagnosticsPanel", () => {
  it("groups server and local JSON diagnostics in a collapsible bottom surface", () => {
    const rendered = renderPanel([
      diagnostic("activity.contract.invalid", "Error", "/contract/outcomes/0"),
      diagnostic("activity.graph.mapping", "Warning", "/outcomeMappings/0"),
      diagnostic("activity.graph.node", "Error", "/rootActivity/structure"),
      diagnostic("activity.provider.issue", "Info", "/extension", "elsa.activity-graph")
    ], [{
      area: "JSON",
      severity: "warning",
      message: "The valid JSON buffer is unapplied."
    }]);

    const details = rendered.querySelector("details");
    expect(details?.open).toBe(true);
    expect(rendered.textContent).toContain("2 errors · 2 warnings · 1 info");
    expect(groupNames(rendered)).toEqual([
      "Public contract",
      "Boundary mappings",
      "Graph",
      "Provider",
      "JSON"
    ]);
  });

  it("starts valid informational results collapsed and forwards focus requests", () => {
    const onFocus = vi.fn().mockResolvedValue({
      kind: "focused",
      announcement: "Focused graph node."
    });
    const rendered = renderPanel([
      diagnostic("activity.graph.note", "Info", "/rootActivity")
    ], [], true, onFocus);

    expect(rendered.querySelector("details")?.open).toBe(false);
    click(rendered.querySelector<HTMLButtonElement>("[aria-label='Focus activity.graph.note']")!);
    expect(onFocus).toHaveBeenCalledWith(
      expect.objectContaining({ code: "activity.graph.note" }),
      expect.any(HTMLButtonElement)
    );
  });
});

function renderPanel(
  diagnostics: StudioActivityDiagnostic[],
  localDiagnostics: Array<{ area: "JSON"; severity: "error" | "warning" | "info"; message: string }> = [],
  isValid = false,
  onFocus = vi.fn().mockResolvedValue({ kind: "unsupported", announcement: "Unavailable." })
) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(
    <ActivityDefinitionDiagnosticsPanel
      validation={{
        draftId: "draft-1",
        revision: 4,
        isValid,
        validatedAt: "2026-07-28T00:00:00Z",
        diagnostics
      }}
      localDiagnostics={localDiagnostics}
      canReturn={false}
      onFocus={onFocus}
      onReturn={vi.fn()}
    />
  ));
  mounted.push(() => {
    flushSync(() => root.unmount());
    container.remove();
  });
  return container;
}

function diagnostic(
  code: string,
  severity: StudioActivityDiagnostic["severity"],
  jsonPointer: string,
  providerKey?: string
): StudioActivityDiagnostic {
  return {
    code,
    severity,
    message: `${code} message`,
    subject: { kind: "ActivityDefinitionDraft", id: "draft-1", revision: 4 },
    location: { jsonPointer, providerKey },
    metadata: {}
  };
}

function groupNames(container: HTMLElement) {
  return [...container.querySelectorAll<HTMLHeadingElement>(".ad-diagnostic-group > h3")]
    .map(heading => heading.textContent);
}

function click(element: HTMLElement) {
  flushSync(() => element.dispatchEvent(new MouseEvent("click", { bubbles: true })));
}
