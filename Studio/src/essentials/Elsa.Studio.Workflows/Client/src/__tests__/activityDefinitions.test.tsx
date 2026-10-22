import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { afterEach, describe, expect, it, vi } from "vitest";
import { authSessionStartedEvent, StudioHttpError, type StudioActivityDefinitionImplementationEditorContribution, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ActivityDefinitionsPage } from "../ActivityDefinitionsPage";
import { activityDesignKeys } from "../api/activityDesign";
import { clearApiCapabilityCache } from "../api/capabilities";
import { activityDefinitionsObservationEvent, observeActivityDefinitions } from "../activityDefinitionObservability";
import type { ActivityDefinitionDraftManagementView, ActivityDefinitionManagementView, ActivityDefinitionVersionManagementView, ActivityManagementPage } from "../activityDefinitionTypes";

afterEach(() => {
  vi.restoreAllMocks();
  clearApiCapabilityCache();
  window.history.replaceState({}, "", "/");
});

describe("Activity Definitions experience", () => {
  it("keeps import and broad upgrade tools collapsed until requested", async () => {
    const navigateToStudioPath = vi.fn();
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      return page([]);
    });
    const rendered = renderPage(
      getJson,
      undefined,
      "/workflows/activity-definitions",
      undefined,
      [],
      undefined,
      navigateToStudioPath
    );
    await waitForText(rendered.container, "No designed activities yet");
    const createActivity = buttonByText(rendered.container, "Create activity");
    expect(createActivity.getAttribute("aria-label") ?? createActivity.textContent).toContain("Create activity");
    click(createActivity);
    await waitForText(rendered.container, "Create Activity Definition");
    click(rendered.container.querySelector<HTMLButtonElement>('button[aria-label="Close"]')!);
    expect(buttonByText(rendered.container, "Browse built-in activities")).toBeTruthy();

    const tools = rendered.container.querySelector<HTMLDetailsElement>(".ad-tools-disclosure")!;
    expect(tools.open).toBe(false);
    tools.open = true;

    click(buttonByText(rendered.container, "Import from Elsa 3"));

    expect(navigateToStudioPath).toHaveBeenCalledWith("/workflows/activity-definitions/import-elsa3");
    click(buttonByText(rendered.container, "Plan broad upgrade"));
    expect(navigateToStudioPath).toHaveBeenCalledWith("/workflows/activity-definitions/upgrades");
    await rendered.unmount();
  });

  it("distinguishes initial loading, an empty collection, and no filter matches", async () => {
    let resolveCollection!: (value: unknown) => void;
    const firstCollection = new Promise(resolve => { resolveCollection = resolve; });
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url.includes("search=missing")) return page([]);
      return firstCollection;
    });
    const rendered = renderPage(getJson);

    await waitForText(rendered.container, "Loading Activity Definitions");
    resolveCollection(page([]));
    await waitForText(rendered.container, "No designed activities yet");

    change(rendered.container.querySelector<HTMLInputElement>("input[placeholder='Name, type key, or category']")!, "missing");
    await waitForText(rendered.container, "No Activity Definitions match");
    await waitFor(() => expect(getJson.mock.calls.some(([url]) => String(url).includes("search=missing") && String(url).includes("authority=Design"))).toBe(true));
    await rendered.unmount();
  });

  it("shows a safe failed-without-data state", async () => {
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      throw new Error("secret upstream failure for definition-hidden");
    });
    const rendered = renderPage(getJson);

    await waitForText(rendered.container, "Couldn't load Activity Definitions");
    expect(rendered.container.textContent).not.toContain("secret upstream failure");
    expect(rendered.container.textContent).not.toContain("definition-hidden");
    await rendered.unmount();
  });

  it("humanizes source-owned CLR titles and renders a placeholder for MinValue updated dates", async () => {
    const clr = "Elsa.Activities.Http.Activities.SendHttpRequest";
    const sourceOwned: ActivityDefinitionManagementView = {
      ...definition({ definitionId: "definition-http", activityTypeKey: clr, displayName: clr, contentAuthority: { kind: "ProviderSource", authorityKey: "elsa.http", sourceId: "elsa.http" } }),
      updatedAt: "0001-01-01T00:00:00"
    };
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url.startsWith("/design/activities/definitions?") && url.includes("authority=ProviderSource")) return page([sourceOwned]);
      if (url.startsWith("/design/activities/definitions?")) return page([]);
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = renderPage(getJson);

    await waitForText(rendered.container, "No designed activities yet");
    click(buttonByText(rendered.container, "Built-in"));
    await waitForText(rendered.container, "Send Http Request");
    const activityCell = rendered.container.querySelector<HTMLElement>("[data-label='Activity']")!;
    const row = rendered.container.querySelector<HTMLElement>(".ad-collection-row")!;
    expect(activityCell.querySelector("strong")?.textContent).toBe("Send Http Request");
    expect([...activityCell.querySelectorAll("small")].some(element => element.textContent === clr)).toBe(false);
    expect(row.title).toContain(`Activity Type Key: ${clr}`);
    const stateCell = rendered.container.querySelector<HTMLElement>("[data-label='State']")!;
    expect(stateCell.textContent).toContain("Published");
    expect(stateCell.textContent).not.toContain("Draft");
    // MinValue updated timestamp renders as an em-dash rather than "01/01/1".
    const updatedCell = rendered.container.querySelector<HTMLElement>("[data-label='Updated']")!;
    expect(updatedCell.textContent).toBe("—");
    await rendered.unmount();
  });

  it("keeps exact definition identity visible and nested identifier actions out of row navigation", async () => {
    const exactId = "activity-definition-0123456789abcdef0123456789abcdef";
    const exactTypeKey = "Contoso.InvoiceEvaluator.With.A.Long.Type.Key";
    const primaryDefinition = definition({
      definitionId: exactId,
      activityTypeKey: exactTypeKey,
      displayName: "Invoice evaluator",
      headVersionId: "activity-version-head-exact",
      recommendedVersionId: "activity-version-recommended-exact"
    });
    const sameNameDefinition = definition({
      definitionId: "activity-definition-duplicate-name",
      displayName: "Invoice evaluator"
    });
    const getJson = vi.fn(async (url: string) => url === "/capabilities" ? capabilities() : page([primaryDefinition, sameNameDefinition]));
    const writeText = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal("navigator", { clipboard: { writeText } });
    const rendered = renderPage(getJson);

    try {
      await waitFor(() => expect(rendered.container.querySelectorAll(".ad-collection-row")).toHaveLength(2));
      const [primaryRow, duplicateRow] = [...rendered.container.querySelectorAll<HTMLElement>(".ad-collection-row")];
      expect(primaryRow.querySelector(".studio-copyable-identifier__value")?.getAttribute("aria-label")).toBe(`Definition ID: ${exactId}`);
      expect(duplicateRow.querySelector(".studio-copyable-identifier__value")?.getAttribute("aria-label")).toBe("Definition ID: activity-definition-duplicate-name");

      const disclosure = primaryRow.querySelector<HTMLButtonElement>(".studio-disclosure-fold-trigger")!;
      const copyButton = primaryRow.querySelector<HTMLButtonElement>('button[aria-label="Copy Definition ID"]')!;
      expect(disclosure.getAttribute("aria-expanded")).toBe("false");
      expect(primaryRow.querySelector(".studio-disclosure-fold-body")?.hasAttribute("hidden")).toBe(true);
      for (const key of ["Enter", " "]) {
        flushSync(() => disclosure.dispatchEvent(new KeyboardEvent("keydown", { key, bubbles: true })));
        flushSync(() => copyButton.dispatchEvent(new KeyboardEvent("keydown", { key, bubbles: true })));
      }
      expect(rendered.container.textContent).not.toContain("Build your activity");
      expect(new URL(window.location.href).searchParams.has("definition")).toBe(false);

      const copyIconPath = copyButton.querySelector("svg path")!;
      flushSync(() => copyIconPath.dispatchEvent(new MouseEvent("click", { bubbles: true })));
      await waitFor(() => expect(writeText).toHaveBeenCalledWith(exactId));
      expect(rendered.container.textContent).not.toContain("Build your activity");

      click(disclosure);
      expect(disclosure.getAttribute("aria-expanded")).toBe("true");
      expect(primaryRow.querySelector(".studio-disclosure-fold-body")?.hasAttribute("hidden")).toBe(false);
      expect(primaryRow.textContent).toContain(exactTypeKey);
      expect(primaryRow.textContent).toContain("activity-version-head-exact");
      expect(primaryRow.textContent).toContain("activity-version-recommended-exact");
      click(copyButton);
      await waitFor(() => expect(writeText).toHaveBeenCalledWith(exactId));
      expect(rendered.container.textContent).not.toContain("Build your activity");
      expect(new URL(window.location.href).searchParams.has("definition")).toBe(false);
    } finally {
      await rendered.unmount();
      vi.unstubAllGlobals();
    }
  });

  it("opens the stable definition workbench and keeps an exact draft identity in the URL", async () => {
    const navigateToStudioPath = vi.fn();
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url.startsWith("/design/activities/definitions?")) return page([definition()]);
      if (url === "/design/activities/definitions/definition-1") return definition();
      if (url.startsWith("/design/activities/definitions/definition-1/drafts?")) return page([draft()]);
      if (url.startsWith("/design/activities/definitions/definition-1/versions?")) return page([version()]);
      if (url === "/design/activities/drafts/draft-1") return draftDetail();
      if (url === "/design/activities/versions/version-1") return versionDetail();
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = renderPage(
      getJson,
      undefined,
      "/workflows/activity-definitions",
      undefined,
      [],
      undefined,
      navigateToStudioPath
    );

    await waitForText(rendered.container, "Invoice evaluator");
    const row = rendered.container.querySelector<HTMLElement>("[role='row'][tabindex='0']")!;
    row.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true }));

    await waitForText(rendered.container, "Build your activity");
    expect(rendered.container.textContent).toContain("Contoso.InvoiceEvaluator");
    expect(rendered.container.textContent).toContain("visual-graph");
    expect(getJson.mock.calls.some(([url]) => String(url).includes("/drafts?"))).toBe(true);
    expect(getJson.mock.calls.some(([url]) => String(url).includes("/versions?"))).toBe(true);
    click(buttonByText(rendered.container, "Drafts"));
    await waitForText(rendered.container, "Draft draft-1");
    click(buttonByText(rendered.container, "Draft draft-1"));

    expect(window.location.pathname).toBe("/workflows/activity-definitions");
    expect(new URLSearchParams(window.location.search).get("definition")).toBe("definition-1");
    expect(new URLSearchParams(window.location.search).get("draft")).toBe("draft-1");
    click(buttonByText(rendered.container, "Plan broad upgrade from this exact draft"));
    expect(navigateToStudioPath).toHaveBeenCalledWith(
      "/workflows/activity-definitions/upgrades?rootKind=ActivityDraft&rootId=draft-1"
    );
    await rendered.unmount();
  });

  it("restores the current collection filters and continuation from a definition route", async () => {
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url === "/design/activities/definitions/definition-2") return definition({ definitionId: "definition-2", displayName: "Second evaluator" });
      if (url.startsWith("/design/activities/definitions/definition-2/drafts?")) return page([draft({ definitionId: "definition-2", draftId: "draft-2" })]);
      if (url.startsWith("/design/activities/definitions/definition-2/versions?")) return page([version({ version: { ...version().version, definitionId: "definition-2", versionId: "version-2" } })]);
      if (url.includes("cursor=activity-cursor-2")) return page([definition({ definitionId: "definition-2", displayName: "Second evaluator" })], { totalCount: 2 });
      return page([definition()], { continuation: "activity-cursor-2", hasMore: true, totalCount: 2 });
    });
    const rendered = renderPage(getJson);
    await waitForText(rendered.container, "Invoice evaluator");

    const search = rendered.container.querySelector<HTMLInputElement>("input[placeholder='Name, type key, or category']")!;
    change(search, "invoice");
    change(controlByLabel<HTMLInputElement>(rendered.container, "Provider key"), "visual-graph");
    change(controlByLabel<HTMLSelectElement>(rendered.container, "Rows per page"), "10");
    click(buttonByText(rendered.container, "All"));
    await waitFor(() => expect(getJson.mock.calls.some(([url]) => {
      const value = String(url);
      return value.includes("search=invoice") && value.includes("providerKey=visual-graph") && value.includes("limit=10") && !value.includes("authority=");
    })).toBe(true));
    await waitFor(() => expect(buttonByText(rendered.container, "Next").disabled).toBe(false));
    click(buttonByText(rendered.container, "Next"));
    await waitForText(rendered.container, "Second evaluator");
    await waitFor(() => expect(getJson.mock.calls.some(([url]) => String(url).includes("cursor=activity-cursor-2"))).toBe(true));
    change(search, "invoice pending");
    change(controlByLabel<HTMLInputElement>(rendered.container, "Provider key"), "visual-graph pending");
    window.history.replaceState({ ...window.history.state, unrelatedHostState: "keep-me" }, "");
    const pushState = vi.spyOn(window.history, "pushState");

    click(rendered.container.querySelector<HTMLElement>("[role='row'][tabindex='0']")!);
    await waitForText(rendered.container, "Build your activity");
    expect(window.history.state.unrelatedHostState).toBe("keep-me");
    const originCollection = JSON.parse(JSON.stringify(window.history.state.elsaActivityDefinitions.collection));
    const requestCountBeforeReturn = getJson.mock.calls.length;
    click(buttonByText(rendered.container, "Activity Definitions"));
    await waitForText(rendered.container, "Second evaluator");
    expect(pushState).toHaveBeenCalled();
    expect(window.history.state.elsaActivityDefinitions.collection).toEqual(originCollection);
    expect(window.history.state.unrelatedHostState).toBe("keep-me");

    expect.soft(rendered.container.querySelector<HTMLInputElement>("input[placeholder='Name, type key, or category']")?.value).toBe("invoice pending");
    expect.soft(controlByLabel<HTMLInputElement>(rendered.container, "Provider key").value).toBe("visual-graph pending");
    expect.soft(controlByLabel<HTMLSelectElement>(rendered.container, "Rows per page").value).toBe("10");
    click(buttonByText(rendered.container, "Refresh"));
    await waitFor(() => expect(getJson.mock.calls.length).toBeGreaterThan(requestCountBeforeReturn));
    const restoredRequests = getJson.mock.calls.slice(requestCountBeforeReturn).map(([url]) => String(url));
    expect.soft(restoredRequests.some(url => {
      return url.includes("search=invoice") && url.includes("providerKey=visual-graph") && url.includes("limit=10") && url.includes("cursor=activity-cursor-2") && !url.includes("authority=");
    })).toBe(true);
    await rendered.unmount();
  }, 15000);

  it("restores separate filter contexts from separate collection history entries", async () => {
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url === "/design/activities/definitions/definition-1") return definition();
      if (url.startsWith("/design/activities/definitions/definition-1/drafts?")) return page([draft()]);
      if (url.startsWith("/design/activities/definitions/definition-1/versions?")) return page([version()]);
      return page([definition()]);
    });
    const rendered = renderPage(getJson);
    await waitForText(rendered.container, "Invoice evaluator");
    change(rendered.container.querySelector<HTMLInputElement>("input[placeholder='Name, type key, or category']")!, "first origin");
    await waitFor(() => expect(getJson.mock.calls.some(([url]) => new URL(String(url), "https://studio.test").searchParams.get("search") === "first origin")).toBe(true));
    const firstOrigin = JSON.parse(JSON.stringify(window.history.state));

    window.history.pushState({ hostEntry: "second" }, "", "/workflows/activity-definitions");
    window.dispatchEvent(new PopStateEvent("popstate"));
    await waitForText(rendered.container, "Invoice evaluator");
    change(rendered.container.querySelector<HTMLInputElement>("input[placeholder='Name, type key, or category']")!, "second origin");
    change(controlByLabel<HTMLInputElement>(rendered.container, "Provider key"), "provider-two");
    change(controlByLabel<HTMLSelectElement>(rendered.container, "Rows per page"), "50");
    click(buttonByText(rendered.container, "Built-in"));
    await waitFor(() => expect(getJson.mock.calls.some(([url]) => {
      const parameters = new URL(String(url), "https://studio.test").searchParams;
      return parameters.get("search") === "second origin" && parameters.get("providerKey") === "provider-two" && parameters.get("authority") === "ProviderSource" && parameters.get("limit") === "50";
    })).toBe(true));
    const secondOrigin = JSON.parse(JSON.stringify(window.history.state));
    expect(firstOrigin.elsaActivityDefinitions.collection.entryId).not.toBe(secondOrigin.elsaActivityDefinitions.collection.entryId);

    click(rendered.container.querySelector<HTMLElement>("[role='row'][tabindex='0']")!);
    await waitForText(rendered.container, "Build your activity");
    window.history.replaceState(secondOrigin, "", "/workflows/activity-definitions");
    window.dispatchEvent(new PopStateEvent("popstate"));
    await waitForText(rendered.container, "Invoice evaluator");
    expect(rendered.container.querySelector<HTMLInputElement>("input[placeholder='Name, type key, or category']")?.value).toBe("second origin");
    expect(controlByLabel<HTMLInputElement>(rendered.container, "Provider key").value).toBe("provider-two");
    expect(controlByLabel<HTMLSelectElement>(rendered.container, "Rows per page").value).toBe("50");
    expect(rendered.container.querySelector(".ad-collection-view button[aria-pressed='true']")?.textContent).toBe("Built-in");

    window.history.replaceState(firstOrigin, "", "/workflows/activity-definitions");
    window.dispatchEvent(new PopStateEvent("popstate"));
    await waitForText(rendered.container, "Invoice evaluator");
    expect(rendered.container.querySelector<HTMLInputElement>("input[placeholder='Name, type key, or category']")?.value).toBe("first origin");
    expect(controlByLabel<HTMLInputElement>(rendered.container, "Provider key").value).toBe("");
    expect(controlByLabel<HTMLSelectElement>(rendered.container, "Rows per page").value).toBe("25");
    expect(rendered.container.querySelector(".ad-collection-view button[aria-pressed='true']")?.textContent).toBe("Designed");
    await rendered.unmount();
  }, 15000);

  it("keeps its collection context when a dirty editor vetoes browser Back", async () => {
    const activityEditor: StudioActivityDefinitionImplementationEditorContribution = {
      ...graphContribution(),
      component: ({ value, onChange, readOnly }) => {
        const payload = value.payload as { edit?: number };
        return <button type="button" disabled={readOnly} onClick={() => onChange({ ...value, payload: { edit: (payload.edit ?? 0) + 1 } })}>Edit implementation {payload.edit ?? 0}</button>;
      }
    };
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url === "/design/activities/definitions/definition-1") return definition();
      if (url.startsWith("/design/activities/definitions/definition-1/drafts?")) return page([{ ...draft(), actions: [{ action: "edit-draft", allowed: true }] }]);
      if (url.startsWith("/design/activities/definitions/definition-1/versions?")) return page([version()]);
      if (url === "/design/activities/drafts/draft-1") return draftDetail();
      if (url === "/design/activities/versions/version-1") return versionDetail();
      return page([definition()]);
    });
    const putJson = vi.fn(async () => { throw new Error("save failed"); });
    const rendered = renderPage(getJson, undefined, "/workflows/activity-definitions", undefined, [activityEditor], putJson);
    await waitForText(rendered.container, "Invoice evaluator");
    change(rendered.container.querySelector<HTMLInputElement>("input[placeholder='Name, type key, or category']")!, "origin history search");
    await waitFor(() => expect(getJson.mock.calls.some(([url]) => String(url).includes("search=origin+history+search"))).toBe(true));
    click(rendered.container.querySelector<HTMLElement>("[role='row'][tabindex='0']")!);
    await waitForText(rendered.container, "Build your activity");
    click(buttonByText(rendered.container, "Drafts"));
    await waitForText(rendered.container, "Draft draft-1");
    click(buttonByText(rendered.container, "Draft draft-1"));
    click(buttonByText(rendered.container, "Open implementation editor"));
    await waitFor(() => expect(rendered.container.textContent).toContain("Saved revision 3"));
    click(buttonByText(rendered.container, "Edit implementation"));
    await waitForText(rendered.container, "Save failed");
    const editorCollection = JSON.parse(JSON.stringify(window.history.state.elsaActivityDefinitions.collection));

    const pushState = vi.spyOn(window.history, "pushState");
    window.history.back();
    await waitFor(() => expect(pushState).toHaveBeenCalled());
    expect(new URLSearchParams(window.location.search).get("section")).toBe("editor");
    expect(window.history.state.elsaActivityDefinitions.collection).toEqual(editorCollection);
    pushState.mockRestore();

    click(buttonByText(rendered.container, "Discard local changes and return"));
    await waitForText(rendered.container, "Invoice evaluator");
    click(buttonByText(rendered.container, "Activity Definitions"));
    await waitForText(rendered.container, "Invoice evaluator");
    expect(window.location.pathname).toBe("/workflows/activity-definitions");
    expect(window.history.state.elsaActivityDefinitions.collection).toEqual(editorCollection);
    expect(rendered.container.querySelector<HTMLInputElement>("input[placeholder='Name, type key, or category']")?.value).toBe("origin history search");
    click(buttonByText(rendered.container, "Refresh"));
    await waitFor(() => expect(getJson.mock.calls.some(([url]) => String(url).includes("search=origin+history+search"))).toBe(true));
    await rendered.unmount();
  }, 20000);

  it("redacts the old collection and restarts paging after auth changes while the collection is unmounted", async () => {
    let changedScope = false;
    let resolveAuthorized!: (value: unknown) => void;
    const authorizedResponse = new Promise(resolve => { resolveAuthorized = resolve; });
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url === "/design/activities/definitions/definition-2") return definition({ definitionId: "definition-2", displayName: "Second evaluator" });
      if (url.startsWith("/design/activities/definitions/definition-2/drafts?")) return page([draft({ definitionId: "definition-2", draftId: "draft-2" })]);
      if (url.startsWith("/design/activities/definitions/definition-2/versions?")) return page([version({ version: { ...version().version, definitionId: "definition-2", versionId: "version-2" } })]);
      if (changedScope && url.startsWith("/design/activities/definitions?")) return authorizedResponse;
      if (url.includes("cursor=unmounted-auth-cursor")) return page([definition({ definitionId: "definition-2", displayName: "Second evaluator" })], { totalCount: 2 });
      return page([definition()], { continuation: "unmounted-auth-cursor", hasMore: true, totalCount: 2 });
    });
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const rendered = renderPage(getJson, queryClient);
    await waitForText(rendered.container, "Invoice evaluator");
    change(rendered.container.querySelector<HTMLInputElement>("input[placeholder='Name, type key, or category']")!, "invoice");
    change(controlByLabel<HTMLInputElement>(rendered.container, "Provider key"), "visual-graph");
    change(controlByLabel<HTMLSelectElement>(rendered.container, "Rows per page"), "10");
    await waitFor(() => expect(getJson.mock.calls.some(([url]) => String(url).includes("search=invoice") && String(url).includes("providerKey=visual-graph") && String(url).includes("limit=10"))).toBe(true));
    click(buttonByText(rendered.container, "Next"));
    await waitForText(rendered.container, "Second evaluator");
    await waitFor(() => expect(getJson.mock.calls.some(([url]) => String(url).includes("cursor=unmounted-auth-cursor"))).toBe(true));
    const originState = JSON.parse(JSON.stringify(window.history.state));
    const baseUrl = rendered.baseUrl;
    const originCollection = originState.elsaActivityDefinitions.collection;
    click(rendered.container.querySelector<HTMLElement>("[role='row'][tabindex='0']")!);
    await waitForText(rendered.container, "Second evaluator");
    await rendered.unmount();

    changedScope = true;
    window.dispatchEvent(new Event(authSessionStartedEvent));
    window.history.replaceState(originState, "", "/workflows/activity-definitions");
    const requestStart = getJson.mock.calls.length;
    const remounted = renderPage(getJson, queryClient, "/workflows/activity-definitions", undefined, [], undefined, undefined, { baseUrl, preserveHistoryState: true });
    await waitFor(() => expect(getJson.mock.calls.length).toBeGreaterThan(requestStart));
    await waitFor(() => expect(window.history.state.elsaActivityDefinitions.collection.authorityRevision).toBeGreaterThan(originCollection.authorityRevision));
    expect(remounted.container.textContent).not.toContain("Invoice evaluator");
    expect(remounted.container.textContent).not.toContain("Second evaluator");
    expect(remounted.container.textContent).toContain("scope changed");
    expect(remounted.container.querySelector<HTMLInputElement>("input[placeholder='Name, type key, or category']")?.value).toBe("invoice");
    expect(controlByLabel<HTMLInputElement>(remounted.container, "Provider key").value).toBe("visual-graph");
    expect(controlByLabel<HTMLSelectElement>(remounted.container, "Rows per page").value).toBe("10");
    const resumedRequests = getJson.mock.calls.slice(requestStart).map(([url]) => String(url)).filter(url => url.startsWith("/design/activities/definitions?") && !url.includes("cursor=unmounted-auth-cursor"));
    expect(resumedRequests.some(url => url.includes("search=invoice") && url.includes("providerKey=visual-graph") && url.includes("limit=10"))).toBe(true);

    resolveAuthorized(page([definition({ displayName: "Authorized after remount" })]));
    await waitFor(() => expect(`${remounted.container.textContent} ${getJson.mock.calls.slice(requestStart).map(([url]) => String(url)).join(" | ")}`).toContain("Authorized after remount"));
    await remounted.unmount();
  }, 20000);

  it("does not show cached collection rows after auth changes before a new-entry request succeeds", async () => {
    let changedScope = false;
    let resolveAuthorized!: (value: unknown) => void;
    const authorizedResponse = new Promise(resolve => { resolveAuthorized = resolve; });
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (changedScope && url.startsWith("/design/activities/definitions?")) return authorizedResponse;
      return page([definition({ displayName: "Cached old-authority row" })]);
    });
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const baseUrl = "test://activity-definitions-new-entry-auth-scope";
    const first = renderPage(getJson, queryClient, "/workflows/activity-definitions", undefined, [], undefined, undefined, { baseUrl });
    await waitForText(first.container, "Cached old-authority row");
    await first.unmount();

    changedScope = true;
    window.dispatchEvent(new Event(authSessionStartedEvent));
    const requestStart = getJson.mock.calls.length;
    const nextEntry = renderPage(getJson, queryClient, "/workflows/activity-definitions", undefined, [], undefined, undefined, { baseUrl });
    await waitFor(() => expect(getJson.mock.calls.length).toBeGreaterThan(requestStart));
    expect(nextEntry.container.textContent).not.toContain("Cached old-authority row");
    expect(nextEntry.container.textContent).toContain("Loading Activity Definitions");

    resolveAuthorized(page([definition({ displayName: "Authorized new-entry row" })]));
    await waitForText(nextEntry.container, "Authorized new-entry row");
    await nextEntry.unmount();
  }, 15000);

  it.each(["authentication", "base URL"] as const)("discards only the old cursor after a %s scope change", async scopeChange => {
    let changedScope = false;
    let resolveAuthorized!: (value: unknown) => void;
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url === "/design/activities/definitions/definition-2") return definition({ definitionId: "definition-2", displayName: "Second evaluator" });
      if (url.startsWith("/design/activities/definitions/definition-2/drafts?")) return page([draft({ definitionId: "definition-2", draftId: "draft-2" })]);
      if (url.startsWith("/design/activities/definitions/definition-2/versions?")) return page([version({ version: { ...version().version, definitionId: "definition-2", versionId: "version-2" } })]);
      if (changedScope && url.startsWith("/design/activities/definitions?")) return new Promise(resolve => { resolveAuthorized = resolve; });
      if (url.includes("cursor=scope-cursor-2")) return page([definition({ definitionId: "definition-2", displayName: "Second evaluator" })], { totalCount: 2 });
      return page([definition()], { continuation: "scope-cursor-2", hasMore: true, totalCount: 2 });
    });
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const rendered = renderPage(getJson, queryClient);
    await waitForText(rendered.container, "Invoice evaluator");
    change(rendered.container.querySelector<HTMLInputElement>("input[placeholder='Name, type key, or category']")!, "invoice");
    change(controlByLabel<HTMLInputElement>(rendered.container, "Provider key"), "visual-graph");
    change(controlByLabel<HTMLSelectElement>(rendered.container, "Rows per page"), "10");
    click(buttonByText(rendered.container, "All"));
    await waitFor(() => expect(getJson.mock.calls.some(([url]) => String(url).includes("search=invoice") && String(url).includes("providerKey=visual-graph") && String(url).includes("limit=10"))).toBe(true));
    click(buttonByText(rendered.container, "Next"));
    await waitForText(rendered.container, "Second evaluator");
    await waitFor(() => expect(getJson.mock.calls.some(([url]) => String(url).includes("cursor=scope-cursor-2"))).toBe(true));
    const originState = JSON.parse(JSON.stringify(window.history.state));

    click(rendered.container.querySelector<HTMLElement>("[role='row'][tabindex='0']")!);
    await waitForText(rendered.container, "Build your activity");
    changedScope = true;
    if (scopeChange === "authentication") window.dispatchEvent(new Event(authSessionStartedEvent));
    else rendered.setBaseUrl("test://activity-definitions-new-authority");
    window.history.replaceState(originState, "", "/workflows/activity-definitions");
    window.dispatchEvent(new PopStateEvent("popstate"));

    await waitFor(() => expect(resolveAuthorized).toBeDefined());
    expect(rendered.container.textContent).not.toContain("Invoice evaluator");
    expect(rendered.container.textContent).not.toContain("Second evaluator");
    expect(rendered.container.textContent).toContain("scope changed");
    expect(rendered.container.querySelector<HTMLInputElement>("input[placeholder='Name, type key, or category']")?.value).toBe("invoice");
    expect(controlByLabel<HTMLInputElement>(rendered.container, "Provider key").value).toBe("visual-graph");
    expect(controlByLabel<HTMLSelectElement>(rendered.container, "Rows per page").value).toBe("10");
    const newScopeRequests = getJson.mock.calls.map(([url]) => String(url)).filter(url => url.startsWith("/design/activities/definitions?") && !url.includes("cursor=scope-cursor-2"));
    expect(newScopeRequests.some(url => url.includes("search=invoice") && url.includes("providerKey=visual-graph") && url.includes("limit=10") && !url.includes("authority="))).toBe(true);
    expect(queryClient.getQueriesData({ queryKey: activityDesignKeys.definitionCollections }).every(([, value]) => (value as { items?: unknown[] } | undefined)?.items?.length === 0)).toBe(true);

    resolveAuthorized(page([definition({ displayName: "Authorized after scope change" })]));
    await waitForText(rendered.container, "Authorized after scope change");
    await rendered.unmount();
  }, 20000);

  it.each([
    { scenario: "a confirmed current head and one historical draft", hasHead: true },
    { scenario: "no current head", hasHead: false }
  ])("opens the only authorized active draft directly with $scenario", async ({ hasHead }) => {
    const active = { ...draft(), actions: [{ action: "edit-draft", allowed: true }] };
    const drafts = hasHead ? page([active, draft({ draftId: "published-draft", status: "Published" })]) : page([active]);
    const rendered = renderOverview({
      drafts,
      versions: hasHead ? page([version()]) : page([]),
      definitionView: hasHead ? undefined : definitionWithoutCurrentHead()
    });
    await waitForText(rendered.container, "1 open draft");
    const details = rendered.container.querySelector<HTMLDetailsElement>(".ad-technical-disclosure")!;
    expect(details.open).toBe(false);
    expect(details.textContent).toContain("Contoso.InvoiceEvaluator");
    const primaryAction = rendered.container.querySelector<HTMLButtonElement>(".ad-primary-action")!;
    const actionName = primaryAction.textContent?.trim();
    click(primaryAction);
    expect(new URLSearchParams(window.location.search).get("section")).toBe("editor");
    expect(new URLSearchParams(window.location.search).get("draft")).toBe("draft-1");
    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Activity Definition"));
    await waitForText(rendered.container, "Build your activity");
    expect(new URLSearchParams(window.location.search).has("draft")).toBe(false);
    expect(actionName).toBe("Continue editing draft");
    await rendered.unmount();
  });

  it.each([
    { label: "parallel active drafts", drafts: page([draft(), draft({ draftId: "draft-2" })]) },
    { label: "an incomplete draft page", drafts: page([{ ...draft(), actions: [{ action: "edit-draft", allowed: true }] }], { hasMore: true, continuation: "next" }) },
    { label: "a draft without edit permission", drafts: page([draft()]) },
    { label: "an unreadable current head", drafts: page([]), versions: page([]), actions: [{ action: "create-draft", allowed: true }] },
    {
      label: "an omitted current head",
      drafts: page([]),
      versions: page([]),
      actions: [{ action: "create-draft", allowed: true }],
      definitionView: definitionWithOmittedHead([{ action: "create-draft", allowed: true }])
    }
  ])("requires explicit selection for $label", async ({ drafts, versions, actions, definitionView }) => {
    const postJson = vi.fn();
    const rendered = renderOverview({ drafts, versions, actions, definitionView, postJson });
    await waitForText(rendered.container, "Build your activity");
    const primaryAction = rendered.container.querySelector<HTMLButtonElement>(".ad-primary-action")!;
    await waitFor(() => expect(primaryAction.disabled).toBe(false));
    expect(primaryAction.textContent?.trim()).toBe("Open drafts");
    expect([...rendered.container.querySelectorAll("button")].some(button => button.textContent?.includes("Continue editing draft"))).toBe(false);
    click(primaryAction);
    expect(new URLSearchParams(window.location.search).get("section")).toBe("drafts");
    expect(new URLSearchParams(window.location.search).has("draft")).toBe(false);
    expect(rendered.container.querySelector("[role='dialog']")).toBeNull();
    expect(postJson).not.toHaveBeenCalled();
    await rendered.unmount();
  });

  it("does not choose the only active draft on a final cursor page", async () => {
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url === "/design/activities/definitions/definition-1") return definition();
      if (url.includes("/definitions/definition-1/drafts?")) return url.includes("cursor=last-page")
        ? page([{ ...draft({ draftId: "last-active", presentationLabel: "Final active draft" }), actions: [{ action: "edit-draft", allowed: true }] }])
        : page([draft({ presentationLabel: "Earlier active draft" })], { hasMore: true, continuation: "last-page", totalCount: 2 });
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = renderPage(getJson, undefined, "/workflows/activity-definitions?definition=definition-1&section=drafts");
    await waitForText(rendered.container, "Earlier active draft");
    click(buttonByText(rendered.container, "Next"));
    await waitForText(rendered.container, "Final active draft");
    expect([...rendered.container.querySelectorAll("button")].some(button => button.textContent === "Continue editing draft")).toBe(false);
    click(buttonByText(rendered.container, "Open drafts"));
    expect(new URLSearchParams(window.location.search).has("draft")).toBe(false);
    await rendered.unmount();
  });

  it("starts editing the authorized published head when only historical draft records remain", async () => {
    const postJson = vi.fn(async () => draftDetail());
    const rendered = renderOverview({
      drafts: page([draft({ status: "Published" })]),
      versions: page([version({ actions: [{ action: "clone-draft", allowed: true }] })]),
      actions: [{ action: "create-draft", allowed: true }], postJson
    });
    await waitForText(rendered.container, "No open drafts");
    const primaryAction = rendered.container.querySelector<HTMLButtonElement>(".ad-primary-action")!;
    const actionName = primaryAction.textContent?.trim();
    click(primaryAction);
    await waitFor(() => expect(new URLSearchParams(window.location.search).get("section")).toBe("editor"));
    expect(postJson).toHaveBeenCalledTimes(1);
    expect(postJson).toHaveBeenCalledWith("/design/activities/definitions/definition-1/drafts", { sourceVersionId: "version-1", presentationLabel: null });
    expect(actionName).toBe("Create draft from current version");
    await rendered.unmount();
  });

  it("offers blank creation when the definition has no current head and creation is authorized", async () => {
    const rendered = renderOverview({
      drafts: page([draft({ status: "Published" })]),
      versions: page([]),
      definitionView: definitionWithoutCurrentHead([{ action: "create-draft", allowed: true }]),
      withAuthoringCapabilities: true,
      activityEditors: [graphContribution()]
    });
    await waitForText(rendered.container, "No open drafts");
    const primaryAction = rendered.container.querySelector<HTMLButtonElement>(".ad-primary-action")!;
    const actionName = primaryAction.textContent?.trim();
    click(primaryAction);

    await waitForText(rendered.container, "Create blank draft");
    const modes = [...rendered.container.querySelectorAll<HTMLInputElement>("input[name='draft-mode']")];
    expect(modes).toHaveLength(2);
    expect(modes[0].checked).toBe(true);
    expect(modes[1].checked).toBe(false);
    expect(actionName).toBe("Create draft");
    await rendered.unmount();
  });

  it("does not guess clone permission from definition-level draft creation", async () => {
    const postJson = vi.fn();
    const rendered = renderOverview({ drafts: page([]), actions: [{ action: "create-draft", allowed: true }], postJson });
    await waitForText(rendered.container, "No open drafts");
    click(buttonByText(rendered.container, "Open drafts"));
    expect(postJson).not.toHaveBeenCalled();
    expect(new URLSearchParams(window.location.search).get("section")).toBe("drafts");
    await rendered.unmount();
  });

  it("resolves an exact draft bookmark even when the selected draft is outside the loaded page", async () => {
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url === "/design/activities/definitions/definition-1") return definition();
      if (url.startsWith("/design/activities/definitions/definition-1/drafts?")) return page([]);
      if (url === "/design/activities/drafts/draft-2") return draftDetail({ draftId: "draft-2", presentationLabel: "Parallel review" });
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = renderPage(getJson, undefined, "/workflows/activity-definitions?definition=definition-1&section=drafts&draft=draft-2");

    await waitForText(rendered.container, "Parallel review");
    expect(rendered.container.textContent).toContain("draft-2");
    expect(rendered.container.textContent).toContain("revision 3");
    await rendered.unmount();
  });

  it("lists parallel duplicate labels with stable fallbacks and creates an unlabeled blank draft", async () => {
    const postJson = vi.fn(async (_url: string, _body: unknown) => draftDetail({ draftId: "draft-blank" }));
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities(true);
      if (url === "/design/activities/definitions/definition-1") return definition({}, [{ action: "create-draft", allowed: true }]);
      if (url.startsWith("/design/activities/definitions/definition-1/drafts?")) return page([
        draft({ draftId: "draft-review-a", presentationLabel: "Review" }),
        draft({ draftId: "draft-review-b", presentationLabel: "Review" }),
        draft({ draftId: "draft-unlabeled", presentationLabel: null })
      ]);
      if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
      if (url === "/design/activities/drafts/draft-blank") return draftDetail({ draftId: "draft-blank" });
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = renderPage(getJson, undefined, "/workflows/activity-definitions?definition=definition-1&section=drafts", postJson, [graphContribution()]);

    await waitForText(rendered.container, "Draft draft-unlabeled");
    expect([...rendered.container.querySelectorAll("strong")].filter(element => element.textContent === "Review")).toHaveLength(2);
    click(buttonByText(rendered.container, "Create parallel draft"));
    await waitForText(rendered.container, "Blank");
    await waitFor(() => expect(rendered.container.querySelector<HTMLSelectElement>(".ad-management-dialog select")?.value).toBe("visual-graph|1"));
    click(buttonByText(rendered.container, "Create blank draft"));

    await waitFor(() => expect(postJson).toHaveBeenCalledTimes(1));
    expect(postJson).toHaveBeenCalledWith("/design/activities/definitions/definition-1/drafts", {
      sourceVersionId: null,
      presentationLabel: null,
      provider: { providerKey: "visual-graph", schemaVersion: "1", payload: { root: null } },
      contract: {
        contractSchemaVersion: "1",
        inputs: [],
        outputs: [],
        outcomes: [{ referenceKey: "done", name: "Done", isEmitted: true, description: null }]
      },
      layout: []
    });
    await waitFor(() => expect(new URLSearchParams(window.location.search).get("draft")).toBe("draft-blank"));
    await rendered.unmount();
  });

  it("clones one authorized exact immutable version without rewriting the source", async () => {
    const navigateToStudioPath = vi.fn();
    const postJson = vi.fn(async (_url: string, _body: unknown) => draftDetail({ draftId: "draft-clone", presentationLabel: "Review" }));
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities(true);
      if (url === "/design/activities/definitions/definition-1") return definition({}, [{ action: "create-draft", allowed: true }]);
      if (url.startsWith("/design/activities/definitions/definition-1/versions?")) return page([
        version({ actions: [{ action: "clone-draft", allowed: true }] })
      ]);
      if (url === "/design/activities/versions/version-1") return versionDetail();
      if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
      if (url === "/design/activities/drafts/draft-clone") return draftDetail({ draftId: "draft-clone", presentationLabel: "Review" });
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = renderPage(
      getJson,
      undefined,
      "/workflows/activity-definitions?definition=definition-1&section=versions&version=version-1",
      postJson,
      [graphContribution()],
      undefined,
      navigateToStudioPath
    );

    await waitForText(rendered.container, "Create draft from this exact version");
    click(buttonByText(rendered.container, "Plan broad upgrade from this exact version"));
    expect(navigateToStudioPath).toHaveBeenCalledWith(
      "/workflows/activity-definitions/upgrades?rootKind=ActivityVersion&rootId=version-1"
    );
    click(buttonByText(rendered.container, "Create draft from this exact version"));
    await waitForText(rendered.container, "Clone exact version");
    change(controlByLabel<HTMLInputElement>(rendered.container, "Draft label"), "Review");
    click(buttonByText(rendered.container, "Clone exact version"));

    await waitFor(() => expect(postJson).toHaveBeenCalledTimes(1));
    expect(postJson).toHaveBeenCalledWith("/design/activities/definitions/definition-1/drafts", {
      sourceVersionId: "version-1",
      presentationLabel: "Review"
    });
    expect(postJson.mock.calls.every(([url]) => url === "/design/activities/definitions/definition-1/drafts")).toBe(true);
    await waitFor(() => expect(new URLSearchParams(window.location.search).get("draft")).toBe("draft-clone"));
    await rendered.unmount();
  });

  it("keeps source-owned history exact and read-only when fork authority is denied", async () => {
    const source = definition({
      contentAuthority: { kind: "ProviderSource", authorityKey: "contoso.catalog", sourceId: "invoice-source" }
    }, [{ action: "fork-definition", allowed: false, unavailableCode: "activity.action.forbidden" }]);
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url === "/design/activities/definitions/definition-1") return source;
      if (url.startsWith("/design/activities/definitions/definition-1/versions?")) return page([
        version({ actions: [{ action: "fork-definition", allowed: false, unavailableCode: "activity.action.forbidden" }] })
      ]);
      if (url === "/design/activities/versions/version-1") return versionDetail({
        definition: source.definition,
        provider: { providerKey: "contoso.source", schemaVersion: "7", manifestFingerprint: "source-fingerprint" }
      });
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = renderPage(getJson, undefined, "/workflows/activity-definitions?definition=definition-1&section=versions&version=version-1");

    await waitForText(rendered.container, "Read-only in Studio.");
    await waitForText(rendered.container, "contoso.source");
    expect(rendered.container.textContent).toContain("contoso.catalog");
    expect(rendered.container.textContent).toContain("invoice-source");
    expect(rendered.container.textContent).toContain("contoso.source");
    expect(rendered.container.textContent).toContain("schema 7");
    expect(rendered.container.textContent).toContain("Inputs, outputs & outcomes");
    expect([...rendered.container.querySelectorAll("button")].some(button => button.textContent?.includes("Fork"))).toBe(false);
    await rendered.unmount();
  }, 15_000);

  it("shows source-provided inputs and outputs without draft management, and folds technical identities", async () => {
    const source = definition({ contentAuthority: { kind: "ProviderSource", authorityKey: "third-party.module", sourceId: "/private/source/path" } });
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url === "/design/activities/definitions/definition-1") return source;
      if (url === "/design/activities/versions/version-1") return versionDetail({ definition: source.definition });
      throw new Error(`Unexpected GET ${url}`);
    });
    // Old source-owned draft bookmarks should recover to the useful overview.
    const rendered = renderPage(getJson, undefined, "/workflows/activity-definitions?definition=definition-1&section=drafts&draft=old-draft");
    await waitForText(rendered.container, "Inputs, outputs & outcomes");
    expect([...rendered.container.querySelectorAll("[role=tab]")].map(tab => tab.textContent)).toEqual(["Overview", "Versions", "Usage"]);
    expect(rendered.container.querySelector("[role=tab][aria-selected=true]")?.textContent).toBe("Overview");
    expect(rendered.container.textContent).not.toContain("Mutable work");
    expect(rendered.container.textContent).not.toContain("View drafts");
    expect(rendered.container.textContent).not.toContain("Fork recommended version");
    const details = rendered.container.querySelector<HTMLDetailsElement>("details.ad-technical-details")!;
    expect(details.open).toBe(false);
    expect(details.textContent).toContain("/private/source/path");
    expect(details.textContent).toContain("third-party.module");
    expect(getJson.mock.calls.some(([url]) => String(url).includes("/drafts"))).toBe(false);
    await rendered.unmount();
  });

  it("does not offer CLR forks when only another provider advertises the same schema number", async () => {
    const source = definition({ contentAuthority: { kind: "ProviderSource", authorityKey: "elsa.clr-activity" } }, [{ action: "fork-definition", allowed: true }]);
    source.lifecycle.recommendation = { ...source.lifecycle.recommendation!, providerKey: "elsa.clr-activity" };
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities(true, true);
      if (url === "/design/activities/definitions/definition-1") return source;
      if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
      if (url === "/design/activities/versions/version-1") return versionDetail({ definition: source.definition });
      throw new Error(`Unexpected GET ${url}`);
    });
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const rendered = renderPage(getJson, queryClient, "/workflows/activity-definitions?definition=definition-1", undefined, [graphContribution()]);
    await waitForText(rendered.container, "Inputs, outputs & outcomes");
    await waitFor(() => expect(queryClient.getQueryData(activityDesignKeys.authoringCapabilities)).toBeDefined());
    expect(rendered.container.textContent).not.toContain("Fork recommended version");
    await rendered.unmount();
  });

  it("does not offer an explicit fork version override when no recommended active version exists", async () => {
    const source = {
      ...definition({
        contentAuthority: { kind: "ProviderSource", authorityKey: "contoso.catalog", sourceId: "invoice-source" }
      }, [{ action: "fork-definition", allowed: true }]),
      lifecycle: {
        ...definition().lifecycle,
        recommendation: { ...definition().lifecycle.recommendation!, lifecycle: "Revoked" }
      }
    };
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities(true, true);
      if (url === "/design/activities/definitions/definition-1") return source;
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = renderPage(getJson, undefined, "/workflows/activity-definitions?definition=definition-1");

    await waitForText(rendered.container, "No recommended active version");
    expect(rendered.container.textContent).toContain("Choose a version to inspect its inputs and outputs.");
    expect([...rendered.container.querySelectorAll("button")].some(button => button.textContent?.includes("Fork"))).toBe(false);
    await rendered.unmount();
  });

  it("fails closed without probing a direct fork path when the preview relation is unavailable", async () => {
    const source = definition({
      contentAuthority: { kind: "ProviderSource", authorityKey: "contoso.catalog", sourceId: "invoice-source" }
    }, [{ action: "fork-definition", allowed: true }]);
    const postJson = vi.fn();
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities(true);
      if (url === "/design/activities/definitions/definition-1") return source;
      if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = renderPage(getJson, undefined, "/workflows/activity-definitions?definition=definition-1", postJson, [graphContribution()]);

    await waitForText(rendered.container, "Fork recommended version");
    click(buttonByText(rendered.container, "Fork recommended version"));
    await waitForText(rendered.container, "Studio does not offer an explicit version override");
    click(await enabledButton(rendered.container, "Create fork preview"));
    await waitForText(rendered.container, "fork preview could not be established");
    expect(postJson).not.toHaveBeenCalled();
    await rendered.unmount();
  });

  it("reviews and atomically applies the recommended active version without offering an explicit version picker", async () => {
    const source = definition({
      contentAuthority: { kind: "ProviderSource", authorityKey: "contoso.catalog", sourceId: "invoice-source" }
    }, [{ action: "fork-definition", allowed: true }]);
    const postJson = vi.fn(async (url: string, _body: unknown) => {
      if (url === "/design/activities/definitions/definition-1/fork-previews") return forkPreview();
      if (url === "/design/activities/fork-candidates/candidate-signed/apply") return {
        ...forkReceipt(),
        idempotencyKey: (_body as { idempotencyKey: string }).idempotencyKey
      };
      throw new Error(`Unexpected POST ${url}`);
    });
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities(true, true);
      if (url === "/design/activities/definitions/definition-1") return source;
      if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
      if (url === "/design/activities/drafts/draft-fork") return draftDetail({ draftId: "draft-fork" });
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = renderPage(getJson, undefined, "/workflows/activity-definitions?definition=definition-1", postJson, [graphContribution()]);

    await waitForText(rendered.container, "Fork recommended version");
    click(buttonByText(rendered.container, "Fork recommended version"));
    await waitForText(rendered.container, "Recommended version");
    expect(rendered.container.querySelector("select")?.textContent).not.toContain("Version");
    click(await enabledButton(rendered.container, "Create fork preview"));

    await waitForText(rendered.container, "elsa.user.invoice-evaluator.forked");
    expect(rendered.container.textContent).toContain("definition-fork");
    expect(rendered.container.textContent).toContain("draft-fork");
    click(buttonByText(rendered.container, "Apply reviewed fork"));

    await waitFor(() => expect(new URLSearchParams(window.location.search).get("definition")).toBe("definition-fork"));
    expect(new URLSearchParams(window.location.search).get("draft")).toBe("draft-fork");
    expect(postJson.mock.calls[0][0]).toBe("/design/activities/definitions/definition-1/fork-previews");
    expect(postJson.mock.calls[0][1]).toMatchObject({
      sourceVersionId: "version-1",
      category: "Finance",
      displayName: "Invoice evaluator",
      targetProviderKey: "visual-graph",
      targetProviderSchemaVersion: "1"
    });
    expect(postJson.mock.calls[1][1]).toMatchObject({
      requestFingerprint: `sha256:${"a".repeat(64)}`
    });
    await rendered.unmount();
  });

  it("reconciles a lost successful Apply response through the advertised fork status relation", async () => {
    const source = definition({
      contentAuthority: { kind: "ProviderSource", authorityKey: "contoso.catalog", sourceId: "invoice-source" }
    }, [{ action: "fork-definition", allowed: true }]);
    const postJson = vi.fn(async (url: string, _body: unknown) => {
      if (url.endsWith("/fork-previews")) return forkPreview();
      if (url.includes("/fork-candidates/")) throw new StudioHttpError(
        500,
        "Outcome unknown",
        null,
        { errorCode: "activity.fork.outcome-unknown" }
      );
      throw new Error(`Unexpected POST ${url}`);
    });
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities(true, true);
      if (url === "/design/activities/definitions/definition-1") return source;
      if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
      if (url.startsWith("/design/activities/forks/")) return {
        ...forkReceipt(),
        idempotencyKey: decodeURIComponent(url.slice("/design/activities/forks/".length))
      };
      if (url === "/design/activities/drafts/draft-fork") return draftDetail({ draftId: "draft-fork" });
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = renderPage(getJson, undefined, "/workflows/activity-definitions?definition=definition-1", postJson, [graphContribution()]);

    await waitForText(rendered.container, "Fork recommended version");
    click(buttonByText(rendered.container, "Fork recommended version"));
    click(await enabledButton(rendered.container, "Create fork preview"));
    await waitForText(rendered.container, "Apply reviewed fork");
    click(buttonByText(rendered.container, "Apply reviewed fork"));

    await waitFor(() => expect(new URLSearchParams(window.location.search).get("draft")).toBe("draft-fork"));
    expect(getJson.mock.calls.some(([url]) => String(url).startsWith("/design/activities/forks/activity-fork-apply-"))).toBe(true);
    await rendered.unmount();
  });

  it("moves the recommendation with exact head, current recommendation, target lifecycle, and reason", async () => {
    const definitionView = lifecycleDefinition("version-2", "version-1");
    const versions = [
      lifecycleVersion("version-1", "1.0.0", true),
      lifecycleVersion("version-2", "2.0.0", false, "Active", [{ action: "set-recommendation", allowed: true }])
    ];
    const putJson = vi.fn(async () => ({
      definitionId: "definition-1",
      headVersionId: "version-2",
      recommendedVersionId: "version-2",
      changedAt: "2026-07-18T12:00:00Z",
      reason: "Validated rollout"
    }));
    const getJson = lifecycleGetJson(definitionView, versions);
    const rendered = renderPage(getJson, undefined, "/workflows/activity-definitions?definition=definition-1&section=versions&version=version-2", undefined, [], putJson);

    await waitForText(rendered.container, "Make recommended");
    click(buttonByText(rendered.container, "Make recommended"));
    await waitForText(rendered.container, "Existing placed occurrences");
    change(controlByLabel<HTMLTextAreaElement>(rendered.container, "Reason"), "Validated rollout");
    click(controlByLabel<HTMLInputElement>(rendered.container, "I reviewed"));
    click(await enabledButton(rendered.container, "Move recommendation"));

    await waitFor(() => expect(putJson).toHaveBeenCalledWith("/design/activities/definitions/definition-1/recommendation", {
      expectedDefinitionHeadVersionId: "version-2",
      expectedRecommendedVersionId: "version-1",
      recommendedVersionId: "version-2",
      expectedRecommendedVersionLifecycle: "Active",
      reason: "Validated rollout"
    }));
    await rendered.unmount();
  });

  it("retires the recommended version only with an exact replacement decision", async () => {
    const definitionView = lifecycleDefinition("version-2", "version-1");
    const versions = [
      lifecycleVersion("version-1", "1.0.0", true, "Active", [{ action: "retire-version", allowed: true }]),
      lifecycleVersion("version-2", "2.0.0", false)
    ];
    const postJson = vi.fn(async () => ({ versionId: "version-1", lifecycle: "Retired", reason: "Superseded", changedAt: "2026-07-18T12:00:00Z" }));
    const rendered = renderPage(lifecycleGetJson(definitionView, versions), undefined, "/workflows/activity-definitions?definition=definition-1&section=versions&version=version-1", postJson);

    await waitForText(rendered.container, "Retire");
    click(buttonByText(rendered.container, "Retire"));
    await waitForText(rendered.container, "Required recommendation decision");
    change(controlByLabel<HTMLTextAreaElement>(rendered.container, "Reason"), "Superseded");
    click(controlByLabel<HTMLInputElement>(rendered.container, "I reviewed"));
    click(await enabledButton(rendered.container, "Retire exact version"));

    await waitFor(() => expect(postJson).toHaveBeenCalledWith("/design/activities/versions/version-1/retire", {
      expectedLifecycle: "Active",
      reason: "Superseded",
      recommendationDecision: {
        expectedDefinitionHeadVersionId: "version-2",
        expectedRecommendedVersionId: "version-1",
        disposition: "Replace",
        replacementVersionId: "version-2",
        expectedReplacementLifecycle: "Active"
      }
    }));
    await rendered.unmount();
  });

  it("restores a retired version without changing recommendation", async () => {
    const definitionView = lifecycleDefinition("version-2", "version-2");
    const versions = [
      lifecycleVersion("version-1", "1.0.0", false, "Retired", [{ action: "restore-version", allowed: true }]),
      lifecycleVersion("version-2", "2.0.0", true)
    ];
    const postJson = vi.fn(async () => ({ versionId: "version-1", lifecycle: "Active", reason: "Incident cleared", changedAt: "2026-07-18T12:00:00Z" }));
    const rendered = renderPage(lifecycleGetJson(definitionView, versions), undefined, "/workflows/activity-definitions?definition=definition-1&section=versions&version=version-1", postJson);

    await waitForText(rendered.container, "Restore");
    click(buttonByText(rendered.container, "Restore"));
    await waitForText(rendered.container, "Restoration does not recommend");
    change(controlByLabel<HTMLTextAreaElement>(rendered.container, "Reason"), "Incident cleared");
    click(controlByLabel<HTMLInputElement>(rendered.container, "I reviewed"));
    click(await enabledButton(rendered.container, "Restore exact version"));

    await waitFor(() => expect(postJson).toHaveBeenCalledWith("/design/activities/versions/version-1/restore", {
      expectedLifecycle: "Retired",
      reason: "Incident cleared",
      recommendationDecision: null
    }));
    await rendered.unmount();
  });

  it("requires authoritative direct dependencies, bounded as-of usage, and typed semantic-version confirmation for terminal revocation", async () => {
    const definitionView = lifecycleDefinition("version-2", "version-2");
    const versions = [
      lifecycleVersion("version-1", "1.0.0", false, "Retired", [{ action: "revoke-version", allowed: true }]),
      lifecycleVersion("version-2", "2.0.0", true)
    ];
    const postJson = vi.fn(async () => ({ versionId: "version-1", lifecycle: "Revoked", reason: "Security response", changedAt: "2026-07-18T12:00:00Z" }));
    const getJson = lifecycleGetJson(definitionView, versions, authoritativeUsageEvidence());
    const rendered = renderPage(getJson, undefined, "/workflows/activity-definitions?definition=definition-1&section=versions&version=version-1", postJson);

    await waitForText(rendered.container, "Revoke");
    click(buttonByText(rendered.container, "Revoke"));
    await waitForText(rendered.container, "Visible inbound uses");
    expect(rendered.container.textContent).toContain("7/18/2026");
    expect(rendered.container.textContent).toContain("Immutable artifacts, identities, dependency evidence, and Runtime Evidence are preserved");
    change(controlByLabel<HTMLInputElement>(rendered.container, "Type semantic version"), "1.0.0");
    change(controlByLabel<HTMLTextAreaElement>(rendered.container, "Reason"), "Security response");
    click(controlByLabel<HTMLInputElement>(rendered.container, "I reviewed"));
    click(await enabledButton(rendered.container, "Revoke exact version permanently"));

    await waitFor(() => expect(postJson).toHaveBeenCalledWith("/design/activities/versions/version-1/revoke", {
      expectedLifecycle: "Retired",
      reason: "Security response",
      recommendationDecision: null
    }));
    await rendered.unmount();
  });

  it("keeps stale lifecycle conflicts privacy-safe and requires a fresh review", async () => {
    const definitionView = lifecycleDefinition("version-1", "version-1");
    const versions = [lifecycleVersion("version-1", "1.0.0", true, "Active", [{ action: "retire-version", allowed: true }])];
    const postJson = vi.fn(async () => { throw new StudioHttpError(409, "hidden current version identity", null); });
    const rendered = renderPage(lifecycleGetJson(definitionView, versions), undefined, "/workflows/activity-definitions?definition=definition-1&section=versions&version=version-1", postJson);

    await waitForText(rendered.container, "Retire");
    click(buttonByText(rendered.container, "Retire"));
    await waitForText(rendered.container, "Continue with no recommended version");
    click(controlByLabel<HTMLInputElement>(rendered.container, "Continue with no recommended version"));
    change(controlByLabel<HTMLTextAreaElement>(rendered.container, "Reason"), "Lifecycle cleanup");
    click(controlByLabel<HTMLInputElement>(rendered.container, "I reviewed"));
    click(await enabledButton(rendered.container, "Retire exact version"));

    await waitForText(rendered.container, "changed after review");
    expect(rendered.container.textContent).not.toContain("hidden current version identity");
    await rendered.unmount();
  });

  it.each([403, 404])("purges retained child identities when a draft collection fails closed with %s", async status => {
    let draftReads = 0;
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url.startsWith("/design/activities/definitions?")) return page([definition()]);
      if (url === "/design/activities/definitions/definition-1") return definition();
      if (url.startsWith("/design/activities/definitions/definition-1/drafts?")) {
        draftReads += 1;
        if (draftReads === 1) return page([{ ...draft(), draft: { ...draft().draft, presentationLabel: "Secret draft identity" } }]);
        throw new StudioHttpError(status, "Hidden draft-1", null, { detail: "Hidden draft-1" });
      }
      throw new Error(`Unexpected GET ${url}`);
    });
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const rendered = renderPage(getJson, queryClient);

    await waitForText(rendered.container, "Invoice evaluator");
    click(rendered.container.querySelector<HTMLElement>("[role='row'][tabindex='0']")!);
    await waitForText(rendered.container, "Build your activity");
    click(buttonByText(rendered.container, "Drafts"));
    await waitForText(rendered.container, "Secret draft identity");

    await queryClient.refetchQueries({ queryKey: ["activity-design", "definition", "definition-1", "drafts"] });
    await waitForText(rendered.container, "No identities or counts are shown");
    expect(rendered.container.textContent).not.toContain("Secret draft identity");
    expect(rendered.container.textContent).not.toContain("draft-1");
    expect(queryClient.getQueriesData({ queryKey: ["activity-design", "definition", "definition-1", "drafts"] }).every(([, data]) => {
      const childPage = data as { items?: unknown[]; totalCount?: number } | undefined;
      return childPage?.items?.length === 0 && childPage.totalCount === 0;
    })).toBe(true);
    await rendered.unmount();
  });

  it("scopes draft and version privacy failures to their definition when navigating", async () => {
    const nextDraft = {
      ...draft({ draftId: "draft-2", definitionId: "definition-2", presentationLabel: "Second evaluator draft" }),
      actions: [{ action: "edit-draft", allowed: true }]
    };
    const nextVersion = version({
      version: { ...version().version, versionId: "version-2", definitionId: "definition-2", version: "2.0.0" }
    });
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url === "/design/activities/definitions/definition-1") return definition();
      if (url === "/design/activities/definitions/definition-2") return definition({ definitionId: "definition-2", displayName: "Second evaluator" });
      if (url.startsWith("/design/activities/definitions/definition-1/drafts?")) throw new StudioHttpError(403, "hidden draft identity", null);
      if (url.startsWith("/design/activities/definitions/definition-1/versions?")) throw new StudioHttpError(403, "hidden version identity", null);
      if (url.startsWith("/design/activities/definitions/definition-2/drafts?")) return page([nextDraft]);
      if (url.startsWith("/design/activities/definitions/definition-2/versions?")) return page([nextVersion]);
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = renderPage(getJson, undefined, "/workflows/activity-definitions?definition=definition-1");

    await waitForText(rendered.container, "Invoice evaluator");
    click(buttonByText(rendered.container, "Drafts"));
    await waitForText(rendered.container, "This account cannot open this collection");
    click(buttonByText(rendered.container, "Versions"));
    await waitForText(rendered.container, "This account cannot open this collection");

    flushSync(() => {
      window.history.replaceState({}, "", "/workflows/activity-definitions?definition=definition-2");
      window.dispatchEvent(new PopStateEvent("popstate"));
    });
    await waitForText(rendered.container, "Second evaluator");
    await waitFor(() => expect(buttonByText(rendered.container, "Continue editing draft").disabled).toBe(false));

    click(buttonByText(rendered.container, "Drafts"));
    await waitForText(rendered.container, "Second evaluator draft");
    click(buttonByText(rendered.container, "Versions"));
    await waitForText(rendered.container, "2.0.0");
    expect(rendered.container.textContent).not.toContain("cannot open this collection");
    await rendered.unmount();
  });

  it("scopes detail privacy failures to their definition and retries the denied identity explicitly", async () => {
    await import("../ActivityDefinitionWorkbench");
    let definitionOneReads = 0;
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url === "/design/activities/definitions/definition-2") return definition({ definitionId: "definition-2", displayName: "Second evaluator" });
      if (url === "/design/activities/definitions/definition-1") {
        definitionOneReads += 1;
        if (definitionOneReads === 1) throw new StudioHttpError(403, "hidden definition identity", null);
        return definition({ definitionId: "definition-1", displayName: "First evaluator" });
      }
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = renderPage(getJson, undefined, "/workflows/activity-definitions?definition=definition-2");

    await vi.dynamicImportSettled();
    await waitForText(rendered.container, "Second evaluator");
    flushSync(() => {
      window.history.pushState({}, "", "/workflows/activity-definitions?definition=definition-1");
      window.dispatchEvent(new PopStateEvent("popstate"));
    });
    await waitForText(rendered.container, "Studio could not confirm an authorized Activity Definition");
    expect(rendered.container.textContent).not.toContain("hidden definition identity");

    flushSync(() => {
      // jsdom does not traverse browser history entries, so restore the back destination and fire its popstate event.
      window.history.replaceState({}, "", "/workflows/activity-definitions?definition=definition-2");
      window.dispatchEvent(new PopStateEvent("popstate"));
    });
    await waitForText(rendered.container, "Second evaluator");
    expect(rendered.container.textContent).not.toContain("Activity Definition unavailable");

    flushSync(() => {
      window.history.pushState({}, "", "/workflows/activity-definitions?definition=definition-1");
      window.dispatchEvent(new PopStateEvent("popstate"));
    });
    await waitForText(rendered.container, "Activity Definition unavailable");
    expect(rendered.container.textContent).not.toContain("First evaluator");
    click(buttonByText(rendered.container, "Try again"));
    await waitForText(rendered.container, "First evaluator");
    await rendered.unmount();
  }, 15000);

  it("uses bounded paging and sends Designed, Built-in, All, search, and advanced filters to the server", async () => {
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url.includes("cursor=cursor-2")) return page([definition({ definitionId: "definition-2", displayName: "Second definition" })]);
      return page([definition()], { continuation: "cursor-2", hasMore: true, totalCount: 2 });
    });
    const rendered = renderPage(getJson);
    await waitForText(rendered.container, "Invoice evaluator");
    const collectionUrls = () => getJson.mock.calls.map(([url]) => String(url)).filter(url => url.includes("/definitions?"));
    await waitFor(() => expect(collectionUrls().some(url => url.includes("authority=Design"))).toBe(true));
    const views = rendered.container.querySelector<HTMLElement>("[role='group'][aria-label='Activity definition collection']")!;
    expect(views.querySelector("button[aria-pressed='true']")?.textContent).toBe("Designed");

    click(buttonByText(rendered.container, "Built-in"));
    await waitFor(() => expect(collectionUrls().some(url => url.includes("authority=ProviderSource"))).toBe(true));
    click(buttonByText(rendered.container, "All"));
    await waitFor(() => expect(collectionUrls().some(url => !url.includes("authority="))).toBe(true));
    click(buttonByText(rendered.container, "Designed"));
    await waitFor(() => expect(collectionUrls().filter(url => url.includes("authority=Design")).length).toBeGreaterThan(1));

    const advancedFilters = rendered.container.querySelector<HTMLDetailsElement>(".ad-advanced-filters")!;
    expect(advancedFilters.open).toBe(false);
    advancedFilters.open = true;

    change(rendered.container.querySelector<HTMLInputElement>("input[placeholder='Name, type key, or category']")!, "invoice");
    change(rendered.container.querySelector<HTMLInputElement>("input[placeholder='Exact provider key']")!, "visual-graph");
    await waitFor(() => expect(getJson.mock.calls.some(([url]) => String(url).includes("search=invoice") && String(url).includes("authority=Design") && String(url).includes("providerKey=visual-graph"))).toBe(true));

    await waitFor(() => expect(buttonByText(rendered.container, "Next").disabled).toBe(false));
    click(buttonByText(rendered.container, "Next"));
    await waitForText(rendered.container, "Second definition");
    expect(getJson.mock.calls.some(([url]) => String(url).includes("limit=25") && String(url).includes("cursor=cursor-2"))).toBe(true);
    await rendered.unmount();
  });

  it("retains rows for same-authority searches but hides them when authority changes", async () => {
    let resolveSearch!: (value: ReturnType<typeof page<ActivityDefinitionManagementView>>) => void;
    let resolveBuiltIn!: (value: ReturnType<typeof page<ActivityDefinitionManagementView>>) => void;
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url.includes("authority=ProviderSource")) return new Promise<ReturnType<typeof page<ActivityDefinitionManagementView>>>(resolve => { resolveBuiltIn = resolve; });
      if (url.includes("search=invoice")) return new Promise<ReturnType<typeof page<ActivityDefinitionManagementView>>>(resolve => { resolveSearch = resolve; });
      return page([definition()]);
    });
    const rendered = renderPage(getJson);
    await waitForText(rendered.container, "Invoice evaluator");
    change(rendered.container.querySelector<HTMLInputElement>("input[placeholder='Name, type key, or category']")!, "invoice");
    await waitFor(() => expect(resolveSearch).toBeDefined());
    expect(rendered.container.textContent).toContain("Invoice evaluator");
    resolveSearch(page([definition()]));
    await waitFor(() => expect(rendered.container.querySelector(".ad-skeleton")).toBeNull());
    click(buttonByText(rendered.container, "Built-in"));
    await waitFor(() => expect(resolveBuiltIn).toBeDefined());
    expect(rendered.container.textContent).not.toContain("Invoice evaluator");
    resolveBuiltIn(page([definition({ displayName: "Built-in activity" })]));
    await waitForText(rendered.container, "Built-in activity");
    await rendered.unmount();
  });

  it("retains confirmed rows after a failed refresh without exposing backend error text", async () => {
    let reads = 0;
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      reads += 1;
      if (reads === 1) return page([definition()]);
      throw new Error("secret backend detail for hidden-definition-99");
    });
    const rendered = renderPage(getJson);
    await waitForText(rendered.container, "Invoice evaluator");

    click(buttonByText(rendered.container, "Refresh"));
    await waitForText(rendered.container, "Refresh failed; showing retained data");

    expect(rendered.container.textContent).toContain("Invoice evaluator");
    expect(rendered.container.textContent).not.toContain("secret backend detail");
    expect(rendered.container.textContent).not.toContain("hidden-definition-99");
    await rendered.unmount();
  });

  it("clears cached identities and counts when authorization is rejected", async () => {
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      throw new StudioHttpError(403, "Hidden Invoice evaluator definition-1", null, { detail: "Hidden Invoice evaluator definition-1" });
    });
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    queryClient.setQueryData(activityDesignKeys.definitionCollection({ limit: 25, cursor: null, search: "", authority: "", providerKey: "" }), page([definition()]));
    queryClient.setQueryData(activityDesignKeys.definition("definition-old"), definition({ definitionId: "definition-old", displayName: "Previously visible" }));
    queryClient.setQueryData(activityDesignKeys.definitionDrafts({ definitionId: "definition-old", limit: 10, cursor: null }), page([draft()]));
    const rendered = renderPage(getJson, queryClient);

    await waitForText(rendered.container, "No identities or counts were loaded");
    expect(rendered.container.textContent).not.toContain("Invoice evaluator");
    expect(rendered.container.textContent).not.toContain("definition-1");
    await waitFor(() => queryClient.getQueriesData({ queryKey: activityDesignKeys.definitionResources }).every(([, data]) => {
      if (data !== null) throw new Error("Waiting for resource cache redaction.");
    }));
    expect(queryClient.getQueriesData({ queryKey: activityDesignKeys.definitionCollections }).every(([, data]) => {
      const page = data as { items?: unknown[]; totalCount?: number } | undefined;
      return page?.items?.length === 0 && page.totalCount === 0;
    })).toBe(true);
    await rendered.unmount();
  });

  it("shows an authorized collection after a scope change clears a prior denial", async () => {
    let mode: "authorized" | "denied" | "pending" = "authorized";
    let resolveAuthorized!: (value: unknown) => void;
    let collectionReads = 0;
    const authorizedResponse = new Promise(resolve => { resolveAuthorized = resolve; });
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      collectionReads += 1;
      if (mode === "denied") throw new StudioHttpError(403, "not authorized", null);
      if (mode === "pending") return authorizedResponse;
      return page([definition({ displayName: "Initial authorized row" })]);
    });
    const rendered = renderPage(getJson);

    await waitForText(rendered.container, "Initial authorized row");
    rendered.setBaseUrl(rendered.baseUrl);
    await waitFor(() => expect(rendered.container.textContent).toContain("Initial authorized row"));

    mode = "denied";
    window.dispatchEvent(new Event(authSessionStartedEvent));
    await waitForText(rendered.container, "No identities or counts were loaded");
    expect(rendered.container.textContent).not.toContain("Initial authorized row");

    mode = "pending";
    const authorizedReads = collectionReads;
    window.dispatchEvent(new Event(authSessionStartedEvent));
    await waitFor(() => expect(collectionReads).toBeGreaterThan(authorizedReads));
    expect(rendered.container.textContent).not.toContain("Initial authorized row");
    expect(rendered.container.textContent).not.toContain("definition-1");

    resolveAuthorized(page([definition({ displayName: "Authorized after reauthentication" })]));
    await waitForText(rendered.container, "Authorized after reauthentication");
    await rendered.unmount();
  }, 20000);

  it("restarts from the first page when a retained cursor snapshot expires", async () => {
    let firstPageReads = 0;
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url.includes("cursor=cursor-2")) throw new StudioHttpError(410, "expired", null);
      firstPageReads += 1;
      return page([definition()], { continuation: "cursor-2", hasMore: true, totalCount: 2 });
    });
    const rendered = renderPage(getJson);
    await waitForText(rendered.container, "Invoice evaluator");

    click(buttonByText(rendered.container, "Next"));
    await waitForText(rendered.container, "Collection snapshot expired");
    click(buttonByText(rendered.container, "Restart paging"));
    await waitFor(() => {
      if (firstPageReads < 2) throw new Error("Waiting for a fresh first-page request.");
    });
    expect(rendered.container.textContent).toContain("Invoice evaluator");
    await rendered.unmount();
  });

  it.each([
    { scenario: "expired snapshot", title: "Collection snapshot expired", rejectedError: () => new StudioHttpError(410, "expired", null) },
    {
      scenario: "invalid continuation",
      title: "Collection continuation is no longer valid",
      rejectedError: () => new StudioHttpError(400, "invalid continuation", null, {
        status: 400,
        errorCode: "activity.management.cursor-invalid",
        recovery: { instruction: "restart-without-cursor" }
      })
    }
  ])("hides a loaded page after a $scenario refresh and cached first page until Restart paging succeeds", async ({ title, rejectedError }) => {
    const collectionRequests: URL[] = [];
    let expireContinuation = false;
    let recoveryPending = false;
    let resolveRecovery!: (value: unknown) => void;
    const recoveryResponse = new Promise(resolve => { resolveRecovery = resolve; });
    const getJson = vi.fn(async (path: string) => {
      if (path === "/capabilities") return capabilities();
      const request = new URL(path, window.location.origin);
      collectionRequests.push(request);
      if (request.searchParams.get("cursor") === "cursor-2") {
        if (expireContinuation) throw rejectedError();
        return page([definition({ definitionId: "definition-2", displayName: "Loaded later page" })], { totalCount: 2 });
      }
      if (recoveryPending) return recoveryResponse;
      return page([definition({ displayName: "Cached first page" })], { continuation: "cursor-2", hasMore: true, totalCount: 2 });
    });
    const rendered = renderPage(getJson);

    await waitForText(rendered.container, "Cached first page");
    change(rendered.container.querySelector<HTMLInputElement>("input[placeholder='Name, type key, or category']")!, "recovery search");
    change(controlByLabel<HTMLInputElement>(rendered.container, "Provider key"), "elsa.profile");
    change(controlByLabel<HTMLSelectElement>(rendered.container, "Rows per page"), "10");
    click(buttonByText(rendered.container, "Built-in"));
    await waitFor(() => expect(collectionRequests.some(request => request.searchParams.get("search") === "recovery search"
      && request.searchParams.get("providerKey") === "elsa.profile"
      && request.searchParams.get("limit") === "10"
      && request.searchParams.get("authority") === "ProviderSource")).toBe(true));

    click(buttonByText(rendered.container, "Next"));
    await waitForText(rendered.container, "Loaded later page");
    expireContinuation = true;
    click(buttonByText(rendered.container, "Refresh"));
    await waitForText(rendered.container, title);
    expect(rendered.container.textContent).not.toContain("Loaded later page");
    expect(rendered.container.querySelector("[role='row'][tabindex='0']")).toBeNull();
    expect(rendered.container.querySelector("button[aria-label='Copy Definition ID']")).toBeNull();

    recoveryPending = true;
    const requestCountBeforeRestart = collectionRequests.length;
    click(buttonByText(rendered.container, "Restart paging"));
    await waitFor(() => expect(collectionRequests.length).toBeGreaterThan(requestCountBeforeRestart));
    const restartRequest = collectionRequests.at(-1)!;
    expect(restartRequest.searchParams.has("cursor")).toBe(false);
    expect(restartRequest.searchParams.get("search")).toBe("recovery search");
    expect(restartRequest.searchParams.get("providerKey")).toBe("elsa.profile");
    expect(restartRequest.searchParams.get("limit")).toBe("10");
    expect(restartRequest.searchParams.get("authority")).toBe("ProviderSource");
    expect(rendered.container.textContent).not.toContain("Cached first page");
    expect(rendered.container.textContent).not.toContain("Loaded later page");
    expect(rendered.container.querySelector("[role='row'][tabindex='0']")).toBeNull();

    resolveRecovery(page([definition({ displayName: "Fresh restarted page" })], { totalCount: 1 }));
    await waitForText(rendered.container, "Fresh restarted page");
    await rendered.unmount();
  }, 20000);

  it("restarts only an explicitly invalid continuation without dropping collection filters", async () => {
    const collectionRequests: URL[] = [];
    const getJson = vi.fn(async (path: string) => {
      if (path === "/capabilities") return capabilities();
      const request = new URL(path, window.location.origin);
      collectionRequests.push(request);
      if (request.searchParams.get("cursor") === "cursor-2") {
        throw new StudioHttpError(400, "invalid continuation", null, {
          status: 400,
          errorCode: "activity.management.cursor-invalid",
          recovery: { instruction: "restart-without-cursor" }
        });
      }
      return page([definition()], { continuation: "cursor-2", hasMore: true, totalCount: 2 });
    });
    const rendered = renderPage(getJson);
    await waitForText(rendered.container, "Invoice evaluator");

    change(rendered.container.querySelector<HTMLInputElement>("input[placeholder='Name, type key, or category']")!, "invoice");
    change(controlByLabel<HTMLInputElement>(rendered.container, "Provider key"), "elsa.profile");
    change(controlByLabel<HTMLSelectElement>(rendered.container, "Rows per page"), "10");
    click(buttonByText(rendered.container, "Built-in"));
    await waitFor(() => {
      const request = collectionRequests.at(-1);
      expect(request?.searchParams.get("search")).toBe("invoice");
      expect(request?.searchParams.get("providerKey")).toBe("elsa.profile");
      expect(request?.searchParams.get("limit")).toBe("10");
      expect(request?.searchParams.get("authority")).toBe("ProviderSource");
    });
    await enabledButton(rendered.container, "Next");
    click(buttonByText(rendered.container, "Next"));
    await waitFor(() => expect(collectionRequests.some(request => request.searchParams.get("cursor") === "cursor-2")).toBe(true));
    await waitForText(rendered.container, "Collection continuation is no longer valid");

    click(buttonByText(rendered.container, "Restart paging"));
    await waitFor(() => {
      const request = collectionRequests.at(-1);
      expect(request?.searchParams.has("cursor")).toBe(false);
      expect(request?.searchParams.get("search")).toBe("invoice");
      expect(request?.searchParams.get("providerKey")).toBe("elsa.profile");
      expect(request?.searchParams.get("limit")).toBe("10");
      expect(request?.searchParams.get("authority")).toBe("ProviderSource");
    });
    await rendered.unmount();
  });

  it("keeps generic bad-request failures on the ordinary retry path", async () => {
    const getJson = vi.fn(async (path: string) => {
      if (path === "/capabilities") return capabilities();
      throw new StudioHttpError(400, "invalid request", null, {
        status: 400,
        errorCode: "activity.request.invalid",
        recovery: { instruction: "restart-without-cursor" }
      });
    });
    const rendered = renderPage(getJson);

    await waitForText(rendered.container, "Couldn't load Activity Definitions");
    expect(buttonByText(rendered.container, "Try again")).toBeTruthy();
    expect(rendered.container.textContent).not.toContain("Restart paging");
    await rendered.unmount();
  });

  it("renders an explicit unavailable contribution and never probes a legacy endpoint", async () => {
    const getJson = vi.fn(async (url: string) => url === "/capabilities" ? { capabilities: [] } : Promise.reject(new Error(`Unexpected domain request ${url}`)));
    const rendered = renderPage(getJson);

    await waitForText(rendered.container, "Unavailable contribution");
    expect(rendered.container.textContent).toContain("No legacy activity fallback is used");
    expect(getJson).toHaveBeenCalledTimes(1);
    expect(getJson).toHaveBeenCalledWith("/capabilities");
    await rendered.unmount();
  });

  it("drops non-allowlisted telemetry fields", () => {
    const listener = vi.fn();
    window.addEventListener(activityDefinitionsObservationEvent, listener);
    try {
      observeActivityDefinitions({
        event: "query-success",
        surface: "collection",
        outcome: "ready",
        resultBand: "one-to-ten",
        displayName: "Secret activity",
        definitionId: "definition-secret",
        providerManifest: { source: "secret" },
        diagnostic: "sensitive free text"
      } as never);
      const detail = (listener.mock.calls[0][0] as CustomEvent).detail;
      expect(detail).toEqual({ event: "query-success", surface: "collection", outcome: "ready", resultBand: "one-to-ten" });
      expect(JSON.stringify(detail)).not.toContain("Secret");
      expect(JSON.stringify(detail)).not.toContain("definition-secret");
    } finally {
      window.removeEventListener(activityDefinitionsObservationEvent, listener);
    }
  });
});

function renderPage(
  getJson: (url: string) => Promise<unknown>,
  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } }),
  path = "/workflows/activity-definitions",
  postJson: (url: string, body: unknown) => Promise<unknown> = vi.fn(),
  activityEditors: StudioActivityDefinitionImplementationEditorContribution[] = [],
  putJson: (url: string, body: unknown) => Promise<unknown> = vi.fn(),
  navigateToStudioPath?: (path: string) => void,
  options: { baseUrl?: string; preserveHistoryState?: boolean } = {}
) {
  window.history.replaceState(options.preserveHistoryState ? window.history.state : {}, "", path);
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  const baseUrl = options.baseUrl ?? `test://activity-definitions-${Math.random()}`;
  let context = { baseUrl, http: { getJson, postJson, putJson } } as unknown as StudioEndpointContext;
  const render = () => <QueryClientProvider client={queryClient}><ActivityDefinitionsPage context={context} activityEditors={() => activityEditors} navigateToStudioPath={navigateToStudioPath} /></QueryClientProvider>;
  flushSync(() => root.render(render()));
  return {
    container,
    baseUrl,
    setBaseUrl(baseUrl: string) {
      context = { ...context, baseUrl };
      flushSync(() => root.render(render()));
    },
    async unmount() {
      flushSync(() => root.unmount());
      container.remove();
    }
  };
}

function renderOverview({ drafts, versions = page([version()]), actions = [], postJson, definitionView, withAuthoringCapabilities = false, activityEditors }: {
  drafts: ReturnType<typeof page<ActivityDefinitionDraftManagementView>>;
  versions?: ReturnType<typeof page<ActivityDefinitionVersionManagementView>>;
  actions?: ActivityDefinitionManagementView["actions"];
  postJson?: (url: string, body: unknown) => Promise<unknown>;
  definitionView?: ActivityDefinitionManagementView;
  withAuthoringCapabilities?: boolean;
  activityEditors?: StudioActivityDefinitionImplementationEditorContribution[];
}) {
  return renderPage(async (url: string) => {
    if (url === "/capabilities") return capabilities(withAuthoringCapabilities);
    if (url === "/design/activities/definitions/definition-1") return definitionView ?? definition({}, actions);
    if (url.includes("/definitions/definition-1/drafts?")) return drafts;
    if (url.includes("/definitions/definition-1/versions?")) return versions;
    if (withAuthoringCapabilities && url === "/design/activities/authoring-capabilities") return authoringCapabilities();
    if (url === "/design/activities/drafts/draft-1") return draftDetail();
    throw new Error(`Unexpected GET ${url}`);
  }, undefined, "/workflows/activity-definitions?definition=definition-1", postJson, activityEditors);
}

function definitionWithoutCurrentHead(actions: ActivityDefinitionManagementView["actions"] = []): ActivityDefinitionManagementView {
  const view = definition({ headVersionId: null, recommendedVersionId: null }, actions);
  return { ...view, lifecycle: { ...view.lifecycle, draftCount: 1, versionCount: 0, head: null, recommendation: null } };
}

function definitionWithOmittedHead(actions: ActivityDefinitionManagementView["actions"]): ActivityDefinitionManagementView {
  const view = definitionWithoutCurrentHead(actions);
  const definitionView = { ...view.definition };
  delete definitionView.headVersionId;
  return {
    ...view,
    definition: definitionView,
    lifecycle: { ...view.lifecycle, draftCount: 0 }
  };
}

function capabilities(includeAuthoring = false, includeFork = false) {
  return { capabilities: [{ id: "elsa.api.activity-design", contractVersion: "1", links: [
    { rel: "activity-definitions", href: "design/activities/definitions" },
    ...(includeAuthoring ? [{ rel: "activity-authoring-capabilities", href: "design/activities/authoring-capabilities" }] : []),
    { rel: "activity-definition", href: "design/activities/definitions/{definitionId}", templated: true },
    { rel: "activity-definition-drafts", href: "design/activities/definitions/{definitionId}/drafts", templated: true },
    { rel: "activity-definition-draft", href: "design/activities/drafts/{draftId}", templated: true },
    { rel: "activity-definition-versions", href: "design/activities/definitions/{definitionId}/versions", templated: true },
    { rel: "activity-definition-version", href: "design/activities/versions/{versionId}", templated: true },
    { rel: "activity-definition-recommendation", href: "design/activities/definitions/{definitionId}/recommendation", templated: true },
    ...(includeFork ? [
      { rel: "activity-definition-fork-preview", href: "design/activities/definitions/{definitionId}/fork-previews", templated: true },
      { rel: "activity-definition-fork-apply", href: "design/activities/fork-candidates/{candidateId}/apply", templated: true },
      { rel: "activity-definition-fork-status", href: "design/activities/forks/{idempotencyKey}", templated: true }
    ] : [])
  ] }] };
}

function page<T>(items: T[], options: { continuation?: string; hasMore?: boolean; totalCount?: number } = {}): ActivityManagementPage<T> {
  return { items, count: items.length, totalCount: options.totalCount ?? items.length, hasMore: options.hasMore ?? false, continuation: options.continuation ?? null, snapshot: { snapshotId: "snapshot-1", asOf: "2026-07-17T10:00:00Z" } };
}

function definition(overrides: Partial<ActivityDefinitionManagementView["definition"]> = {}, actions: ActivityDefinitionManagementView["actions"] = []): ActivityDefinitionManagementView {
  return {
    definition: { definitionId: "definition-1", activityTypeKey: "Contoso.InvoiceEvaluator", tenantId: null, category: "Finance", displayName: "Invoice evaluator", description: "Evaluates invoice policy.", contentAuthority: { kind: "design", authorityKey: "elsa.activity-design", sourceId: null }, forkedFrom: null, headVersionId: "version-1", recommendedVersionId: "version-1", ...overrides },
    lifecycle: { draftCount: 1, versionCount: 1, head: { versionId: "version-1", version: "1.0.0", lifecycle: "active", providerKey: "visual-graph", providerSchemaVersion: "1" }, recommendation: { versionId: "version-1", version: "1.0.0", lifecycle: "active", providerKey: "visual-graph", providerSchemaVersion: "1" } },
    actions,
    updatedAt: "2026-07-17T10:00:00Z"
  };
}

function draft(overrides: Partial<ActivityDefinitionDraftManagementView["draft"]> = {}): ActivityDefinitionDraftManagementView {
  return { draft: { draftId: "draft-1", definitionId: "definition-1", revision: 3, sourceVersionId: "version-1", status: "Active", providerKey: "visual-graph", providerSchemaVersion: "1", updatedAt: "2026-07-17T10:00:00Z", presentationLabel: null, ...overrides }, actions: [] };
}

function version(overrides: Partial<ActivityDefinitionVersionManagementView> = {}): ActivityDefinitionVersionManagementView {
  return { version: { versionId: "version-1", definitionId: "definition-1", version: "1.0.0", lifecycle: "active", publishedAt: "2026-07-17T09:00:00Z" }, providerKey: "visual-graph", providerSchemaVersion: "1", isRecommended: true, actions: [], ...overrides };
}

function lifecycleDefinition(headVersionId: string, recommendedVersionId: string | null): ActivityDefinitionManagementView {
  const view = definition({ headVersionId, recommendedVersionId });
  const reference = (versionId: string) => ({
    versionId,
    version: versionId === "version-1" ? "1.0.0" : "2.0.0",
    lifecycle: "Active",
    providerKey: "visual-graph",
    providerSchemaVersion: "1"
  });
  return {
    ...view,
    lifecycle: {
      ...view.lifecycle,
      versionCount: 2,
      head: reference(headVersionId),
      recommendation: recommendedVersionId ? reference(recommendedVersionId) : null
    }
  };
}

function lifecycleVersion(
  versionId: string,
  semanticVersion: string,
  isRecommended: boolean,
  lifecycle = "Active",
  actions: ActivityDefinitionVersionManagementView["actions"] = []
): ActivityDefinitionVersionManagementView {
  return {
    version: { versionId, definitionId: "definition-1", version: semanticVersion, lifecycle, publishedAt: "2026-07-17T09:00:00Z" },
    providerKey: "visual-graph",
    providerSchemaVersion: "1",
    isRecommended,
    actions
  };
}

function lifecycleGetJson(
  definitionView: ActivityDefinitionManagementView,
  versions: ActivityDefinitionVersionManagementView[],
  evidence = authoritativeUsageEvidence()
) {
  return vi.fn(async (url: string) => {
    if (url === "/capabilities") return capabilities();
    if (url === "/design/activities/definitions/definition-1") return definitionView;
    if (url.startsWith("/design/activities/definitions/definition-1/versions?")) return page(versions);
    if (url.includes("/dependencies?")) return url.includes("direction=outbound") ? evidence.directDependencies : evidence.inboundUsage;
    const exact = versions.find(item => url === `/design/activities/versions/${item.version.versionId}`);
    if (exact) return versionDetail({
      definition: definitionView.definition,
      versionId: exact.version.versionId,
      version: exact.version.version,
      lifecycle: exact.version.lifecycle,
      publishedAt: exact.version.publishedAt
    });
    throw new Error(`Unexpected GET ${url}`);
  });
}

function authoritativeUsageEvidence() {
  const base = {
    root: { kind: "ActivityVersion", definitionId: "definition-1", versionId: "version-1", version: "1.0.0", lifecycle: "Retired" },
    items: [{
      relationshipId: "relationship-1",
      owner: { kind: "ActivityVersion", definitionId: "owner-1", versionId: "owner-version-1" },
      dependency: { kind: "ActivityVersion", definitionId: "definition-1", versionId: "version-1" },
      occurrence: { occurrenceId: "occurrence-1", nodeOrigin: [] },
      isDirect: true,
      depth: 1,
      path: []
    }],
    nextCursor: null
  };
  return {
    directDependencies: {
      ...base,
      query: { direction: "Outbound", transitive: false, include: ["Versions"] },
      consistency: { kind: "AuthoritativeDirect", isAuthoritative: true, asOfSequence: null, asOf: null, rebuildId: null }
    },
    inboundUsage: {
      ...base,
      query: { direction: "Inbound", transitive: true, include: ["Drafts", "Versions"] },
      consistency: { kind: "DerivedProjection", isAuthoritative: false, asOfSequence: 42, asOf: "2026-07-18T12:00:00Z", rebuildId: "rebuild-42" }
    }
  };
}

function draftDetail(overrides: Partial<{ draftId: string; presentationLabel: string | null }> = {}) { return { ...draftDetailBase(), ...overrides }; }
function draftDetailBase() { return { draftId: "draft-1", definitionId: "definition-1", tenantId: null, revision: 3, sourceVersionId: "version-1", status: "Active", contract: emptyContract(), provider: { providerKey: "visual-graph", schemaVersion: "1", manifestFingerprint: "fingerprint", payload: { hidden: "not rendered" } }, layout: [], validation: null, createdAt: "2026-07-17T09:00:00Z", updatedAt: "2026-07-17T10:00:00Z", presentationLabel: null }; }
function versionDetail(overrides: Record<string, unknown> = {}) { return { definition: definition().definition, versionId: "version-1", version: "1.0.0", sourceDraftId: "draft-1", sourceVersionId: null, contract: emptyContract(), provider: { providerKey: "visual-graph", schemaVersion: "1", manifestFingerprint: "fingerprint", payload: { hidden: "not rendered" } }, template: {}, lifecycle: "Active", publishedAt: "2026-07-17T09:00:00Z", ...overrides }; }
function emptyContract() { return { contractSchemaVersion: "1", inputs: [], outputs: [], outcomes: [] }; }
function authoringCapabilities() { return { contractSchemaVersions: ["1"], activityTypeKeyRules: { serverGenerated: true, allowsPreCreationOverride: false, immutable: true, prefix: "elsa.user", pattern: "^elsa\\\\.user\\\\..+$", maximumLength: 160, collisionScope: "tenant" }, providers: [{ providerKey: "visual-graph", displayName: "Visual graph", manifestSchemas: [{ schemaVersion: "1", isAuthorable: true, migratableFromSchemaVersions: ["1"] }], requiredOutcomes: [{ referenceKey: "done", name: "Done", isEmitted: true, description: null }] }], types: [], storageDriverKeys: [], snapshotFingerprint: "sha256:test" }; }
function graphContribution(): StudioActivityDefinitionImplementationEditorContribution { return { id: "visual-graph", providerKey: "visual-graph", providerSchemaVersion: "1", createInitialImplementation: () => ({ payload: { root: null }, layout: [] }), component: () => <div>Visual graph editor</div> }; }

function forkPreview() {
  return {
    candidateId: "candidate-signed",
    requestFingerprint: `sha256:${"a".repeat(64)}`,
    status: "Reserved",
    accessBinding: { fingerprint: `sha256:${"b".repeat(64)}` },
    source: { definitionId: "definition-1", versionId: "version-1", version: "1.0.0", lifecycle: "Active", providerKey: "visual-graph", providerSchemaVersion: "1", providerFingerprint: `sha256:${"c".repeat(64)}` },
    presentation: { category: "Finance", displayName: "Invoice evaluator", description: "Evaluates invoice policy." },
    target: { definitionId: "definition-fork", activityTypeKey: "elsa.user.invoice-evaluator.forked", draftId: "draft-fork", providerKey: "visual-graph", providerSchemaVersion: "1", manifestFingerprint: `sha256:${"d".repeat(64)}`, contract: emptyContract() },
    providerMigration: { sourceProviderKey: "visual-graph", sourceProviderSchemaVersion: "1", targetProviderKey: "visual-graph", targetProviderSchemaVersion: "1", targetManifestFingerprint: `sha256:${"d".repeat(64)}`, diagnostics: [] },
    contractComparison: { sourceFingerprint: `sha256:${"e".repeat(64)}`, targetFingerprint: `sha256:${"e".repeat(64)}`, isCompatible: true, changes: [] },
    createdAt: "2026-07-17T10:00:00Z",
    expiresAt: "2026-07-17T10:15:00Z"
  };
}

function forkReceipt() {
  return {
    idempotencyKey: "activity-fork-apply-test",
    candidateId: "candidate-signed",
    requestFingerprint: `sha256:${"a".repeat(64)}`,
    outcome: "Applied",
    accessBinding: { fingerprint: `sha256:${"b".repeat(64)}` },
    definition: { ...definition().definition, definitionId: "definition-fork", activityTypeKey: "elsa.user.invoice-evaluator.forked", contentAuthority: { kind: "Design", authorityKey: "elsa.activity-design" }, forkedFrom: { definitionId: "definition-1", versionId: "version-1", version: "1.0.0" } },
    draft: { ...draft().draft, draftId: "draft-fork", definitionId: "definition-fork", revision: 1 },
    appliedAt: "2026-07-17T10:01:00Z"
  };
}

function buttonByText(container: HTMLElement, text: string) {
  const button = [...container.querySelectorAll("button")].find(candidate => candidate.textContent?.includes(text));
  if (!button) throw new Error(`Button '${text}' not found.`);
  return button;
}

async function enabledButton(container: HTMLElement, text: string) {
  let button!: HTMLButtonElement;
  await waitFor(() => {
    button = buttonByText(container, text) as HTMLButtonElement;
    expect(button.disabled).toBe(false);
  });
  return button;
}

function click(element: Element) { flushSync(() => element.dispatchEvent(new MouseEvent("click", { bubbles: true }))); }
function change(element: HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement, value: string) { flushSync(() => { const prototype = element instanceof HTMLInputElement ? HTMLInputElement.prototype : element instanceof HTMLSelectElement ? HTMLSelectElement.prototype : HTMLTextAreaElement.prototype; const setter = Object.getOwnPropertyDescriptor(prototype, "value")?.set; setter?.call(element, value); element.dispatchEvent(new Event("change", { bubbles: true })); element.dispatchEvent(new Event("input", { bubbles: true })); }); }
function controlByLabel<T extends HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>(container: HTMLElement, text: string) {
  const label = [...container.querySelectorAll("label")].find(candidate => candidate.textContent?.includes(text));
  const control = label?.querySelector("input, select, textarea");
  if (!control) throw new Error(`Control labelled '${text}' not found.`);
  return control as T;
}
async function waitForText(container: HTMLElement, text: string) { await waitFor(() => { if (!container.textContent?.includes(text)) throw new Error(`Waiting for '${text}'.`); }); }
async function waitFor(assertion: () => void) { await vi.waitFor(assertion, { timeout: 10_000 }); }
