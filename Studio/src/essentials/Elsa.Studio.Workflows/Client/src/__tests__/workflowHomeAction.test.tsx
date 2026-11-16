import { describe, expect, it, vi } from "vitest";
import {
  button,
  click,
  definition,
  pagedDefinitionCapabilities,
  renderWorkflowRoute,
  response,
  waitForCondition,
  waitForText
} from "./workflowDefinitionTestHelpers";

describe("Home workflow actions", () => {
  it("opens the existing create flow once and preserves the rest of the URL and history state", async () => {
    const historyState = { preserved: "home-navigation-state" };
    const originalHistoryState = structuredClone(historyState);
    const fetchMock = async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("/definition-pages")) return response({ items: [], nextContinuationToken: null });
      if (url.pathname.endsWith("/activities/catalog")) return response({ activities: [] });
      return response({ items: [] });
    };
    const rendered = await renderWorkflowRoute(
      "/workflows/definitions?homeAction=create&keep=one&keep=two#home-anchor",
      fetchMock,
      pagedDefinitionCapabilities(),
      { followNavigation: true, historyState }
    );

    await waitForText(rendered.container, "No active workflow definitions");
    await vi.dynamicImportSettled();
    await waitForCondition(() => expect(rendered.container.querySelector("[role='dialog']")).not.toBeNull());
    await waitForCondition(() => {
      expect(`${window.location.pathname}${window.location.search}${window.location.hash}`)
        .toBe("/workflows/definitions?keep=one&keep=two#home-anchor");
      // Browse history adds its own namespace while preserving the Home caller's state.
      expect(window.history.state).toMatchObject(originalHistoryState);
      expect(historyState).toEqual(originalHistoryState);
    });
    await click(button(rendered.container, "Cancel"));
    expect(rendered.container.querySelector("[role='dialog']")).toBeNull();

    window.dispatchEvent(new PopStateEvent("popstate"));
    await waitForCondition(() => expect(rendered.container.querySelector("[role='dialog']")).toBeNull());
    expect(window.location.search).toBe("?keep=one&keep=two");
  });

  it("opens an explicitly selected definition instead of replaying a conflicting create intent", async () => {
    const detailRequests: string[] = [];
    const fetchMock = async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("/definitions/chosen")) {
        detailRequests.push(url.pathname);
        return response({ definition: definition({ id: "chosen", name: "Selected workflow" }), draft: null, versions: [] });
      }
      if (url.pathname.includes("/activities/availability/diagnostics")) return response({ items: [], sets: [] });
      if (url.pathname.includes("/activities")) return response({ activities: [] });
      return response({ items: [] });
    };
    const rendered = await renderWorkflowRoute(
      "/workflows/definitions?definition=chosen&homeAction=create&keep=one#selected",
      fetchMock,
      pagedDefinitionCapabilities(),
      { followNavigation: true }
    );

    await waitForCondition(() => expect(detailRequests).toContain("/design/workflows/definitions/chosen"));
    expect(rendered.container.querySelector("[role='dialog']")).toBeNull();
    expect(new URLSearchParams(window.location.search).get("definition")).toBe("chosen");
    expect(new URLSearchParams(window.location.search).has("homeAction")).toBe(false);
    expect(new URLSearchParams(window.location.search).get("keep")).toBe("one");
    expect(window.location.hash).toBe("#selected");
  });

  it("keeps the exact selected definition and a recoverable error after Retry also fails", async () => {
    let reads = 0;
    let finishRetry!: () => void;
    const retryReturned = new Promise<void>(resolve => { finishRetry = resolve; });
    const fetchMock = async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("/definitions/selected")) {
        reads++;
        if (reads > 1) finishRetry();
        return response({ detail: "Selected definition is temporarily unavailable." }, 503);
      }
      if (url.pathname.includes("/activities/availability/diagnostics")) return response({ items: [], sets: [] });
      if (url.pathname.includes("/activities")) return response({ activities: [] });
      return response({ items: [] });
    };
    const rendered = await renderWorkflowRoute(
      "/workflows/definitions?definition=selected",
      fetchMock,
      pagedDefinitionCapabilities(),
      { followNavigation: true }
    );

    await waitForCondition(() => expect(reads).toBe(1));
    await waitForCondition(() => expect(button(rendered.container, "Retry loading this workflow")).not.toBeNull());
    const retry = button(rendered.container, "Retry loading this workflow");
    await click(retry);
    await retryReturned;
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(reads).toBe(2);
    expect(rendered.container.textContent).toContain("selected");
    expect(rendered.container.querySelector("[role='alert']")?.textContent).not.toContain("Loading workflow editor");
    expect(button(rendered.container, "Retry loading this workflow")).not.toBeNull();
  });

  it("shows the exact selected definition and retains a failed Reload outcome when it has no draft", async () => {
    let reads = 0;
    let finishReload!: () => void;
    const reloadReturned = new Promise<void>(resolve => { finishReload = resolve; });
    const fetchMock = async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("/definitions/selected")) {
        reads++;
        if (reads > 1) {
          finishReload();
          return response({ detail: "Selected definition is temporarily unavailable." }, 503);
        }
        return response({ definition: definition({ id: "selected", name: "Selected workflow" }), draft: null, versions: [] });
      }
      if (url.pathname.includes("/activities/availability/diagnostics")) return response({ items: [], sets: [] });
      if (url.pathname.includes("/activities")) return response({ activities: [] });
      return response({ items: [] });
    };
    const rendered = await renderWorkflowRoute(
      "/workflows/definitions?definition=selected",
      fetchMock,
      pagedDefinitionCapabilities(),
      { followNavigation: true }
    );

    await waitForText(rendered.container, "no current draft");
    await click(button(rendered.container, "Reload this workflow"));
    await reloadReturned;
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(reads).toBe(2);
    expect(rendered.container.textContent).toContain("no current draft");
    expect(rendered.container.textContent).toContain("selected");
    expect(rendered.container.querySelector("[role='alert']")).not.toBeNull();
    expect(button(rendered.container, "Reload this workflow")).not.toBeNull();
  });

  it("does not let an older definition response replace the newly selected workflow", async () => {
    let resolveAlpha!: (value: Response) => void;
    const alphaResponse = new Promise<Response>(resolve => { resolveAlpha = resolve; });
    let alphaRequested!: () => void;
    const alphaRequest = new Promise<void>(resolve => { alphaRequested = resolve; });
    const fetchMock = async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("/definitions/alpha")) {
        alphaRequested();
        return alphaResponse;
      }
      if (url.pathname.endsWith("/definitions/beta")) {
        return response({ definition: definition({ id: "beta", name: "Beta workflow" }), draft: null, versions: [] });
      }
      if (url.pathname.includes("/activities/availability/diagnostics")) return response({ items: [], sets: [] });
      if (url.pathname.includes("/activities")) return response({ activities: [] });
      return response({ items: [] });
    };
    const rendered = await renderWorkflowRoute(
      "/workflows/definitions?definition=alpha&keep=one",
      fetchMock,
      pagedDefinitionCapabilities(),
      { followNavigation: true }
    );

    await alphaRequest;
    window.history.pushState({}, "", "/workflows/definitions?definition=beta&keep=one");
    window.dispatchEvent(new PopStateEvent("popstate"));
    await waitForText(rendered.container, "Selected workflow definition:");
    await waitForCondition(() => expect(rendered.container.textContent).toContain("beta"));
    resolveAlpha(response({
      definition: definition({ id: "alpha", name: "Alpha workflow" }),
      draft: { id: "draft-alpha", definitionId: "alpha", state: { variables: [], rootActivity: null, inputs: [], outputs: [] }, layout: [], validationErrors: [] },
      versions: []
    }));
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(window.location.search).toBe("?definition=beta&keep=one");
    expect(rendered.container.textContent).toContain("Selected workflow definition:");
    expect(rendered.container.textContent).toContain("beta");
    expect(rendered.container.textContent).not.toContain("Alpha workflow");
    expect(rendered.container.textContent).toContain("no current draft");
  });
});
