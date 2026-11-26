import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it } from "vitest";
import { GraphAuthoringInspector } from "../graph-authoring/GraphAuthoringInspector";
import { GraphAuthoringWorkspace } from "../graph-authoring/GraphAuthoringWorkspace";

afterEach(() => {
  document.body.replaceChildren();
});

describe("GraphAuthoringWorkspace", () => {
  it("keeps shared palette, canvas, and inspector landmarks in predictable order", () => {
    const container = document.createElement("div");
    document.body.appendChild(container);
    const root = createRoot(container);

    flushSync(() => root.render(
      <GraphAuthoringWorkspace
        resourceKind="activity-definition-graph"
        palette={<aside aria-label="Activities panel">Palette</aside>}
        canvas={<main aria-label="Graph canvas">Canvas</main>}
        inspector={<GraphAuthoringInspector>Selection</GraphAuthoringInspector>}
      />
    ));

    const workspace = container.querySelector("[data-graph-authoring-resource='activity-definition-graph']");
    expect(workspace).not.toBeNull();
    expect([...workspace!.children].map(element => element.tagName)).toEqual(["ASIDE", "MAIN", "ASIDE"]);
    expect(container.querySelector("[aria-label='Inspector panel']")?.textContent).toContain("Selection");

    flushSync(() => root.unmount());
  });
});
